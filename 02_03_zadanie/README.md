# S02E03 — "failure"

Agent z Function Calling kompresujący dobowy log elektrowni (248 KB, 2137 linii, ~72,6K tokenów)
do digestu **≤ 1500 tokenów**, jedno zdarzenie na linię, z zachowaniem daty, godziny, poziomu
i identyfikatora podzespołu. Centrala odpowiada feedbackiem wskazującym podzespół, którego
awarii technicy nie potrafią wyjaśnić (`-948`), więc rozwiązanie to pętla popraw-i-wyślij.

## Co jest w logu

- Format `[YYYY-MM-DD HH:MM:SS] [LEVEL] treść`. **Identyfikator podzespołu nie jest osobnym
  polem**, siedzi w treści (`... on WTRPMP ...`, `ECCS8 reported ...`), czasem dwa w jednej linii.
  Parser wyciąga go regexem na tokeny pisane wielkimi literami.
- Siedem podzespołów: `ECCS8`, `WTRPMP`, `WTANK07`, `FIRMWARE`, `STMTURB12`, `PWR01`, `WSTPOOL2`.
  Poziomy: INFO 1247, WARN 494, ERRO 282, CRIT 114. Jedna linia co ~26 s od 06:00 do 21:37.
- Log jest **skrajnie powtarzalny**: tylko 90 różnych treści komunikatów (55 na poziomie WARN+),
  szablony INFO po 100-136 wystąpień. 24 komunikaty występują dokładnie raz i to one tworzą
  właściwą fabułę awarii.
- Sama deduplikacja nie wystarcza: 55 różnych treści WARN+ po jednej, bez skracania, to 1872
  tokeny. Limit wymaga jeszcze selekcji i skracania.

## Architektura

- **Dwa modele, sekcje konfiguracji nazwane po roli**: `Agent` (`gpt-4.1`, pętla) i `Scanner`
  (`gpt-4.1-mini`, subagent czytający surowe linie). Obie sekcje bindują się na
  `LlmProviderSettings`, więc zamiana skanera na innego dostawcę to edycja `appsettings`.
  Warstwa `Llm/` z S02E02 bez części vision.
- **Parsowanie i agregacja w kodzie** (`Analysis/`): `LogParser`, `LogFilter` (poziomy, podzespół,
  regex, okno czasu), `LogAnalyzer` (przegląd + zwijanie identycznych treści w typy zdarzeń
  z licznikiem i przedziałem pierwsze..ostatnie), `TokenCounter` (`o200k_base`).
- **Narzędzia agenta** (`Tools/`), w układzie czterech poziomów nawigacji z lekcji:
  - `log_overview` (perspektywa): rozmiar, poziomy, podzespoły, powtarzalność.
  - `list_event_types` (powiązania): typy zdarzeń chronologicznie po pierwszym wystąpieniu.
  - `search_log` (szczegóły): grep z limitem 50/200 linii.
  - `summarize_component` (subagent): wszystkie linie jednego podzespołu idą do skanera, wraca
    oś czasu do 25 linii. Główny agent nigdy nie widzi surowych linii.
  - `check_digest`: tokeny vs budżet oraz walidacja każdej linii ze źródłem. Digest, który
    przeszedł, jest zapamiętywany.
  - `submit_logs`: te same kontrole, odmowa przy błędach, zapis digestu na dysk, surowy feedback.
    Bez argumentów wysyła ostatni zapamiętany digest, żeby model nie powtarzał tekstu w kontekście.
- **`DigestValidator`** sprawdza deterministycznie: format `[YYYY-MM-DD HH:MM] [LEVEL] ...`,
  istnienie wpisu źródłowego o tej dacie, minucie, poziomie i podzespole (parafraza dozwolona,
  zmyślone zdarzenia nie) oraz to, że znaczniki `klucz=wartość` z wpisu źródłowego przetrwały
  parafrazę. `TokenBudget`: limit bezpieczny 1400 przy twardym 1500 (tokenizer Huba nieznany).
- Flagę wykrywa regex w `MissionState`; pętla odmawia zakończenia bez flagi (max 3 ponaglenia).
- Każdy bieg agenta zapisuje do `log-cache/run-<data>/` pełny transkrypt (wywołania narzędzi
  z pełnymi wynikami) i każdy wysłany digest. Wysyłki i feedback lądują w `failure-log.jsonl`.

## Przebieg i pułapka, która kosztowała trzy wysyłki

1. Agent (`--run`) przeczytał przegląd i typy zdarzeń, zlecił skanerowi podsumowania wszystkich
   siedmiu podzespołów (~36K tokenów skanera poza kontekstem agenta), zbudował digest na 40 linii
   / 1215 tokenów i wysłał. Feedback: *unable to determine what happened to device FIRMWARE*.
2. Dwie kolejne wysyłki (41 i 44 linie) dostały **identyczny** feedback. Agent reagował
   dokładaniem linii FIRMWARE (4 → 5 → 8), aż w digeście były wszystkie typy zdarzeń tego
   podzespołu. Diagnoza "za mało linii" była błędna.
3. Prawdziwa przyczyna: parafraza zgubiła szczegół. Źródłowe
   `[14:52] [CRIT] Safety bootstrap read missing environment marker SAFETY_CHECK=pass` stało się
   "Safety bootstrap read missing marker". `SAFETY_CHECK=pass` to **jedyny znacznik
   `klucz=wartość` w całym logu** i dokładnie to, czego technicy szukali w FIRMWARE.
4. Poprawka jednej linii (przywrócenie znacznika), wysyłka ręczna przez `--submit`
   (44 linie, 1341 tokenów): HTTP 200 i flaga.

Wnioski wpisane do kodu i promptu: "compress filler words, never facts" (znaczniki, wartości,
nazwy dosłownie), a feedback wskazujący podzespół, który już jest w digeście, oznacza problem
treści, nie liczby linii. Walidator blokuje teraz wysyłkę digestu z porzuconym znacznikiem.

## Koszty i limity

- Kontekst agenta urósł do ~30K tokenów (siedem podsumowań + digest powtarzany w `check_digest`
  i `submit_logs`), więc `gpt-4.1` łapał 429 od limitu tokenów na minutę. Retry z `Retry-After`
  to obsłużył, ale każda iteracja trwała 20-40 s. Mitygacje: `submit_logs` przez referencję do
  zapamiętanego digestu i `Agent.MinSecondsBetweenRequests = 15`.
- Skaner `gpt-4.1-mini` na ~36K tokenów wejścia to ułamek centa. Deterministyczne zwijanie
  w kodzie zrobiło jednak większość kompresji za darmo; subagent służy do zrozumienia historii
  jednego podzespołu, nie do samej kompresji.

## Uruchomienie

1. Uzupełnij `appsettings.Development.json` (gitignored): `AI_DevsApiKey`, `Agent:ApiKey`,
   `Scanner:ApiKey`.
2. Statystyki i typy zdarzeń bez LLM i bez `/verify` (log cache'owany w `log-cache/failure.log`,
   `--refresh` pobiera ponownie):

   ```
   dotnet run -- --analyze
   ```

3. Próba generalna: pętla agenta z symulowaną wysyłką, pierwszy digest, który przejdzie kontrole,
   ląduje w `log-cache/run-*/digest-01.txt` i bieg się kończy:

   ```
   dotnet run -- --draft
   ```

4. Pełna pętla (**każdy zaakceptowany lokalnie digest to żądanie na `/verify`**):

   ```
   dotnet run -- --run
   ```

5. Ścieżka ręczna, bez LLM: kontrola lokalnego pliku i wysyłka tylko wtedy, gdy przejdzie:

   ```
   dotnet run -- --check <plik.txt>
   dotnet run -- --submit <plik.txt>
   ```
