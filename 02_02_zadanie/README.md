# S02E02 — "electricity"

Agent z Function Calling rozwiązujący puzzle elektryczne 3x3: doprowadzenie prądu do trzech
elektrowni przez obracanie pól planszy (90° w prawo) tak, aby układ kabli odpowiadał schematowi
docelowemu. Każdy obrót to osobny POST na `/verify`, więc uruchomienie pętli jest opt-in.

## Architektura

- **Pętla agenta**: OpenAI `gpt-4.1` (sekcja `OpenAI` w konfiguracji).
- **Vision**: Gemini przez **endpoint zgodny z OpenAI**
  (`https://generativelanguage.googleapis.com/v1beta/openai`), darmowy w Google AI Studio.
  Domyślnie stabilny `gemini-3.6-flash` — lekcja poleca `gemini-3-flash-preview`, ale
  free tier modelu preview to tylko 20 zapytań/dzień, a `gemini-2.5-flash` nie jest już
  dostępny dla nowych kont (404 z API). Obaj providerzy używają tej samej klasy
  `OpenAiCompatibleLlmClient`, różnią się wyłącznie konfiguracją (`LlmProviderSettings`).
- **Throttling w kliencie**: free tier Gemini to ~10 zapytań/min, więc klient wymusza
  minimalny odstęp między żądaniami (`MinSecondsBetweenRequests`, domyślnie 7 s) i serializuje
  wywołania przez `SemaphoreSlim`. Retry na 429 z `Retry-After`.

## Decyzje projektowe

- **Vision jako narzędzie, nie część głównej pętli** (wskazówka z lekcji): agent nigdy nie
  widzi obrazka. `read_board`/`read_target` zwracają tekstowy opis planszy zbudowany z
  wycinków kafelków — wycinki usuwają niejednoznaczność adresowania siatki.
- **Wszystkie 9 kafelków w jednym zapytaniu vision**: free tier `gemini-3-flash` (preview)
  to tylko **20 zapytań dziennie** (błąd 429 `GenerateRequestsPerDayPerProjectPerModel-FreeTier`),
  więc wariant „kafelek = zapytanie" spalał cały budżet w dwóch odczytach planszy.
  Model dostaje 9 obrazków w kolejności row-major i odpowiada JSON-em z dziewięcioma
  kluczami (`{"1x1": ["top", "left"], ...}`). Bez `MaxTokens` — to model „myślący",
  tokeny rozumowania wliczają się w limit i mały limit ucina odpowiedź w połowie.
- **Detekcja siatki zamiast sztywnych współrzędnych** (`BoardImageSlicer`): plansza (800x450)
  i schemat docelowy (598x419) mają różne proporcje. Linie siatki wykrywane są jako
  wiersze/kolumny z długimi ciągłymi przebiegami ciemnych pikseli (≥35% wymiaru obrazu) —
  tytuł i ikony elektrowni mają tylko krótkie przebiegi. Kafelki są skalowane 3x przed
  wysłaniem do modelu.
- **Reprezentacja pola jako zbiór krawędzi** U/R/D/L + nazwana forma (straight/corner/T/cross).
  Obrót w prawo to deterministyczne mapowanie U→R→D→L→U, opisane wprost w prompcie —
  agent sam porównuje stan z celem i wylicza liczbę obrotów (podejście polecane w lekcji).
- **Flaga wykrywana w kodzie** (`MissionState` + regex), jak w S02E01: pętla odmawia
  zakończenia bez prawdziwej flagi w wyniku narzędzia (max 3 ponaglenia), a prompt zakazuje
  zmyślania flagi.
- **Cache**: opis schematu docelowego jest statyczny — vision liczy go raz, wynik ląduje
  w `board-cache/target-description.txt`. Wszystkie wycinki kafelków też trafiają do
  `board-cache/` do ręcznej inspekcji.
- **Log**: każde żądanie do Huba (pobrania obrazka i obroty) ląduje w `electricity-log.jsonl`
  z kluczem API zredagowanym.

## Uruchomienie

1. Uzupełnij `appsettings.Development.json` (gitignored): `AI_DevsApiKey`, `OpenAI:ApiKey`,
   `Gemini:ApiKey` (darmowy klucz: https://aistudio.google.com/apikey).
2. Test vision bez dotykania `/verify` (tylko endpointy danych):

   ```
   dotnet run -- --describe
   ```

   Wypisze opisy celu i aktualnej planszy; wycinki kafelków lądują w `board-cache/`.
3. Pełna pętla agenta (**każdy obrót to żądanie na `/verify`**):

   ```
   dotnet run -- --run
   ```

   `--reset` (łączy się z oboma trybami) przywraca planszę do stanu początkowego.

Debug: `dotnet run -- --slice <plik.png>` tnie lokalny obrazek na kafelki offline
(bez sieci i kluczy) — do weryfikacji detekcji siatki.
