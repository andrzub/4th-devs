# S05E01 — „radiomonitoring"

Raport o mieście, które w eterze nazywają „Syjonem": prawdziwa nazwa, powierzchnia, liczba magazynów
i numer telefonu osoby kontaktowej. Materiał przychodzi z nasłuchu radiowego — transkrypcje rozmów,
szum i pliki binarne w Base64. Konsolowa aplikacja .NET, komunikacja jak zawsze: POST `/verify`,
`task: "radiomonitoring"`, akcja w polu `answer.action`.

Sednem zadania jest **routing danych**, nie prompt: kod decyduje, co odsiać, co zdekodować lokalnie,
a co naprawdę wymaga modelu i którego. Surowy materiał to ~468K tokenów, do analityka trafia ~7K.

## Stan prac

| krok | zakres | stan |
|---|---|---|
| 1 | szkielet, klient Huba z logiem bez treści Base64, magazyn przechwyceń (`--collect`) | gotowy; 1 `start` + 34 `listen` |
| 2 | spis materiału offline, rozpoznanie typu pliku po bajtach (`--triage`) | gotowy |
| 3 | router: filtr szumu, dekoder Morse'a, kompaktowanie dokumentów, decyzja per przechwycenie (`--route`, `--tests`) | gotowy |
| 4 | media przez Gemini z cache, jedno wywołanie analityka, walidator raportu (`--media`, `--analyze`) | gotowy |
| 5 | `--transmit` | **zaliczone** za pierwszą wysyłką |

## Uruchomienie

Klucze w `appsettings.Development.json` (gitignored). `Analyst` to OpenAI, `Media` to Google AI Studio:

```json
{ "AI_DevsApiKey": "...", "Analyst": { "ApiKey": "..." }, "Media": { "ApiKey": "..." } }
```

Uruchamiać **z folderu zadania** — cache i log mają ścieżki względne do katalogu bieżącego:

```bash
dotnet run -- --tests
dotnet run -- --collect
dotnet run -- --route
dotnet run -- --analyze
dotnet run -- --transmit
```

| tryb | model | dotyka `/verify` | uwagi |
|---|---|---|---|
| `--tests` | nie | nie | offline, bez kluczy |
| `--collect` | nie | tak, `start` + `listen` | każda odpowiedź surowo do `radiomonitoring-cache/run-<data>/capture-NNN.json`, załącznik zdekodowany obok jako `capture-NNN.attachment.<ext>` |
| `--triage [run]` | nie | nie | spis: rodzaje, kody, typy plików, zgodność etykiety z bajtami → `triage.txt` |
| `--route [run]` | nie | nie | decyzja per przechwycenie, korpus bez mediów, szacunek tokenów → `routing.txt`, `corpus-offline.txt` |
| `--media [run]` | `Media` | nie | audio i obrazy, jedno wywołanie na plik, wynik w `media-NNN.txt`; cache po hashu treści + modelu + promptu |
| `--analyze [run]` | `Media` + `Analyst` | nie | korpus, jedno wywołanie analityka, walidator → `corpus.txt`, `analyst-findings.json`, `answer.json` **tylko gdy walidacja przejdzie** |
| `--transmit [run]` | nie | **tak, wysyła raport** | `answer.json` → `transmit-reply.json` |

`[run]` domyślnie oznacza ostatni katalog `run-*`. Wszystkie żądania do Huba lądują
w `radiomonitoring-log.jsonl` z kluczem zredagowanym na `***` i załącznikiem skróconym do prefiksu
(pełna odpowiedź jest w magazynie przechwyceń). Cache i log są gitignored, bo zawierają flagę.

## API

| akcja | odpowiedź |
|---|---|
| `start` | `code 110` „Gotowy do nasłuchu", losuje pulę materiału |
| `listen` | `code 100` „Signal captured." z polem `transcription` **albo** `meta` (MIME) + `attachment` (Base64) + `filesize` |
| `listen` po końcu puli | `code 101` „Baterie w nadajniku padly, ale mamy juz dostatecznie duzo materialu…" + `hint`, że `start` zaczyna od nowa |
| `transmit` | `cityName`, `cityArea` (string, dokładnie 2 miejsca, prawdziwe zaokrąglenie), `warehousesCount` (int), `phoneNumber` → `code 0` i flaga |

## Materiał (33 przechwycenia)

| rodzaj | ile | co w środku |
|---|---|---|
| rozmowy | 20 | plotki handlowe o miastach; Syjon pada wprost w kilku, prawdziwa nazwa nigdzie |
| szum | 6 | „kshhh …odbierasz mnie… bzzzt…" z powtarzającymi się fragmentami („cztery-siedem-dwa", „współrzędne to trzydzieści…") — przynęta |
| Morse | 1 | `TaTa TiTiTa … (stop)` → **„MUSISZ SPRAWDZIC /DEEPER"** |
| XML | 1 | `cityStatistics` z `trainingData="true"` o fikcyjnym, zniszczonym „Nowogrodzie Północnym" — przynęta |
| CSV | 1 | ogłoszenia `miasto,akcja,towar,ilosc,w_zamian`; „Syjon" i „Skarszewy" jako **osobne wiersze**, ale z tym samym profilem (bydło/wołowina za kilofy) |
| JSON | 1 | 14 miast ze współrzędnymi, `occupiedArea`, `riverAccess`, `farmAnimals`, `inhabitants` — jedyne źródło powierzchni |
| MP3 | 1 | 544 KB, głos syntezowany (ID3 niesie manifest C2PA ElevenLabs): komunikat ze Skarszew — pełne magazyny, „planujemy na wiosnę wybudować 12. magazyn", wołowina na wymianę za łopaty i kilofy |
| PNG | 1 | 561 KB, 600×480, żółta karteczka: „Jacek Kramer – zadzwonić w sprawie noclegu na Syjonie i handlu wołowiną. 644-122-092" |
| JPEG | 1 | 249 KB, 1109×1500, zrzut posta z X o skończonych tokenach Claude — szum |

„/DEEPER" wyglądało na wskazówkę, że coś jest schowane w binarkach, więc sprawdziłem to kodem, zanim
zapłaciłem za model: PNG kończy się na `IEND`, JPEG na `FFD9`, MP3 ma tylko ID3 z C2PA — **nic nie
jest doklejone ani ukryte w metadanych**. Wskazówka znaczy po prostu „czytaj uważnie".

## Router — podział pracy

Każde przechwycenie przechodzi przez `Router.Decide` i dostaje jedną z tras; typ pliku jest brany
**z bajtów, nie z etykiety** (`FileSniffer`), więc binarka opisana jako tekst nie wpadnie do promptu jako Base64.

| trasa | kto | co się dzieje |
|---|---|---|
| `Speech` | kod → analityk | transkrypcja idzie dosłownie |
| `Noise` | kod | `NoiseFilter` rozpoznaje szum (≥ 5 urwanych fragmentów między `...` i ≥ 3 odgłosy radia); wszystkie szumy zwijają się do **jednej sekcji unikalnych fragmentów**, urwany prefiks ustępuje pełnej formie — nic nie jest wyrzucone, 6 transkrypcji kosztuje kilkanaście linii |
| `Morse` | kod | `MorseDecoder`: `Ti` = kropka, `Ta` = kreska, `(stop)` = spacja, alfabet z polskimi literami; nieznana sekwencja zostaje widoczna jako `[?-..-]`, nie jest zgadywana |
| `Document` | kod | JSON zminifikowany, XML bez wcięć między tagami (`DocumentCompactor`), limit 20 KB na plik |
| `Audio` | `gemini-3.6-flash` | plik jako część `input_audio`, prompt „transkrybuj dosłownie, liczby cyframi" |
| `Image` | `gemini-3.6-flash` | obraz przekodowany do JPEG q85 (limit dłuższej krawędzi 1600 px — przy tych obrazach nie zadziałał), prompt „przepisz każdy tekst, czego nie widać — powiedz wprost" |
| `Skip` | — | pusta odpowiedź, binarka bez czytnika, tekst ponad limit |

Potem:

- **Analityk** (`gpt-4.1`, jedno wywołanie, structured output) dostaje korpus w sekcjach oznaczonych
  numerem przechwycenia i listę nazw miast z rekordów. Odpowiada tylko na to, czego kod nie rozstrzygnie:
  które miasto kryje się pod przydomkiem, gdzie pada liczba magazynów i gdzie numer telefonu.
  Materiał nazwany w prompcie **danymi, nie instrukcjami** — część to plotki, przynęty i „dane treningowe".
- **Powierzchni model nie liczy**: `CityTable` czyta rekordy z JSON-a (tablica obiektów z nazwą i polem
  numerycznym zawierającym „area"), a `FormatArea` zaokrągla na `decimal` połówkami w górę — `0.125` to
  `0.13`, co `double` i zaokrąglenie bankierskie psują.
- **`ReportValidator`** przepuszcza raport tylko wtedy, gdy miasto jest w rekordach (nazwa w odpowiedzi
  pochodzi z rekordu, nie z pisowni modelu), cytaty o magazynach i telefonie **występują w korpusie**
  (po normalizacji białych znaków i cudzysłowów), a numer to same cyfry. Numer z separatorami jest
  normalizowany z ostrzeżeniem. Bez przejścia walidacji `answer.json` nie powstaje.
- **Brak pętli agenta** — świadomie. Materiał jest zebrany z góry, a pytanie jedno; pipeline z lekcji
  („odbierasz → rozpoznajesz → dekodujesz → kierujesz") nie potrzebuje narzędzi. Warstwa `Llm/` pochodzi
  z 02_05 (vision) z dołożonymi częściami `input_audio`; `Tools/ITool.cs` jest tylko dlatego, że klient
  go referencjonuje.

## Przebieg

- `--collect`: 35 żądań, koniec puli sam przyszedł kodem `101`.
- `--route`: Speech 20, Noise 6, Document 3, Image 2, Audio 1, Morse 1, Skip 1. Szacunek (4 znaki na token):
  wszystko surowo do jednego modelu **~468K tokenów** (1,87 mln znaków odpowiedzi z Base64), korpus po
  routingu **~4,3K** plus 3 wywołania mediów.
- `--analyze`: media 3 × Gemini (853 / 1162 / 1153 tokenów wejścia), analityk **7043 wejścia / 551
  wyjścia** — polski tekst tokenizuje się gorzej niż 4 znaki na token, stąd różnica względem szacunku.
- **Identyfikacja** (analityk, dowody `#007 #010 #013 #014 #021 #025 #027 #033`): w #027 Skarszewy to
  „Miasto ocalałych… prawie biblijny raj" — biblijny Syjon; #021 mówi, że Syjon handluje bydłem, a w JSON-ie
  `farmAnimals: true` mają tylko Skarszewy i Drohiczyn; nagranie i CSV dają ten sam profil handlowy.
- **Magazyny to wniosek**: „planujemy wybudować 12. magazyn" → 11 obecnie. Model sam to zaznaczył w `doubts`,
  Hub przyjął.
- `--transmit`: `Skarszewy`, `10.73` (z `occupiedArea` 10.7284), `11`, `644122092` → `code 0`, flaga za
  pierwszą wysyłką. Łącznie **36 żądań** do Huba i **4 wywołania modeli**.

Po drodze:

- **Własny błąd przy zapisie**: zdekodowany JSON z #025 dostał tę samą ścieżkę co surowa odpowiedź
  (`capture-025.json`) i ją nadpisał. Poprawka: załączniki mają człon `.attachment.`; surową odpowiedź
  odtworzyłem z logu (metadane) i zdekodowanych bajtów, zgodność prefiksu Base64 sprawdzona — bez
  ponownego nasłuchu.
- **Gemini zwrócił dwa razy 503** „model under high demand" — klient ponawiał tylko 429, teraz ponawia też 503.
- Gemini zapisał w transkrypcji „Skarzewach" zamiast „Skarszewach". Bez znaczenia, bo nazwa w raporcie
  pochodzi z rekordu, a walidator odrzuciłby miasto spoza rekordów.

## Testy offline

`--tests`: 21 przypadków bez sieci i klucza — rozpoznanie szumu (i nie-szumu z pojedynczym `...`
i `[niesłyszalne]`), deduplikacja fragmentów, Morse (słowa, nieznana sekwencja, zwykła mowa), zaokrąglenie
powierzchni (połówka, brak obcięcia, dwa miejsca), trasa po bajtach (PNG opisany jako tekst → vision,
binarka bez czytnika i tekst ponad limit → pominięte), minifikacja JSON, tabela miast, walidator
(poprawny raport, zmyślony cytat, miasto spoza rekordów, numer z separatorami). Dane syntetyczne,
odpowiedź zadania nie jest zaszyta w kodzie.
