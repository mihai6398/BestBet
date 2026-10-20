# Ghid de rulare — BestBet

Aplicația e privată: nimic nu răspunde fără autentificare, nu există înregistrare publică, iar
fiecare utilizator își vede doar pariurile și balanța lui. Datele stau într-o bază PostgreSQL.

Poate fi pornită în două feluri. Alege în funcție de ce vrei să faci:

| | Docker | Local (Visual Studio / CLI) |
|---|---|---|
| **Când** | Vrei doar s-o folosești | Vrei să modifici codul |
| **Îți trebuie** | Doar Docker Desktop | .NET 8 SDK + Node 22 + Docker (doar pentru bază) |
| **Pornire** | O comandă | Baza în Docker + două procese |
| **Hot reload** | Nu | Da |
| **Adresa** | <http://localhost:8080> | <https://localhost:5173> |

---

## 1. Docker — modul recomandat pentru folosire

### Ce îți trebuie

[Docker Desktop](https://www.docker.com/products/docker-desktop/) pornit, cu containere Linux
(setarea implicită pe Windows). Nu ai nevoie nici de .NET, nici de Node instalate.

### Pornire

1. **Setările.** În rădăcina proiectului, copiază fișierul de exemplu:

   ```bash
   cp .env.example .env
   ```

   Deschide `.env` și completează:

   - `POSTGRES_PASSWORD` — un șir lung, la întâmplare; nu trebuie să-l ții minte.
   - `ADMIN_EMAIL` și `ADMIN_PASSWORD` — primul cont, cel de administrator. Parola are **cel puțin 12
     caractere**; altfel serverul refuză să pornească și îți spune de ce în log.

   Fișierul `.env` nu intră în git și nici în imaginea Docker. Nicio parolă nu e scrisă în cod.

2. **Pornești:**

   ```bash
   docker compose up -d --build
   ```

   Pornesc două servicii: `db` (PostgreSQL 16) și `server` (API-ul și interfața, pe același port).
   Serverul așteaptă ca baza să fie gata, **aplică singur migrațiile** și creează contul de admin din
   `.env`, dacă nu există deja. Prima rulare durează câteva minute — se descarcă imaginile și se
   instalează pachetele; următoarele sunt de ordinul secundelor.

3. **Prima intrare.** Deschide **<http://localhost:8080>**, intră cu `ADMIN_EMAIL` / `ADMIN_PASSWORD`
   și schimbă parola din **Contul meu**. Valoarea din `.env` se citește doar cât timp contul nu
   există, deci după schimbare nu mai e o parolă valabilă.

4. **Ceilalți utilizatori** îi creezi tu, din **Conturi** (vezi §1b).

> Ambele porturi sunt deschise **doar pe acest calculator** (`127.0.0.1`). Ca să intre cineva de pe
> alt calculator, pune aplicația în spatele unui reverse proxy cu HTTPS (Caddy, nginx, Traefik) — nu
> deschide portul 8080 în rețea. Cookie-ul de sesiune e `Secure`, deci un browser de pe alt
> calculator îl trimite doar peste HTTPS.

### Comenzi de zi cu zi

```bash
docker compose logs -f server
```

```bash
docker compose restart
```

```bash
docker compose down
```

`down` oprește și șterge containerele, **dar nu și datele** — baza stă într-un volum separat. Ca să
pornești din nou, `docker compose up -d`.

### După ce modifici codul

```bash
docker compose up -d --build
```

`--build` este obligatoriu: fără el Docker repornește imaginea veche și te întrebi de ce nu se vede
modificarea. Migrațiile noi se aplică singure la pornire.

### Unde sunt datele

În PostgreSQL, în volumul Docker `bestbet_pgdata`. Supraviețuiește oricărui `restart`, `down` sau
`up --build`. Cheile care semnează cookie-ul de sesiune stau în volumul `bestbet_keys`, ca o
reconstruire a containerului să nu deconecteze pe nimeni.

O copie de siguranță se face cu baza pornită:

```bash
docker compose exec db pg_dump -U bestbet -d bestbet -F c -f /tmp/bestbet.dump
```

```bash
docker compose cp db:/tmp/bestbet.dump ./bestbet-backup.dump
```

Restaurarea merge invers (suprascrie ce e acum în bază):

```bash
docker compose cp ./bestbet-backup.dump db:/tmp/bestbet.dump
```

```bash
docker compose exec db pg_restore -U bestbet -d bestbet --clean --if-exists /tmp/bestbet.dump
```

Dacă ai schimbat `POSTGRES_USER` sau `POSTGRES_DB` în `.env`, folosește valorile tale în loc de
`bestbet`.

Ca să ștergi tot și s-o iei de la zero:

```bash
docker compose down -v
```

`-v` șterge și volumele. **Datele și conturile dispar definitiv.**

### Reglaje

Se schimbă în `.env`, apoi `docker compose up -d`:

- **Alt port**, dacă 8080 e ocupat — `BESTBET_PORT=9000`, apoi <http://localhost:9000>. La fel
  `POSTGRES_PORT` pentru bază.
- **Înregistrare publică** — `AUTH_ALLOW_REGISTRATION=true`. Lasă-o oprită: aplicația e privată.
- **Swagger**, dacă vrei să te joci cu API-ul direct — pune `ASPNETCORE_ENVIRONMENT: Development` în
  `docker-compose.yml` și vei avea documentația interactivă la `/swagger`. În `Production` este
  dezactivat intenționat.

---

## 1b. Conturi și securitate

**Conturile le face administratorul.** Nu există pagină de înregistrare. Din **Conturi** creezi un
cont cu email și o parolă temporară (butonul *Generează* îți dă una). Parola temporară apare **o
singură dată** — o trimiți omului pe un canal sigur. La prima intrare e obligat să o schimbe; până
atunci restul aplicației nu-i răspunde, nici din interfață, nici din API. Așa tu nu știi niciodată o
parolă folosită.

Tot din **Conturi** poți **dezactiva** un cont (nu mai poate intra, iar sesiunile deschise se închid
în cel mult un minut), îl poți reactiva, sau îi poți **reseta parola** dacă a uitat-o (primește din
nou una temporară).

**Ce te apără:**

- **Încercări de ghicire:** după 5 parole greșite contul se blochează 15 minute, iar intrarea e
  limitată și pe adresă — câteva încercări pe minut. Mesajul de eroare e același pentru email greșit,
  parolă greșită sau cont blocat, ca nimeni să nu poată afla ce emailuri au cont.
- **Sesiunea** stă într-un cookie pe care scripturile din pagină nu-l pot citi și pe care alt site nu-l
  poate trimite. În browser nu se păstrează niciun token.
- **Fiecare cu datele lui:** pariurile și balanța au proprietar, luat din sesiune, nu din ce trimite
  browserul. Un pariu al altcuiva arată exact ca unul care nu există.
- Orice punct nou adăugat în API e protejat implicit — trebuie deschis explicit, nu închis.

---

## 2. Local — modul pentru dezvoltare

### Ce îți trebuie

- .NET 8 SDK
- Node.js 22.12+ (sau 20.19+) — Vite 8 refuză versiuni mai vechi
- Certificatul de dezvoltare ASP.NET, o singură dată:

  ```bash
  dotnet dev-certs https --trust
  ```

### Baza de date

Rulează doar baza în Docker (îți trebuie `.env`, ca la §1):

```bash
docker compose up -d db
```

Apoi spune-i serverului unde e baza și care e primul cont. Se țin în **user-secrets**, în afara
proiectului, nu în cod:

```bash
dotnet user-secrets set ConnectionStrings:BestBet "Host=localhost;Port=5432;Database=bestbet;Username=bestbet;Password=PAROLA_DIN_ENV" --project BestBet.Server
```

```bash
dotnet user-secrets set ADMIN_EMAIL "tu@exemplu.ro" --project BestBet.Server
```

```bash
dotnet user-secrets set ADMIN_PASSWORD "cel-putin-12-caractere" --project BestBet.Server
```

În modul Development, **migrațiile se aplică singure** la pornirea serverului.

### Din Visual Studio

Deschide `BestBet.sln` și apasă **F5**. Pornește singur și API-ul, și serverul Vite, și îți deschide
browserul. Intri cu contul din user-secrets.

### Din linia de comandă

Două terminale. În primul, API-ul:

```bash
dotnet run --project BestBet.Server --launch-profile https
```

În al doilea, interfața:

```bash
npm --prefix bestbet.client install
```

```bash
npm --prefix bestbet.client run dev
```

Deschide **<https://localhost:5173>**. Vite trimite mai departe tot ce începe cu `/api` către API-ul
de pe `https://localhost:7243`. Browserul vorbește doar cu Vite, deci cookie-ul de sesiune merge
exact ca în producție, fără configurări suplimentare.

Modificările din `bestbet.client/src` apar instantaneu în browser. Modificările din C# cer repornirea
primului proces (sau `dotnet watch run` în loc de `dotnet run`).

### O migrație nouă

După ce schimbi modelul de date (uneltele EF sunt fixate în `.config/dotnet-tools.json`):

```bash
dotnet tool restore
```

```bash
dotnet dotnet-ef migrations add NumeleMigratiei --project BestBet.Server --output-dir Data/Migrations
```

---

## 3. Import rapid: captură de ecran, statistici, cote, sau un JSON

Completarea a opt câmpuri cu meciul în desfășurare este partea lentă. Sunt patru scurtături, în capul
formularului — una pentru o captură de ecran, una pentru statistici, una pentru cote copiate ca text,
una pentru tot deodată.

> **Interfața este în română.** Comutatorul `Română / English` este în colțul din dreapta sus și
> ține minte alegerea. Textele explicative de pe ecran spun, la fiecare câmp, exact ce număr intră
> acolo și de unde se citește.

> **Toate trei citesc versiunea HTML de pe clipboard, nu doar textul.** Când copiezi dintr-o pagină,
> browserul pune pe clipboard două variante: `text/plain`, deja aplatizat, în care un grid de cote
> ajunge adesea un șir de numere fără să mai știi care cu care merge; și `text/html`, care păstrează
> structura de rânduri. Aplicația o preferă pe a doua. Dacă lipești text simplu, merge oricum.

### 3a. Cote dintr-o captură de ecran

Butonul **„Importă cote din captură de ecran”**, primul panou, deschis din start. Este pentru cazul
în care din aplicația casei de pariuri **nu se poate selecta textul**: faci o captură (Win+Shift+S pe
Windows), o lipești cu **Ctrl+V** oriunde pe pagină, și cotele sunt citite din imagine.

Merge și prin tragerea fișierului peste zona punctată, și prin **alege un fișier**.

**Imaginea nu pleacă nicăieri.** Recunoașterea rulează în browserul tău: motorul, nucleul WebAssembly
și datele de antrenament sunt servite de aplicație, nu de pe internet. O captură a contului tău de
pariuri nu se încarcă pe niciun server, iar importul merge și fără conexiune.

**Ce vezi după citire:**

1. **Încrederea medie a recunoașterii.** Sub 70% apare un avertisment — atunci textul chiar trebuie
   verificat cifră cu cifră.
2. **Textul recunoscut, editabil.** Ce scrie acolo este ce a înțeles din imagine. Orice corectezi
   este luat în seamă imediat; piețele de dedesubt se recitesc la fiecare tastă.
3. **Piețele complete găsite**, fiecare cu **marja casei** calculată lângă ea. Marja este verificarea
   care contează: o pereche de cote reale dă între 3% și 15%. Dacă apare o marjă atipică, cele două
   cote aproape sigur nu sunt cele două fețe ale aceleiași piețe — de exemplu una de 90 de minute
   lângă una de repriza a doua — și aplicația o spune.
4. **O piață căreia îi lipsește o cotă nu este oferită deloc**, și ți se spune care este. Linia cu
   lacăt (piață suspendată) intră exact în cazul ăsta: nu are preț, și nu i se inventează unul.

**Sfaturi pentru o citire bună:** decupează doar blocul de cote, fă captura la rezoluția cea mai
mare pe care o ai, și include **ambele coloane** — și Sub, și Peste. Dacă fundalul nu este recunoscut
corect (aplicațiile de pariuri sunt aproape toate pe fundal închis), comută `Fundal` pe
*Închis (inversează)* sau *Deschis* și apasă **Citește din nou**.

#### Analiza în bloc — toate selecțiile deodată

O captură cu o scară de goluri nu este un pariu, sunt opt. Care dintre ele este greșit cotat nu se
vede cu ochiul liber, iar introducerea lor pe rând în formular durează atât încât, în practică, se
verifică doar linia care îți plăcea deja — exact cea greșită de verificat.

Sub lista de piețe este butonul **„Analizează toate cele N selecții”**. Fiecare selecție bifată trece
prin exact aceeași analiză ca una singură — aceeași scoatere a marjei, același model, aceleași praguri
de verdict — iar rezultatele apar într-un tabel sortat **după avantaj, descrescător**, cu cea mai bună
linie evidențiată.

Tabelul arată pentru fiecare selecție: cota, probabilitatea corectă (fără marjă), estimarea modelului,
avantajul în puncte procentuale, EV-ul la cota oferită, miza sugerată și verdictul. De pe fiecare rând
poți apăsa **Deschide** (trece selecția în formular și arată calculul complet) sau **Salvează** (o
trece direct în jurnalul de poziții).

Liniile pierzătoare rămân în tabel intenționat. De cele mai multe ori casa are dreptate pe toată scara,
iar asta este rezultatul — nu o eroare.

> **Are nevoie de starea live a meciului.** Minutul și xG-ul acumulat sunt ce transformă cotele în
> probabilități; fără ele nu există model din care să iasă un avantaj. Înainte de meci probabilitatea
> trebuie dată de tine, una câte una, ceea ce nu mai este o analiză în bloc — sunt opt presupuneri
> într-un tabel. Butonul rămâne blocat și îți spune ce lipsește.

### 3b. Statistici lipite de pe Sofascore și altele

Butonul **„Importă statistici de pe Sofascore”**. Deschizi pagina meciului, apeși **Ctrl+A** și
**Ctrl+C**, și lipești tot acolo. Nu trebuie să selectezi exact panoul de statistici — pagina întreagă
merge, cu reclame, clasament, cronologia golurilor și subsol cu tot.

Dintr-o singură lipire ies:

| Câmp | De unde |
|---|---|
| Meci | numele echipelor din antet |
| Minut | ceasul de lângă scor |
| Goluri gazde / oaspeți | scorul din antet |
| xG gazde / oaspeți | rândul „Goluri estimate (xG)" din panoul de statistici |

Merge pe Sofascore, Flashscore, FotMob și pe orice site care așază aceleași numere la fel, în română
sau în engleză. Recunoaște toate așezările pe care le produce un copy: valoare/etichetă/valoare pe
linii separate, tot pe un rând, sau separat prin taburi. Ceasul e citit ca minutul *în curs*: `35:57`
înseamnă minutul 36.

**Ce nu confundă, pe o pagină întreagă:** ora de start (`11.10.2026` / `22:30`) nu devine minutul 23 —
data de lângă ea o dă de gol; fila `Final` de lângă `În direct` nu înseamnă că meciul s-a terminat;
xG-ul unui singur șut din harta șuturilor nu devine xG-ul meciului; iar minutele golurilor din
cronologie nu fură locul ceasului, fiindcă ceasul se caută lângă scor, acolo unde îl pune orice site.

> **Lasă filele statisticilor pe „All".** Dacă pe Sofascore e selectată fila `1.` sau `2.`, xG-ul
> afișat este doar pe o repriză, iar în text nu scrie nicăieri asta — aplicația ar citi o jumătate de
> meci ca pe tot meciul. Este singura capcană de care nu te poate feri singură, de aceea ți-o spune
> în panou.

> **Trucul pentru fereastra recentă.** Vrei cel mai valoros câmp al corecției de ritm (vezi §4)?
> Comută statisticile Sofascore pe `2.`, copiază din nou pagina și treci acel xG în *Fereastra
> recentă*, cu fereastra = minutul curent minus 45. Asta îi spune modelului ce face jocul **acum**,
> nu ce a făcut în medie.

**Nu aplică nimic în tăcere.** Îți arată întâi ce a citit — meci, minut, xG, scor, plus celelalte
statistici pe care le-a recunoscut (posesie, șuturi, cornere), ca să vezi dintr-o privire că ai copiat
pagina corectă — și abia apoi apeși *Folosește aceste numere*. Dacă nu găsește un câmp obligatoriu,
îți spune care și butonul rămâne blocat.

> **De ce lipit și nu un buton care aduce singur datele.** Furnizorii ăștia nu oferă un API public.
> Un buton „adu datele” ar însemna să le interoghez un endpoint intern nedocumentat — împotriva
> termenilor lor, și stricat la prima redenumire de câmp de partea lor. Lipitul costă un Ctrl+C și
> merge la toți, nu doar la unul. Dacă vrei totuși varianta automată, se poate — vezi finalul.

### 3c. Cote de la casa de pariuri, copiate ca text

Butonul **„Importă cote dintr-un text copiat”**. Copiezi piața pe care o ai deschisă — 7777.md, Superbet,
Betano, oricare — o lipești, și aplicația îți arată ce piețe a găsit, cu cotele lor. **Apeși pe partea
pe care pariezi**, iar restul cotelor din acea piață devin automat cotele surori.

Asta e partea cea mai ușor de uitat și fără de care nu se poate: fără celelalte cote din aceeași
piață nu se poate scoate marja casei, deci nici calcula ceva real.

Recunoaște:

| Piață | Cum o găsește |
|---|---|
| Total goluri | `Sub 6.5 1.91` / `Peste 6.5 1.91`, sau linia ca titlu cu cotele dedesubt |
| Rezultat final | `1 / X / 2` fiecare lângă cota lui, sau etichetele pe un rând și cotele pe următorul |
| Ambele echipe marchează | Are nevoie de titlu (`Ambele echipe`, `GG/NG`, `BTTS`) |
| Primul gol | Are nevoie de titlu (`Primul gol`, `Next goal`) |

Merge după **cuvintele** rezultatelor, nu după structura HTML a unui site anume — de asta nu se strică
la primul redesign și merge la toate casele, nu doar la una.

**Ce refuză:** o piață incompletă. Dacă găsește `Sub 6.5` dar nu și `Peste 6.5`, n-o oferă deloc și
îți spune de ce. Pentru `Da`/`Nu` cere titlul pieței, fiindcă sunt cuvinte prea comune ca să fie
citite singure drept cote.

### 3d. Un meci dintr-un singur JSON

Butonul **„Lipește un meci ca JSON”** acceptă un singur obiect care umple tot, piață și cote incluse.

```json
{
  "match": "Barcelona - Racing Club",
  "minute": 36,
  "xgHome": 2.38,
  "xgAway": 0.30,
  "goalsHome": 3,
  "goalsAway": 1,
  "market": "TotalGoals",
  "selection": "Over",
  "line": 6.5,
  "odds": 1.91,
  "siblingOdds": [1.91],
  "bankroll": 263
}
```

Butonul **Export current** face drumul invers: transformă formularul curent în același JSON și îl pune
în clipboard. Util ca să-ți ții un șablon pe care doar îl editezi între meciuri.

### Câmpuri

| Câmp | Obligatoriu | Note |
|---|---|---|
| `market` | da | `TotalGoals`, `NextGoal`, `BothTeamsToScore`, `Moneyline1X2`, `Handicap` |
| `selection` | da | Ce parte a pieței — vezi tabelul de mai jos |
| `line` | doar la `TotalGoals` și `Handicap` | 2.5, 6.5, −1.5 |
| `odds` | da | Cota zecimală pe care chiar o poți lua |
| `siblingOdds` | da | Celelalte cote din **aceeași** piață. Una singură poate fi scrisă fără paranteze |
| `minute` | la meci live | 1–120 |
| `xgHome`, `xgAway` | la meci live | xG cumulat, citit de pe ecranul de statistici |
| `goalsHome`, `goalsAway` | nu | Implicit 0–0 |
| `match` | nu | Doar pentru jurnal |
| `bankroll` | nu | Dacă lipsește, rămâne cel din browser |
| `live` | nu | Implicit `true` dacă există `minute` |
| `ownProbability` | la meci **ne**-live | Estimarea ta: `62` sau `0.62`, la fel |

### Selecții acceptate

| Piață | Ce poți scrie la `selection` |
|---|---|
| `TotalGoals` | `Over` / `Peste`, `Under` / `Sub` |
| `NextGoal` | `home` / `1`, `away` / `2`, `none` / `niciun` / `0` |
| `BothTeamsToScore` | `yes` / `da` / `gg`, `no` / `nu` / `ng` |
| `Moneyline1X2` | `home` / `1`, `draw` / `x` / `egal`, `away` / `2` |
| `Handicap` | `home` / `1`, `away` / `2` |

### Ce iartă și ce nu

Numele câmpurilor se potrivesc fără să țină cont de majuscule sau de separatori — `xgHome`, `xg_home`
și `XG Home` sunt același lucru. Numerele pot fi scrise ca text și cu virgulă zecimală (`"2,38"`),
cum le dă o tastatură românească. `market` acceptă și prescurtări (`total`, `btts`, `1x2`, `goluri`).

Ce **nu** iartă, pentru că o presupunere greșită aici ar dimensiona o miză pe partea greșită a pieței:
o selecție care nu aparține pieței cerute, un număr greșit de cote surori, o cotă sub 1.0, sau lipsa
oricărui câmp obligatoriu. În toate cazurile îți spune exact care câmp și ce aștepta.

### Exemplu pre-meci

```json
{
  "match": "Sevilla - Valencia",
  "live": false,
  "market": "BothTeamsToScore",
  "selection": "Da",
  "odds": 1.80,
  "siblingOdds": [1.95],
  "ownProbability": 62
}
```

---

## 4. Dinamica meciului

Modelul din specificație împarte xG-ul acumulat la minutele jucate și duce ritmul ăsta plat până la
final. E greșit, și greșit exact când pare cel mai atrăgător.

Testat pe Barcelona – Racing Club, minutul 36, 3–1, xG 2.38 la 0.30: ritmul plat proiectează **5.95
xG** pentru Barcelona pe tot meciul, dă *Peste 6.5* la **76.5%** contra unei piețe la 50%, și
recomandă pariu cu miza la plafon. Piața avea dreptate.

Trei corecții, fiecare vizibilă în rezultat, toate pornite din start:

| Corecție | Ce face | De ce |
|---|---|---|
| **Regresie la medie** | Trage ritmul observat spre ce produc de obicei cele două echipe: cifrele din panoul pre-meci (§5b), dacă le folosești, altfel un meci mediu de ligă (1.35 xG per echipă) | Un ritm citit din 36 de minute e o estimare pe eșantion mic, nu un fapt. Face aproape toată treaba: singură, duce *Peste 6.5* de la 76% la 50% |
| **Starea scorului** | Echipa care conduce atacă mai puțin, cea care urmărește mai mult | Efectul crește spre finalul meciului, deci e scalat după cât s-a jucat |
| **Recență** | Cântărește ce face meciul *acum* peste media lui | Prinde jocul care tocmai s-a deschis, sau care a murit |

Pe meciul de mai sus, cu toate pornite: ritm **6.70 → 4.29 xG/90**, estimare **47.5%**, edge
**−2.5pp**, verdict **SKIP**. Adică răspunsul corect.

### De ce contează cifrele pre-meci în timpul meciului

Fără ele, ambele echipe sunt trase spre **aceeași** medie de 1.35, oricine ar juca. Pentru o echipă
de top asta e o greșeală sistematică: un Bayern care chiar merge pe 2.55 goluri la 90 de minute e
tras, la minutul 30, până la 1.83 — adică tratat ca o echipă de mijlocul clasamentului, iar modelul
înclină spre *Sub* tocmai la meciurile unde *Peste* iese cel mai des.

Dacă rulezi panoul pre-meci pentru meciul respectiv și apeși **Folosește în analiză**, cifrele lui
rămân în uz și după fluier: ritmul fiecărei echipe e tras spre **propriile** goluri așteptate. Pe
același exemplu, Bayern rămâne la 2.58, iar *Peste 3.5* urcă de la 30% la 38%. Merge și invers: două
echipe defensive pe un început mai animat sunt trase în jos mai tare decât de media ligii. Nu e o
regulă „pune *Peste* la echipele mari", ci „crede ce știi despre echipele astea".

Cardul rezultatului spune mereu spre ce a fost tras ritmul: *cifrele pre-meci ale echipelor* sau *o
medie de ligă*. Panoul pre-meci are nevoie de cheia API-Football (§5b); fără ea rămâne media de ligă.

### Fereastra recentă

E cel mai ascuțit input de aici și e opțional. Furnizorii dau xG pe reprize — tab-ul `1.` de lângă
`ALL` pe Sofascore. Repriza a doua este **tot meciul minus repriza întâi**; pui durata ei și cele
două xG-uri în câmpurile *Recent window*, iar modelul cântărește ce se întâmplă acum peste medie.

Dacă completezi doar o parte din fereastră, e ignorată complet. Jumătate de fereastră nu e date, iar
partea lipsă tratată ca zero ar inventa o prăbușire a jocului pe care n-a raportat-o nimeni.

### Ce vezi în rezultat

- O insignă **Pace down / Pace up** cu procentul, și ritmul înainte → după, în xG pe 90 de minute.
- Fiecare corecție care s-a aplicat, scrisă pe litere. Un număr care înjumătățește o probabilitate
  trebuie să dea socoteală.
- Un avertisment separat când proiecția brută e implauzibilă (peste 4 xG pentru o echipă).

### Avertismentul care minte

Specificația cerea un text fix: *„treat this as a floor, not a ceiling”* — tratează cifra ca pe un
minim. E adevărat pentru cazul pentru care a fost scris (meci liniștit care se deschide brusc) și
**fals pe dos** pentru un start fierbinte. Pe Barcelona, modelul dădea 76% și apoi te sfătuia că
realitatea e probabil mai sus.

Acum textul spune în ce direcție a corectat: *„Pace corrected DOWN to 64%…”* sau *„corrected UP…”*.
Cu corecțiile oprite, primești din nou exact formularea din specificație.

### Cum oprești corecțiile

Bifa **„Correct for pace and score state”**, în secțiunea *Match state*. Oprită, primești
extrapolarea brută din specificație. Merită s-o compari: diferența dintre cele două numere e o
măsură directă a cât de mult din „edge” era de fapt eroare de model.

Prin API, trimite `"pace": { "shrink": false, "scoreState": false }`.

---

## 5. Linie de referință (Polymarket)

Panoul **„Linie de referință"**, sub importul de statistici.

### De ce există

Aplicația face două lucruri cu cotele casei: scoate marja din ele ca să afle probabilitatea corectă,
și folosește cota afișată ca să calculeze EV-ul și miza. Problema e că aceeași casă e bună doar la a
doua treabă. O casă de pariuri pune 5–15% marjă în cote, iar linia „corectă" scoasă din ele e pe atât
de aproximativă — mai ales pe piețele lăturalnice, unde marja e cea mai lată.

Pe Polymarket se tranzacționează direct probabilități: 63 de cenți înseamnă 63%, iar prețurile unei
piețe se adună la ~100%. Nu e nimic de scos din ele — sunt deja linia corectă. Dar nu poți pune miza
acolo.

Soluția este să le separi: **avantajul se măsoară față de referință, EV-ul și miza rămân la cota ta.**
Cu alte cuvinte, piața ascuțită spune dacă există avantaj, iar cota ta spune cât ești plătit. Dacă
le-ai amesteca — adică ai calcula miza la un preț pe care nu-l poți lua — ar fi singurul mod în care
funcția asta te-ar putea costa bani.

### Cum se folosește

Scrii numele meciului, echipei sau competiției și apeși **Caută**. Aplicația întreabă API-ul public
Polymarket și îți arată piețele găsite, fiecare cu volumul tranzacționat și cu prețurile ei — și în
procente, și convertite în cote zecimale. Apeși pe partea care corespunde pariului tău, iar în
rezultat apare un panou nou: *Corectă după Polymarket* lângă *Corectă după casă*, plus diferența în
puncte procentuale.

> **Tu verifici că e aceeași piață.** Aplicația nu are cum să știe dacă „Peste 2.5 goluri" de la casa
> ta și întrebarea de pe Polymarket înseamnă același lucru. O referință greșită mută avantajul exact
> în direcția în care te-ai înșela, așa că întrebarea de pe piață este afișată integral și ai și un
> link ca s-o deschizi.

> **Polymarket nu acoperă tot.** Are mai ales meciurile mari, finalele și pariurile pe sezon. Pentru
> un meci obișnuit de ligă adesea nu există nimic — atunci nu folosești referință și totul merge ca
> înainte, față de linia casei.

### Ce pleacă de pe calculator

Doar textul căutării, și doar când apeși *Caută*. Este una dintre cele trei cereri pe care
aplicația le face în afară: celelalte două sunt cea din §5b, care cere o cheie, și citirea meciului
prin link din §7, care trimite doar ID-ul meciului. Nu pleacă nimic altceva: nici capitalul, nici miza, nici jurnalul, nici capturile
de ecran. Nu există cont, cheie sau autentificare. Dacă Polymarket nu răspunde, primești un mesaj și
restul aplicației funcționează neschimbat — citirea capturilor și a statisticilor merg în continuare
fără internet.

Marja referinței este afișată lângă ea tocmai ca verificare: o piață de predicții arată ~0%. Dacă
vezi 8%, nu te uiți la o piață fără marjă.

---

## 5b. Pre-meci: echipe și jucători

Pentru un meci care nu a început, analiza nu are xG live, deci până acum trebuia să scrii tu
probabilitatea. Panoul **Calculează din echipe și jucători (pre-meci)** o calculează:

1. **Forma echipelor.** Din clasamentul competiției se iau golurile marcate și primite de fiecare
   echipă, acasă și în deplasare, față de media ligii. O echipă de top cu atac puternic contra unei
   apărări slabe ajunge la 2.5–3 goluri așteptate; două echipe medii, la media ligii. Primele meciuri
   ale sezonului contează puțin: mediile sunt trase spre media ligii (echivalentul a 6 meciuri).
2. **Jucătorii.** Din statisticile sezonului (goluri și șuturi, nu doar goluri — șuturile sunt mult mai
   stabile) se estimează cine aduce golurile echipei, ținând cont de câte minute joacă. Bifezi un
   jucător **Lipsește** (accidentat, suspendat, odihnit) și golurile lui pleacă din echipă, doar în
   parte, fiindcă înlocuitorul tot mai marchează.
3. **„Cine poate marca".** Pentru fiecare jucător vezi șansa să marcheze, cota corectă și, dacă scrii
   cota casei, dacă *merită*. Testul e strict: nu scoate marja casei și folosește pragurile din
   analiza pre-meci (3 puncte avantaj = merită, 1 punct = la limită).

Apoi **Folosește în analiză**: analiza de dedesubt calculează șansa pieței alese — 1X2, Peste/Sub, GG,
handicap — din golurile așteptate, în loc să te întrebe. Verdictul, EV-ul și miza rămân cele de până
acum.

### Cheia API-Football

Statisticile jucătorilor nu se pot lua fără cont (Sofascore refuză orice cerere care nu vine dintr-un
browser). De aceea funcția are nevoie de o **cheie gratuită** de la API-Football (100 de cereri pe zi):

1. Îți faci cont pe <https://dashboard.api-football.com> și copiezi cheia.
2. **Docker:** creează un fișier `.env` lângă `docker-compose.yml` cu linia `APIFOOTBALL_KEY=cheia_ta`
   și rulează `docker compose up -d`. Fișierul `.env` nu intră în git.
3. **Local:** `dotnet user-secrets set "ApiFootball:Key" "cheia_ta" --project BestBet.Server`.

Un meci costă 3 cereri (clasamentul și cele două loturi), apoi rezultatul stă în memorie 6–12 ore:
poți analiza zeci de meciuri pe zi. Dacă planul tău nu acoperă sezonul curent, primești mesajul
API-Football, nu un număr inventat.

### Ce pleacă de pe calculator

Competiția, sezonul și id-urile echipelor, plus cheia (care rămâne pe server, nu ajunge în browser).
Nu pleacă: capitalul, miza, cotele, jurnalul. Fără cheie, restul aplicației merge neschimbat.

### Ce nu știe

Nu știe formația de start (lotul e cel al sezonului — verifică înainte de miză), tactica, motivația,
vremea sau știrile de după citirea datelor, și tratează golurile celor două echipe ca independente.
E o estimare de pornire mai bună decât una scrisă de mână, nu un preț; de aceea trece prin același
test de avantaj ca orice altceva.

---

## 6. Pariurile mele: salvare, închidere, balanță și recalculare

### Salvezi un pariu

Sub rezultatul unei analize apeși **Salvează ca pariu…**. Se deschide un formular cu cota,
probabilitatea și avantajul deja completate din analiză; adaugi miza pusă efectiv, casa de pariuri,
competiția și, dacă vrei, ora de start și note. Un pariu făcut fără analiză îl adaugi de mână, din
**Pariurile mele → Adaugă pariu**.

### Pagina „Pariurile mele”

- **Rezumatul:** câte pariuri, câte deschise, cât e în joc, cât ai mizat pe cele închise, profitul,
  **yield-ul** (profit împărțit la suma mizată pe pariurile închise), **ROI-ul** (profit împărțit la
  banii depuși, minus retragerile) și cât de des bați cota de închidere.
- **Tab-urile Deschise / Închise.** Un pariu deschis îl **închizi** cu rezultatul: câștigat, pierdut,
  anulat, câștigat pe jumătate, pierdut pe jumătate sau cash-out (la cash-out scrii suma încasată).
  Înainte să confirmi vezi cât încasezi și ce profit iese. Îl poți **edita** sau **șterge** (cu
  confirmare); unul închis îl poți **redeschide** dacă ai greșit rezultatul.

### Balanța

Balanța nu se scrie de mână și nu se stochează: e **depunerile, minus retragerile, plus profitul
pariurilor închise**, calculată la fiecare citire. Așa nu poate ajunge să spună altceva decât
pariurile, iar dacă corectezi un rezultat, balanța se corectează cu el. Prima depunere e balanța ta
inițială. **Disponibil** înseamnă balanța minus ce e în joc pe pariuri deschise; o retragere mai mare
decât atât e refuzată.

Calculatorul pornește miza de la balanța din cont. În caseta *Capital* poți scrie altă sumă pentru
un singur calcul, fără să atingi balanța reală — butonul *Folosește balanța* te întoarce la ea.

### Corectezi cota și miza

Cota de intrare și miza din jurnal sunt **câmpuri editabile**. Scrii peste ele și apeși Enter sau
dai clic în altă parte.

Există pentru că rândul înseamnă *ce ai pariat efectiv*, nu ce ți-a sugerat analiza. O cotă live se
mișcă între momentul în care o citești și clicul pe bilet, iar miza se rotunjește la ce acceptă casa.
Un jurnal care păstrează sugestia în loc de execuție măsoară altceva decât crezi — și pe el se
bazează tot scorul de CLV.

La salvare, **linia corectă și avantajul se recalculează din cota nouă**, fiindcă amândouă sunt
funcții de preț. Estimarea modelului rămâne neatinsă: faptul că ai prins o cotă mai proastă nu
schimbă cu nimic ce făcea meciul în momentul acela.

### Recalculezi un pariu deschis

Butonul **„Recalculează"** apare pe rândurile încă nedecontate.

Folosește starea meciului din formularul de sus — deci întâi reîmprospătezi statisticile (lipești
din nou pagina Sofascore), apoi apeși. Rulează exact același model ca la intrare, pe minutul și
xG-ul de acum, și îți arată:

| Ce vezi | Ce înseamnă |
|---|---|
| Șansa de câștig acum | cât de probabil e să câștige pariul, la minutul curent |
| La intrare / Diferență | de unde a plecat și cât s-a mișcat, în puncte procentuale |
| EV la cota ta | EV-ul la cota deja prinsă — singura care mai poate conta |
| EV pe miza plasată | același lucru, în bani, pe suma ta |
| Cotă corectă acum | `1 / probabilitate` — cu ea compari un cash-out sau o acoperire |

Ultimul rând este cel practic: dacă aplicația spune că acum cota corectă e 1.19 și casa îți oferă
cash-out la echivalentul lui 1.40, oferta e în dezavantajul tău.

> **Nu se salvează nimic.** Rândul păstrează datele de la intrare — minutul, xG-ul și probabilitatea
> de atunci — fiindcă ele sunt dovada că decizia a fost rezonabilă în momentul în care ai luat-o.
> Recalcularea este o întrebare pusă rândului, nu o modificare a lui, așa că o poți repeta ori de
> câte ori se mișcă meciul.

---

## 7. Monitorizare în timp real

Panoul **„Monitorizare în timp real"**, primul sub importul din captură. Pariurile deschise se
recalculează singure, la fiecare citire, cât timp meciul merge.

### Calea obișnuită: lipești linkul meciului

1. Deschizi panoul, lipești în câmpul **„Link de meci sau ID"** adresa meciului — de pe FotMob
   (`https://www.fotmob.com/match/5881191`) sau de pe Sofascore
   (`https://www.sofascore.com/football/match/arouca-porto/...`). Merge și doar ID-ul de pe FotMob.
2. Apeși **„Urmărește meciul"**. Atât.

De aici serverul cere singur datele la câteva secunde. Nu trebuie să lași nicio filă deschisă și nu
ai ce trage nicăieri. Linkul rămâne pus și după un refresh al paginii.

Datele vin de la **FotMob**, care răspunde unei cereri obișnuite — fără cheie, fără cont și fără ca
serverul să se dea drept altceva. Linkul de Sofascore merge fără să se ceară nimic de la Sofascore:
din adresa lor se iau numele echipelor, iar meciul e căutat după nume la FotMob.

> **Dacă liga nu are xG la FotMob**, panoul îți spune exact asta și nu inventează niciun număr. Fără
> xG modelul n-are pe ce să ruleze, deci pentru meciul acela treci pe semnul de carte de mai jos sau
> lipești statisticile de mână. Ligile mari (Bundesliga, LaLiga, Serie A) au xG; multe dintre cele
> mici nu.

### Rezerva: semnul de carte, din fila ta de Sofascore

Pentru meciurile fără xG la FotMob, singura cale rămasă e pagina pe care o ai tu deschisă — iar
acolo **Sofascore răspunde 403 la orice cerere care nu vine dintr-un browser real**, inclusiv la
`robots.txt`. Verificat direct de pe calculatorul tău:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://www.sofascore.com/robots.txt
```

Întoarce `403`. Ca să treacă, ar trebui ca serverul să se dea drept browser — amprentă TLS falsificată,
browser fără interfață, proxy-uri. Asta înseamnă și încălcarea termenilor lor, și o construcție care
se rupe definitiv prima dată când ei strâng șurubul. Nu merită și nu am făcut-o.

Dar **browserul tău are deja voie** — acolo pagina e deschisă și se încarcă normal. Deci citirea vine
de acolo: un semn de carte (bookmarklet) pornit pe fila respectivă citește textul paginii și îl
trimite aplicației de pe calculatorul tău. Nu se cere nimic în plus de la Sofascore; se folosește ce
browserul tău încărcase oricum.

### Cum se pornește

1. În panou apeși **„Nu merge prin link? Monitorizare din fila ta de Sofascore"** și **tragi butonul
   „BestBet: monitorizează" în bara de favorite**. (Sau apeși *Copiază* și îți faci singur un favorit
   cu textul acela.)
2. Deschizi meciul pe Sofascore, mergi la **fila Statistici**, și apeși pe favoritul nou. Apare o
   confirmare. Apeși din nou pe el ca să oprești.
3. În aplicație bifezi **Monitorizare activă**.

> **Cât timp ai un link pus, linkul are întâietate** și citirea din semnul de carte e ignorată —
> altfel formularul ar sări între două minute fără să se vadă de ce. Apasă **Oprește** lângă câmpul
> de link și semnul de carte preia imediat.

De aici, pe oricare din cele două căi, aplicația:

- umple singură minutul, scorul și xG-ul din formular;
- **recalculează singură pariurile deschise din acel meci** — panoul de recalculare apare sub fiecare
  rând, fără să apeși nimic;
- arată în panou cât de veche e ultima citire și ce s-a înțeles din ea.

Panoul spune și **care rânduri sunt urmărite**. Un pariu dintr-alt meci nu este atins: potrivirea se
face pe numele echipelor și cere două cuvinte comune, tocmai ca să nu primească un pariu de la alt
meci un număr foarte sigur despre un joc care n-are nicio legătură cu el.

### Ce te apără de citiri greșite

| Situație | Ce face aplicația |
|---|---|
| Fila s-a închis sau a adormit | După 2½ minute spune „citirea e veche" și o marchează |
| Citire fără xG (ești pe altă filă decât Statistici) | O refuză și îți spune să te întorci pe Statistici |
| Rezultatul de sus e calculat la un minut depășit | Cardul își spune singur că a fost depășit |
| Pariul s-a decis deja (Sub 1.5 la 1–1) | Arată 0%, EV −100%, și „nu mai există o cotă corectă" |
| Liga nu are xG la furnizor | Ți-o spune limpede și te trimite la semnul de carte sau la paste; nu inventează un xG |
| Furnizorul nu mai răspunde la link | Vârsta citirii crește mai departe și o marchează ca veche |
| Meciul nu a început sau s-a terminat | Ți-o spune, în loc să arate 0–0 în minutul 1 |

### Ce circulă și unde

Prin semnul de carte, citirea merge **doar între browserul tău și aplicația de pe calculatorul
tău**. Prin link, din calculator pleacă **doar ID-ul meciului**, către furnizor — nimic altceva:
nici banca, nici miza, nici pozițiile salvate. Oricum, citirea nu se salvează
nicăieri — stă în memorie și dispare când oprești aplicația. Punctul de intrare al semnului de carte e protejat cu un
token generat la fiecare pornire, pe care doar interfața aplicației îl poate citi; o pagină care nu
l-a primit primește `401`.

> **În modul de dezvoltare, certificatul trebuie să fie de încredere.** Interfața rulează pe HTTPS cu
> certificatul local, iar o cerere de pe Sofascore către un certificat în care browserul n-are
> încredere eșuează fără mesaj. Rulează o dată `dotnet dev-certs https --trust`. În Docker nu apare
> problema — acolo e `http://localhost:8080` și nu intervine niciun certificat.

> **Dacă nu vine nicio citire:** verifică să fi apăsat semnul de carte *pe fila Sofascore* (nu în
> aplicație), ca fila aia să fie încă deschisă, și ca portul din semn să fie cel pe care rulează
> aplicația. Semnul se generează cu adresa aplicației pe care l-ai copiat — dacă schimbi portul,
> ia-l din nou.

---

## 8. Testele

```bash
dotnet test
```

396 de teste, în câteva secunde. Printre ele, în
`BestBet.Server.Tests/Acceptance/SpecWorkedExamplesTests.cs`, exemplele numerice din specificație —
acelea sunt definiția lui „matematica e încă bună”. Dacă vreunul pică, se repară întâi el.

Testele de integrare pornesc aplicația întreagă peste un PostgreSQL adevărat, într-un container
temporar, deci **Docker trebuie să fie pornit**. Ele verifică, printre altele, că orice punct din API
răspunde 401 fără autentificare, că parola nu se poate ghici și că un utilizator nu poate citi,
modifica sau șterge pariurile ori banii altuia.

Pentru client (250 de teste pe parserele de import, pe citirea capturilor, pe analiza în bloc, pe
monitorizare și pe formularele de pariuri):

```bash
npm --prefix bestbet.client test
```

```bash
npm --prefix bestbet.client run lint
```

```bash
npm --prefix bestbet.client run build
```

---

## 9. Verificare rapidă că merge

După pornire, intră în aplicație și deschide **Lipește un meci ca JSON**. Lipește asta — cazul de
acceptanță din specificație, Sunderland–Arsenal la minutul 45:

```json
{"match":"Sunderland - Arsenal","market":"NextGoal","selection":"away","odds":1.60,"siblingOdds":[3.20,4.50],"minute":45,"xgHome":0.13,"xgAway":0.86,"bankroll":263}
```

Cifrele din specificație sunt pentru modelul **fără** corecții de ritm, așa că debifează întâi
**Corectează ritmul și starea scorului**. Apoi apasă **Analizează**: estimarea proprie trebuie să fie
**54.6%** (0.5459), linia corectă a pieței 53.9%, avantajul +0.7pp, EV **−0.1266**, verdictul
**Skip**, miza **0**. Cu corecțiile pornite (cum e implicit) estimarea iese altfel — e normal, nu o
eroare.

Fără autentificare, același apel direct către `/api/analyze` primește **401** — e corect, nu o eroare.

---

## 10. Când ceva nu merge

**`docker compose up` se oprește cu „port is already allocated”**
Ai deja ceva pe 8080 (sau 5432). Pune alt port în `.env`: `BESTBET_PORT=9000` sau `POSTGRES_PORT=5433`.

**`docker compose up` spune „Set POSTGRES_USER in .env”**
Nu ai fișierul `.env`. `cp .env.example .env` și completează-l.

**Nu pot intra: nu există niciun cont**
`ADMIN_EMAIL` sau `ADMIN_PASSWORD` lipsesc. Serverul scrie asta în log la pornire
(`docker compose logs server`). Completează-le și repornește; contul se creează o singură dată.

**Containerul `server` se tot repornește**
Uită-te în `docker compose logs server`. Cel mai des: parola de admin are sub 12 caractere, sau baza nu
e accesibilă cu datele din `.env`.

**Pe pagină scrie „Serverul nu răspunde”**
Rulezi în modul local și API-ul nu e pornit, sau nu ajunge la bază (`docker compose up -d db`).
Interfața are nevoie de API, iar API-ul de bază.

**Serverul local nu pornește: „The connection string 'BestBet' is not set”**
Lipsește user-secret-ul cu legătura la bază — vezi §2, *Baza de date*.

**Login-ul merge pe calculatorul meu, dar nu de pe alt dispozitiv**
Cookie-ul de sesiune e `Secure`: de pe alt dispozitiv browserul îl trimite doar peste HTTPS. Pune
aplicația în spatele unui reverse proxy cu HTTPS.

**„Prea multe încercări de pe această adresă”**
Limita de intrare pe adresă. Așteaptă un minut. Dacă un cont anume e blocat după 5 parole greșite, se
deblochează singur după 15 minute, sau îl deblochează administratorul din **Conturi** (*Activează*).

**`npm run dev` sau `npm run build` crapă cu „Cannot find native binding”**
Este [un bug cunoscut al npm](https://github.com/npm/cli/issues/4828) cu dependențele opționale.
Soluția:

```powershell
Remove-Item -Recurse -Force bestbet.client/node_modules, bestbet.client/package-lock.json
```

```bash
npm --prefix bestbet.client install
```

În Docker nu apare — acolo instalarea se face de la zero, pe Linux.

**Browserul se plânge de certificat pe <https://localhost:5173>**
Rulează `dotnet dev-certs https --trust` și repornește browserul. Alternativ, pornește serverul Vite
peste HTTP simplu: setează `BESTBET_DEV_HTTP=true` înainte de `npm run dev`.

**Modificările din cod nu se văd în container**
Ai uitat `--build`. `docker compose up -d --build`.

**Vreau să văd ce se întâmplă înăuntrul containerului**

```bash
docker compose exec server sh
```

Baza de date se deschide cu:

```bash
docker compose exec db psql -U bestbet -d bestbet
```
