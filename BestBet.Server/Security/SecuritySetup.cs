using System.Globalization;
using System.Threading.RateLimiting;
using BestBet.Data;
using BestBet.Data.Entities;
using BestBet.Server.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BestBet.Security;

/// <summary>Names of the rate-limiting policies.</summary>
public static class RateLimits
{
    /// <summary>Sign-in, sign-up and password change: everything that checks a password.</summary>
    public const string Login = "login";
}

/// <summary>
/// Everything that keeps the app closed: who may sign in, how the session is carried, and what a
/// request has to look like to be answered.
/// </summary>
public static class SecuritySetup
{
    /// <summary>The header every state-changing request from the app carries. See <see cref="UseCsrfHeaderCheck"/>.</summary>
    public const string CsrfHeader = "X-BestBet-Csrf";

    public static IServiceCollection AddBestBetSecurity(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services
            .AddIdentity<AppUser, IdentityRole<Guid>>(options =>
            {
                // Length over composition rules, as current guidance has it: a long passphrase beats a
                // short password with a digit and a symbol in it, and is easier to remember.
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<BestBetDbContext>()
            .AddClaimsPrincipalFactory<AppUserClaimsFactory>();

        // A disabled account or a reset password ends the user's other sessions this quickly, rather
        // than at the default half hour.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "bestbet.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;

            // An API answers with a status, not with a redirect to a login page it does not have. The
            // SPA reads the 401 and shows its own login.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Active, policy => Active(policy))
            .AddPolicy(AuthPolicies.Admin, policy => Active(policy.RequireRole(AuthPolicies.AdminRole)))
            .SetDefaultPolicy(Active(new AuthorizationPolicyBuilder()).Build())
            // The rule that makes the app closed by default: an endpoint that says nothing about
            // authorization requires an active, signed-in user. Opening one up takes an explicit
            // [AllowAnonymous], which is easy to find in review.
            .SetFallbackPolicy(Active(new AuthorizationPolicyBuilder()).Build());

        // Keys that sign the cookie, kept on disk so a restart or a rebuilt container does not sign
        // everybody out. In Docker this directory is a volume.
        services.AddDataProtection()
            .SetApplicationName("BestBet")
            .PersistKeysToFileSystem(new DirectoryInfo(
                configuration["DataProtection:KeysPath"] ?? Path.Combine(environment.ContentRootPath, "keys")));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Too many requests",
                    Detail = "Too many attempts from this address. Wait a minute and try again.",
                    Status = StatusCodes.Status429TooManyRequests,
                }, cancellationToken);
            };

            var loginPermits = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
            var globalPermits = configuration.GetValue("RateLimiting:RequestsPerMinute", 600);

            // Per address, on top of the per-account lockout: the lockout stops guessing one password,
            // this stops one address guessing a few passwords each across many accounts.
            options.AddPolicy(RateLimits.Login, context => RateLimitPartition.GetFixedWindowLimiter(
                Address(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginPermits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    Address(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPermits,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    private static AuthorizationPolicyBuilder Active(AuthorizationPolicyBuilder policy)
        => policy.RequireAuthenticatedUser().RequireAssertion(context => !AuthPolicies.MustChangePassword(context.User));

    private static string Address(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Headers that tell the browser what this app will never need: being framed, guessing content
    /// types, sending the address on to other sites, or running script from anywhere but itself.
    /// </summary>
    /// <remarks>
    /// The content security policy allows WebAssembly and blob workers because the screenshot reader
    /// runs Tesseract in a worker, and inline styles because React sets style attributes. Script comes
    /// from this origin only. Swagger, which is development-only and full of inline script, is left
    /// out of it.
    /// </remarks>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers.ContentSecurityPolicy =
                "default-src 'self'; " +
                "script-src 'self' 'wasm-unsafe-eval'; " +
                "worker-src 'self' blob:; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: blob:; " +
                "connect-src 'self' blob: data:; " +
                "font-src 'self'; " +
                "object-src 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'none'";
        }

        await next();
    });

    /// <summary>
    /// Refuses a state-changing API request that does not carry the app's own header.
    /// </summary>
    /// <remarks>
    /// The SameSite=Strict cookie already keeps another site's request from arriving signed in. This
    /// is the second lock on the same door: a page on another site cannot add a custom header to a
    /// request without a CORS preflight, and the only CORS policy in the app allows nothing but the
    /// live-feed push — which is the one request exempt here, because it comes from a provider's page
    /// by design and is guarded by its own token instead.
    /// </remarks>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var changesState = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
        var isLiveFeedPush = HttpMethods.IsPost(request.Method)
                             && string.Equals(request.Path.Value?.TrimEnd('/'), "/api/live-feed", StringComparison.OrdinalIgnoreCase);

        if (changesState && request.Path.StartsWithSegments("/api") && !isLiveFeedPush
            && request.Headers[CsrfHeader] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Request refused",
                Detail = $"State-changing requests must carry the {CsrfHeader} header.",
                Status = StatusCodes.Status403Forbidden,
            });
            return;
        }

        await next();
    });

    /// <summary>
    /// Creates the first admin from <c>ADMIN_EMAIL</c> and <c>ADMIN_PASSWORD</c>, once, if that account
    /// does not exist yet. An existing account is never touched, so the variables can stay set.
    /// </summary>
    /// <remarks>
    /// Throws when the variables are set but the account cannot be made — a password the policy
    /// refuses, say. Starting anyway would leave an app nobody can sign in to, with the reason buried
    /// in a log line.
    /// </remarks>
    public static async Task SeedAdminAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roles.RoleExistsAsync(AuthPolicies.AdminRole))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(AuthPolicies.AdminRole));
        }

        var email = configuration["ADMIN_EMAIL"]?.Trim();
        var password = configuration["ADMIN_PASSWORD"];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            if (!users.Users.Any())
            {
                logger.LogWarning(
                    "There are no accounts and ADMIN_EMAIL / ADMIN_PASSWORD are not set, so nobody can sign in. " +
                    "Set both and restart to create the first admin.");
            }

            return;
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true, CreatedUtc = DateTime.UtcNow };
        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the admin account from ADMIN_EMAIL / ADMIN_PASSWORD: {AuthController.Describe(created)}");
        }

        await users.AddToRoleAsync(admin, AuthPolicies.AdminRole);
        logger.LogInformation("Created the admin account {Email}", email);
    }
}
