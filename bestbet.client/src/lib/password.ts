/**
 * A temporary password for the admin to hand over: four short words' worth of random letters and
 * digits, grouped so it can be read out or retyped without mistakes.
 *
 * Letters that are easy to confuse (0/O, 1/l/I) are left out. 20 characters from a 31-symbol
 * alphabet is about 99 bits — far beyond guessing, and it only has to survive until the first sign-in.
 */
const ALPHABET = 'abcdefghjkmnpqrstuvwxyz23456789';

export function temporaryPassword(groups = 4, groupLength = 5): string {
    const bytes = new Uint32Array(groups * groupLength);
    crypto.getRandomValues(bytes);

    const characters = Array.from(bytes, (n) => ALPHABET[n % ALPHABET.length]);
    const parts: string[] = [];
    for (let i = 0; i < groups; i++) {
        parts.push(characters.slice(i * groupLength, (i + 1) * groupLength).join(''));
    }

    return parts.join('-');
}
