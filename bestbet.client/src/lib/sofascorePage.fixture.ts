/**
 * A real Sofascore match page, copied whole.
 *
 * Not a tidied extract: the point of this fixture is everything around the stats panel, because
 * selecting just the panel is the part a user will not bother doing at half time. The middle of the
 * page — standings, voting widgets, the footer's league lists — is cut for length, but every shape
 * that could be mistaken for the numbers we want is kept:
 *
 *  - a title line with a dash between two club names, which is not a scoreline;
 *  - the header stacked vertically, full name over abbreviation, with the clock inside it;
 *  - a kickoff date and time, as its own line and glued together, either of which reads as minute 23;
 *  - "Final" as a tab label beside "În direct", on a match that is very much not finished;
 *  - a goal timeline full of minutes, and a half-time score in the momentum strip;
 *  - a single shot's xG in the shot map, which must not become the match xG;
 *  - a second copy of the stats further down, and an "Expected goals on target" row next to it.
 */
export const SOFASCORE_WHOLE_PAGE = `
logo

Search Sofascore
CtrlK
User image
FotbalPortugaliaLiga Portugal Betclic, Repriză 8FC Arouca - CF Estrela Amadora scor live, rezultate H2H, clasament și predicție
FC Arouca
Arouca
1 - 0
54:53
CF Estrela Amadora
Estrela Amadora
Ivan Barbero 5'
11.10.2026
22:30

Liga Portugal Betclic
Estádio Municipal de Arouca
FinalÎn direct
1
1.44
X
4.00
2
9.50

Momentum meci
FC Arouca
CF Estrela Amadora
Repriză 2 1 - 0
50'
M. Scholze
Fault
HT 1 - 0
45' +8
A. Marcus
Protest
45' +6
P. Gozálbez
Fault
Timp suplimentar 5
5'
I. Barbero
N. Djouahra
Dată și oră
11.10.202622:30

Competiție
Fotbal, Portugalia, Liga Portugal Betclic, Repriză 8
Arbitru
Luis Carlos Mateus Filipe
Medie cartonașe0.244.61
Echipe de start
Statistici
Clasament
H2H

All

1.

2.
Analiza meciului
42%
Posesia mingii
58%
0.40
Goluri estimate (xG)
1.38
2
Șanse mari
1
5
Șuturi
9
2
Intervenții de portar
1
4
Cornere
3
7
Faulturi
4
226
Pase
314
2
Cartonașe galbene
2
Harta șuturilor

FC Arouca

CF Estrela Amadora
Tiago Esgaio
T. Esgaio
45' +5
xG
0.25
xGOT
0.33
Rezultat
Apărate
Attack map
Șuturi
5
Șuturi
9
2
Șuturi pe poartă
2
0.40
Goluri estimate (xG)
1.38
0.90
Expected goals on target
0.55
0
Bare
1
Portar
2
Intervenții de portar
1
0.55
Goluri evitate
-0.10
Detalii despre meci
`;
