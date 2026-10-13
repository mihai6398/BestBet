# Ghid de rulare — BestBet

Aplicația poate fi pornită în două feluri. Alege în funcție de ce vrei să faci:

| | Docker | Local (Visual Studio / CLI) |
|---|---|---|
| **Când** | Vrei doar s-o folosești | Vrei să modifici codul |
| **Îți trebuie** | Doar Docker Desktop | .NET 8 SDK + Node 22 |
| **Pornire** | O comandă | Două procese |
| **Hot reload** | Nu | Da |
| **Adresa** | <http://localhost:8080> | <https://localhost:5173> |

---

## 1. Docker — modul recomandat pentru folosire

### Ce îți trebuie

[Docker Desktop](https://www.docker.com/products/docker-desktop/) pornit, cu containere Linux
(setarea implicită pe Windows). Nu ai nevoie nici de .NET, nici de Node instalate.

### Pornire

Din rădăcina proiectului (`C:\Documents\GitHub\BestBet`):

```bash
docker compose up -d --build
```

Prima rulare durează câteva minute — se descarcă imaginile de bază și se instalează pachetele npm.
Rulările următoare sunt de ordinul secundelor, pentru că Docker refolosește straturile.

Deschide apoi **<http://localhost:8080>**. Atât — interfața și API-ul sunt servite de același
container, pe același port.

### Comenzi de zi cu zi

```bash
docker compose logs -f
```

```bash
docker compose restart
```

```bash
docker compose down
```

`down` oprește și șterge containerul, **dar nu și datele** — jurnalul de poziții stă într-un volum
separat. Ca să pornești din nou, `docker compose up -d`.

### După ce modifici codul

```bash
docker compose up -d --build
```

`--build` este obligatoriu: fără el Docker repornește imaginea veche și te întrebi de ce nu se vede
modificarea.

### Unde sunt datele

Jurnalul de poziții este un fișier SQLite într-un volum Docker numit `bestbet_bestbet-data`. Supravie-
țuiește oricărui `restart`, `down` sau `up --build`.

Ca să faci o copie de siguranță, oprește întâi containerul — SQLite ține scrierile recente într-un
fișier `-wal` separat, iar o copie făcută „la cald” le-ar putea pierde:

```bash
docker compose stop
```

```bash
docker cp bestbet:/app/data/bestbet.db ./bestbet-backup.db
```

```bash
docker compose start
```

Restaurarea merge invers: `docker compose stop`, apoi
`docker cp ./bestbet-backup.db bestbet:/app/data/bestbet.db`, apoi `docker compose start`.

Ca să ștergi tot și s-o iei de la zero:

```bash
docker compose down -v
```

`-v` șterge și volumul. **Datele dispar definitiv.**

### Reglaje

Se schimbă în `docker-compose.yml`:

- **Alt port**, dacă 8080 e ocupat — schimbă doar cifra din stânga: `"9000:8080"`, apoi
  <http://localhost:9000>.
- **Swagger**, dacă vrei să te joci cu API-ul direct — pune `ASPNETCORE_ENVIRONMENT: Development` și
  vei avea documentația interactivă la `/swagger`. În `Production` este dezactivat intenționat.

---

## 2. Local — modul pentru dezvoltare

### Ce îți trebuie

- .NET 8 SDK
- Node.js 22.12+ (sau 20.19+) — Vite 8 refuză versiuni mai vechi
- Certificatul de dezvoltare ASP.NET, o singură dată:

  ```bash
  dotnet dev-certs https --trust
  ```

### Din Visual Studio

Deschide `BestBet.sln` și apasă **F5**. Pornește singur și API-ul, și serverul Vite, și îți deschide
browserul. Nu mai ai nimic de făcut.

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
de pe `https://localhost:7243`, deci nu ai nevoie de configurări suplimentare.

Modificările din `bestbet.client/src` apar instantaneu în browser. Modificările din C# cer repornirea
primului proces (sau `dotnet watch run` în loc de `dotnet run`).

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
| **Regresie la medie** | Trage ritmul observat spre un meci mediu (1.35 xG per echipă) | Un ritm citit din 36 de minute e o estimare pe eșantion mic, nu un fapt. Face aproape toată treaba: singură, duce *Peste 6.5* de la 76% la 50% |
| **Starea scorului** | Echipa care conduce atacă mai puțin, cea care urmărește mai mult | Efectul crește spre finalul meciului, deci e scalat după cât s-a jucat |
| **Recență** | Cântărește ce face meciul *acum* peste media lui | Prinde jocul care tocmai s-a deschis, sau care a murit |

Pe meciul de mai sus, cu toate pornite: ritm **6.70 → 4.29 xG/90**, estimare **47.5%**, edge
**−2.5pp**, verdict **SKIP**. Adică răspunsul corect.

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

Doar textul căutării, și doar când apeși *Caută*. Este singura cerere pe care aplicația o face
vreodată în afară. Nu pleacă nimic altceva: nici capitalul, nici miza, nici jurnalul, nici capturile
de ecran. Nu există cont, cheie sau autentificare. Dacă Polymarket nu răspunde, primești un mesaj și
restul aplicației funcționează neschimbat — citirea capturilor și a statisticilor merg în continuare
fără internet.

Marja referinței este afișată lângă ea tocmai ca verificare: o piață de predicții arată ~0%. Dacă
vezi 8%, nu te uiți la o piață fără marjă.

---

## 6. Jurnalul de poziții: corectare și recalculare

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

## 7. Testele

```bash
dotnet test
```

236 de teste, sub o secundă. Printre ele, în
`BestBet.Server.Tests/Acceptance/SpecWorkedExamplesTests.cs`, exemplele numerice din specificație —
acelea sunt definiția lui „matematica e încă bună”. Dacă vreunul pică, se repară întâi el.

Pentru client (151 de teste pe parserele de import, pe citirea capturilor și pe analiza în bloc):

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

## 8. Verificare rapidă că merge

După pornire, indiferent de mod, cazul de acceptanță din specificație (Sunderland–Arsenal, minutul 45)
trebuie să dea exact aceste cifre:

```bash
curl -s -X POST http://localhost:8080/api/analyze -H "Content-Type: application/json" -d "{\"market\":{\"kind\":\"NextGoal\",\"label\":\"Primul gol: 2\",\"decimalOdds\":1.60,\"siblingOdds\":[3.20,4.50]},\"liveState\":{\"minutePlayed\":45,\"cumulativeXgHome\":0.13,\"cumulativeXgAway\":0.86,\"currentGoalsHome\":0,\"currentGoalsAway\":0,\"isPreMatch\":false},\"bankroll\":263.0,\"selection\":\"NextGoalAway\"}"
```

Estimarea proprie trebuie să fie **0.5459**, EV **−0.1266**, verdictul **Skip**, miza **0**.

---

## 9. Când ceva nu merge

**`docker compose up` se oprește cu „port is already allocated”**
Ai deja ceva pe 8080. Schimbă portul din stânga în `docker-compose.yml` (`"9000:8080"`).

**Pagina se încarcă, dar tabelul de poziții e gol și apare o eroare de rețea**
Rulezi în modul local și API-ul nu e pornit. Interfața are nevoie de ambele procese.

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
docker compose exec bestbet sh
```

Baza de date este la `/app/data/bestbet.db`.
