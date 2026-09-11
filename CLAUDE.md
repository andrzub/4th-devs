# AI_devs 4 Builders — kontekst repozytorium

To repo jest **forkiem** oficjalnego repo kursu [i-am-alice/4th-devs](https://github.com/i-am-alice/4th-devs).
Foldery z przykładami kursowymi (`01_01_grounding`, `01_02_tools`, `02_*`, `03_*`, `mcp/`, …) pochodzą
z upstreamu i są w Node.js — **nie modyfikujemy ich**. Moja praca żyje w folderach `*_lekcja` i `*_zadanie`.

## O kursie

- Kurs: **AI_devs 4 Builders**, platforma: https://bravecourses.circle.so/ (sekcja "AI_devs 4 Builders").
- Nowa lekcja + jedno zadanie: codziennie pn–pt o 5:00.
- Weryfikacja zadań i postęp ("siatka zadań"): **https://hub.ag3nts.org/** (logowanie kontem EasyCart,
  https://app.easycart.pl/customer, produkt AI_devs 4).
- Zadania sprawdzane automatycznie — odpowiedzi wysyła się POST-em na `https://hub.ag3nts.org/verify`
  (payload: `{ "apikey": ..., "task": "<nazwa>", "answer": ... }`).
- Poprawna odpowiedź zwraca **flagę** `{FLG:...}`, którą wpisuje się ręcznie na https://hub.ag3nts.org/.
  **Flag nie publikujemy** (ani w repo, ani w komentarzach na platformie).
- Zadań jest **25**; certyfikat = min. **80% (20/25)**. Tury certyfikatów: 13.04, 18.05, 01.07, 01.10.2026.
  Ostateczny termin zaliczeń: **30 września 2026**. Dostęp do materiałów: do 9 marca 2027.
- Misje poboczne istnieją, ale nie liczą się do zaliczenia.
- Kontakt organizacyjny: aidevs@brave.courses (rozliczenia) lub Kasia Ćwiklińska na Circle
  (https://bravecourses.circle.so/u/83579ad8) — platforma/organizacja.

## Struktura mojej pracy (wzorzec do kontynuowania)

Dla każdej lekcji `SxxEyy` tworzę parę folderów w root repo:

| Folder | Zawartość |
|---|---|
| `xx_yy_lekcja` | Treść lekcji jako plik `.md` pobrany z platformy (opis zadania jest na końcu lekcji, sekcja `## Zadanie`) |
| `xx_yy_zadanie` | Rozwiązanie zadania — **konsolowy projekt .NET** (cel: nauka dotnet + AI, nie Node jak upstream) |

Przykład: `01_01_lekcja` + `01_01_zadanie` = S01E01 (zaliczone), `01_02_lekcja` + `01_02_zadanie` = S01E02.

Konwencje w projektach `*_zadanie`:

- Konsolowa aplikacja .NET (top-level statements w `Program.cs`), własny `.slnx` + `.csproj` per zadanie.
- Konfiguracja: `appsettings.json` (szablon, commitowany, bez sekretów) + `appsettings.Development.json`
  (**gitignored**, tu żyją klucze: `AI_DevsApiKey`, `OpenAI:ApiKey`). Każdy folder zadania ma własny `.gitignore`
  (ignoruje też `bin/`, `obj/`).
- Klient LLM pisany ręcznie na `HttpClient` (bez SDK) — świadomie, żeby rozumieć API. W `01_02_zadanie`:
  `LLM/` (klient, modele request/response, tool calls) + `Tools/` (narzędzia implementujące `ITool`).
- Branch per epizod (np. `S01E01`), commit rozwiązania po zaliczeniu zadania.

## Stan zadań

| Zadanie | Status | Notatki |
|---|---|---|
| S01E01 | ✅ zaliczone | Wynik: lista podejrzanych wysłana do Huba = `transport_people_response.json` (kopia leży też w `01_02_zadanie`, bo jest wejściem do S01E02) |
| S01E02 "findhim" | ✅ zaliczone | Pętla agenta z Function Calling (`Program.cs`), narzędzia: locations, accesslevel, batch-Haversine (`find_nearest_power_plant`), submit do `/verify`. Model: `gpt-4.1` (konto OpenAI bez weryfikacji organizacji nie ma dostępu do `gpt-5-mini`) |
| S01E03 "proxy" | ✅ zaliczone | Publiczny endpoint HTTP (ASP.NET Core minimal API) udający człowieka z dyspozytorni + osobny serwer MCP z narzędziami paczek. Tunel: pinggy (hasło: dowolny niepusty znak, np. `x`) |
| S01E04 "sendit" | ✅ zaliczone | Deklaracja przewozowa SPK wypełniona na podstawie rozproszonej dokumentacji, z której część danych jest **tylko w PNG** (vision). Agent z narzędziami `fetch_document` / `analyze_image` / `submit_declaration`. Model: `gpt-4.1` także dla vision |
| S01E05 "railway" | ✅ zaliczone | Aktywacja trasy `X-01` przez niedokumentowane, samodokumentujące się API (akcja `help`). Cała trudność to **limity**: celowe 503 i ostry rate-limit — retry/backoff po stronie kodu, nie modelu. Model: `gpt-4.1` |
| S02E01 "categorize" | ✅ zaliczone | Szablon promptu dla 100-tokenowego klasyfikatora (DNG/NEU, wyjątek reaktorowy zawsze NEU). Pułapka: **pomyłka zeruje saldo** (-890 → same -910) — to problem trafności, nie budżetu. Model: `gpt-4.1`, finał dokończony ręcznie |
| S02E02 "electricity" | ✅ zaliczone | Puzzle kablowe 3x3 odczytywane z PNG. Pierwsze zadanie z **dwoma providerami**: pętla agenta `gpt-4.1` (OpenAI), vision `gemini-3.6-flash` (Google AI Studio, darmowy) przez endpoint zgodny z OpenAI. 7 obrotów, zero zmarnowanych |
| S02E03 "failure" | ✅ zaliczone | Kompresja dobowego logu (72,6K tokenów) do digestu ≤1500 tokenów. Agent `gpt-4.1` + subagent-skaner `gpt-4.1-mini`; zwijanie identycznych komunikatów w kodzie. 3 wysyłki agenta odrzucone (FIRMWARE), bo parafraza zgubiła `SAFETY_CHECK=pass`; 4. wysyłka ręczna po poprawce jednej linii = flaga |
| S02E04 "mailbox" | ✅ zaliczone | Trzy fakty (data ataku, hasło, kod `SEC-`) ze skrzynki operatora przez API zmail. Pierwsze zadanie **wieloagentowe**: koordynator `gpt-4.1` z `delegate` + badacze `gpt-4.1-mini` ze świeżym kontekstem, blackboard z wykrywaniem konfliktów. Pułapka: ukryta wiadomość pod `rowID 0` z kodem o 35 znakach |
| S02E05 "drone" | ✅ zaliczone | Dron leci misję zarejestrowaną na `PWR6132PL`, ale ładunek spada na tamę obok. Sektor tamy ustalany **dwoma niezależnymi odczytami** mapy (histogram nasycenia w kodzie + `gemini-3.6-flash` na 12 kafelkach), pętla `gpt-4.1` rozgryza celowo przeciążone API drona. Siatka jest **3×4**, nie 3×3; sektor to `set(2,4)`. Flaga w 5. wysyłce — `flyToLocation` musi być **ostatni** |
| S03E01 "evaluation" | ✅ zaliczone | Anomalie w 9999 odczytach czujników. Trzy z czterech definicji anomalii rozstrzyga kod (46 plików z błędnymi danymi), model odpowiada tylko na pytanie o tonację notatki operatora. Deduplikacja dwupoziomowa: 9999 → 2032 unikalne notatki → **325 unikalnych klauzul**; odpowiedź modelu to **same numery wyjątków**. Pierwsze zadanie z warstwą **observability i evals**: bramka jakości przed zbudowaniem odpowiedzi. Model: `gpt-4.1-mini` |
| S03E02 "firmware" | ✅ zaliczone | Uruchomienie sterownika ECCS na maszynie wirtualnej dostępnej wyłącznie przez API powłoki. Maszyna to **dyspozytor 13 komend** — bez potoków i **bez czasownika uruchamiającego program**: binarkę odpala się, podając jej ścieżkę jako całą komendę. Naruszenie czarnej listy (`/etc`, `/root`, `/proc`, wpisy z `.gitignore`) **odbudowuje maszynę**, więc lista jest egzekwowana w kodzie przed wysłaniem komendy (36 przypadków offline). Flaga za pierwszą wysyłką, zero banów. **Pętla agenta nieuruchomiona** — zadanie zrobione ścieżką ręczną przez ten sam guard |

## Zadanie S01E02 — "findhim" (szczegóły)

- Wejście: podejrzani z S01E01 (`transport_people_response.json`: name, surname, born, city, tags)
  oraz lista elektrowni `findhim_locations.json` (pobrana z `https://hub.ag3nts.org/data/<apikey>/findhim_locations.json`).
- **Uwaga**: `findhim_locations.json` zawiera tylko *miasta* + kody `PWRxxxxPL`, **bez współrzędnych** —
  współrzędne miast musi dostarczyć LLM z wiedzy własnej; odległość liczy narzędzie z Haversine.
- API Huba (POST, raw JSON, zawsze z `apikey`):
  - `/api/location` → `{apikey, name, surname}` → lista koordynatów, gdzie widziano osobę,
  - `/api/accesslevel` → `{apikey, name, surname, birthYear:int}` → poziom dostępu.
- Odpowiedź: POST `/verify`, task `findhim`, `answer` = `{name, surname, accessLevel, powerPlant}`
  (kod elektrowni, przy której osoba była najbliżej).
- Zalecenia z lekcji: pętla agenta z limitem iteracji (10–15), model `gpt-5-mini` / `gpt-5`.
- **Rezultat (zaliczony)**: wytypowany został **Wojciech Bielik** (ur. 1986), `accessLevel = 7`,
  elektrownia **Chełmno `PWR2758PL`** — widziany ~980 m od niej (sighting 53.355, 18.415).
  Pozostali podejrzani byli najbliżej: Żurek ~2,6 km (Zabrze), Nowak ~8,9 km (Zabrze),
  Sieradzki ~9,4 km (Tczew), Jasiński ~14 km (Radom).


## Zadanie S01E03 — "proxy" (szczegóły)

- Rozwiązanie: `01_03_zadanie` — dwa projekty w jednym `.slnx`:
  - `ProxyServer` — MCP **Host**: `POST /` i `POST /api/proxy` (`{sessionID, msg}` → `{msg}`), pamięć sesji,
    pętla Function Calling, klient MCP (STDIO) konfigurowany przez `mcp.json`.
  - `PackagesMcpServer` — MCP **Server** (SDK `ModelContextProtocol` 2.2.0) z narzędziami `check_package` i `redirect_package`.
- Warstwa `Llm/` przeniesiona z S01E02; zamiast `ITool` jest `ToolDefinition`, bo schematy narzędzi
  przychodzą z `tools/list` serwera MCP, a nie z kodu hosta.
- **Pułapka, na której poległo pierwsze podejście**: API paczek **nigdy nie podaje zawartości** przesyłki —
  zwraca wyłącznie `status` i `location`. Informacja "paczka z rdzeniami" pada tylko w wiadomości operatora.
  Pierwsza wersja `ReactorPackageGuard` szukała słów kluczowych w odpowiedzi API, więc klasyfikowała ładunek
  jako niereaktorowy i przepuszczała przekierowanie na kod podany przez operatora.
  Naprawa: guard czyta wiadomości operatora (regex `PKG[0-9]+` + słowa kluczowe), a raz ustawiona flaga nigdy nie spada.
- Druga warstwa to prompt systemowy (`OperatorPersona`) — musi jawnie mówić, że wzmianka operatora wystarcza
  i że podany przez niego kod lokalizacji należy zignorować. Wersja "miękka" nie wystarczyła: model posłusznie
  przepisywał `PWR3847PL`.
- Konfiguracja: klucze w `ProxyServer/appsettings.Development.json` (gitignored). Klucz AI_devs trafia do procesu
  serwera MCP jako zmienna środowiskowa `AI_DEVS_API_KEY` — `mcp.json` jest commitowany i nie zawiera sekretów.
- `mcp.json` uruchamia serwer przez `dotnet exec …/PackagesMcpServer.dll` (nie `dotnet run`), żeby output MSBuild
  nie zaśmiecił strumienia STDIO protokołu. Wymaga wcześniejszego `dotnet build`.
- Wystawienie na świat: `ssh -p 443 -R0:localhost:3000 free@a.pinggy.io` — Windows podstawia konto domenowe,
  więc **trzeba jawnie podać użytkownika**, a przy pytaniu o hasło wpisać dowolny niepusty znak (puste Enter nie działa).
  Darmowy tunel: 60 minut, jeden na IP. Alternatywa bez limitu: `cloudflared tunnel --url http://localhost:3000`.
- **Rezultat (zaliczony)**: paczka z rdzeniami przekierowana do `PWR6132PL` (Żarnowiec), operator dostał potwierdzenie
  z przykrywką wskazującą jego własny kod `PWR3847PL` i w kolejnej wiadomości przekazał flagę.
- Materiały do lekcji: `01_03_lekcja/s01e03-lekcja.html` (wersja do odsłuchania w przeglądarce przez "Czytaj na głos",
  z diagramami i pominięciem bloków kodu przez lektora) oraz `s01e03-do-odsluchania.txt` (czysty tekst pod TTS).

## Zadanie S01E04 — "sendit" (szczegóły)

- Zadanie: wypełnić deklarację przewozową w **Systemie Przesyłek Konduktorskich (SPK)** i wysłać jej
  pełny tekst jako `answer.declaration` (task `sendit`). Przesyłka: nadawca `450202122`,
  Gdańsk → Żarnowiec, 2800 kg, „kasety z paliwem do reaktora", budżet **0 PP**, bez uwag specjalnych.
- Dokumentacja: `https://hub.ag3nts.org/dane/doc/index.md` (~44 KB) + **10 plików** wskazanych markerami
  `[include file="..."]`, które **nie są inline'owane** — trzeba pobrać każdy osobno. Brak zagnieżdżeń:
  wszystkie `include` są tylko w `index.md`. Endpoint nie wymaga `apikey`.
- **Kluczowa pułapka multimodalna**: `trasy-wylaczone.png` to jedyne źródło tabeli tras wyłączonych.
  Kod trasy Gdańsk – Żarnowiec (`X-01`, 80 km) **nie występuje w żadnym pliku tekstowym** —
  bez modelu vision zadania nie da się rozwiązać.
- Rozstrzygnięcia potrzebne do wypełnienia deklaracji:
  - **Kategoria A (Strategiczna)** — jedyna spełniająca oba warunki naraz: sekcja 8.3 dopuszcza wyłączone
    trasy do Żarnowca tylko dla kat. A i B, a sekcja 9.4 zwalnia kat. A i B z opłat. Kat. B (medyczna)
    nie pasuje do zawartości; opis kat. A wprost wymienia „ogniwa paliwowe".
  - **Trasa `X-01`** — z PNG; potwierdza to załącznik H (wersja 7.020: „Trasa Gdańsk - Żarnowiec oddana do testów").
  - **`WDP: 4`** — skrót rozwinięty **tylko w słowniku** (załącznik G) jako *Wagony Dodatkowe Płatne*.
    `dodatkowe-wagony.md`: standardowy skład = lokomotywa + 2 wagony po 500 kg = 1000 kg udźwigu,
    każdy dodatkowy wagon to kolejne 500 kg. Na 2800 kg brakuje 1800 kg → `ceil(1800/500) = 4`.
  - **`KWOTA DO ZAPŁATY: 0 PP`** — kat. A jest zwolniona z opłaty bazowej, wagowej i trasowej, a
    `dodatkowe-wagony.md` dodaje, że przy przesyłkach strategicznych i medycznych opłata 55 PP
    za wagon **nie jest naliczana**. Bez tego zwolnienia wyszłoby 220 PP, czyli poza budżetem.
- **Błąd, na którym poległo pierwsze podejście**: wpisane `WDP: 0` (odczyt „skoro nie płacę, to płatnych
  wagonów jest zero"). Hub odrzucił: `{"code": -760, "message": "The shipment will not fit on the train."}`.
  `WDP` opisuje **wagony**, nie pieniądze — zwolnienie dotyczy opłaty, a nie istnienia wagonów.
  Dlatego `WDP` i `KWOTA` nie są redundantne: pierwsze jest operacyjne, drugie finansowe.
- Format deklaracji (wzór w załączniku E) jest weryfikowany ściśle: separatory dokładnie **54 znaki**,
  końce linii **LF**, etykiety pól bez zmian razem z nawiasami (`OPIS ZAWARTOŚCI (max 200 znaków):`).
  Działające warianty pól spornych: `UWAGI SPECJALNE: ` (puste, ze spacją) i `KWOTA DO ZAPŁATY: 0 PP`.
- Rozwiązanie: `01_04_zadanie` — konsolowy agent z Function Calling, `gpt-4.1` w pętli i w vision.
  Warstwa `Llm/` przeniesiona z S01E02; `Message` dostał `ImageDataUrls`, a `BuildMessages` serializuje
  wtedy treść jako tablicę content-parts (`text` + `image_url`) — to jedyny kształt, jaki API przyjmuje dla vision.
- Decyzje projektowe (opisane szerzej w `01_04_zadanie/README.md`):
  - Prompt systemowy jest **zgeneralizowany** — podaje cel, ograniczenia i wzorce, ale nie zdradza
    kategorii ani kodu trasy. Przy sztywnym procesie wystarczyłby workflow zamiast agenta.
  - Vision jako **narzędzie**, nie część głównej pętli: agent podaje nazwę pliku, dostaje odpowiedź tekstem.
    Model pod `analyze_image` ma osobną instrukcję („transkrybuj wiernie, czego nie widać — powiedz wprost")
    jako zabezpieczenie przed zmyśleniem zawartości tabeli.
  - `detail: "high"` w części obrazowej — tabela jest gęsta, niski tier downsampluje ją poniżej czytelności.
  - `fetch_document` **odmawia** obsługi grafik i kieruje do `analyze_image`, żeby agent nie pominął cicho
    danych, których nie ma nigdzie indziej.
  - `DocumentLibrary` cache'uje pobrane pliki na dysku i sanityzuje nazwę (pochodzi od modelu):
    tylko czysta nazwa z katalogu dokumentacji, bez `..`, podkatalogów i absolutnych URL-i.
  - Wysyłka jest **opt-in**: bez `--submit` deklaracja jest tylko zapisywana i wypisywana.

## Zadanie S01E05 — "railway" (szczegóły)

- Zadanie: oznaczyć trasę kolejową **X-01** jako otwartą przez API bez zewnętrznej dokumentacji.
  Komunikacja jak zawsze: POST na `/verify`, `task: "railway"`, payload w polu `answer`.
- **API dokumentuje się samo** — akcja `help` zwraca listę akcji z polami `requires` / `optional` / `about`.
  Zwróciło pięć akcji: `help`, `reconfigure` (wymaga `route`), `getstatus` (`route`),
  `setstatus` (`route`, `value`; `allowed_values`: `RTOPEN` / `RTCLOSE`) oraz `save` (`route`).
- **Kluczowa zależność kolejności** siedzi w `notes` odpowiedzi `help`: żeby zmienić status trasy, trzeba
  **najpierw** wprowadzić ją w tryb `reconfigure`. `save` nie jest „zapisem" w potocznym sensie —
  opis mówi wprost *„Exit reconfigure mode"*, czyli zamyka tryb rekonfiguracji.
- **Działająca sekwencja** (4 wywołania, potwierdzone w logu):
  `help` → `reconfigure {route: X-01}` → `setstatus {route: X-01, value: RTOPEN}` → `save {route: X-01}`.
  Agent **nie wołał** `getstatus` — przy ostrym limicie byłoby to zmarnowane zapytanie.
- **Limity zadziałały realnie**: 2 z 4 wywołań (`reconfigure` i `save`) dostały **429** i przeszły dopiero
  za drugim podejściem, po odczekaniu wynikającym z nagłówków. 503 w tym biegu nie wystąpiły.
  Gdyby retry leżał po stronie modelu, każdy taki błąd kosztowałby iterację i część budżetu zapytań.
- Rozwiązanie: `01_05_zadanie` — agent z Function Calling, jedno narzędzie `call_railway_api(action, params?)`
  (klucze z `params` wtapiane **obok** `action`, bo taki kształt ma przykład z lekcji). Warstwa `Llm/`
  przeniesiona z S01E04 **bez części vision** — w tym zadaniu nie ma grafik.
- Decyzje projektowe (szerzej w `01_05_zadanie/README.md`):
  - **Retry i limity są w kodzie, nie w modelu** — `RailwayClient` robi backoff na 503, czyta nagłówki
    rate-limitu i przy wyczerpanym budżecie **zasypia do resetu jeszcze przed wysłaniem** kolejnego żądania.
    Wywołania są zserializowane (`SemaphoreSlim(1)`), więc równoległe tool calls nie przebiją limitu.
  - `RateLimitSnapshot` parsuje kilka konwencji nazw nagłówków i reset w czterech formatach
    (delta, unix w sekundach, unix w ms, HTTP-date) — taniej obsłużyć wszystkie niż zgadnąć jeden.
  - Prompt **nie zdradza** nazw akcji ani kolejności; osobno zabrania powtórnego wołania `help`
    (odpowiedź zostaje w kontekście) i mówi, że wynik, który dotarł do modelu, jest **ostateczny**.
  - Odpowiedzi wracają do modelu **surowe** — komunikaty błędów precyzyjnie wskazują problem,
    więc parafraza mogłaby tylko zgubić informację.
  - Uruchomienie jest **opt-in** (`--run`): tu każda akcja agenta to żądanie na `/verify`, więc nie da się
    rozdzielić „pracy" od „wysyłki" jak w S01E04 — bez flagi pętla agenta w ogóle nie startuje.
  - Każde wywołanie (łącznie z retry) ląduje w `railway-log.jsonl`: payload, status, **wszystkie** nagłówki
    i treść, z kluczem API zredagowanym na `***`.

## Zadanie S02E01 — "categorize" (szczegóły)

- Zadanie: napisać **szablon promptu** dla zdalnego, 100-tokenowego klasyfikatora ładunków (`DNG`/`NEU`),
  wysyłany po razie dla każdego z 10 towarów z CSV (`/data/<apikey>/categorize.csv`, rotuje co kilka minut,
  nagłówek `code,description`). Wszystko związane z reaktorem ma wychodzić `NEU`. Budżet 1,5 PP na batch,
  `{"prompt":"reset"}` odnawia saldo. Flaga przychodzi w odpowiedzi na 10. poprawną klasyfikację.
- **Kluczowa mechanika, której nie ma w treści zadania**: błędna klasyfikacja (HTTP 406, kod -890)
  **natychmiast zeruje saldo** — dalsze wywołania w cyklu to -910 „Insufficient funds", choćby zostało
  1,2 PP. Odróżnienie „problemu trafności" od „problemu kosztu" jest sednem zadania.
- Realna ekonomia (z `categorize-log.jsonl`): koszty liniowe 0,002 PP/token (wejście i wyjście),
  odpowiedź `NEU` = 2 tokeny, cache prefiksu działa już od ~17 tokenów i tnie wejście o ~połowę.
  Czysty przebieg przy ~45-tokenowych promptach ≈ 0,7 PP — budżet komfortowy, o ile nie ma pomyłek.
- **Porażka pierwszego biegu agenta** (gpt-4.1, 8 cykli): czytał -910 jako problem budżetu i skracał
  prompt zamiast poprawiać trafność; ultra-krótkie szablony bez nakazu jednego słowa powodowały, że
  klasyfikator „gadał" (-890 „DNG / NEU - choose one!"); na końcu agent **sfabrykował flagę** w tekście.
  Poprawki: cykl zatrzymuje się na pierwszym -890/-910 (+ pole `stoppedEarly` różnicujące przypadki),
  prompt zakazuje zmyślania flagi, pętla odrzuca zakończenie bez prawdziwej flagi (flagę wykrywa regex w kodzie).
- **Działający szablon** (vs ostatni szablon agenta dodane tylko „a weapon or" — mały model nie uważał
  pałki ani grotu włóczni za „dangerous"):
  `If item is nuclear reactor-related, respond NEU. If it is a weapon or dangerous, respond DNG. Else respond NEU. {id} {description} One word:`
  Wyjątek reaktorowy pierwszy (wygrywa kolejnością), „One word:" na końcu zapobiega gadaniu,
  placeholdery na końcu maksymalizują cache (30 tok. prefiksu w cache przy ~45-tokenowych promptach).
- Rozwiązanie: `02_01_zadanie` — agent-inżynier promptów z narzędziami `validate_template` (darmowa lokalna
  walidacja: placeholdery `{id}`/`{description}`, tokeny `o200k_base` z marginesem, szacunek kosztu z cache
  i bez) oraz `run_classification_cycle` (reset → świeży CSV → wysyłki do pierwszej pomyłki). Warstwa `Llm/`
  z S01E05. Finalny bieg dokończony ręcznie (skrypt PowerShell) po diagnozie logu.

## Zadanie S02E02 — "electricity" (szczegóły)

- Zadanie: puzzle kablowe na planszy 3x3 — doprowadzić prąd do trzech elektrowni, obracając pola
  (tylko 90° w prawo) tak, by układ zgadzał się ze schematem `https://hub.ag3nts.org/i/solved_electricity.png`.
  Stan planszy to **PNG** (`/data/<apikey>/electricity.png`, reset przez `?reset=1`), a **każdy obrót
  to osobny POST na `/verify`** z `answer: {rotate: "AxB"}`.
- **Pierwsze zadanie z dwoma providerami naraz**: pętla agenta na OpenAI `gpt-4.1`, vision na Gemini.
  Gemini wchodzi przez **endpoint zgodny z OpenAI** (`https://generativelanguage.googleapis.com/v1beta/openai`),
  więc obaj providerzy dzielą jedną klasę `OpenAiCompatibleLlmClient` i różnią się wyłącznie konfiguracją
  (`LlmProviderSettings`: klucz, base URL, model, `MinSecondsBetweenRequests`).
- **Droga przez modele Gemini** (lekcja poleca `google/gemini-3-flash-preview`):
  - `gemini-3-flash-preview` — free tier modelu **preview to tylko 20 zapytań na dobę**
    (`GenerateRequestsPerDayPerProjectPerModel-FreeTier`), nie 1500 jak dla stabilnych Flash.
  - `gemini-2.5-flash` — **404**: „no longer available to new users".
  - `gemini-3.6-flash` — stabilny, zadziałał i na nim zadanie zaliczone.
- **Trzy pułapki techniczne**, każda kosztowała jeden błędny bieg:
  - `MaxTokens = 200` przy vision **ucinało odpowiedź** — Gemini 3 to model „myślący", tokeny rozumowania
    wliczają się w limit, więc widoczna treść kończyła się na samym ` ```json `. Rozwiązanie: bez limitu.
  - Kafelek = jedno zapytanie spalało dobowy budżet w dwóch odczytach planszy. Rozwiązanie: **wszystkie
    9 wycinków w jednym zapytaniu**, odpowiedź to JSON z kluczami `1x1`…`3x3` (odczyt planszy = 1 zapytanie).
  - Gemini **nie wysyła nagłówka `Retry-After`** — sugerowany czas siedzi w treści błędu jako
    `"retryDelay": "41s"` (`google.rpc.RetryInfo`). Klient parsuje to z body.
- **Detekcja siatki zamiast sztywnych współrzędnych** (`BoardImageSlicer`): plansza (800x450) i schemat
  docelowy (598x419) mają różne proporcje i marginesy. Linie siatki to wiersze/kolumny z ciągłym przebiegiem
  ciemnych pikseli ≥35% wymiaru obrazu — tytuł i ikony elektrowni dają tylko krótkie przebiegi.
  Kafelki skalowane 3x przed wysłaniem do modelu.
- Reprezentacja pola: **zbiór krawędzi** U/R/D/L + nazwana forma (straight/corner/T/cross). Obrót w prawo
  to mapowanie U→R→D→L→U opisane wprost w prompcie — porównanie z celem i liczbę obrotów wylicza **agent**
  (podejście zalecane w lekcji), a nie kod.
- Flagę wykrywa **regex w kodzie** (`MissionState`), jak w S02E01: pętla odmawia zakończenia bez prawdziwej
  flagi w wyniku narzędzia (max 3 ponaglenia), a prompt zakazuje jej zmyślania.
- **Rezultat (zaliczony)**: **7 obrotów na 5 polach** — `1x2`, `1x3`, `2x1`, `2x2` (×3), `3x1` — wszystkie
  z odpowiedzią `Done`, flaga po ostatnim. Zero zmarnowanych obrotów, żadnego resetu.
- Tryby uruchomienia: `--describe` (sam odczyt planszy i celu, **bez** `/verify`) i `--run` (pełna pętla).
  Debug offline: `--slice <plik.png>` tnie lokalny obrazek na kafelki bez sieci i kluczy.
- **Uwaga o Copilocie** (sprawdzone przy okazji): subskrypcja GitHub Copilot **nie daje** klucza API do
  dowolnych zastosowań. Legalna darmowa alternatywa to **GitHub Models** (`https://models.github.ai/inference`,
  autoryzacja PAT-em, protokół OpenAI, limity rosną z tierem Copilota) — nadaje się pod pętlę agenta,
  ale ma limit **8K tokenów wejścia** na żądanie, więc pod paczkę 9 obrazków się nie nadaje.

## Zadanie S02E03 — "failure" (szczegóły)

- Zadanie: z dobowego logu elektrowni (`/data/<apikey>/failure.log`, 248 KB, 2137 linii, ~72,6K tokenów
  `o200k_base`) zbudować digest **≤ 1500 tokenów**, jedno zdarzenie na linię, z datą `YYYY-MM-DD`, godziną
  `HH:MM`, poziomem i identyfikatorem podzespołu. POST `/verify`, task `failure`, `answer: {logs: "...\n..."}`.
  Odrzucenie to HTTP 400, kod **-948**: *„unable to determine what happened to device X"* — feedback wskazuje
  jeden podzespół naraz.
- **Kształt danych** (kluczowe dla projektu): identyfikator podzespołu **nie jest osobnym polem**, siedzi
  w treści komunikatu (czasem dwa w jednej linii). Siedem podzespołów: `ECCS8`, `WTRPMP`, `WTANK07`,
  `FIRMWARE`, `STMTURB12`, `PWR01`, `WSTPOOL2`. Poziomy INFO 1247 / WARN 494 / ERRO 282 / CRIT 114,
  jedna linia co ~26 s, 06:00–21:37 jednego dnia.
- Log jest **skrajnie powtarzalny**: tylko **90 różnych treści** (55 na WARN+), szablony INFO po 100–136
  wystąpień; 24 komunikaty występują raz i to one są fabułą awarii. Sama deduplikacja nie wystarcza:
  55 treści WARN+ po jednej, bez skracania, to 1872 tokeny — trzeba jeszcze selekcjonować i skracać.
- Rozwiązanie: `02_03_zadanie` — agent `gpt-4.1` (sekcja `Agent`) + subagent-skaner `gpt-4.1-mini`
  (sekcja `Scanner`); sekcje konfiguracji nazwane **po roli**, obie bindowane na `LlmProviderSettings`.
  Warstwa `Llm/` z S02E02 bez vision. Parsowanie i agregacja w kodzie (`Analysis/`), narzędzia w układzie
  czterech poziomów nawigacji z lekcji: `log_overview` (perspektywa), `list_event_types` (identyczne treści
  zwinięte w typy zdarzeń z licznikiem i przedziałem pierwsze..ostatnie), `search_log` (grep),
  `summarize_component` (skaner czyta wszystkie linie jednego podzespołu, zwraca oś czasu), `check_digest`,
  `submit_logs`.
- **`DigestValidator`** blokuje wysyłkę deterministycznie: format linii, istnienie wpisu źródłowego o tej
  dacie/minucie/poziomie/podzespole (parafraza OK, zmyślone zdarzenia nie) oraz zachowanie znaczników
  `klucz=wartość`. `TokenBudget`: bezpieczny limit 1400 przy twardym 1500. Flaga przez regex w `MissionState`.
- **Przebieg (4 wysyłki)**: agent w `--run` zbudował digest (40 linii / 1215 tokenów) po przeglądzie, typach
  zdarzeń i 7 podsumowaniach skanera (~36K tokenów poza kontekstem agenta). Trzy wysyłki (40/41/44 linii)
  dostały **identyczny** -948 dla FIRMWARE; agent dokładał linii FIRMWARE (4→5→8) — zła diagnoza.
- **Prawdziwa przyczyna**: parafraza zgubiła szczegół. `[14:52] [CRIT] Safety bootstrap read missing
  environment marker SAFETY_CHECK=pass` stało się „missing marker". `SAFETY_CHECK=pass` to **jedyny znacznik
  `klucz=wartość` w całym logu**. Przywrócenie go w jednej linii i wysyłka ręczna (`--submit`, 44 linie /
  1341 tokenów) = HTTP 200 i flaga. Wniosek w prompcie: „compress filler words, never facts"; feedback
  o podzespole, który już jest w digeście, to problem treści, nie liczby linii.
- **429 od OpenAI**: kontekst agenta urósł do ~30K tokenów (podsumowania + digest powtarzany w `check_digest`
  i `submit_logs`) i `gpt-4.1` łapał limit TPM; retry z `Retry-After` działał, ale iteracja trwała 20–40 s.
  Mitygacje: `check_digest` zapamiętuje digest, `submit_logs` bez argumentów wysyła go przez referencję;
  `Agent.MinSecondsBetweenRequests = 15`.
- Tryby: `--analyze` (statystyki, bez LLM), `--check <plik>` / `--submit <plik>` (ścieżka ręczna bez LLM,
  te same kontrole co w narzędziu agenta), `--draft` (pętla z symulowaną wysyłką, kończy się na pierwszym
  digeście, który przejdzie kontrole), `--run`. Każdy bieg pisze do `log-cache/run-<data>/` transkrypt
  i wysłane digesty; wysyłki w `failure-log.jsonl`.
- Drobiazg: szablonowy `.gitignore` VS ignoruje katalogi `Logs/`, więc folder źródłowy nazywa się `Analysis/`.

## Zadanie S02E04 — "mailbox" (szczegóły)

- Zadanie: ze skrzynki jednego z operatorów Systemu wyciągnąć trzy wartości i wysłać razem
  (POST `/verify`, task `mailbox`, `answer: {date, password, confirmation_code}`): dzień planowanego
  ataku na elektrownię (`YYYY-MM-DD`), hasło do systemu pracowniczego oraz kod potwierdzenia z ticketa
  działu bezpieczeństwa (`SEC-` + 32 znaki = 36 znaków). Punkt wyjścia: Wiktor z ruchu oporu wysłał
  donos z domeny `proton.me`. **Skrzynka jest cały czas w użyciu** — w trakcie pracy wpływają nowe maile.
- API skrzynki: `POST /api/zmail`, sześć akcji z `help`: `getInbox`, `getThread`, `getMessages`,
  `search` (operatory jak w Gmailu), `reset` (zeruje licznik zapytań) i `help`. Tryb `read_only`.
  71 wiadomości na starcie, głównie szum korporacyjny i ruch operacyjny elektrowni.
- Rozwiązanie: `02_04_zadanie` — pierwsze zadanie **wieloagentowe**, architektura **orchestrator**
  z lekcji: koordynator `gpt-4.1` (`delegate` / `mission_status` / `submit_answer`) i badacze
  `gpt-4.1-mini` ze świeżym kontekstem (`search_mail` / `get_inbox` / `get_thread` / `get_messages` /
  `report_finding`). Sekcje konfiguracji nazwane po roli (`Coordinator`, `Researcher`), obie bindowane
  na `LlmProviderSettings`. Warstwa `Llm/` z S02E03, `AgentLoop` wspólna dla obu ról.
- **Koordynator nie ma ani jednego narzędzia pocztowego**, więc każdy fakt musi przyjść przez badacza,
  a treści maili nigdy nie wchodzą do jego kontekstu. W teście dymnym trzej badacze przeczytali po
  ~3900 tokenów wejścia, koordynator zużył 1163 na całą turę.
- **Równoległość jest prawdziwa**: kilka `delegate` w jednej turze modelu leci przez `Task.WhenAll`,
  a klient LLM zwalnia bramkę, gdy `MinSecondsBetweenRequests = 0`. Dostęp do skrzynki pozostaje
  zserializowany (`SemaphoreSlim` w `ZmailClient`), więc równoległość nie przebije budżetu zapytań.
- **Blackboard** (`MissionState`): findingi nie są nadpisywane — dwie różne wartości dla tego samego
  faktu zostają obie w historii, `delegate` zwraca `CONFLICT`, decyzję podejmuje koordynator
  (strategia „historia zmian + agent zarządzający" z lekcji). `MessageStore` cache'uje treści po
  `messageID`, więc dwóch badaczy w tym samym wątku płaci za jedno pobranie.
- **Trzy rzeczy odkryte sondowaniem API**, każda zamieniona w zabezpieczenie w kodzie:
  - **`rowID` nie jest stabilny** — ta sama wiadomość wystąpiła jako `rowID 127`, chwilę później jako
    `130`. Stabilny jest tylko 32-znakowy `messageID`: cache kluczuje po nim, `report_finding` odrzuca
    dowód wskazany przez `rowID`, prompt badacza mówi o tym wprost.
  - **Pod `rowID 0` siedzi podstawiona wiadomość** — `ids` przyjmuje też numeryczne `rowID`, więc
    identyfikator z 32 zer trafia w wiadomość, **której nie ma w żadnym listingu**, a której kod
    potwierdzenia ma **35 znaków zamiast 36**; treść pod tym `rowID` zmienia się między wywołaniami.
    `MessageStore` porównuje każdą zwróconą wiadomość z tym, o co pytano, i wszystko poza tym oddaje
    w osobnej sekcji z ostrzeżeniem „nie traktuj tego jako odpowiedzi na swoje pytanie".
  - **API liczy zapytania** (pole `request` w `getMessages`, `reset` je zeruje) — klient prowadzi własny
    budżet (`Mailbox:MaxZmailRequests`), pokazuje go modelowi przy każdym wyniku narzędzia i przerywa
    bieg, gdy się skończy, zamiast dobijać się do API.
- **Kod woła `help` na starcie biegu i wkleja surową odpowiedź do promptu każdego badacza** — gramatyka
  wyszukiwarki pochodzi od tego, kto ją implementuje, a nie z parafrazy w prompcie (to też punkt 1
  instrukcji zadania).
- **Gwarancje w kodzie, nie w prompcie**: `AnswerValidator` sprawdza realną datę `YYYY-MM-DD` i długość
  36 znaków kodu (przynęta z 35 znakami nie ma szans dojść do Huba — `report_finding` ją odrzuca);
  zgłoszenie bez cytatu z treści, bez `messageID` albo z wartością już odrzuconą przez Huba jest
  **odrzucane w narzędziu** i badacz szuka dalej; `submit_answer` wysyła to, co leży na blackboardzie,
  a nie to, co model przepisze w argumentach, i ma `IsParallelSafe => false`; flagę wykrywa regex
  w `MissionState`. Pominięte w połowie tury `tool_calls` dostają wynik „Skipped", bo API odrzuca
  kolejne żądanie z niedopowiedzianym wywołaniem.
- **Świadomie pominięte narzędzie `message`** z lekcji (dwukierunkowa komunikacja wstrzymująca pętlę
  badacza): skrzynka jest tylko do czytania, briefingi są samowystarczalne, a jedyny realny przypadek
  „brakuje mi informacji" to *nie znalazłem* — wraca do koordynatora jako raport `found=false` z listą
  prób, a koordynator decyduje, czy ponowić, bo poczta mogła właśnie dojść.
- Obserwacja z testu dymnego: badacz daty szukał najpierw po **angielsku** w polskiej skrzynce i dostał
  zero trafień. Prompt dostał wprost „Query in Polish" plus uwagę, że dwa słowa to `AND`.
- Tryby: `--help-api`, `--inbox [strona]`, `--search "<query>"`, `--thread <id>`, `--read <id>...`,
  `--reset` (ręczne czytanie, bez LLM), `--draft` (pełna pętla, wysyłka symulowana do
  `mailbox-cache/run-*/draft-answer.json`), `--run` (prawdziwe `/verify`),
  `--submit --date ... --password ... --code ...` (dokończenie ręczne, jedno żądanie, bez LLM).
  Każdy bieg pisze `mailbox-cache/run-<data>/` z transkryptem na agenta; wszystkie żądania
  w `mailbox-log.jsonl` z kluczem zredagowanym na `***`.

## Zadanie S02E05 — "drone" (szczegóły)

- Zadanie: zaprogramować przejętego drona `DRN-BMB7` tak, by poleciał misję zarejestrowaną przeciwko
  elektrowni w Żarnowcu (`PWR6132PL`), ale **jedyny ładunek spadł na tamę obok** — woda ma trafić do
  systemu chłodzenia. POST `/verify`, task `drone`, `answer: {instructions: [...]}`.
  Dokumentacja API: `https://hub.ag3nts.org/dane/drone.html`, mapa: `/data/<apikey>/drone.png`.
- **Sedno: przeciążony `set(...)`** — jedna nazwa to sześć funkcji rozpoznawanych po *kształcie*
  argumentu: `set(3,4)` sektor lądowania, `set(4m)` wysokość, `set(1%)` moc, `set(engineON)` silniki,
  `set(destroy)`/`set(image)`/`set(video)`/`set(return)` cele misji. Podstęp działa, bo dokumentacja
  **rozdziela obiekt docelowy od sektora lądowania**: `setDestinationObject(ID)` to cel, który
  rejestruje System, a `set(x,y)` to miejsce, gdzie faktycznie spada ładunek.
- **Mapa: siatka jest 3 kolumny × 4 wiersze, nie 3×3.** Prawdopodobnie zamierzona pułapka — obraz
  1920×929 (proporcja 2:1) z dwiema liniami pionowymi bardzo chętnie zostaje opisany jako „3×3"
  z samego pattern-matchingu, a wskazówka z lekcji wprost chwali modele za „zliczanie kolumn i wierszy".
  `MapGridDetector` liczy siatkę w kodzie po czystej czerwieni (`R > 150`, `R − max(G,B) > 70`,
  pokrycie >60% wiersza/kolumny) i tnie kafelki **ściśle pomiędzy** liniami.
- **Sektor tamy: `col2-row4` → `set(2,4)`**, ustalony **dwoma niezależnymi odczytami**, które muszą
  się zgodzić, bo dron niesie jeden ładunek: (1) `WaterSignalAnalyzer` liczy piksele podbitej wody
  (`B ≥ R+25`, `G ≥ R+25`, nasycenie HSV > 0,35) — **9321 z 9322 trafień w jednym sektorze**, wynik
  stabilny przy progach 0,35–0,65; (2) `gemini-3.6-flash` dostaje wszystkie 12 kafelków w **jednym**
  zapytaniu i sam wskazuje sektor („concrete weir with sluice gates and a crest walkway"), nie znając
  wyniku analizy pikselowej. Rozbieżność przerywa bieg.
- **Gwarancje w kodzie, nie w prompcie** (`InstructionValidator`): lista z `flyToLocation` musi mieć
  dokładnie jeden `set(x,y)` równy sektorowi tamy oraz `setDestinationObject(PWR6132PL)`; formaty
  z dokumentacji (ID obiektu, dwa słowa właściciela, LED `#RRGGBB`, wysokość 1–100 m, moc 0–100%)
  sprawdzane lokalnie, więc **odrzucenie nie kosztuje wysyłki**; nieznane metody przechodzą, bo
  sondowanie jest legalną strategią. Flagę wykrywa regex w `MissionState`. Guard przetestowany
  offline na **17 przypadkach** (osobny projekt linkujący same pliki walidatora, bez sieci).
- **Konsekwencja projektowa**: dron **utrzymuje konfigurację między żądaniami** (inaczej `hardReset`
  nie miałby sensu), więc gdyby pozwolić budować misję przyrostowo, walidator nie wiedziałby, na co
  naprawdę leci ładunek. Każda lista jest wysyłana jako **kompletna misja**, a prompt mówi o tym wprost.
- Prompt (`DronePrompt`) jest **sekcyjny wg anatomii z lekcji**: `<identity>` / `<protocol>` /
  `<mission>` / `<api>` / `<rules>` / `<limits>`. Dokumentacja trafia tam **w oryginale**
  (`DroneManual` zamienia HTML na tekst zachowując tabelę i przykłady JSON) — parafraza rozwiązywałaby
  za model dokładnie te kolizje nazw, które są substancją zadania.
- Agent ma **jedno narzędzie** `send_instructions`: dokumentacja i sektor są statyczne i znane przed
  startem pętli, więc siedzą w prompcie. Nie ma `--draft` (jedyną akcją agenta *jest* żądanie do Huba,
  więc symulacja nie dałaby mu czego czytać) ani równoległych wywołań narzędzi.
- Tryby: `--map` (siatka, kafelki, histogram — bez LLM i bez `/verify`), `--locate` (+ Gemini
  i kontrola krzyżowa), `--manual` (dokumentacja tak, jak zobaczy ją agent), `--prompt` (pełny odczyt
  terenu + wypisanie promptu systemowego bez uruchamiania pętli), `--run` (pełna pętla, prawdziwe
  `/verify`), `--submit "instr" ...` (ręcznie, bez modelu, te same kontrole), `--refresh` wymusza
  pobranie zamiast cache'u. Transkrypt w `drone-cache/run-<data>/`, żądania w `drone-log.jsonl`.
- **Rezultat (zaliczony): 5 wysyłek, z czego 4 odrzucone tym samym kodem `-880`** — *„If we send the
  drone without a return instruction, we will lose it forever"* — **mimo że `set(return)` był w liście
  za każdym razem**. Komunikat jest mylący: prawdziwym problemem była **kolejność**. W próbach 1–4 cele
  misji stały *po* `flyToLocation`, w próbie 5. *przed* nim. Czyli `flyToLocation` musi być **ostatnią**
  instrukcją — dokumentacja mówi tylko, że kolejność *między celami* nie ma znaczenia, i nigdzie nie
  wspomina, że cele trzeba ustawić przed startem lotu. Agent przez cztery próby przestawiał
  `set(return)` względem `set(destroy)`, bo dokładnie na to wskazywał błąd.
  Działająca lista: `setDestinationObject(PWR6132PL)`, `set(2,4)`, `set(destroy)`, `set(return)`,
  `set(20m)`, `set(engineON)`, `flyToLocation`. Przy okazji: **`set(...%)` nie jest wymagane** —
  zwycięska próba poszła bez ustawiania mocy. Agent nie użył `hardReset`, `selfCheck`, `setName`,
  `setOwner`, `setLed` ani kalibracji, zgodnie z zasadą „konfiguruj tylko to, co potrzebne".
- Zachowanie zabezpieczeń w praktyce: sektor `set(2,4)` był we **wszystkich pięciu** listach,
  walidator ani razu nie zablokował listy poprawnej formalnie (odrzucenia przyszły z Huba, nie
  z kodu), zużyte 5 z 15 wysyłek.
- Drobiazg z budowy: plik zapisany przez narzędzie edycyjne dostał **bajty NUL zamiast spacji**
  (`grep` raportował „Binary file matches", kompilacja przechodziła). Warto skanować nowe pliki
  pod kątem `\0`, zanim się je zacommituje.

## Zadanie S03E01 — "evaluation" (szczegóły)

- Zadanie: w archiwum `https://hub.ag3nts.org/dane/sensors.zip` (3,6 MB) jest **9999 plików JSON**
  (`0001`–`9999`, lekcja mówi „10 000") z odczytami czujników. Trzeba odesłać identyfikatory
  **wszystkich** plików z anomalią — POST `/verify`, task `evaluation`, `answer: {recheck: [...]}`.
  Cztery definicje anomalii: wartość poza normą, kanał nieaktywny raportujący dane, notatka
  „wszystko OK" przy złych danych, notatka „są błędy" przy danych poprawnych.
- **Podział pracy kod / model** to sedno zadania. Trzy z czterech definicji to arytmetyka na
  zadeklarowanych zakresach: `sensor_type` mówi, które kanały są aktywne, aktywny musi mieścić się
  w normie, nieaktywny musi zwracać dokładnie `0`. Model nie jest o to pytany ani razu. Dostaje
  **jedno** pytanie, na które kod nie odpowie: *czy ta wypowiedź operatora twierdzi, że sprzęt jest
  sprawny, czy że coś jest nie tak?*
- **Warstwa deterministyczna znajduje 46 plików** — 22 wartości poza normą i 24 „duchy" (nieaktywny
  kanał coś raportuje; najczęściej wilgotność, 12 plików). Część duchów jest podstępna, bo mieści
  się w normie *swojego* kanału: `3123` to `humidity/pressure/temperature`, który zwraca
  `voltage_supply_v = 230.5` — wartość całkowicie zdrową dla napięcia. Sam zakres tego nie wyłapie,
  wyłapuje porównanie z `sensor_type`.
- **Redukcja kosztu, trzy poziomy** (wskazówka z lekcji mówi wprost, że notatki się powtarzają):
  - 9999 notatek to **2032 unikalne treści** (20,3 % archiwum),
  - notatki są **szablonowe** — 2031 z 2032 to dokładnie trzy klauzule po przecinkach, dobierane
    z trzech pul (43 otwarcia, 38 środków, 243 zakończenia); unikalnych klauzul jest **325**, czyli
    ~11× mniej wejścia (7,4K vs 84K tokenów). Struktura jest **odkrywana w kodzie** (`NoteDecomposer`),
    nie zaszyta na sztywno; notatka, która się nie rozkłada, idzie do modelu w całości,
  - **kształt odpowiedzi**: output kosztuje kilka razy więcej niż input, a 98 % wypowiedzi jest
    rutynowych, więc model zwraca **tylko numery wyjątków** (`PROBLEM: 14,37` / `UNCLEAR:`).
    Wszystko niewymienione to `Ok`. Wyroki lądują w `ToneCache` na dysku (klucz zawiera nazwę
    modelu), więc powtórny bieg całego pipeline'u nie kosztuje nic.
- **Cena tego formatu i zabezpieczenie**: odpowiedź ucięta, leniwa albo pusta wygląda **identycznie**
  jak „nic do zgłoszenia". Dlatego do każdego batcha wstrzykiwane są dwie **wypowiedzi kontrolne**
  o znanym werdykcie (jedna awaryjna, jedna rutynowa), na pozycjach losowanych z ziarna równego
  numerowi batcha. Model, który przestanie oznaczać podstawioną awarię albo zacznie oznaczać
  podstawioną rutynę, dostaje batch z powrotem (do 3 prób). To ewaluacja **online**, w trakcie biegu.
  Teksty kontrolne są spoza słownika elektrowni, więc nie zderzą się z prawdziwą notatką.
- **Observability** (`Observability/`) odwzorowuje hierarchię z lekcji lokalnie: bieg = *session*
  (`runId`, katalog `sensors-cache/run-<data>/`), `classify:clauses` / `eval:clauses` / `report` =
  *trace*, każde wywołanie modelu = *generation* z własnymi tokenami, kosztem i czasem, plus *event*
  (`run-started`, `classification-planned`, `answer-built`). Wszystko do `classification-log.jsonl`,
  pełne treści interakcji (prompt systemowy, prompt, surowa odpowiedź, decyzja o batchu) do plików
  katalogu biegu — materiał do Playgroundu. `UsageMeter` liczy tokeny i pieniądze per trace, razem
  z tokenami z cache prefiksu providera (`prompt_tokens_details.cached_tokens`) po niższej stawce;
  **cennik siedzi w `appsettings.json`**, bo taryfy zmieniają się częściej niż kod.
- **Evals** (`Evals/note-tone.labeled.json`): 30 ręcznie oznaczonych wypowiedzi, 12 `Ok` / 12
  `Problem` / 6 `Unclear` (balans), połowa to prawdziwe notatki z archiwum, połowa pisana pod
  konkretne tryby porażki (pokrycie + różnorodność): negacja („nothing suggests a fault condition"
  jest zdrowe, choć nazywa awarię), słownictwo awaryjne w zdrowym kontekście, ton mieszany, tekst
  nieorzekający o niczym. `--evals` liczy macierz pomyłek i precision/recall/F1 na klasę.
  **Kluczowe: `--report` uruchamia ewaluację jako bramkę i odmawia zbudowania odpowiedzi poniżej
  progu** (domyślnie 95 %) — regresja w ocenie notatek jest niewidoczna w wyniku (lista ID wygląda
  tak samo), a przesądza o zaliczeniu. `--skip-evals` to świadome obejście, nie domyślka.
- `--selftest` sprawdza **offline, bez klucza i bez sieci**, całą otoczkę klasyfikatora: parsowanie
  odpowiedzi, działanie wypowiedzi kontrolnych (w tym odrzucenie odpowiedzi, która nie flaguje nic
  i takiej, która flaguje wszystko), dekompozycję i składanie werdyktów oraz wszystkie cztery
  definicje anomalii na syntetycznych odczytach. **34 przypadki, zero tokenów.**
- **Model nie jest agentem**: warstwa `Llm/` przeniesiona z S02E05 **bez function callingu** — brak
  pętli, narzędzi i `ToolCall`. Pliki są lokalne, pytanie jest jedno i identyczne dla każdej
  wypowiedzi, więc pętla agenta byłaby kosztem bez zysku.
- Świadome decyzje: klasyfikowane są **wszystkie** notatki, nie tylko z plików o zdrowych danych —
  do samej odpowiedzi wystarczyłyby te drugie (plik z błędnymi danymi jest anomalią niezależnie od
  notatki), ale wtedy raport nie wskazałby plików, w których operator **podpisał błędny odczyt jako
  zdrowy**, czyli dokładnie dowodu nierzetelności, o który prosi fabuła. `Unclear` przy zdrowych
  danych **nie wchodzi do odpowiedzi** (nie pasuje do żadnej z czterech definicji), ale ląduje
  w sekcji raportu „do spojrzenia przez człowieka".
- **Easter egg**: `2137.json` ma jedyną nieszablonową notatkę w archiwum — *„The report looks
  completely normal. I will go to check status of all other devices."* Dane zdrowe, notatka
  pozytywna, więc **nie jest anomalią**. Trafiła do zbioru testowego jako przypadek brzegowy, bo
  jako jedyna nie rozkłada się na klauzule.
- Tryby: `--fetch`, `--analyze` (cała warstwa deterministyczna + porównanie kosztu obu tierów, bez
  LLM), `--selftest`, `--evals`, `--classify`, `--report` (bramka + lista anomalii → `answer.json`,
  bez wysyłki), `--submit` (jedyne wyjście na `/verify`). Opcje: `--tier clauses|notes`, `--refresh`,
  `--skip-evals`.

## Zadanie S03E02 — "firmware" (szczegóły)

- Zadanie: uruchomić oprogramowanie sterownika ECCS (`/opt/firmware/cooler/cooler.bin`) na maszynie
  wirtualnej dostępnej **wyłącznie** przez `POST /api/shell` (`{apikey, cmd}`). Poprawny start
  wypisuje kod `ECCS-` + 40 znaków → POST `/verify`, task `firmware`, `answer: {confirmation}`.
  Twarde zasady: konto zwykłego użytkownika, zakaz `/etc`, `/root`, `/proc`, obowiązek respektowania
  każdego `.gitignore`. Naruszenie = ban czasowy **plus reset maszyny do stanu początkowego**.
- **`help` jest punktem wyjścia i zmienia projekt w trzech miejscach.** Maszyna nie ma powłoki,
  tylko dyspozytor trzynastu komend (`help`, `ls`, `cat`, `cd`, `pwd`, `rm`, `editline`, `reboot`,
  `date`, `uptime`, `find`, `history`, `whoami`) — bez potoków, przekierowań, łączenia, `grep`-a:
  - **nie ma komendy uruchamiającej program** — binarkę odpala się, podając jej ścieżkę jako całą
    komendę, więc parser musi traktować token zaczynający się od `/` jak pełnoprawny czasownik,
  - **`editline <plik> <nr> <treść>`** to jedyny zapis (to jest ta „edycja inna niż w standardowym
    systemie" ze wskazówek) — zmiana ustawienia wymaga najpierw odczytania pliku i policzenia linii,
  - **`find <wzorzec>`** dopasowuje *nazwy* w całym systemie plików, a nie ścieżki.
- Zamknięta gramatyka okazała się prezentem: skoro `CommandSpec` zna **arność i znaczenie każdego
  argumentu**, guard sprawdza ścieżki, nie myląc ich z treścią — wartość pisana przez `editline`
  może zawierać spacje i średniki i nie jest ścieżką (osobny przypadek testowy).
- **Rdzeń rozwiązania to `CommandGuard`** — czarna lista w kodzie, nie w prompcie (lekcja mówi
  wprost: *„Dostęp do nich musi być kontrolowany programistycznie"*). Stoi **przed** wysłaniem,
  więc odrzucenie kosztuje jedną turę agenta i zero żądań, podczas gdy odrzucenie przez maszynę
  kosztuje cały bieg. Kolejno: nieznany czasownik nie wychodzi na zewnątrz → metaznaki w argumencie
  ścieżkowym → **wirtualne `cwd`** (bez lustra `cd /` + `cat etc` przechodzi każdy naiwny filtr, bo
  nie nazywa niczego zakazanego; przy niepewnym stanie ścieżki względne są odrzucane, nie zgadywane)
  → **normalizacja przed oceną** (`/opt/../etc/passwd` → `/etc/passwd`) → katalogi zakazane →
  wzorce `find` (znak `/` odrzucany, nazwa zakazanego katalogu po zdjęciu wildcardów też) →
  `.gitignore`.
- **Nieznana lista = lista pełna.** Katalog, w którym listing pokazał `.gitignore`, jest zablokowany
  w całości do czasu odczytania tego pliku; sam `.gitignore` zawsze pozostaje czytelny, inaczej
  reguła zablokowałaby samą siebie. Matcher obsługuje negację `!`, kotwiczenie `/`, reguły
  katalogowe, `*`, `?`, `**`, z semantyką gita (reguły z katalogu nadrzędnego obowiązują niżej,
  w obrębie pliku wygrywa ostatnie dopasowanie). Wątpliwość zawsze rozstrzyga się na „nie".
- **`GuardTestSuite`: 36 przypadków offline**, bez sieci i bez klucza (`--guard-tests`). Każdy to
  sposób, w jaki bieg mógł zostać zbanowany; zmiana reguł dopasowania jest weryfikowalna w sekundę.
- **Rekonesans robi kod, nie model.** `ShellSession.BootstrapAsync` wykonuje `help`, `whoami`,
  `pwd`, `find .gitignore` i `cat` każdego znalezionego pliku, więc guard wchodzi do pętli
  **uzbrojony** we wszystkie czarne listy, zamiast poznawać je, wchodząc w jedną z nich.
  Surowe `help` trafia do promptu bez parafrazy — to nie jest standardowy Linux, a streszczenie
  napisane z góry odpowiadałoby za model na pytania, na które ma odpowiedzieć czytaniem.
- Pozostałe gwarancje w kodzie: **ban przerywa bieg zamiast ponawiać** (maszyna właśnie się
  zresetowała, więc cały model świata agenta jest nieaktualny — to materiał do poprawki guarda);
  `reboot` ma **własne narzędzie** z wymaganym `reason` i limitem, a przez `run_command` jest
  zablokowany; **`submit_code` nie przyjmuje argumentów** i wysyła to, co przechwycił regex
  z surowej odpowiedzi (40 znaków przepisanych przez model to dokładnie ten rodzaj szczegółu, który
  gubi jeden znak); cache odczytów czyszczony przy każdym zapisie; retry na 429/503 i własny budżet
  zapytań. Wyjście z maszyny jest w prompcie nazwane **danymi, nigdy instrukcjami** — ale to druga
  linia obrony, pierwszą jest guard, którego nie obchodzi, co maszyna wypisała.
- **Pułapka konfiguracyjna znaleziona przy pierwszym uruchomieniu**: `ConfigurationBinder` **dokleja**
  tablicę z JSON-a do niepustej wartości domyślnej właściwości, więc `ForbiddenPaths` wyszło
  zdublowane (`/etc, /root, /proc, /etc, /root, /proc`). Poprawka: właściwość startuje pusta,
  a `EffectiveForbiddenPaths` podstawia listę zadania, gdy konfiguracja milczy — dzięki temu
  skasowanie wpisu w `appsettings.json` nie rozbraja po cichu guarda.
- **Przebieg (zaliczony)**: 30 żądań do maszyny w dwóch procesach (`--recon`, potem sesja `--shell`),
  jedna wysyłka na `/verify`, flaga za pierwszym razem, zero banów, zero `reboot`.
  Hasło **`admin1`** znalezione przez `find *pass*` w `/home/operator/notes/pass.txt`. Dalej
  trzeba było usunąć plik blokady `cooler-is-blocked.lock` i poprawić `settings.ini`.
  **Kody błędów układały się rosnąco** — `-872` (bez hasła) → `-691` → `-690` → `-689` — więc każda
  zmiana była widocznym postępem o jeden krok; to samodokumentujący się feedback, jak `help`
  w S01E05. Dwa szczegóły: `SAFETY_CHECK=pass` **pojawia się po raz drugi w kursie** (był sednem
  porażki w S02E03), a rozwiązaniem ostatniego kroku okazało się **zakomentowanie** linii
  (`#enabled=true`), nie zmiana jej wartości — po trzech próbach przestawiania `enabled`.
- **Czego nie sprawdzono w praktyce**: pętla agenta nie została uruchomiona (`--run` nigdy nie
  wystartował, brak katalogu `firmware-cache/`). Zadanie rozwiązane ścieżką ręczną przez ten sam
  guard i tę samą sesję. Realnie zweryfikowane są: guard (36/36 offline + 30 prawdziwych komend
  bez ani jednej fałszywej odmowy), `ShellSession`, rekonesans i `--submit-code`. Kod agenta,
  prompt i narzędzia są zbudowane i kompilują się, ale nieprzetestowane w biegu.
- Tryby: `--guard-tests` i `--guard "<cmd>" [--cwd <dir>]` (offline, bez sieci i klucza),
  `--help-api`, `--recon`, `--shell "<cmd>"...` (ręcznie, przez ten sam guard, bez `/verify`),
  `--run` (pętla, kończy się na wypisaniu kodu, nic nie wysyła), `--run --submit` (prawdziwe
  `/verify`), `--submit-code "ECCS-..."` (ręcznie, jedno żądanie, bez modelu). Transkrypt biegu
  w `firmware-cache/run-<data>/`, wszystkie żądania w `firmware-log.jsonl` z kluczem zredagowanym.
- Uwaga o fallbacku na Sonneta (lekcja poleca go do tego zadania): podmiana jest **konfiguracyjna**
  (`BaseUrl` na `https://api.anthropic.com/v1` + nazwa modelu), ale **subskrypcja Claude Pro/Max
  nie jest kluczem API** — to ta sama pułapka co z Copilotem; API to osobne konto z kredytami
  w Anthropic Console.

## Zasady pracy w tym repo

- **Nie uruchamiać wysyłki odpowiedzi do Huba (`/verify`)** — ani bezpośrednio, ani przez uruchomienie
  agenta, który ją wysyła. Zadanie kończy się na gotowym, zbudowanym kodzie + instrukcji uruchomienia.
  Rozwiązanie uruchamiam i odpowiedź wysyłam **ja sam** — chcę prześledzić proces i się uczyć.
  (Pomocnicze wywołania endpointów *danych* Huba, np. `/api/location`, przy debugowaniu są OK.)
- Język komunikacji ze mną: polski. Komentarze w kodzie: angielski (moja globalna zasada).
- Nie commitować sekretów ani flag. Przed commitem sprawdzić, czy `appsettings.Development.json` nie wpadł do stage.
- Nie ruszać folderów przykładów z upstreamu — ułatwia to przyszłe merge z oryginalnym repo.
