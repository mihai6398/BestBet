# Datele motorului de recunoaștere (OCR)

`lang/eng.traineddata.gz` este modelul de recunoaștere folosit de importul din captură de ecran.

Stă aici, și nu este luat de pe un CDN, din două motive: o captură a contului tău de pariuri nu
trebuie să ajungă pe serverul nimănui, iar importul trebuie să meargă și fără conexiune la internet.
Motorul propriu-zis și nucleul WebAssembly vin din `node_modules` și sunt împachetate de Vite la
build; doar fișierul ăsta trebuie adus separat.

Dacă lipsește (importul din captură dă eroare la pornirea motorului), adu-l înapoi cu:

```bash
curl -sSL -o bestbet.client/public/tesseract/lang/eng.traineddata.gz \
  https://tessdata.projectnaptha.com/4.0.0_fast/eng.traineddata.gz
```

Modelul `eng` este de ajuns: din blocul de cote se citesc cifre și câteva cuvinte latine
(`Peste`, `Sub`, `Da`, `Nu`, `1 X 2`), iar diacriticele românești nu schimbă nicio cotă. Varianta
`_fast` are 1,9 MB în loc de 10 MB și citește o captură curată la fel de bine.

Licență: Apache 2.0, proiectul tesseract-ocr/tessdata_fast.
