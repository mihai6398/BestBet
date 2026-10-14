import { createContext, useContext } from 'react';
import { DISCLAIMER } from '../types/domain';
import type { MarketKind, MarketSelection, PositionOutcome, Verdict } from '../types/domain';
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

    bankroll: {
        label: 'Capital (bankroll)',
        note:
            'Rămâne doar în acest browser și ajunge la API exclusiv ca să transforme un procent de ' +
            'miză într-o sumă. Nu se stochează pe server.',
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

    monitor: {
        open: 'Monitorizează meciul în timp real ›',
        openNote:
            'Pariurile deschise se recalculează singure, pe măsură ce meciul merge mai departe. ' +
            'Datele vin din fila Sofascore pe care o ai deja deschisă.',
        title: 'Monitorizare în timp real',
        hide: 'Ascunde',
        why:
            'Sofascore refuză orice cerere care nu vine dintr-un browser real — răspunde 403 inclusiv ' +
            'la robots.txt — deci serverul acestei aplicații nu are cum să întrebe singur. Browserul ' +
            'tău are voie, fiindcă acolo pagina e deja deschisă. Semnul de carte de mai jos citește ' +
            'pagina pe care o ai în față și o trimite încoace; nu se cere nimic în plus de la ei.',
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
        waiting: 'Se așteaptă prima citire din browser…',
        lastReading: (seconds: number) =>
            seconds < 60
                ? `Ultima citire acum ${seconds} s`
                : `Ultima citire acum ${Math.round(seconds / 60)} min`,
        reading: (minute: number, home: number, away: number, xgHome: number, xgAway: number) =>
            `minutul ${minute} · ${home}–${away} · xG ${xgHome}–${xgAway}`,
        unknownMatch: 'meci nerecunoscut',
        unusable:
            'Citirea a ajuns, dar nu conține tot ce trebuie. Pe Sofascore mergi la fila Statistici ' +
            'și lasă filele pe „All" — de acolo vine xG-ul.',
        stale:
            'Citirea e veche. Fila s-a închis, a adormit, sau ai oprit semnul din favorite. Până ' +
            'revine, numerele de mai jos sunt dintr-un minut care a trecut.',
        tracking: (labels: string) => `Se recalculează: ${labels}`,
        nothingTracked:
            'Niciun pariu deschis din meciul ăsta. Citirea actualizează formularul, dar nu are ce ' +
            'recalcula.',
        clear: 'Uită citirea',
        privacy:
            'Citirea circulă doar între browserul tău și aplicația de pe calculatorul tău. Nu se ' +
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
            'Un ritm citit din câteva minute este o estimare, nu un fapt, așa că este tras spre un ' +
            'meci mediu de ligă și ajustat pentru echipa care apără un avantaj. Oprește opțiunea ca ' +
            'să vezi extrapolarea brută.',
        rawNote:
            'Extrapolare brută: ritmul de pe ceas este dus liniar până la final. O primă repriză ' +
            'intensă proiectează atunci un scor care aproape niciodată nu se întâmplă.',
        windowNote:
            'Opțional, dar cel mai valoros câmp de aici: împărțirea pe reprize a furnizorului de ' +
            'statistici. Ia repriza a doua ca tot meciul minus prima repriză, iar modelul va cântări ' +
            'ce face jocul acum mai mult decât media lui.',
        windowMinutes: 'Fereastra recentă (min)',
        recentXgHome: 'xG gazde în ea',
        recentXgAway: 'xG oaspeți în ea',
        preMatchNote:
            'Înainte de start nu există date live din care să iasă o probabilitate, deci estimarea ' +
            'trebuie să fie a ta. Tot ce urmează — avantajul, miza, verdictul — valorează exact cât ' +
            'valorează acest singur număr.',
        yourProbability: 'Probabilitatea ta (%)',
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
        save: 'Salvează în jurnal',
        saved: 'Salvat în jurnal',
        saving: 'Se salvează…',
        saveHint:
            'Salvarea reține cota de intrare, ca să poți completa mai târziu cota de închidere și ' +
            'să afli dacă procesul chiar bate piața.',
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
        Pending: 'În așteptare',
        Won: 'Câștigat',
        Lost: 'Pierdut',
        Void: 'Anulat',
    } as Record<PositionOutcome, string>,

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

    bankroll: {
        label: 'Bankroll',
        note:
            'Kept in this browser only, and sent to the API purely to turn a stake percentage into ' +
            'an amount. It is never stored server-side.',
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

    monitor: {
        open: 'Watch the match live ›',
        openNote:
            'Open bets re-price themselves as the match moves on. The readings come from the ' +
            'Sofascore tab you already have open.',
        title: 'Live monitoring',
        hide: 'Hide',
        why:
            'Sofascore refuses any request that is not a real browser — it answers 403 down to ' +
            'robots.txt — so this app\u2019s server cannot ask on its own. Your browser is allowed, ' +
            'because that is where the page is already open. The bookmarklet below reads the page in ' +
            'front of you and sends it here; nothing extra is requested from them.',
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
        waiting: 'Waiting for the first reading from the browser…',
        lastReading: (seconds: number) =>
            seconds < 60 ? `Last reading ${seconds}s ago` : `Last reading ${Math.round(seconds / 60)} min ago`,
        reading: (minute: number, home: number, away: number, xgHome: number, xgAway: number) =>
            `minute ${minute} · ${home}–${away} · xG ${xgHome}–${xgAway}`,
        unknownMatch: 'match not recognised',
        unusable:
            'A reading arrived but it does not carry everything needed. On Sofascore, go to the ' +
            'Statistics tab and leave the tabs on "All" — that is where the xG is.',
        stale:
            'The reading is old. The tab closed, went to sleep, or the bookmark was switched off. ' +
            'Until it comes back, the numbers below are from a minute that has passed.',
        tracking: (labels: string) => `Re-pricing: ${labels}`,
        nothingTracked:
            'No open bet on this match. The reading still fills the form, but there is nothing to ' +
            're-price.',
        clear: 'Forget the reading',
        privacy:
            'The reading only travels between your browser and the app on your own machine. It is ' +
            'not saved anywhere and goes when the app stops.',
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
            'towards a league-average match and adjusted for the side protecting a lead. Switch ' +
            'this off to see the raw extrapolation instead.',
        rawNote:
            'Raw extrapolation: whatever pace the clock shows is run flat to full time. A hot first ' +
            'half then projects a scoreline that almost never happens.',
        windowNote:
            "Optional, and the sharpest input here: the provider's per-half split. Take the second " +
            'half as all match minus first half and the model will weight what the game is doing ' +
            'now over its average.',
        windowMinutes: 'Recent window (min)',
        recentXgHome: 'xG home in it',
        recentXgAway: 'xG away in it',
        preMatchNote:
            'Before kick-off there is no live data to derive a probability from, so the estimate ' +
            'has to be yours. Everything downstream — the edge, the stake, the verdict — is only as ' +
            'good as this one number.',
        yourProbability: 'Your probability (%)',
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
        save: 'Save to positions log',
        saved: 'Saved to log',
        saving: 'Saving…',
        saveHint:
            'Saving records the entry price so you can fill in the closing price later and find out ' +
            'whether this process actually beats the market.',
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
        Pending: 'Pending',
        Won: 'Won',
        Lost: 'Lost',
        Void: 'Void',
    } as Record<PositionOutcome, string>,

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
