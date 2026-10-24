import { createContext, useContext } from 'react';
import { DISCLAIMER } from '../types/domain';
import type { MarketKind, MarketSelection, BetStatus, LiveUnavailable, Verdict } from '../types/domain';
import type { ParseIssue } from './oddsPaste';

/**
 * Interface copy, in Romanian first.
 *
 * Romanian is the default rather than an option bolted on: the odds come off a Romanian bookmaker,
 * and the point of the wording here is to say which number goes in which box. A user who cannot tell
 * "sibling odds" from "the other side of the market" will put the wrong price in and get a confident
 * answer to the wrong question.
 *
 * The dictionary is a plain nested object rather than key lookups, so a missing or misspelled string
 * is a compile error instead of a "shot.title" showing up on screen. `ro` is the source of truth for
 * the shape; `en` has to match it.
 */

export type Lang = 'ro' | 'en';

/**
 * The readout for one reading, with whatever was not read left out rather than shown as a zero.
 *
 * A league the provider has no xG for is an ordinary outcome now, and "xG 0–0" for it would
 * contradict the warning beside it while reading as two teams who have created nothing. Dropping
 * the clause says the same thing without inventing a number — and the warning above it is what
 * explains why the clause is gone.
 */
const readingParts = (parts: ReadonlyArray<string | null>, nothing: string): string => {
    const said = parts.filter((part): part is string => part !== null);
    return said.length === 0 ? nothing : said.join(' · ');
};

const ro = {
    langName: { ro: 'Română', en: 'English' },

    app: {
        title: 'BestBet',
        tagline:
            'Scoate marja din cotă, estimează singur probabilitatea, compară-le. Aplicația doar ' +
            'recomandă; tu decizi și tu plasezi, dacă plasezi.',
        analyse: 'Analizează',
        analysing: 'Se analizează…',
        emptyResult:
            'Completează cota pe care o poți lua, celelalte cote din aceeași piață și xG-ul live. ' +
            'Rezultatul apare aici.',
        genericError: 'Ceva nu a mers.',
        saveError: 'Poziția nu a putut fi salvată.',
        updateError: 'Cota de închidere nu a putut fi actualizată.',
        outcomeError: 'Rezultatul nu a putut fi actualizat.',
        deleteError: 'Poziția nu a putut fi ștearsă.',
        language: 'Limbă',
        disclaimer:
            'Doar sprijin educațional pentru decizii. Nu este consultanță financiară și nu este ' +
            'un îndemn la pariere.',
    },

    common: {
        loading: 'Se încarcă…',
        cancel: 'Renunță',
        close: 'Închide',
        failed: 'Ceva nu a mers.',
    },

    nav: {
        calculator: 'Calculator',
        bets: 'Pariurile mele',
        admin: 'Conturi',
        account: 'Contul meu',
        logout: 'Ieși',
        main: 'Navigare',
    },

    auth: {
        title: 'Intră în BestBet',
        closed: 'Aplicația e privată: conturile le creează administratorul.',
        email: 'Email',
        password: 'Parolă',
        rememberMe: 'Ține-mă minte pe acest dispozitiv',
        submit: 'Intră',
        submitting: 'Se verifică…',
        // The same words whatever went wrong, as on the server: the page must not reveal whether an
        // email has an account.
        failed: 'Email sau parolă greșită, ori contul e blocat temporar după prea multe încercări.',
        tooMany: 'Prea multe încercări de pe această adresă. Așteaptă un minut și încearcă din nou.',
        checking: 'Se verifică sesiunea…',
        unreachable: 'Serverul nu răspunde. Pornește backend-ul și baza de date, apoi reîncearcă.',
        retry: 'Reîncearcă',
    },

    account: {
        title: 'Contul meu',
        signedInAs: (email: string) => `Ești conectat ca ${email}.`,
        forced:
            'Contul tău are o parolă temporară, primită de la administrator. Alege una pe care doar tu ' +
            'o știi — până atunci restul aplicației nu răspunde.',
        current: 'Parola actuală',
        next: 'Parola nouă',
        confirm: 'Repetă parola nouă',
        rule: 'Cel puțin 12 caractere. O propoziție scurtă e mai sigură și mai ușor de ținut minte decât un cuvânt cu simboluri.',
        submit: 'Schimbă parola',
        submitting: 'Se salvează…',
        saved: 'Parola a fost schimbată. Celelalte sesiuni ale tale se închid în cel mult un minut.',
        mismatch: 'Cele două parole noi nu sunt la fel.',
    },

    bets: {
        title: 'Pariurile mele',
        open: (count: number) => `Deschise (${count})`,
        closed: (count: number) => `Închise (${count})`,
        add: 'Adaugă pariu',
        emptyOpen: 'Niciun pariu deschis.',
        emptyClosed: 'Niciun pariu închis încă.',
        loadError: 'Pariurile nu au putut fi încărcate.',
        columns: {
            placed: 'Plasat',
            event: 'Eveniment',
            market: 'Piață',
            bookmaker: 'Casă',
            odds: 'Cotă',
            stake: 'Miză',
            probability: 'Prob. estimată',
            edge: 'Avantaj',
            status: 'Status',
            payout: 'Încasat',
            profit: 'Profit',
            actions: 'Acțiuni',
        },
        settle: 'Închide',
        edit: 'Editează',
        delete: 'Șterge',
        reopen: 'Redeschide',
        settleTitle: (label: string) => `Închide pariul: ${label}`,
        result: 'Rezultat',
        cashOut: 'Suma încasată la cash-out',
        closingOdds: 'Cota de închidere (opțional)',
        preview: (payout: string, profit: string) => `Încasezi ${payout}, adică ${profit}.`,
        previewNeedsAmount: 'Scrie suma încasată ca să vezi rezultatul.',
        confirmSettle: 'Închide pariul',
        confirmDelete: (label: string) =>
            `Ștergi pariul „${label}”? Dispare și din statistici și din balanță, și nu se mai poate recupera.`,
        confirmDeleteButton: 'Da, șterge',
        actionFailed: 'Operația nu a reușit.',
        form: {
            titleAdd: 'Adaugă un pariu',
            titleEdit: 'Editează pariul',
            titleSave: 'Salvează ca pariu',
            fromAnalysis: (selection: string, odds: string, probability: string, edge: string) =>
                `Din analiză: ${selection} la ${odds}, probabilitate estimată ${probability}, avantaj ${edge}.`,
            match: 'Eveniment',
            matchPlaceholder: 'Echipa A - Echipa B',
            sport: 'Sport',
            competition: 'Competiție',
            kickoff: 'Început',
            marketKind: 'Tip de piață',
            marketLabel: 'Piața, cum o scrie casa',
            selection: 'Selecția',
            selectionPlaceholder: 'Over 2.5',
            bookmaker: 'Casa de pariuri',
            odds: 'Cota luată',
            otherOdds: 'Celelalte cote din piață',
            otherOddsHint: 'Opțional. Cu ele se scoate marja casei; fără ele avantajul include marja.',
            stake: 'Miza',
            probability: 'Probabilitatea ta (%)',
            probabilityHint: 'Opțional. Fără ea avantajul e zero, nu inventat.',
            closingOdds: 'Cota de închidere',
            notes: 'Note',
            save: 'Salvează',
            saving: 'Se salvează…',
        },
        errors: {
            match: 'Scrie evenimentul.',
            odds: 'Cota trebuie să fie mai mare decât 1.',
            otherOdds: 'Fiecare cotă din piață trebuie să fie mai mare decât 1.',
            stake: 'Miza trebuie să fie un număr, cel puțin 0.',
            probability: 'Probabilitatea e între 0 și 100.',
            closingOdds: 'Cota de închidere trebuie să fie mai mare decât 1.',
        } as Record<'match' | 'odds' | 'otherOdds' | 'stake' | 'probability' | 'closingOdds', string>,
        stats: {
            title: 'Rezumat',
            bets: 'Pariuri',
            open: 'Deschise',
            openStake: 'În joc',
            staked: 'Mizat (închise)',
            profit: 'Profit',
            yield: 'Yield',
            yieldHint: 'Profit împărțit la suma mizată pe pariurile închise.',
            roi: 'ROI',
            roiHint: 'Profit împărțit la banii depuși, minus retragerile.',
            beatClose: 'Bate cota de închidere',
            none: '—',
        },
    },

    bankroll: {
        label: 'Capital (bankroll)',
        note:
            'Pornește de la balanța din contul tău. O poți schimba aici doar pentru calculul ăsta; ' +
            'balanța reală se ține din „Pariurile mele”.',
        account: (balance: string) => `Balanța din cont: ${balance}`,
        overridden: 'Folosești altă sumă decât balanța, doar pentru acest calcul.',
        useAccount: 'Folosește balanța',
        noStart: 'Nu ai setat încă balanța inițială. O setezi în „Pariurile mele”, cu o depunere.',
        title: 'Balanța',
        balance: 'Balanța',
        balanceHint: 'Depuneri minus retrageri plus profitul pariurilor închise. Mizele se calculează din ea.',
        available: 'Disponibil',
        availableHint: 'Balanța minus ce e în joc pe pariuri deschise.',
        deposits: 'Depuneri',
        withdrawals: 'Retrageri',
        settled: 'Profit din pariuri',
        kind: 'Tip',
        kinds: { Deposit: 'Depunere', Withdrawal: 'Retragere' } as Record<'Deposit' | 'Withdrawal', string>,
        amount: 'Suma',
        movementNote: 'Notă (opțional)',
        startingNote: 'Sold inițial',
        add: 'Adaugă',
        adding: 'Se adaugă…',
        history: 'Istoric',
        empty: 'Nicio mișcare încă. Prima depunere e balanța ta inițială.',
        confirmDelete: (what: string) => `Ștergi mișcarea „${what}”? Balanța se recalculează fără ea.`,
        amountError: 'Suma trebuie să fie un număr mai mare decât 0.',
    },

    admin: {
        title: 'Conturi',
        intro:
            'Nu există înregistrare publică: un cont există doar dacă îl creezi tu aici. Parola dată e ' +
            'temporară — omul trebuie să o schimbe la prima intrare, deci tu nu știi nicio parolă folosită.',
        create: 'Cont nou',
        email: 'Email',
        tempPassword: 'Parolă temporară',
        generate: 'Generează',
        createSubmit: 'Creează contul',
        created: (email: string, password: string) =>
            `Contul ${email} a fost creat. Parola temporară: ${password} — trimite-o pe un canal sigur; nu va mai fi afișată.`,
        columns: { email: 'Email', role: 'Rol', state: 'Stare', created: 'Creat', actions: 'Acțiuni' },
        roleAdmin: 'Administrator',
        roleUser: 'Utilizator',
        stateActive: 'Activ',
        stateDisabled: 'Dezactivat',
        stateLocked: 'Blocat temporar',
        stateMustChange: 'Parolă temporară',
        disable: 'Dezactivează',
        enable: 'Activează',
        reset: 'Resetează parola',
        confirmDisable: (email: string) =>
            `Dezactivezi contul ${email}? Nu mai poate intra, iar sesiunile deschise se închid în cel mult un minut.`,
        confirmReset: (email: string) =>
            `Resetezi parola pentru ${email}? Primește o parolă temporară nouă și sesiunile lui se închid.`,
        resetDone: (email: string, password: string) =>
            `Parola temporară nouă pentru ${email}: ${password} — nu va mai fi afișată.`,
        you: 'tu',
    },

    market: {
        title: 'Piața',
        match: 'Meci',
        matchPlaceholder: 'Sunderland - Arsenal',
        kind: 'Tip de pariu',
        side: 'Selecție',
        line: 'Linie',
        bookLabel: 'Eticheta casei',
        optional: '(opțional)',
        bookLabelPlaceholder: 'Sub 4',
        decimalOdds: 'Cotă zecimală',
        otherPrices: 'Celelalte cote din aceeași piață',
        note:
            'Fără ele nu se poate afla câtă marjă a pus casa în cota pe care o privești, deci ' +
            'eliminarea marjei — și tot ce urmează după ea — ar fi pe ghicite.',
        price: (index: number) => `Cota ${index}`,
    },

    marketKind: {
        Moneyline1X2: 'Rezultat final (1X2)',
        TotalGoals: 'Total goluri (Sub / Peste)',
        BothTeamsToScore: 'Ambele echipe înscriu (GG/NG)',
        NextGoal: 'Următorul gol',
        Handicap: 'Handicap',
    } as Record<MarketKind, string>,

    /** Short enough for a table column, where the full name wraps onto four lines. */
    marketKindShort: {
        Moneyline1X2: '1X2',
        TotalGoals: 'Total goluri',
        BothTeamsToScore: 'GG/NG',
        NextGoal: 'Următorul gol',
        Handicap: 'Handicap',
    } as Record<MarketKind, string>,

    marketKindHint: {
        Moneyline1X2: 'Celelalte două cote din această piață',
        TotalGoals: 'Cealaltă parte a aceleiași linii',
        BothTeamsToScore: 'Cealaltă parte a aceleiași piețe',
        NextGoal: 'Celelalte două cote din această piață',
        Handicap: 'Cealaltă parte a aceluiași handicap',
    } as Record<MarketKind, string>,

    selection: {
        Home: 'Gazde (1)',
        Draw: 'Egal (X)',
        Away: 'Oaspeți (2)',
        Over: 'Peste',
        Under: 'Sub',
        Yes: 'Da (GG)',
        No: 'Nu (NG)',
        NextGoalHome: 'Gazdele (1)',
        NextGoalAway: 'Oaspeții (2)',
        NextGoalNone: 'Niciun alt gol (0)',
        HandicapHome: 'Gazde',
        HandicapAway: 'Oaspeți',
    } as Record<MarketSelection, string>,

    parse: {
        incomplete: (title: string, found: number, required: number) =>
            `${title}: s-au găsit ${found} din cele ${required} cote de care are nevoie piața, deci ` +
            'marja casei nu poate fi scoasă din ea. Include piața întreagă, cu ambele părți.',
        headingWithoutPrices: (title: string) =>
            `S-a recunoscut titlul „${title}”, dar nicio cotă sub el.`,
    },

    reference: {
        open: 'Adaugă o linie de referință (Polymarket) ›',
        openNote:
            'A doua opinie despre probabilitate, de pe o piață fără marjă. Avantajul se măsoară față ' +
            'de ea; miza rămâne calculată la cota ta.',
        title: 'Linie de referință',
        hide: 'Ascunde',
        note:
            'O casă de pariuri pune 5–15% marjă în cote, iar linia ei „corectă" este pe atât de ' +
            'aproximativă. Pe Polymarket se tranzacționează direct probabilități: prețurile se adună ' +
            'la ~100%, deci nu e nimic de scos din ele. Când există o referință, avantajul se ' +
            'măsoară față de ea, iar EV-ul și miza rămân la cota ta — singura care te plătește.',
        privacy:
            'Textul căutării pleacă de pe calculator către Polymarket. Nimic altceva nu pleacă: nici ' +
            'capitalul, nici miza, nici jurnalul, nici capturile.',
        searchLabel: 'Caută meciul, echipa sau competiția',
        searchPlaceholder: 'Arsenal',
        search: 'Caută',
        searching: 'Se caută…',
        noResults:
            'Nicio piață potrivită. Polymarket acoperă mai ales meciurile mari și pariurile pe ' +
            'sezon — pentru un meci obișnuit de ligă adesea nu există nimic.',
        volume: (value: string) => `volum ${value}`,
        pick: 'Alege partea care corespunde pariului tău',
        mismatch:
            'Verifică tu că e vorba de același lucru. Aplicația nu are cum să știe dacă „Peste 2.5 ' +
            'goluri" de la casa ta și întrebarea de pe Polymarket înseamnă aceeași piață, iar o ' +
            'referință greșită mută avantajul exact în direcția în care te-ai înșela.',
        active: (source: string) => `Referință activă: ${source}`,
        remove: 'Renunță la referință',
        openMarket: 'Vezi pe Polymarket',
        failed: 'Referința nu a putut fi adusă.',
        offlineNote:
            'Dacă Polymarket nu răspunde, restul aplicației merge neschimbat — referința e un plus, ' +
            'nu o condiție.',
    },

    reprice: {
        action: 'Recalculează',
        title: 'Recalculat pentru minutul curent',
        probabilityNow: 'Șansa de câștig acum',
        atEntry: 'La intrare',
        change: 'Diferență',
        evNow: 'EV la cota ta',
        evAmount: 'EV pe miza plasată',
        fairNow: 'Cotă corectă acum',
        fairNowHint: 'Cu ea compari un cash-out sau o acoperire: sub ea, oferta e în dezavantajul tău.',
        decided:
            'Pariul e deja decis de ce s-a întâmplat pe teren — nu mai există o cotă corectă de ' +
            'comparat.',
        note:
            'Nu se salvează nimic. Rândul păstrează datele de la intrare, care sunt dovada că ' +
            'decizia a fost rezonabilă atunci — poți recalcula ori de câte ori se mișcă meciul.',
        needsLive:
            'Recalcularea folosește starea meciului din formularul de sus. Bifează „Meciul este în ' +
            'desfășurare", completează minutul și xG-ul, apoi apasă din nou.',
        close: 'Închide',
        failed: 'Poziția nu a putut fi recalculată.',
    },

    prematch: {
        open: 'Calculează din echipe și jucători (pre-meci) ›',
        openNote:
            'Probabilitate pentru un meci care nu a început: forma echipelor în acest sezon și ' +
            'cine joacă, în loc de o cifră scrisă de mână.',
        title: 'Pre-meci: echipe și jucători',
        hide: 'Ascunde',
        intro:
            'Din clasamentul competiției se iau golurile marcate și primite de fiecare echipă, acasă ' +
            'și în deplasare, față de media ligii. Din statisticile jucătorilor se vede cine aduce ' +
            'golurile: dacă un golgheter lipsește, echipa pierde o parte din ele. Rezultatul intră în ' +
            'analiza obișnuită, așa că verdictul și miza se calculează ca până acum.',
        notConfigured:
            'Funcția are nevoie de o cheie gratuită de la API-Football (100 de cereri pe zi). O ' +
            'creezi pe dashboard.api-football.com, apoi o pui în ApiFootball__Key (fișierul .env ' +
            'pentru Docker, sau „dotnet user-secrets set ApiFootball:Key …"), și repornești ' +
            'aplicația. Detalii în GHID_RULARE.md.',
        privacy:
            'Către API-Football pleacă doar competiția, sezonul și echipele. Cheia rămâne pe ' +
            'server. Nu pleacă: capitalul, miza, cotele sau jurnalul.',
        league: 'Competiția',
        home: 'Gazde',
        away: 'Oaspeți',
        homePlaceholder: 'Manchester City',
        awayPlaceholder: 'Arsenal',
        run: 'Calculează',
        running: 'Se citesc datele…',
        failed: 'Calculul pre-meci nu a putut fi făcut.',
        season: (year: number, name: string) =>
            `${name} ${year}/${String((year + 1) % 100).padStart(2, '0')}`,
        expectedGoals: 'Goluri așteptate',
        beforeAbsences: (value: string) => `înainte de absențe: ${value}`,
        missing: (percent: string) => `lipsesc ${percent} din golurile echipei`,
        homeWin: 'Victorie gazde',
        draw: 'Egal',
        awayWin: 'Victorie oaspeți',
        btts: 'Ambele marchează',
        over: (line: string) => `Peste ${line}`,
        form: 'Forma în sezon',
        formLine: (gf: string, ga: string, games: number) =>
            `${gf} marcate / ${ga} primite pe meci (${games} meciuri)`,
        formHome: 'acasă',
        formAway: 'în deplasare',
        scorers: 'Cine poate marca',
        player: 'Jucător',
        pos: 'Poz.',
        goals: 'Goluri',
        shots: 'Șuturi',
        minutes: 'Min.',
        chance: 'Șansă',
        fairOdds: 'Cotă corectă',
        yourOdds: 'Cota casei',
        verdict: 'Merită?',
        absent: 'Lipsește',
        absentHint:
            'Bifează un jucător care nu joacă (accidentat, suspendat, odihnit); calculul se reface.',
        worth: 'Merită',
        borderline: 'La limită',
        notWorth: 'Nu merită',
        edgeOf: (pp: string) => `avantaj ${pp}`,
        scorerNote:
            'Jucătorii sunt cei din lotul sezonului, nu din echipa de start: confirmă formația ' +
            'înainte de miză. „Merită" compară șansa estimată cu cota ta, fără să scoată marja casei, ' +
            'deci e un test strict.',
        apply: 'Folosește în analiză',
        applied: 'Folosit în analiza de mai jos',
        applyNote:
            'Analiza de mai jos va calcula șansa pieței alese (1X2, Peste/Sub, GG) din aceste goluri ' +
            'așteptate. Funcționează pentru un meci care nu a început: debifează „Meciul este în ' +
            'desfășurare".',
        remove: 'Renunță la model',
        limits:
            'Nu știe nimic despre tactică, motivație, vreme sau știri apărute după citirea datelor, ' +
            'și tratează golurile celor două echipe ca independente. E o estimare de pornire mai ' +
            'bună decât una scrisă de mână, nu un preț.',
    },

    monitor: {
        open: 'Monitorizează meciul în timp real ›',
        openNote:
            'Pariurile deschise se recalculează singure, pe măsură ce meciul merge mai departe. ' +
            'Lipești linkul meciului și nu mai ai nimic de făcut.',
        title: 'Monitorizare în timp real',
        hide: 'Ascunde',
        why:
            'Lipește linkul meciului de pe FotMob sau de pe Sofascore, apasă o dată, și aplicația ' +
            'ia singură minutul, scorul și xG-ul la fiecare câteva secunde. Nu trebuie să lași ' +
            'nicio filă deschisă.',

        // --- The link, which is the way in for anything FotMob covers -------
        linkLabel: 'Link de meci sau ID',
        linkPlaceholder: 'https://www.fotmob.com/match/5881191',
        linkHint:
            'Merge linkul de pe FotMob (de acolo vin datele) și linkul de pe Sofascore (meciul e ' +
            'căutat pe FotMob după numele echipelor). Merge și doar ID-ul de meci de pe FotMob.',
        linkStart: 'Urmărește meciul',
        linkStop: 'Oprește',
        linkInvalid:
            'Nu pare un link de meci. Copiază adresa din bara browserului, de pe pagina meciului de ' +
            'pe FotMob sau de pe Sofascore.',
        linkWorking: 'Se caută meciul…',
        linkFound: (provider: string, label: string) => `${label} · citit de pe ${provider}`,
        linkNoXg:
            'Meciul a fost găsit, dar furnizorul nu calculează xG pentru liga asta — iar fără xG ' +
            'modelul nu are pe ce să ruleze. Pentru meciul ăsta folosește semnul de carte de mai ' +
            'jos sau lipește statisticile de mână.',
        linkFinished:
            'Meciul s-a terminat. Citirile nu se mai schimbă; închide monitorizarea sau pune alt meci.',
        linkNotStarted:
            'Meciul nu a început încă. Nu există minut, scor sau xG de citit — revino după fluier.',
        linkStale:
            'Furnizorul nu a mai răspuns de o vreme. Numerele de mai jos sunt dintr-un minut care ' +
            'a trecut.',

        // --- The bookmarklet, which stays the only way in for the rest ------
        fallback: 'Nu merge prin link? Monitorizare din fila ta de Sofascore ›',
        fallbackWhy:
            'Sofascore refuză orice cerere care nu vine dintr-un browser real — răspunde 403 inclusiv ' +
            'la robots.txt — deci serverul acestei aplicații nu are cum să întrebe singur, iar FotMob ' +
            'nu are xG la toate ligile. Browserul tău are voie, fiindcă acolo pagina e deja deschisă. ' +
            'Semnul de carte de mai jos citește pagina pe care o ai în față și o trimite încoace; nu ' +
            'se cere nimic în plus de la ei.',
        step1: 'Trage semnul ăsta în bara de favorite (sau copiază-l și fă-ți un favorit cu el):',
        bookmarkName: 'BestBet: monitorizează',
        copy: 'Copiază',
        copied: 'Copiat',
        step2:
            'Deschide meciul pe Sofascore, mergi la fila Statistici, și apasă pe semnul din favorite. ' +
            'Apasă-l din nou ca să oprești.',
        step3: (seconds: number) =>
            `Lasă fila deschisă. Trimite o citire la fiecare ${seconds} de secunde, iar pariurile ` +
            'deschise din acel meci se recalculează singure.',
        watching: 'Monitorizare activă',
        waiting: 'Se așteaptă prima citire…',
        lastReading: (seconds: number) =>
            seconds < 60
                ? `Ultima citire acum ${seconds} s`
                : `Ultima citire acum ${Math.round(seconds / 60)} min`,
        reading: (
            minute: number | null,
            home: number | null,
            away: number | null,
            xgHome: number | null,
            xgAway: number | null,
        ) =>
            readingParts(
                [
                    minute === null ? null : `minutul ${minute}`,
                    home === null || away === null ? null : `${home}–${away}`,
                    xgHome === null || xgAway === null ? null : `xG ${xgHome}–${xgAway}`,
                ],
                'nimic de citit în citirea asta',
            ),
        unknownMatch: 'meci nerecunoscut',
        unusable:
            'Citirea a ajuns, dar nu conține tot ce trebuie. Pe Sofascore mergi la fila Statistici ' +
            'și lasă filele pe „All" — de acolo vine xG-ul.',
        stale:
            'Citirea e veche. Fila s-a închis, a adormit, sau ai oprit semnul din favorite. Până ' +
            'revine, numerele de mai jos sunt dintr-un minut care a trecut.',
        tracking: (labels: string) => `Se recalculează: ${labels}`,
        // Says nothing about the form on purpose: a reading without xG does not reach it, and this
        // line sits right under the warning that explains why.
        nothingTracked: 'Niciun pariu deschis din meciul ăsta, deci nu e nimic de recalculat.',
        clear: 'Uită citirea',
        privacy:
            'Prin semnul de carte, citirea circulă doar între browserul tău și aplicația de pe ' +
            'calculatorul tău. Prin link, din calculator pleacă doar ID-ul meciului, către furnizor — ' +
            'nimic altceva: nici banca, nici miza, nici pozițiile salvate. Oricum, citirea nu se ' +
            'salvează nicăieri și dispare când oprești aplicația.',
        failed: 'Citirea nu a putut fi luată.',
    },

    slip: {
        open: 'Importă un pariu deja plasat',
        openNote:
            'Din biletul de la casa de pariuri: cota, miza și scorul se citesc singure, iar apoi ' +
            'îl urmărești cu datele meciului.',
        title: 'Importă un pariu din bilet',
        hide: 'Ascunde',
        intro:
            'Din aplicația casei de pariuri nu se poate copia nimic, deci biletul intră printr-o ' +
            'captură (sau prin textul lui, dacă îl ai). Citirea se face în browser; nu pleacă ' +
            'nimic din calculator. Verifică câmpurile de mai jos înainte să salvezi — o cifră ' +
            'citită greșit înseamnă un pariu urmărit cu alte numere decât cele pe care ai mizat.',
        dropHere: 'Trage aici captura biletului sau',
        dropActive: 'Dă drumul ca să citesc biletul',
        chooseFile: 'alege un fișier',
        notAnImage: 'Fișierul nu este o imagine.',
        reading: (percent: number) => `Se citește biletul… ${percent}%`,
        loadingEngine: 'Se încarcă motorul de citire (doar prima dată)…',
        failed: 'Biletul nu a putut fi citit din imagine.',
        textLabel: 'Textul biletului',
        textPlaceholder: 'Sub 2 2.75\nTotal\nAtletico Madrid 1\nManchester United 0\nSuma pariată: 2.00',
        textNote:
            'Aici apare ce a citit motorul de pe imagine. Îl poți corecta sau înlocui cu textul ' +
            'biletului, apoi apeși „Citește biletul”.',
        read: 'Citește biletul',
        clear: 'Golește',
        formTitle: 'Verifică pariul',
        match: 'Meci',
        matchPlaceholder: 'Atletico Madrid - Manchester United',
        kind: 'Piața',
        side: 'Partea',
        line: 'Linia',
        bookLabel: 'Cum scrie pe bilet',
        odds: 'Cota',
        stake: 'Miza',
        minute: 'Minutul',
        scoreHome: 'Gol gazde',
        scoreAway: 'Gol oaspeți',
        xgHome: 'xG gazde',
        xgAway: 'xG oaspeți',
        stateTitle: 'Starea meciului când ai pariat',
        stateNote:
            'Biletul are minutul și scorul, dar nu și xG-ul. Fără xG nu se poate calcula ce șansă ' +
            'avea pariul la intrare — nu e o problemă: din „Pariuri deschise” introduci starea ' +
            'actuală oricând și vezi șansa de acum.',
        save: 'Salvează ca pariu deschis',
        saving: 'Se salvează…',
        saved: 'Salvat. Îl găsești mai jos, la „Pariuri deschise”.',
        saveFailed: 'Pariul nu a putut fi salvat.',
        issues: {
            'no-odds': 'Nu am găsit cota. Completeaz-o tu.',
            'no-stake': 'Nu am găsit miza. Completeaz-o tu.',
            'no-market':
                'Nu am recunoscut piața. Aplicația citește automat doar totalurile (Sub/Peste); ' +
                'alege piața și partea de mai jos.',
            'no-match': 'Nu am găsit echipele și scorul. Completează-le tu, dacă vrei să-l urmărești cu scorul real.',
            'no-minute': 'Nu am găsit minutul. Completează-l dacă pariul e live.',
            'payout-mismatch':
                'Câștigul de pe bilet nu iese din cotă × miză. O cifră a fost citită greșit — ' +
                'verifică cota și miza.',
        } as Record<string, string>,
        problems: {
            odds: 'Cota trebuie să fie mai mare decât 1.00.',
            stake: 'Completează miza (poate fi 0).',
            line: 'Completează linia, de ex. 2.5.',
            minute: 'Minutul trebuie să fie între 1 și 130, și e necesar dacă dai xG.',
            score: 'Golurile trebuie să fie numere întregi, 0 sau mai mult.',
            xg: 'Completează xG-ul pentru ambele echipe, ca numere de 0 sau mai mult.',
        } as Record<string, string>,
    },

    track: {
        title: 'Pariuri deschise',
        intro:
            'Introdu ultimele date ale meciului și vezi imediat ce șansă are pariul să câștige. ' +
            'Datele rămân pe pariu, iar fiecare citire se păstrează în istoric, ca să vezi cum se ' +
            'mișcă șansa pe parcursul meciului.',
        empty:
            'Niciun pariu deschis. Importă unul din bilet sau salvează o analiză, și apare aici.',
        minute: 'Minut',
        goalsHome: 'Gol gazde',
        goalsAway: 'Gol oaspeți',
        xgHome: 'xG gazde',
        xgAway: 'xG oaspeți',
        fromForm: 'Ia datele din formularul de sus',
        fromFormNote: 'Formularul de sus nu are încă un minut și xG completate.',
        needState: 'Completează minutul și xG-ul ambelor echipe ca să vezi șansa pariului.',
        updating: 'Se calculează…',
        failed: 'Șansa nu a putut fi calculată.',
        chanceWin: 'Șansa de câștig',
        chancePush: 'Miza înapoi',
        chanceLose: 'Șansa de pierdere',
        pushNote: (line: string) =>
            `Pe linia întreagă ${line}, exact ${line} goluri returnează miza. De aceea cele trei ` +
            'șanse adună 100%, iar „Șansa de câștig” e cea reală, cu miza înapoi tratată separat.',
        decidedWon: 'Scorul a decis pariul: este câștigat, orice s-ar întâmpla până la final.',
        decidedLost: 'Scorul a decis pariul: este pierdut, orice s-ar întâmpla până la final. Îl poți marca mai sus.',
        evNow: 'EV la cota ta',
        evAmount: 'EV pe miza plasată',
        ifWin: 'Dacă câștigă',
        ifLose: 'Dacă pierde',
        fairNow: 'Cotă corectă acum',
        stake: 'Miza',
        odds: 'Cota',
        atEntry: 'La intrare',
        entryUnknown: 'necunoscută',
        history: 'Istoric',
        historyNote: (count: number) => `${count} citiri · cea mai nouă prima`,
        clearHistory: 'Șterge istoricul',
        settle: 'Închide pariul',
        note:
            'Șansa vine din modelul Poisson pe xG și ritmul meciului. E o estimare, nu o certitudine: ' +
            'se mișcă cu fiecare gol și cu fiecare xG nou introdus. Nu se schimbă nimic în jurnal.',
    },

    live: {
        title: 'Starea meciului',
        isLive: 'Meciul este în desfășurare',
        note:
            'Citește-le direct de pe ecranul de statistici. Modelul transformă xG-ul acumulat ' +
            'până acum într-un ritm pe minut și duce ritmul mai departe peste minutele rămase.',
        minute: 'Minut',
        xgHome: 'xG gazde',
        xgAway: 'xG oaspeți',
        goalsHome: 'Goluri gazde',
        goalsAway: 'Goluri oaspeți',
        dynamics: 'Dinamica meciului',
        dynamicsHint: ' — cum este dus ritmul mai departe',
        correct: 'Corectează ritmul și starea scorului',
        correctedNote:
            'Un ritm citit din câteva minute este o estimare, nu un fapt, așa că este tras spre ce ' +
            'produc de obicei cele două echipe — cifrele din panoul pre-meci, dacă le folosești, altfel ' +
            'un meci mediu de ligă — și ajustat pentru echipa care apără un avantaj. Oprește opțiunea ' +
            'ca să vezi extrapolarea brută.',
        rawNote:
            'Extrapolare brută: ritmul de pe ceas este dus liniar până la final. O primă repriză ' +
            'intensă proiectează atunci un scor care aproape niciodată nu se întâmplă.',
        windowNote:
            'Cel mai valoros câmp de aici: xG-ul de acum, nu media meciului. Când urmărești meciul ' +
            'prin link se completează singur din șuturile FotMob, pentru ultimele 20 de minute. Altfel ' +
            'e opțional: ia repriza a doua ca tot meciul minus prima repriză, iar modelul va cântări ' +
            'ce face jocul acum mai mult decât media lui.',
        windowMinutes: 'Fereastra recentă (min)',
        recentXgHome: 'xG gazde în ea',
        recentXgAway: 'xG oaspeți în ea',
        stoppage: 'Prelungiri și eliminări',
        stoppageHint: ' — completate singure când urmărești meciul prin link',
        stoppageNote:
            'Tabla arbitrului de rezervă spune câte minute se mai joacă. Până apare, modelul ' +
            'numără prelungirile obișnuite (3 min în prima repriză, 4,5 în a doua), iar un ' +
            'cartonaș roșu scade ritmul echipei rămase în zece și îl crește pe al celeilalte.',
        period: 'Repriza',
        periodAuto: 'După minut',
        periodFirst: 'Repriza 1',
        periodHalfTime: 'Pauză',
        periodSecond: 'Repriza 2',
        addedFirst: 'Tabla la 45 (+min)',
        addedSecond: 'Tabla la 90 (+min)',
        redHome: 'Roșii gazde',
        redAway: 'Roșii oaspeți',
        redMinute: 'Minutul ultimului roșu',
        preMatchNote:
            'Înainte de start nu există date live din care să iasă o probabilitate, deci estimarea ' +
            'trebuie să fie a ta. Tot ce urmează — avantajul, miza, verdictul — valorează exact cât ' +
            'valorează acest singur număr.',
        yourProbability: 'Probabilitatea ta (%)',
    },

    chart: {
        title: 'Pariurile live, minut cu minut',
        intro:
            'Șansa de câștig a fiecărui pariu deschis, recalculată la fiecare citire a meciului. ' +
            'Linia punctată e pragul cotei luate: deasupra ei pariul valorează mai mult decât a costat, ' +
            'sub ea mai puțin — linia cu care se compară o ofertă de cash-out. Benzile gri sunt ' +
            'prelungirile, cât de late le-a anunțat arbitrul.',
        empty: 'Încă nicio citire pentru pariul ăsta.',
        summary: (now: string, minute: string, entry: string | null, threshold: string) =>
            `Acum ${now} (${minute})` +
            (entry === null ? '' : `, la intrare ${entry}`) +
            `; pragul cotei e ${threshold}.`,
        breakEven: (value: string) => `prag cotă ${value}`,
        keyWin: 'șansa de câștig',
        keyEntry: (value: string) => `la intrare ${value}`,
        keyRed: 'cartonaș roșu',
        win: 'câștig',
        push: 'miză returnată',
        halfTime: 'Pauză',
        reds: (home: number, away: number) => `roșii ${home}-${away}`,
        left: (minutes: number) => `mai sunt ~${minutes} min de joc`,
        table: (count: number) => `Toate citirile (${count})`,
        minute: 'Minut',
        score: 'Scor',
        remaining: 'Min. rămase',
        clear: 'Șterge istoricul',
        auto:
            'Monitorizarea e activă: fiecare pariu deschis se recalculează singur în fiecare minut, ' +
            'din propriul meci găsit pe FotMob după numele echipelor. Meciul urmărit prin link se ' +
            'recalculează la fiecare citire.',
        paused:
            'Monitorizarea e oprită, deci pariurile nu se mai actualizează singure. Pornește-o din ' +
            'panoul de monitorizare ca fiecare pariu deschis să se recalculeze în fiecare minut.',
        notYet: 'Se caută meciul…',
        updated: (seconds: number) => `Actualizat acum ${seconds} s`,
        from: (provider: string) => `din ${provider}:`,
        foundAt: (provider: string) => `Meci găsit la ${provider}:`,
        next: (seconds: number) => `următoarea recalculare în ${seconds} s`,
        failed: (message: string) => `Recalcularea nu a mers: ${message} Se reîncearcă în 2 minute.`,
        unavailable: {
            'not-found':
                'Meciul nu a fost găsit la FotMob după numele echipelor din pariu. Se reîncearcă din ' +
                '5 în 5 minute; ca să-l urmărești acum, pune linkul meciului în panoul de monitorizare.',
            'not-started': 'Meciul nu a început încă. Se verifică din 5 în 5 minute.',
            'no-xg':
                'FotMob nu are xG pentru liga asta, deci pariul nu se poate recalcula de aici. ' +
                'Urmărește meciul cu semnul de carte, de pe Sofascore.',
            finished: 'Meciul s-a terminat. Decontează pariul în jurnal sau în Pariurile mele.',
        } as Record<LiveUnavailable, string>,
    },

    result: {
        title: 'Rezultat',
        fair: 'Corectă (fără marjă)',
        yourEstimate: 'Estimarea ta',
        edge: 'Avantaj (edge)',
        ev: 'EV pe unitate',
        stake: 'Miză sugerată',
        paceDown: 'Ritm redus la',
        paceUp: 'Ritm crescut la',
        paceHint: (raw: string, adjusted: string) => `${raw} → ${adjusted} xG la 90 de minute, ambele echipe`,
        pacePriorTeams: (home: string, away: string) =>
            `Tras spre cifrele pre-meci ale echipelor: ${home} gazde, ${away} oaspeți, pe meci`,
        pacePriorLeague: (value: string) =>
            `Tras spre o medie de ligă de ${value} pe echipă — panoul pre-meci dă o bază mai bună, ` +
            'din echipele care chiar joacă',
        overtaken: (minute: string) =>
            `Calculat la minutul ${minute}, iar meciul a mers mai departe. Apasă Analizează din nou ` +
            'ca rezultatul să fie pe minutul curent.',
        referenceTitle: 'Linie de referință',
        referenceFair: (source: string) => `Corectă după ${source}`,
        bookFair: 'Corectă după casă',
        disagreement: 'Diferență',
        referenceNote: (source: string) =>
            `Avantajul de mai sus este măsurat față de ${source}. EV-ul și miza rămân calculate la ` +
            'cota ta, fiindcă e singura care te plătește.',
        referenceOverround: (value: string) =>
            `Marja referinței: ${value}. Aproape de zero înseamnă o piață fără marjă de scos.`,
        mismatch:
            'Avantajul și EV-ul se contrazic aici, și nu e o eroare. Avantajul se măsoară față de ' +
            'linia corectă, fără marjă; EV-ul se măsoară față de cota afișată efectiv. Avantaj ' +
            'pozitiv cu EV negativ înseamnă că marja casei este mai lată decât avantajul tău — ai ' +
            'lua pariul la o cotă corectă, iar o cotă corectă nu ți se oferă.',
        working: 'Cum s-a ajuns la acest număr',
        method: 'Metodă',
        overround: (value: string) => `Marja casei: ${value} peste o piață fără marjă`,
        resolvedSelection: 'Selecție interpretată',
        minutes: 'Minute peste care s-a extrapolat',
        xgRate: (home: string, away: string) => `Ritm xG: ${home} gazde / ${away} oaspeți pe minut`,
        expectedRemaining: (home: string, away: string) =>
            `Goluri așteptate în continuare: ${home} gazde / ${away} oaspeți`,
        flatProjection: (home: string, away: string) =>
            `Proiecție liniară la 90: ${home} gazde / ${away} oaspeți xG`,
        push: (push: string, raw: string) =>
            `Egalitate (miza returnată): ${push} — probabilitatea brută de câștig înainte de ` +
            `ajustarea pentru egalitate este ${raw}`,
        save: 'Salvează ca pariu…',
        saved: 'Salvat în pariurile tale',
        saving: 'Se salvează…',
        saveHint:
            'Deschide formularul cu cota, probabilitatea și avantajul deja completate; adaugi miza ' +
            'pusă, casa și competiția. Cota de intrare rămâne, ca să afli mai târziu dacă procesul ' +
            'chiar bate piața.',
    },

    /** The short codes the server returns for how a stake was sized. Anything else is shown raw. */
    capReason: {
        'no-positive-edge': 'fără avantaj pozitiv',
        'skip-below-threshold': 'sub prag — fără miză',
        'floor-3pct': 'prag minim de 3% din capital',
        'cap-5pct': 'plafon de 5% din capital',
        'quarter-kelly': 'un sfert de Kelly',
    } as Record<string, string>,

    method: {
        'poisson-live-xg': 'Poisson pe xG live',
        'poisson-live-xg-paced': 'Poisson pe xG live, cu ritmul corectat',
        'poisson-live-xg-paced-prematch': 'Poisson pe xG live, cu ritmul corectat spre cifrele pre-meci',
        'poisson-prematch-squad': 'Poisson pre-meci, din echipe și jucători',
        'manual-input': 'introdusă de tine',
        'slip-import': 'importat din bilet, fără estimare',
        'slip-import-model': 'importat din bilet, estimat cu modelul',
        'external-consensus': 'consens extern',
    } as Record<string, string>,

    verdict: {
        Bet: 'PARIAZĂ',
        Marginal: 'LA LIMITĂ',
        Skip: 'RENUNȚĂ',
    } as Record<Verdict, string>,

    outcome: {
        Open: 'Deschis',
        Won: 'Câștigat',
        Lost: 'Pierdut',
        Void: 'Anulat',
        HalfWon: 'Câștigat pe jumătate',
        HalfLost: 'Pierdut pe jumătate',
        CashedOut: 'Cash-out',
    } as Record<BetStatus, string>,

    log: {
        title: 'Jurnal de poziții',
        empty:
            'Nimic salvat încă. Rulează o analiză și salveaz-o; rândul este cel care îți permite să ' +
            'revii mai târziu, să treci cota de închidere și să vezi dacă avantajul a fost real.',
        savedCount: 'Salvate',
        pricedAtClose: 'Cu cotă de închidere',
        of: (total: number) => `din ${total}`,
        beatClose: 'Au bătut închiderea',
        rows: (count: number) => `${count} rânduri`,
        averageClv: 'CLV mediu',
        settledPl: 'P/L decontat',
        settled: (count: number) => `${count} decontate`,
        columns: {
            saved: 'Salvat',
            match: 'Meci',
            market: 'Piață',
            entry: 'Intrare',
            fair: 'Corectă',
            yours: 'A ta',
            edge: 'Avantaj',
            stake: 'Miză',
            verdict: 'Verdict',
            close: 'Închidere',
            clv: 'CLV',
            outcome: 'Rezultat',
            pl: 'P/L',
        },
        delete: 'Șterge',
        edit: 'Editează',
        editHint:
            'Cota și miza sunt editabile pentru că rândul înseamnă ce ai pariat efectiv, nu ce ți-a ' +
            'sugerat analiza — cotele live se mișcă între citire și clic. La salvare, linia corectă ' +
            'și avantajul se recalculează din cota nouă.',
        save: 'Salvează',
        cancel: 'Renunță',
    },

    statsPaste: {
        open: 'Importă statistici de pe Sofascore ›',
        openNote:
            'Copiază pagina meciului de pe Sofascore, Flashscore sau FotMob — toată, dacă vrei — și ' +
            'lipește-o aici. Ies minutul, scorul, xG-ul și numele echipelor.',
        title: 'Importă statistici',
        hide: 'Ascunde',
        note:
            'Mergi pe pagina meciului, apasă Ctrl+A și Ctrl+C și lipește tot aici — nu trebuie să ' +
            'selectezi exact panoul de statistici. Din antet ies minutul, scorul și numele echipelor, ' +
            'din panou xG-ul. Merge în română sau engleză.',
        tabTip:
            'Pe Sofascore lasă filele statisticilor pe „All". Dacă e selectată „1." sau „2.", xG-ul ' +
            'afișat este doar pe o repriză, iar aplicația nu are de unde să știe asta — ar citi o ' +
            'jumătate de meci ca pe tot meciul.',
        windowTip:
            'Pentru „Fereastra recentă" de mai jos: comută statisticile Sofascore pe „2.", copiază ' +
            'din nou și treci acel xG acolo, cu fereastra = minutul curent minus 45. Este cel mai ' +
            'valoros câmp al corecției de ritm.',
        whatWasRead: 'Ce s-a citit',
        match: 'Meci',
        minute: 'Minut',
        xgHome: 'xG gazde',
        xgAway: 'xG oaspeți',
        score: 'Scor',
        alsoRecognised: 'Recunoscute în plus, ca verificare că s-a copiat blocul potrivit: ',
        missing: (fields: string) =>
            `Nu s-au găsit: ${fields}. Selectează tot panoul de statistici, inclusiv antetul, sau ` +
            'scrie valorile lipsă de mână în formular.',
        use: 'Folosește aceste numere',
        clear: 'Golește',
        notInPaste: 'nu e în text',
        notFound: 'negăsit',
    },

    /** The stats recognised alongside the xG, shown so the right page is recognisable at a glance. */
    stat: {
        xg: 'Goluri estimate (xG)',
        shotsOnTarget: 'Șuturi pe poartă',
        bigChances: 'Șanse mari',
        shots: 'Șuturi',
        possession: 'Posesie',
        corners: 'Cornere',
        saves: 'Intervenții de portar',
        fouls: 'Faulturi',
        yellowCards: 'Cartonașe galbene',
    } as Record<string, string>,

    /** The fields an analysis cannot run without, for the message that says which one is missing. */
    field: {
        minute: 'minutul',
        xgHome: 'xG gazde',
        xgAway: 'xG oaspeți',
    } as Record<string, string>,

    oddsPaste: {
        open: 'Importă cote dintr-un text copiat ›',
        openNote:
            'Lipește blocul de cote și alege-ți partea — celelalte cote se completează singure.',
        title: 'Importă cote',
        hide: 'Ascunde',
        note:
            'Copiază piața pe care o privești — 7777.md, Superbet, Betano, oricare — și lipește-o ' +
            'mai jos. Apoi apasă pe partea pe care o joci. Cotele rămase devin cotele surori, adică ' +
            'exact partea ușor de uitat și fără de care marja nu poate fi scoasă.',
        noMarket:
            'Nicio piață completă acolo. O piață are nevoie de toate cotele ei înainte ca marja ' +
            'casei să poată fi scoasă — copiază blocul întreg, cu ambele părți.',
        pickSide: 'Alege partea pe care o joci',
        clear: 'Golește',
        hint:
            'Nu se aplică nimic până nu alegi o parte. Importul de statistici completează starea ' +
            'meciului; acesta completează piața.',
    },

    quickImport: {
        open: 'Lipește un meci ca JSON ›',
        openNote: 'Mai rapid decât completarea formularului câmp cu câmp în timpul unui meci.',
        title: 'Import rapid',
        hide: 'Ascunde',
        note:
            'Lipește un singur obiect JSON și fiecare câmp de mai jos se completează singur. ' +
            'Numele sunt recunoscute lejer, iar selecțiile acceptă română (Sub, Peste, Da).',
        fill: 'Completează formularul',
        loadExample: 'Încarcă exemplu',
        exportCurrent: 'Exportă ce e acum',
        copied: 'Copiat',
        readError: 'Nu am putut citi asta.',
    },

    shot: {
        open: 'Importă cotele dintr-o captură de ecran ›',
        openNote:
            'Pentru aplicațiile de casă de pariuri din care textul nu se poate selecta: fă o captură, ' +
            'lipește-o aici și cotele se citesc automat.',
        title: 'Importă cote din captură de ecran',
        hide: 'Ascunde',
        intro:
            'Lipește imaginea cu Ctrl+V, trage-o peste zona de mai jos sau alege un fișier. Imaginea ' +
            'nu pleacă nicăieri: textul este citit aici, în browser.',
        dropHere: 'Lipește (Ctrl+V), trage imaginea aici sau',
        chooseFile: 'alege un fișier',
        dropActive: 'Dă drumul imaginii',
        tips:
            'Decupează doar blocul de cote și fă captura la rezoluția cea mai mare pe care o ai. ' +
            'Textul mic și neclar este singurul lucru care încurcă citirea.',
        reading: 'Se citește imaginea…',
        readingStatus: (percent: number) => `Se citește imaginea… ${percent}%`,
        loadingEngine: 'Se pregătește motorul de recunoaștere (prima dată durează câteva secunde)…',
        failed: 'Imaginea nu a putut fi citită.',
        notAnImage: 'Asta nu e o imagine. Lipește o captură de ecran sau alege un fișier imagine.',
        clipboardEmpty:
            'În clipboard nu era nicio imagine. Fă o captură (Win+Shift+S) și apoi apasă Ctrl+V aici.',
        confidence: (value: number) => `Încredere medie a recunoașterii: ${value}%`,
        lowConfidence:
            'Recunoașterea a mers greu pe imaginea asta. Verifică textul de mai jos cifră cu cifră ' +
            'înainte să analizezi.',
        invert: 'Fundal',
        invertAuto: 'Auto',
        invertAlways: 'Închis (inversează)',
        invertNever: 'Deschis',
        invertedNote: 'Imaginea a fost inversată: text deschis pe fundal închis.',
        fileName: (name: string) => `Imagine: ${name}`,
        recognised: 'Text recunoscut — corectează-l dacă e nevoie',
        recognisedNote:
            'Ce scrie aici este ce s-a înțeles din imagine. Orice corectezi este luat în seamă ' +
            'imediat: piețele de mai jos se recitesc la fiecare tastă.',
        again: 'Citește din nou',
        clear: 'Golește',
        marketsFound: (count: number) =>
            count === 1 ? 'O piață completă găsită' : `${count} piețe complete găsite`,
        noMarkets:
            'Nicio piață completă în imaginea asta. O piață are nevoie de toate cotele ei ca să i ' +
            'se poată scoate marja — include în captură ambele coloane, Sub și Peste.',
        selectAll: 'Bifează tot',
        selectNone: 'Debifează tot',
        margin: (value: string) => `marjă ${value}`,
        marginOdd:
            'marjă atipică — probabil cele două cote nu sunt cele două fețe ale aceleiași piețe',
        analyseSelected: (count: number) =>
            count === 1 ? 'Analizează selecția' : `Analizează toate cele ${count} selecții`,
        needLive:
            'Analiza în bloc are nevoie de starea live a meciului: minutul și xG-ul acumulat. ' +
            'Bifează „Meciul este în desfășurare" și completează-le mai jos.',
        needMinute: 'Completează minutul și xG-ul live înainte de a analiza în bloc.',
    },

    batch: {
        title: 'Toate selecțiile, calculate',
        subtitle: (count: number, match: string) =>
            match ? `${count} selecții · ${match}` : `${count} selecții`,
        running: (done: number, total: number) => `Se calculează… ${done} din ${total}`,
        columns: {
            market: 'Piață',
            selection: 'Selecție',
            odds: 'Cotă',
            fair: 'Corectă',
            yours: 'A ta',
            edge: 'Avantaj',
            ev: 'EV',
            stake: 'Miză',
            verdict: 'Verdict',
        },
        best: 'Cea mai bună',
        failed: 'eroare',
        sortedBy: 'Sortate după avantaj, de la cel mai mare. Verdictul folosește aceleași praguri ca analiza simplă.',
        openOne: 'Deschide',
        openOneHint: 'Trece selecția în formular și arată calculul complet pentru ea.',
        save: 'Salvează',
        clear: 'Închide tabelul',
        nothingPositive:
            'Nicio selecție din captură nu are avantaj pozitiv față de linia corectă. Asta este un ' +
            'rezultat, nu o eroare: de cele mai multe ori piața are dreptate.',
        disclaimerLive: (minute: string, xgHome: string, xgAway: string) =>
            `Calculat pentru minutul ${minute}, xG ${xgHome} – ${xgAway}. Dacă meciul a mers mai ` +
            'departe, recitește statisticile și calculează din nou.',
    },
};

export type Dictionary = typeof ro;

const en: Dictionary = {
    langName: { ro: 'Română', en: 'English' },

    app: {
        title: 'BestBet',
        tagline:
            'De-vig the price, price it yourself, compare the two. It recommends; you decide and ' +
            'place anything yourself.',
        analyse: 'Analyse',
        analysing: 'Analysing…',
        emptyResult:
            'Fill in the price you can get, the other prices in the same market, and the live xG. ' +
            'The result appears here.',
        genericError: 'Something went wrong.',
        saveError: 'Could not save the position.',
        updateError: 'Could not update the closing price.',
        outcomeError: 'Could not update the outcome.',
        deleteError: 'Could not delete the position.',
        language: 'Language',
        disclaimer: DISCLAIMER,
    },

    common: {
        loading: 'Loading…',
        cancel: 'Cancel',
        close: 'Close',
        failed: 'Something went wrong.',
    },

    nav: {
        calculator: 'Calculator',
        bets: 'My bets',
        admin: 'Accounts',
        account: 'My account',
        logout: 'Sign out',
        main: 'Navigation',
    },

    auth: {
        title: 'Sign in to BestBet',
        closed: 'This app is private: the admin creates the accounts.',
        email: 'Email',
        password: 'Password',
        rememberMe: 'Keep me signed in on this device',
        submit: 'Sign in',
        submitting: 'Checking…',
        failed: 'Wrong email or password, or the account is locked for a while after too many attempts.',
        tooMany: 'Too many attempts from this address. Wait a minute and try again.',
        checking: 'Checking your session…',
        unreachable: 'The server is not answering. Start the backend and the database, then retry.',
        retry: 'Retry',
    },

    account: {
        title: 'My account',
        signedInAs: (email: string) => `Signed in as ${email}.`,
        forced:
            'Your account has a temporary password from the admin. Choose one only you know — until ' +
            'then the rest of the app will not answer.',
        current: 'Current password',
        next: 'New password',
        confirm: 'New password again',
        rule: 'At least 12 characters. A short sentence is safer and easier to remember than a word with symbols.',
        submit: 'Change password',
        submitting: 'Saving…',
        saved: 'Password changed. Your other sessions end within a minute.',
        mismatch: 'The two new passwords do not match.',
    },

    bets: {
        title: 'My bets',
        open: (count: number) => `Open (${count})`,
        closed: (count: number) => `Settled (${count})`,
        add: 'Add a bet',
        emptyOpen: 'No open bets.',
        emptyClosed: 'No settled bets yet.',
        loadError: 'The bets could not be loaded.',
        columns: {
            placed: 'Placed',
            event: 'Event',
            market: 'Market',
            bookmaker: 'Bookmaker',
            odds: 'Odds',
            stake: 'Stake',
            probability: 'Est. prob.',
            edge: 'Edge',
            status: 'Status',
            payout: 'Payout',
            profit: 'Profit',
            actions: 'Actions',
        },
        settle: 'Settle',
        edit: 'Edit',
        delete: 'Delete',
        reopen: 'Reopen',
        settleTitle: (label: string) => `Settle: ${label}`,
        result: 'Result',
        cashOut: 'Amount received on cash-out',
        closingOdds: 'Closing odds (optional)',
        preview: (payout: string, profit: string) => `Pays ${payout}, that is ${profit}.`,
        previewNeedsAmount: 'Enter the amount received to see the result.',
        confirmSettle: 'Settle the bet',
        confirmDelete: (label: string) =>
            `Delete the bet "${label}"? It leaves the statistics and the balance too, and cannot be recovered.`,
        confirmDeleteButton: 'Yes, delete',
        actionFailed: 'That did not work.',
        form: {
            titleAdd: 'Add a bet',
            titleEdit: 'Edit the bet',
            titleSave: 'Save as a bet',
            fromAnalysis: (selection: string, odds: string, probability: string, edge: string) =>
                `From the analysis: ${selection} at ${odds}, estimated probability ${probability}, edge ${edge}.`,
            match: 'Event',
            matchPlaceholder: 'Team A - Team B',
            sport: 'Sport',
            competition: 'Competition',
            kickoff: 'Kick-off',
            marketKind: 'Market type',
            marketLabel: 'Market, as the bookmaker words it',
            selection: 'Selection',
            selectionPlaceholder: 'Over 2.5',
            bookmaker: 'Bookmaker',
            odds: 'Odds taken',
            otherOdds: 'Other prices in the market',
            otherOddsHint: 'Optional. They remove the margin; without them the edge still contains it.',
            stake: 'Stake',
            probability: 'Your probability (%)',
            probabilityHint: 'Optional. Without it the edge is zero rather than invented.',
            closingOdds: 'Closing odds',
            notes: 'Notes',
            save: 'Save',
            saving: 'Saving…',
        },
        errors: {
            match: 'Enter the event.',
            odds: 'The odds must be greater than 1.',
            otherOdds: 'Every price in the market must be greater than 1.',
            stake: 'The stake must be a number, 0 or more.',
            probability: 'The probability is between 0 and 100.',
            closingOdds: 'The closing odds must be greater than 1.',
        } as Record<'match' | 'odds' | 'otherOdds' | 'stake' | 'probability' | 'closingOdds', string>,
        stats: {
            title: 'Summary',
            bets: 'Bets',
            open: 'Open',
            openStake: 'At stake',
            staked: 'Staked (settled)',
            profit: 'Profit',
            yield: 'Yield',
            yieldHint: 'Profit over the amount staked on settled bets.',
            roi: 'ROI',
            roiHint: 'Profit over the money deposited, less withdrawals.',
            beatClose: 'Beating the close',
            none: '—',
        },
    },

    bankroll: {
        label: 'Bankroll',
        note:
            'Starts from your account balance. You can change it here for this calculation only; the ' +
            'real balance is kept under "My bets".',
        account: (balance: string) => `Account balance: ${balance}`,
        overridden: 'Using an amount other than your balance, for this calculation only.',
        useAccount: 'Use my balance',
        noStart: 'No starting balance yet. Set it under "My bets" with a deposit.',
        title: 'Balance',
        balance: 'Balance',
        balanceHint: 'Deposits less withdrawals plus the profit of settled bets. Stakes are sized from it.',
        available: 'Available',
        availableHint: 'The balance less what is riding on open bets.',
        deposits: 'Deposits',
        withdrawals: 'Withdrawals',
        settled: 'Profit from bets',
        kind: 'Type',
        kinds: { Deposit: 'Deposit', Withdrawal: 'Withdrawal' } as Record<'Deposit' | 'Withdrawal', string>,
        amount: 'Amount',
        movementNote: 'Note (optional)',
        startingNote: 'Starting balance',
        add: 'Add',
        adding: 'Adding…',
        history: 'History',
        empty: 'No movements yet. The first deposit is your starting balance.',
        confirmDelete: (what: string) => `Delete the movement "${what}"? The balance is worked out again without it.`,
        amountError: 'The amount must be a number greater than 0.',
    },

    admin: {
        title: 'Accounts',
        intro:
            'There is no public sign-up: an account exists only if you create it here. The password you ' +
            'give is temporary — they have to change it at their first sign-in, so you never know a ' +
            'password that is in use.',
        create: 'New account',
        email: 'Email',
        tempPassword: 'Temporary password',
        generate: 'Generate',
        createSubmit: 'Create the account',
        created: (email: string, password: string) =>
            `Created ${email}. Temporary password: ${password} — send it over a safe channel; it will not be shown again.`,
        columns: { email: 'Email', role: 'Role', state: 'State', created: 'Created', actions: 'Actions' },
        roleAdmin: 'Admin',
        roleUser: 'User',
        stateActive: 'Active',
        stateDisabled: 'Disabled',
        stateLocked: 'Locked for a while',
        stateMustChange: 'Temporary password',
        disable: 'Disable',
        enable: 'Enable',
        reset: 'Reset password',
        confirmDisable: (email: string) =>
            `Disable ${email}? They can no longer sign in, and their open sessions end within a minute.`,
        confirmReset: (email: string) =>
            `Reset the password for ${email}? They get a new temporary password and their sessions end.`,
        resetDone: (email: string, password: string) =>
            `New temporary password for ${email}: ${password} — it will not be shown again.`,
        you: 'you',
    },

    market: {
        title: 'Market',
        match: 'Match',
        matchPlaceholder: 'Sunderland - Arsenal',
        kind: 'Market kind',
        side: 'Side',
        line: 'Line',
        bookLabel: "Bookmaker's label",
        optional: '(optional)',
        bookLabelPlaceholder: 'Sub 4',
        decimalOdds: 'Decimal odds',
        otherPrices: 'Other prices in this market',
        note:
            'Without these there is no way to tell how much margin the bookmaker built into the ' +
            'price you are looking at, so the de-vig — and every number downstream of it — would be ' +
            'guesswork.',
        price: (index: number) => `Price ${index}`,
    },

    marketKind: {
        Moneyline1X2: 'Match result (1X2)',
        TotalGoals: 'Total goals (Under / Over)',
        BothTeamsToScore: 'Both teams to score (GG/NG)',
        NextGoal: 'Next goal',
        Handicap: 'Handicap',
    } as Record<MarketKind, string>,

    marketKindShort: {
        Moneyline1X2: '1X2',
        TotalGoals: 'Total goals',
        BothTeamsToScore: 'GG/NG',
        NextGoal: 'Next goal',
        Handicap: 'Handicap',
    } as Record<MarketKind, string>,

    marketKindHint: {
        Moneyline1X2: 'The other two prices in this market',
        TotalGoals: 'The other side of the same line',
        BothTeamsToScore: 'The other side of the same market',
        NextGoal: 'The other two prices in this market',
        Handicap: 'The other side of the same handicap',
    } as Record<MarketKind, string>,

    selection: {
        Home: 'Home (1)',
        Draw: 'Draw (X)',
        Away: 'Away (2)',
        Over: 'Over',
        Under: 'Under',
        Yes: 'Yes (GG)',
        No: 'No (NG)',
        NextGoalHome: 'Home (1)',
        NextGoalAway: 'Away (2)',
        NextGoalNone: 'No further goal (0)',
        HandicapHome: 'Home',
        HandicapAway: 'Away',
    } as Record<MarketSelection, string>,

    parse: {
        incomplete: (title: string, found: number, required: number) =>
            `${title}: found ${found} of the ${required} prices this market needs, so the ` +
            "bookmaker's margin cannot be removed from it. Include the whole market, both sides.",
        headingWithoutPrices: (title: string) => `Recognised the heading "${title}" but no prices under it.`,
    },

    reference: {
        open: 'Add a reference line (Polymarket) ›',
        openNote:
            'A second opinion on the probability, from a market with no margin. The edge is measured ' +
            'against it; the stake stays sized at your price.',
        title: 'Reference line',
        hide: 'Hide',
        note:
            'A bookmaker builds 5–15% of margin into its prices, so its own fair line is only that ' +
            'accurate. Polymarket trades probabilities directly: its prices add up to about 100%, so ' +
            'there is nothing to remove. With a reference present the edge is measured against it, ' +
            'while EV and the stake stay at your price — the only one that pays you.',
        privacy:
            'The search text leaves this machine for Polymarket. Nothing else does: not the bankroll, ' +
            'not the stake, not the log, not the screenshots.',
        searchLabel: 'Search for the match, team or competition',
        searchPlaceholder: 'Arsenal',
        search: 'Search',
        searching: 'Searching…',
        noResults:
            'No market matched. Polymarket mostly covers the big matches and season-long bets — for ' +
            'an ordinary league fixture there is often nothing there.',
        volume: (value: string) => `volume ${value}`,
        pick: 'Pick the side that matches your bet',
        mismatch:
            'Check that these are the same thing. Nothing here can tell whether the bookmaker\u2019s ' +
            '"Over 2.5 goals" and the Polymarket question mean the same market, and a mismatched ' +
            'reference moves the edge in exactly the direction you would be wrong in.',
        active: (source: string) => `Reference in use: ${source}`,
        remove: 'Drop the reference',
        openMarket: 'Open on Polymarket',
        failed: 'The reference could not be fetched.',
        offlineNote:
            'If Polymarket does not answer, the rest of the app works unchanged — a reference is an ' +
            'addition, not a requirement.',
    },

    reprice: {
        action: 'Re-price',
        title: 'Re-priced at the current minute',
        probabilityNow: 'Chance of winning now',
        atEntry: 'At entry',
        change: 'Change',
        evNow: 'EV at your price',
        evAmount: 'EV on the stake placed',
        fairNow: 'Fair price now',
        fairNowHint: 'What to judge a cash-out or a hedge against: below it, the offer is against you.',
        decided: 'What happened on the pitch has decided this bet — there is no fair price left to compare.',
        note:
            'Nothing is saved. The row keeps its entry snapshot, which is the evidence that the ' +
            'decision was reasonable at the time — re-price as often as the match moves.',
        needsLive:
            'Re-pricing uses the match state from the form above. Tick "This match is live", fill in ' +
            'the minute and the xG, then press again.',
        close: 'Close',
        failed: 'The position could not be re-priced.',
    },

    prematch: {
        open: 'Calculate from teams and players (pre-match) ›',
        openNote:
            'A probability for a match that has not started: how the teams have done this season and ' +
            'who is playing, instead of a number typed by hand.',
        title: 'Pre-match: teams and players',
        hide: 'Hide',
        intro:
            'The competition table gives each team\u2019s goals scored and conceded, home and away, ' +
            'against the league average. Player statistics show who the goals come from: if a top ' +
            'scorer is missing, the team loses part of them. The result feeds the usual analysis, so ' +
            'the verdict and the stake are worked out as before.',
        notConfigured:
            'This needs a free API-Football key (100 requests a day). Create one at ' +
            'dashboard.api-football.com, put it in ApiFootball__Key (a .env file for Docker, or ' +
            '"dotnet user-secrets set ApiFootball:Key …"), and restart the app. Details in ' +
            'GHID_RULARE.md.',
        privacy:
            'Only the competition, the season and the teams go to API-Football. The key stays on ' +
            'the server. Nothing else leaves: not the bankroll, the stake, the prices or the log.',
        league: 'Competition',
        home: 'Home',
        away: 'Away',
        homePlaceholder: 'Manchester City',
        awayPlaceholder: 'Arsenal',
        run: 'Calculate',
        running: 'Reading the data…',
        failed: 'The pre-match calculation could not be run.',
        season: (year: number, name: string) =>
            `${name} ${year}/${String((year + 1) % 100).padStart(2, '0')}`,
        expectedGoals: 'Expected goals',
        beforeAbsences: (value: string) => `before absences: ${value}`,
        missing: (percent: string) => `${percent} of the team\u2019s goals are missing`,
        homeWin: 'Home win',
        draw: 'Draw',
        awayWin: 'Away win',
        btts: 'Both teams score',
        over: (line: string) => `Over ${line}`,
        form: 'Form this season',
        formLine: (gf: string, ga: string, games: number) =>
            `${gf} scored / ${ga} conceded per game (${games} games)`,
        formHome: 'home',
        formAway: 'away',
        scorers: 'Who can score',
        player: 'Player',
        pos: 'Pos.',
        goals: 'Goals',
        shots: 'Shots',
        minutes: 'Min.',
        chance: 'Chance',
        fairOdds: 'Fair price',
        yourOdds: 'Book price',
        verdict: 'Worth it?',
        absent: 'Out',
        absentHint: 'Tick a player who will not play (injured, suspended, rested); it recalculates.',
        worth: 'Worth it',
        borderline: 'Marginal',
        notWorth: 'Not worth it',
        edgeOf: (pp: string) => `edge ${pp}`,
        scorerNote:
            'Players are the season\u2019s squad, not the starting eleven: confirm the line-up before ' +
            'staking. "Worth it" compares the estimated chance with your price without removing the ' +
            'bookmaker\u2019s margin, so it is a strict test.',
        apply: 'Use in the analysis',
        applied: 'Used in the analysis below',
        applyNote:
            'The analysis below will work out the chosen market (1X2, Over/Under, BTTS) from these ' +
            'expected goals. It works for a match that has not started: untick "This match is live".',
        remove: 'Stop using the model',
        limits:
            'It knows nothing about tactics, motivation, weather or news that broke after the data ' +
            'was read, and treats the two teams\u2019 goals as independent. It is a better starting ' +
            'estimate than a typed one, not a price.',
    },

    monitor: {
        open: 'Watch the match live ›',
        openNote:
            'Open bets re-price themselves as the match moves on. Paste the match link and there is ' +
            'nothing else to do.',
        title: 'Live monitoring',
        hide: 'Hide',
        why:
            'Paste the match link from FotMob or from Sofascore, press once, and the app fetches the ' +
            'minute, the score and the xG itself every few seconds. No tab has to stay open.',

        // --- The link, which is the way in for anything FotMob covers -------
        linkLabel: 'Match link or id',
        linkPlaceholder: 'https://www.fotmob.com/match/5881191',
        linkHint:
            'A FotMob link works (that is where the numbers come from) and so does a Sofascore link ' +
            '(the match is then found at FotMob by the team names). A bare FotMob match id works too.',
        linkStart: 'Watch this match',
        linkStop: 'Stop',
        linkInvalid:
            'That does not look like a match link. Copy the address out of your browser\u2019s bar, ' +
            'from the match page on FotMob or on Sofascore.',
        linkWorking: 'Looking for the match…',
        linkFound: (provider: string, label: string) => `${label} · read from ${provider}`,
        linkNoXg:
            'The match was found, but the provider does not compute xG for this league — and ' +
            'without xG the model has nothing to run on. For this match use the bookmarklet below, ' +
            'or paste the stats by hand.',
        linkFinished:
            'The match is over. The readings will not change again; switch monitoring off or point ' +
            'it at another match.',
        linkNotStarted:
            'The match has not kicked off. There is no minute, score or xG to read yet — come ' +
            'back after the whistle.',
        linkStale:
            'The provider has not answered for a while. The numbers below are from a minute that ' +
            'has passed.',

        // --- The bookmarklet, which stays the only way in for the rest ------
        fallback: 'Link not an option? Monitor from your own Sofascore tab ›',
        fallbackWhy:
            'Sofascore refuses any request that is not a real browser — it answers 403 down to ' +
            'robots.txt — so this app\u2019s server cannot ask on its own, and FotMob does not ' +
            'have xG for every league. Your browser is allowed, because that is where the page is ' +
            'already open. The bookmarklet below reads the page in front of you and sends it here; ' +
            'nothing extra is requested from them.',
        step1: 'Drag this onto your bookmarks bar (or copy it and make a bookmark of it):',
        bookmarkName: 'BestBet: watch',
        copy: 'Copy',
        copied: 'Copied',
        step2:
            'Open the match on Sofascore, go to the Statistics tab, and click the bookmark. Click it ' +
            'again to stop.',
        step3: (seconds: number) =>
            `Leave the tab open. It sends a reading every ${seconds} seconds, and the open bets on ` +
            'that match re-price themselves.',
        watching: 'Monitoring on',
        waiting: 'Waiting for the first reading…',
        lastReading: (seconds: number) =>
            seconds < 60 ? `Last reading ${seconds}s ago` : `Last reading ${Math.round(seconds / 60)} min ago`,
        reading: (
            minute: number | null,
            home: number | null,
            away: number | null,
            xgHome: number | null,
            xgAway: number | null,
        ) =>
            readingParts(
                [
                    minute === null ? null : `minute ${minute}`,
                    home === null || away === null ? null : `${home}–${away}`,
                    xgHome === null || xgAway === null ? null : `xG ${xgHome}–${xgAway}`,
                ],
                'nothing to read in this one',
            ),
        unknownMatch: 'match not recognised',
        unusable:
            'A reading arrived but it does not carry everything needed. On Sofascore, go to the ' +
            'Statistics tab and leave the tabs on "All" — that is where the xG is.',
        stale:
            'The reading is old. The tab closed, went to sleep, or the bookmark was switched off. ' +
            'Until it comes back, the numbers below are from a minute that has passed.',
        tracking: (labels: string) => `Re-pricing: ${labels}`,
        nothingTracked: 'No open bet on this match, so there is nothing to re-price.',
        clear: 'Forget the reading',
        privacy:
            'Through the bookmarklet, the reading only travels between your browser and the app on ' +
            'your own machine. Through a link, the only thing that leaves this machine is the match ' +
            'id, and it goes to the provider — nothing else does: not the bankroll, not the stake, ' +
            'not a saved position. Either way the reading is not stored anywhere and goes when the ' +
            'app stops.',
        failed: 'The reading could not be fetched.',
    },

    slip: {
        open: 'Import a bet that is already placed',
        openNote:
            'From the bookmaker\'s slip: the price, the stake and the score are read for you, and ' +
            'then you track it with the match.',
        title: 'Import a bet from its slip',
        hide: 'Hide',
        intro:
            'Nothing can be copied out of the bookmaker\'s app, so the slip comes in as a screenshot ' +
            '(or as its text, if you have it). Reading happens in the browser; nothing leaves your ' +
            'computer. Check the fields below before you save — a digit read wrongly means tracking ' +
            'a bet with different numbers from the ones you staked.',
        dropHere: 'Drop the slip screenshot here or',
        dropActive: 'Release to read the slip',
        chooseFile: 'choose a file',
        notAnImage: 'That file is not an image.',
        reading: (percent: number) => `Reading the slip… ${percent}%`,
        loadingEngine: 'Loading the reading engine (first time only)…',
        failed: 'The slip could not be read from the image.',
        textLabel: 'Slip text',
        textPlaceholder: 'Under 2 2.75\nTotal\nAtletico Madrid 1\nManchester United 0\nStake: 2.00',
        textNote:
            'What the engine read off the image shows up here. Correct it or replace it with the ' +
            'slip\'s text, then press "Read the slip".',
        read: 'Read the slip',
        clear: 'Clear',
        formTitle: 'Check the bet',
        match: 'Match',
        matchPlaceholder: 'Atletico Madrid - Manchester United',
        kind: 'Market',
        side: 'Side',
        line: 'Line',
        bookLabel: 'As written on the slip',
        odds: 'Price',
        stake: 'Stake',
        minute: 'Minute',
        scoreHome: 'Home goals',
        scoreAway: 'Away goals',
        xgHome: 'Home xG',
        xgAway: 'Away xG',
        stateTitle: 'Match state when you bet',
        stateNote:
            'The slip has the minute and the score but not the xG. Without xG there is no chance ' +
            'to compute for the entry — that is fine: from "Open bets" you enter the current ' +
            'state any time and see the chance as it is now.',
        save: 'Save as an open bet',
        saving: 'Saving…',
        saved: 'Saved. It is below, under "Open bets".',
        saveFailed: 'The bet could not be saved.',
        issues: {
            'no-odds': 'I could not find the price. Fill it in.',
            'no-stake': 'I could not find the stake. Fill it in.',
            'no-market':
                'I did not recognise the market. Only totals (Under/Over) are read automatically; ' +
                'choose the market and side below.',
            'no-match': 'I could not find the teams and the score. Fill them in to track it against the real score.',
            'no-minute': 'I could not find the minute. Fill it in if the bet is live.',
            'payout-mismatch':
                'The return on the slip does not follow from price × stake. A digit was misread — ' +
                'check the price and the stake.',
        } as Record<string, string>,
        problems: {
            odds: 'The price must be greater than 1.00.',
            stake: 'Fill in the stake (it can be 0).',
            line: 'Fill in the line, e.g. 2.5.',
            minute: 'The minute must be between 1 and 130, and it is needed if you give xG.',
            score: 'Goals must be whole numbers, 0 or more.',
            xg: 'Fill in the xG for both teams, as numbers of 0 or more.',
        } as Record<string, string>,
    },

    track: {
        title: 'Open bets',
        intro:
            'Enter the latest match data and see at once what chance the bet has of winning. The ' +
            'data stays on the bet, and every reading is kept in a history, so you can see how the ' +
            'chance moves through the match.',
        empty: 'No open bets. Import one from its slip or save an analysis, and it shows up here.',
        minute: 'Minute',
        goalsHome: 'Home goals',
        goalsAway: 'Away goals',
        xgHome: 'Home xG',
        xgAway: 'Away xG',
        fromForm: 'Take the data from the form above',
        fromFormNote: 'The form above has no minute and xG filled in yet.',
        needState: 'Fill in the minute and both teams\' xG to see the bet\'s chance.',
        updating: 'Calculating…',
        failed: 'The chance could not be calculated.',
        chanceWin: 'Chance of winning',
        chancePush: 'Stake back',
        chanceLose: 'Chance of losing',
        pushNote: (line: string) =>
            `On the whole line ${line}, exactly ${line} goals returns the stake. That is why the ` +
            'three chances add to 100%, and "Chance of winning" is the real one, with the returned ' +
            'stake counted separately.',
        decidedWon: 'The score has decided the bet: it is won, whatever happens to the end.',
        decidedLost: 'The score has decided the bet: it is lost, whatever happens to the end. You can settle it above.',
        evNow: 'EV at your price',
        evAmount: 'EV on the stake placed',
        ifWin: 'If it wins',
        ifLose: 'If it loses',
        fairNow: 'Fair price now',
        stake: 'Stake',
        odds: 'Price',
        atEntry: 'At entry',
        entryUnknown: 'unknown',
        history: 'History',
        historyNote: (count: number) => `${count} readings · newest first`,
        clearHistory: 'Clear history',
        settle: 'Settle the bet',
        note:
            'The chance comes from the Poisson model on xG and the match tempo. It is an estimate, ' +
            'not a certainty: it moves with every goal and every new xG you enter. Nothing in the ' +
            'log changes.',
    },

    live: {
        title: 'Match state',
        isLive: 'This match is live',
        note:
            'Read these straight off the stats screen. The model turns the xG accrued so far into a ' +
            'rate per minute and runs that rate forward over whatever is left.',
        minute: 'Minute',
        xgHome: 'xG home',
        xgAway: 'xG away',
        goalsHome: 'Goals home',
        goalsAway: 'Goals away',
        dynamics: 'Match dynamics',
        dynamicsHint: ' — how the pace is carried forward',
        correct: 'Correct for pace and score state',
        correctedNote:
            'A rate read off a handful of minutes is an estimate, not a fact, so it is pulled ' +
            'towards what these two teams usually produce — the pre-match panel’s numbers if you ' +
            'use them, a league-average match otherwise — and adjusted for the side protecting a ' +
            'lead. Switch this off to see the raw extrapolation instead.',
        rawNote:
            'Raw extrapolation: whatever pace the clock shows is run flat to full time. A hot first ' +
            'half then projects a scoreline that almost never happens.',
        windowNote:
            'The sharpest input here: the xG of the game as it is now, not its average. Watching by ' +
            "link, it fills itself in from FotMob's shots for the last 20 minutes. Otherwise it is " +
            'optional: take the second half as all match minus first half and the model will weight ' +
            'what the game is doing now over its average.',
        windowMinutes: 'Recent window (min)',
        recentXgHome: 'xG home in it',
        recentXgAway: 'xG away in it',
        stoppage: 'Stoppage and red cards',
        stoppageHint: ' — filled in for you when the match is watched by link',
        stoppageNote:
            "The fourth official's board says how many more minutes will be played. Until it goes " +
            'up the model counts the usual stoppage (3 min in the first half, 4.5 in the second), ' +
            'and a red card lowers the rate of the side down to ten and raises the other side’s.',
        period: 'Half',
        periodAuto: 'From the minute',
        periodFirst: 'First half',
        periodHalfTime: 'Half time',
        periodSecond: 'Second half',
        addedFirst: 'Board at 45 (+min)',
        addedSecond: 'Board at 90 (+min)',
        redHome: 'Reds home',
        redAway: 'Reds away',
        redMinute: 'Minute of the last red',
        preMatchNote:
            'Before kick-off there is no live data to derive a probability from, so the estimate ' +
            'has to be yours. Everything downstream — the edge, the stake, the verdict — is only as ' +
            'good as this one number.',
        yourProbability: 'Your probability (%)',
    },

    chart: {
        title: 'Live bets, minute by minute',
        intro:
            'The win chance of every open bet, re-priced at each reading of the match. The dotted ' +
            'line is the break-even of the price taken: above it the bet is worth more than it cost, ' +
            'below it less — the line a cash-out offer is measured against. The grey bands are ' +
            'stoppage time, as wide as the referee announced.',
        empty: 'No readings for this bet yet.',
        summary: (now: string, minute: string, entry: string | null, threshold: string) =>
            `Now ${now} (${minute})` +
            (entry === null ? '' : `, ${entry} at entry`) +
            `; the price breaks even at ${threshold}.`,
        breakEven: (value: string) => `break-even ${value}`,
        keyWin: 'chance of winning',
        keyEntry: (value: string) => `${value} at entry`,
        keyRed: 'red card',
        win: 'win',
        push: 'stake returned',
        halfTime: 'HT',
        reds: (home: number, away: number) => `reds ${home}-${away}`,
        left: (minutes: number) => `~${minutes} min of play left`,
        table: (count: number) => `All readings (${count})`,
        minute: 'Minute',
        score: 'Score',
        remaining: 'Min. left',
        clear: 'Clear history',
        auto:
            'Monitoring is on: every open bet re-prices itself once a minute, from its own match as ' +
            'found at FotMob by the team names. The match watched by link re-prices at every reading.',
        paused:
            'Monitoring is off, so the bets no longer update themselves. Turn it on in the monitor ' +
            'panel and every open bet re-prices once a minute.',
        notYet: 'Looking for the match…',
        updated: (seconds: number) => `Updated ${seconds} s ago`,
        from: (provider: string) => `from ${provider}:`,
        foundAt: (provider: string) => `Found at ${provider}:`,
        next: (seconds: number) => `next in ${seconds} s`,
        failed: (message: string) => `Re-pricing failed: ${message} Trying again in 2 minutes.`,
        unavailable: {
            'not-found':
                "No match by the bet's team names was found at FotMob. Trying again every 5 minutes; " +
                'to follow it now, put the match link in the monitor panel.',
            'not-started': 'The match has not started yet. Checking every 5 minutes.',
            'no-xg':
                'FotMob has no xG for this league, so the bet cannot be re-priced from here. Watch ' +
                'the match with the bookmarklet, from Sofascore.',
            finished: 'The match is over. Settle the bet in the log or in My bets.',
        } as Record<LiveUnavailable, string>,
    },

    result: {
        title: 'Result',
        fair: 'Fair (de-vigged)',
        yourEstimate: 'Your estimate',
        edge: 'Edge',
        ev: 'EV per unit',
        stake: 'Suggested stake',
        paceDown: 'Pace down to',
        paceUp: 'Pace up to',
        paceHint: (raw: string, adjusted: string) => `${raw} → ${adjusted} xG per 90, both sides`,
        pacePriorTeams: (home: string, away: string) =>
            `Pulled towards the teams' pre-match numbers: ${home} home, ${away} away, per match`,
        pacePriorLeague: (value: string) =>
            `Pulled towards a league average of ${value} per team — the pre-match panel gives a better ` +
            'base, from the teams actually playing',
        overtaken: (minute: string) =>
            `Computed at minute ${minute}, and the match has moved on. Press Analyse again to price ` +
            'it at the current minute.',
        referenceTitle: 'Reference line',
        referenceFair: (source: string) => `Fair per ${source}`,
        bookFair: 'Fair per the book',
        disagreement: 'Disagreement',
        referenceNote: (source: string) =>
            `The edge above is measured against ${source}. EV and the stake stay at your price, ` +
            'because that is the only one that pays you.',
        referenceOverround: (value: string) =>
            `Reference overround: ${value}. Near zero means a market with no margin to remove.`,
        mismatch:
            'Edge and EV disagree here, and that is not a bug. The edge is measured against the ' +
            'de-vigged fair line; the EV is measured against the price actually on the screen. A ' +
            "positive edge with negative EV means the bookmaker's margin is wider than your edge — " +
            'you would take this bet at a fair price, and you cannot get one.',
        working: 'How this number was reached',
        method: 'Method',
        overround: (value: string) => `Book overround: ${value} above a margin-free market`,
        resolvedSelection: 'Resolved selection',
        minutes: 'Minutes extrapolated over',
        xgRate: (home: string, away: string) => `xG rate: ${home} home / ${away} away per minute`,
        expectedRemaining: (home: string, away: string) =>
            `Expected remaining goals: ${home} home / ${away} away`,
        flatProjection: (home: string, away: string) =>
            `Flat projection over 90: ${home} home / ${away} away xG`,
        push: (push: string, raw: string) =>
            `Push (stake returned): ${push} — raw win probability before the push adjustment is ${raw}`,
        save: 'Save as a bet…',
        saved: 'Saved to your bets',
        saving: 'Saving…',
        saveHint:
            'Opens the form with the odds, probability and edge already filled in; you add the stake ' +
            'placed, the bookmaker and the competition. The entry price is kept so you can find out ' +
            'later whether this process actually beats the market.',
    },

    capReason: {
        'no-positive-edge': 'no positive edge',
        'skip-below-threshold': 'below threshold — no stake',
        'floor-3pct': 'a 3% floor of bankroll',
        'cap-5pct': 'a 5% cap on bankroll',
        'quarter-kelly': 'quarter Kelly',
    } as Record<string, string>,

    method: {
        'poisson-live-xg': 'Poisson on live xG',
        'poisson-live-xg-paced': 'Poisson on live xG, pace corrected',
        'poisson-live-xg-paced-prematch': 'Poisson on live xG, pace corrected towards the pre-match numbers',
        'poisson-prematch-squad': 'Poisson pre-match, from teams and players',
        'manual-input': 'entered by hand',
        'slip-import': 'imported from a slip, no estimate',
        'slip-import-model': 'imported from a slip, estimated by the model',
        'external-consensus': 'external consensus',
    } as Record<string, string>,

    verdict: {
        Bet: 'BET',
        Marginal: 'MARGINAL',
        Skip: 'SKIP',
    } as Record<Verdict, string>,

    outcome: {
        Open: 'Open',
        Won: 'Won',
        Lost: 'Lost',
        Void: 'Void',
        HalfWon: 'Half won',
        HalfLost: 'Half lost',
        CashedOut: 'Cashed out',
    } as Record<BetStatus, string>,

    log: {
        title: 'Positions log',
        empty:
            'Nothing saved yet. Run an analysis and save it; the row is what lets you come back ' +
            'later, type in the closing price, and find out whether the edge was real.',
        savedCount: 'Saved',
        pricedAtClose: 'Priced at close',
        of: (total: number) => `of ${total}`,
        beatClose: 'Beat the close',
        rows: (count: number) => `${count} rows`,
        averageClv: 'Average CLV',
        settledPl: 'Settled P/L',
        settled: (count: number) => `${count} settled`,
        columns: {
            saved: 'Saved',
            match: 'Match',
            market: 'Market',
            entry: 'Entry',
            fair: 'Fair',
            yours: 'Yours',
            edge: 'Edge',
            stake: 'Stake',
            verdict: 'Verdict',
            close: 'Close',
            clv: 'CLV',
            outcome: 'Outcome',
            pl: 'P/L',
        },
        delete: 'Delete',
        edit: 'Edit',
        editHint:
            'The price and the stake are editable because the row means what you actually bet, not ' +
            'what the analysis suggested — live prices move between reading and clicking. On save, ' +
            'the fair line and the edge are re-derived from the new price.',
        save: 'Save',
        cancel: 'Cancel',
    },

    statsPaste: {
        open: 'Import stats from Sofascore ›',
        openNote:
            'Copy the match page from Sofascore, Flashscore or FotMob — all of it, if you like — and ' +
            'paste it here. Out come the minute, the score, the xG and the team names.',
        title: 'Import stats',
        hide: 'Hide',
        note:
            'Open the match page, press Ctrl+A then Ctrl+C, and paste the lot below — there is no ' +
            'need to select the stats panel exactly. The header gives the minute, the score and the ' +
            'team names; the panel gives the xG. Works in Romanian or English.',
        tabTip:
            'On Sofascore, leave the stats tabs on "All". With "1." or "2." selected the xG shown is ' +
            'one half only, and nothing in the paste says so — half a match would be read as all of it.',
        windowTip:
            'For the "Recent window" below: switch the Sofascore stats to "2.", copy again, and put ' +
            'that xG there with the window set to the current minute minus 45. It is the sharpest ' +
            'input the pace correction takes.',
        whatWasRead: 'What was read',
        match: 'Match',
        minute: 'Minute',
        xgHome: 'xG home',
        xgAway: 'xG away',
        score: 'Score',
        alsoRecognised: 'Also recognised, as a check that the right block was copied: ',
        missing: (fields: string) =>
            `Could not find ${fields}. Select the whole stats panel, including its header, or type ` +
            'the missing values into the form by hand.',
        use: 'Use these numbers',
        clear: 'Clear',
        notInPaste: 'not in paste',
        notFound: 'not found',
    },

    stat: {
        xg: 'Expected goals (xG)',
        shotsOnTarget: 'Shots on target',
        bigChances: 'Big chances',
        shots: 'Total shots',
        possession: 'Possession',
        corners: 'Corners',
        saves: 'Goalkeeper saves',
        fouls: 'Fouls',
        yellowCards: 'Yellow cards',
    } as Record<string, string>,

    field: {
        minute: 'the minute',
        xgHome: 'home xG',
        xgAway: 'away xG',
    } as Record<string, string>,

    oddsPaste: {
        open: 'Import odds from copied text ›',
        openNote: 'Paste the odds block and pick your side — the other prices fill themselves in.',
        title: 'Import odds',
        hide: 'Hide',
        note:
            'Copy the market you are looking at — 7777.md, Superbet, Betano, anything — and paste ' +
            'it below. Then click the side you are backing. The remaining prices in that market ' +
            'become the sibling odds, which is the part that is both easy to forget and impossible ' +
            'to de-vig without.',
        noMarket:
            'No complete market found in that. A market needs every one of its prices before the ' +
            "bookmaker's margin can be removed — copy the whole block, both sides included.",
        pickSide: 'Pick the side you are backing',
        clear: 'Clear',
        hint:
            'Nothing is applied until you pick a side. The stats import fills the match state; this ' +
            'fills the market.',
    },

    quickImport: {
        open: 'Paste a match as JSON ›',
        openNote: 'Faster than filling the form field by field while a match is running.',
        title: 'Quick import',
        hide: 'Hide',
        note:
            'Paste one JSON object and every field below fills itself. Names are matched loosely, ' +
            'and selections accept Romanian (Sub, Peste, Da).',
        fill: 'Fill the form',
        loadExample: 'Load example',
        exportCurrent: 'Export current',
        copied: 'Copied',
        readError: 'Could not read that.',
    },

    shot: {
        open: 'Import odds from a screenshot ›',
        openNote:
            'For bookmaker apps whose text cannot be selected: take a screenshot, paste it here and ' +
            'the prices are read for you.',
        title: 'Import odds from a screenshot',
        hide: 'Hide',
        intro:
            'Paste the image with Ctrl+V, drag it onto the area below, or choose a file. The image ' +
            'goes nowhere: the text is read here, in your browser.',
        dropHere: 'Paste (Ctrl+V), drag an image here, or',
        chooseFile: 'choose a file',
        dropActive: 'Drop the image',
        tips:
            'Crop to the odds block and capture at the highest resolution you have. Small, blurry ' +
            'text is the one thing that defeats the reader.',
        reading: 'Reading the image…',
        readingStatus: (percent: number) => `Reading the image… ${percent}%`,
        loadingEngine: 'Starting the recognition engine (a few seconds the first time)…',
        failed: 'The image could not be read.',
        notAnImage: 'That is not an image. Paste a screenshot or choose an image file.',
        clipboardEmpty:
            'There was no image on the clipboard. Take a screenshot (Win+Shift+S), then press ' +
            'Ctrl+V here.',
        confidence: (value: number) => `Mean recognition confidence: ${value}%`,
        lowConfidence:
            'Recognition struggled with this image. Check the text below digit by digit before you ' +
            'analyse.',
        invert: 'Background',
        invertAuto: 'Auto',
        invertAlways: 'Dark (invert)',
        invertNever: 'Light',
        invertedNote: 'The image was inverted: light text on a dark background.',
        fileName: (name: string) => `Image: ${name}`,
        recognised: 'Recognised text — correct it if needed',
        recognisedNote:
            'This is what was made of the image. Anything you fix counts immediately: the markets ' +
            'below are re-read on every keystroke.',
        again: 'Read again',
        clear: 'Clear',
        marketsFound: (count: number) =>
            count === 1 ? 'One complete market found' : `${count} complete markets found`,
        noMarkets:
            'No complete market in that image. A market needs all of its prices before the margin ' +
            'can be removed — include both columns, Under and Over, in the capture.',
        selectAll: 'Select all',
        selectNone: 'Select none',
        margin: (value: string) => `margin ${value}`,
        marginOdd: 'unusual margin — these two prices are probably not two sides of one market',
        analyseSelected: (count: number) =>
            count === 1 ? 'Analyse the selection' : `Analyse all ${count} selections`,
        needLive:
            'A batch needs the live match state: the minute and the xG accrued. Tick "This match is ' +
            'live" and fill them in below.',
        needMinute: 'Fill in the minute and the live xG before running a batch.',
    },

    batch: {
        title: 'Every selection, priced',
        subtitle: (count: number, match: string) =>
            match ? `${count} selections · ${match}` : `${count} selections`,
        running: (done: number, total: number) => `Pricing… ${done} of ${total}`,
        columns: {
            market: 'Market',
            selection: 'Selection',
            odds: 'Price',
            fair: 'Fair',
            yours: 'Yours',
            edge: 'Edge',
            ev: 'EV',
            stake: 'Stake',
            verdict: 'Verdict',
        },
        best: 'Best',
        failed: 'failed',
        sortedBy: 'Sorted by edge, largest first. The verdict uses the same thresholds as a single analysis.',
        openOne: 'Open',
        openOneHint: 'Puts the selection in the form and shows its full working.',
        save: 'Save',
        clear: 'Close the table',
        nothingPositive:
            'Nothing in this capture has a positive edge against the fair line. That is a result, ' +
            'not a failure: most of the time the market is right.',
        disclaimerLive: (minute: string, xgHome: string, xgAway: string) =>
            `Priced at minute ${minute}, xG ${xgHome} – ${xgAway}. If the match has moved on, ` +
            're-read the stats and price it again.',
    },
};

const DICTIONARIES: Record<Lang, Dictionary> = { ro, en };

export const dictionaryFor = (lang: Lang): Dictionary => DICTIONARIES[lang] ?? ro;

export const LANGUAGES: ReadonlyArray<Lang> = ['ro', 'en'];

/** Which number and date conventions each language brings with it. */
const LOCALES: Record<Lang, string> = { ro: 'ro-RO', en: 'en-GB' };

/**
 * Defaulting to Romanian rather than to the browser's language: the bookmaker this is used against
 * is Romanian, and an interface that silently switches to English on a different machine is a worse
 * surprise than one that is always in the language the odds are in.
 */
export const DEFAULT_LANG: Lang = 'ro';

/** "Total goluri 2.5" — the market kind in the reader's language, plus the line when there is one. */
export const marketTitle = (t: Dictionary, kind: MarketKind, line: number | null): string =>
    line === null ? t.marketKind[kind] : `${t.marketKind[kind]} ${line}`;

/**
 * The server's short codes for how a stake was sized and how a probability was reached, said in the
 * reader's language. An unknown code is shown as it arrived rather than hidden: a new code on the
 * server should look unfamiliar, not invisible.
 */
export const capReasonLabel = (t: Dictionary, code: string): string => t.capReason[code] ?? code;

export const methodLabel = (t: Dictionary, code: string): string => t.method[code] ?? code;

/** A recognised stat row and a missing input field, named in the reader's language. */
export const statLabel = (t: Dictionary, row: { id: string; label: string }): string =>
    t.stat[row.id] ?? row.label;

export const fieldLabel = (t: Dictionary, field: string): string => t.field[field] ?? field;

/** The same, in the short form a table column has room for. */
export const marketTitleShort = (t: Dictionary, kind: MarketKind, line: number | null): string =>
    line === null ? t.marketKindShort[kind] : `${t.marketKindShort[kind]} ${line}`;

/** A parser issue, said in the reader's language. */
export function describeIssue(t: Dictionary, issue: ParseIssue): string {
    return issue.kind === 'incomplete'
        ? t.parse.incomplete(marketTitle(t, issue.market, issue.line), issue.found, issue.required)
        : t.parse.headingWithoutPrices(marketTitle(t, issue.market, null));
}

export interface LanguageState {
    lang: Lang;
    setLang: (lang: Lang) => void;
    t: Dictionary;
}

export const LanguageContext = createContext<LanguageState>({
    lang: DEFAULT_LANG,
    setLang: () => {},
    t: ro,
});

/** The copy for the current language. */
export const useT = (): Dictionary => useContext(LanguageContext).t;

/** The locale to format money and dates with — the chosen language's, never the browser's. */
export const useLocale = (): string => LOCALES[useContext(LanguageContext).lang] ?? LOCALES.ro;

/** The current language and the setter behind the switch in the header. */
export const useLanguage = (): LanguageState => useContext(LanguageContext);
