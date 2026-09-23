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
| S03E03 "reactor" | ✅ zaliczone | Robot z modułem chłodzenia przez planszę 7×5 z bloczkami jeżdżącymi góra/dół. **Bloki ruszają się tylko na komendę**, a stan planszy da się czytać **za darmo** osobnym endpointem podglądu, więc patrzenie nic nie kosztuje, kosztuje dopiero ruch. Pierwszy bieg zgubił robota w **ruchomej ścianie** trzech zsynchronizowanych kolumn; poprawka to `RouteFinder` — graf stanów `(kolumna, tick mod 6)` rozstrzygany w całości, guard odrzuca ślepe zaułki. Flaga w drugim biegu, 9 komend |
| S03E04 "negotiations" | ✅ zaliczone | **Odwrócenie ról**: agenta ma centrala, ja dostarczam mu 2 narzędzia HTTP z parametrem w języku naturalnym. Dopasowanie opisu do przedmiotu liczy **kod** (IDF + dokładne dopasowanie tokenów z cyfrą), bo cisza narzędzia przerywa misję agenta. Pułapka napięciowa: komplet istnieje tylko w 48 V → **Domatowo i Skolwin**. Drugie nieudokumentowane ograniczenie (`-875`): **300 znaków na opis narzędzia**. Agent zapytał 3 razy, użył tylko pierwszego narzędzia |
| S03E05 "savethem" | ✅ zaliczone | Trasa posłańca do Skolwina po planszy 10×10, gdzie **narzędzia odkrywa się w runtime** przez wyszukiwarkę narzędzi. Rzeka jest nie do objechania — jedyne suche pola za nią to ślepe zaułki, więc wodę przechodzi się pieszo albo koniem: `rocket` 8 ruchów → `dismount` → 3 pieszo. Nieudokumentowane: **80 znaków na `query`** narzędzia. Pierwszy bieg agenta poległ (33 iteracje, zero danych), drugi po pięciu poprawkach: **12 iteracji, 15 żądań** |
| S04E01 "okoeditor" | ✅ zaliczone | Zmiany w Centrum Operacyjnym OKO przez tylne wejście `okoeditor` (POST `/verify`), przy czym panel webowy jest **wyłącznie do czytania** — egzekwuje to `OkoPanelGuard` (whitelista ścieżek; `/edit/` i `/delete/` to zwykłe GET-y). **Identyfikatory są wspólne dla stron** (`incydenty`/`notatki`/`zadania`), więc `UpdateGuard` wymaga pary `page`+`id` z odczytanego listingu — inaczej cicha edycja cudzego rekordu. Kod klasyfikacji (`MOVE04` = zwierzęta) agent czyta z notatki i rejestruje; kod pilnuje tylko kształtu. Trzy zmiany (reklasyfikacja Skolwina, zadanie done+bobry, decoy o Komarowie przez nadpisanie incydentu o Domatowie), potem `done`. Flaga pełną pętlą agenta |
| S04E02 "windpower" | ✅ zaliczone | Harmonogram turbiny w **oknie serwisowym 40 s**. Pierwsze zadanie **bez modelu językowego** — nie ma pytania, na które kod by nie odpowiedział. Cała trudność to kolejność: prognoza zjada **24 s z 40** i podpisy stoją za nią w łańcuchu, bo obejmują `windMs`. Prognoza jest **losowana per sesja** (73 z 84 odczytów), ale wichury są stałe, a podpis nie jest związany z sesją. Zaliczone w **26,28 s**, flaga za pierwszą wysyłką |
| S04E03 "domatowo" | ✅ zaliczone | Misja ratunkowa: partyzant „w jednym z najwyższych bloków" na mapie 11×11, 300 punktów akcji, każda akcja gry przez `/verify`. **Drugie zadanie bez modelu**: wpisy `getLogs` to proza bez podpowiedzi, o trafieniu rozstrzyga flaga serwera. Planer wycenia wyczerpująco 24 porządki przeszukania po **koszcie oczekiwanym** pod budżetem najgorszego przypadku, wykonawca gra jedną akcją na raz przez guard, ledger pamięta załogi i obejrzane pola (hub tego nie oddaje). Trafienie na `G1` jako 12. z 14 pól, **155 z 300 punktów**, zero odrzuceń guarda. Pierwsze zadanie, w którym akcje gry wykonywał Claude, helikopter wezwałem ja |

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

## Zadanie S03E03 — "reactor" (szczegóły)

- Zadanie: doprowadzić robota transportującego moduł chłodzenia z kolumny 1 do slotu `G` w kolumnie 7,
  po najniższym wierszu planszy **7×5**. Sterowanie: `POST /verify`, `task: "reactor"`,
  `answer: {command}` — **jedna komenda na żądanie** (`start`, `reset`, `left`, `wait`, `right`).
  Bloki rdzenia mają **2 pola wysokości**, jeżdżą cyklicznie góra/dół i **ruszają się wyłącznie wtedy,
  gdy wydasz komendę** — `wait` jest ruchem, nie pauzą.
- **Odkrycie, które zmienia ekonomię zadania**: podgląd graficzny (`reactor_preview.html`) pobiera stan
  z **`POST /reactor_backend.php`** (`x-www-form-urlencoded`, pole `key` = klucz AI_devs). Ten odczyt
  **nie jest komendą** — nie rusza bloków i nie zużywa budżetu. Patrzenie na reaktor jest darmowe,
  kosztuje dopiero ruch. Kod podglądu zdradza też pełny kształt odpowiedzi, więc parser powstał bez
  ani jednej wysyłki na `/verify`.
- Odpowiedź `/verify` okazała się potem **nieść tę samą planszę** (`code: 100`, `message`, `board`,
  `blocks[] {col, top_row, bottom_row, direction}`, `player`, `goal`, `reached_goal`, `is_crushed`,
  `flag`). Parser jest tolerancyjny (szuka `board` gdziekolwiek w odpowiedzi, przyjmuje `top_row`
  i `topRow`, wiersze jako tablice i jako stringi, robota z `player` albo ze znacznika `P`), a sesja
  i tak dociąga stan darmowym odczytem, gdy odpowiedź go nie niesie — obie ścieżki były gotowe zanim
  poznaliśmy prawdziwy kształt.
- **Podział pracy**: kod odpowiada na „czy ta komenda zabije robota?" (geometria), agent na „którą
  z komend, które przeżyją, teraz wydać?". `MoveGuard` odrzuca ruch **przed wysłaniem**, gdy kolumna
  docelowa jest zajęta *teraz* albo zajmie się *w tym samym ticku* — oba sprawdzenia są celowe, bo
  dokumentacja nie mówi, czy na serwerze pierwszy rusza się robot, czy bloki.
- **Porażka pierwszego biegu i prawdziwa nauka**: robot **nie został zgnieciony** — guard zatrzymał go
  w pozycji bez wyjścia. Bloki w kolumnach 2, 3 i 4 jadą **zsynchronizowane**, więc schodzą na parter
  naraz i zamykają trzy kolumny w jednym ticku. Błąd zapadł **dwa ticki wcześniej**, przy wejściu
  w kolumnę, która wtedy była jeszcze otwarta. Guard patrzący jeden tick do przodu tego nie widzi.
- **Poprawka — `RouteFinder`**: ruch bloków jest deterministyczny **i okresowy** (parter kolumny jest
  zajęty jako funkcja samego ticku; okres `2 × (maxTop − minTop)` = 6), więc cała przyszłość to graf
  `(kolumna, tick mod 6)` — **42 stany**, czyli rozmiar do rozstrzygnięcia w całości.
  `CanSurviveFrom` to największy punkt stały (wykreślanie stanów bez wyjścia, aż zbiór przestaje się
  kurczyć) — guard odrzuca ruch poza ten zbiór. `TicksToGoalFrom` to BFS po tym samym grafie i trafia
  do raportu jako **informacja, nie zakaz**. Wyjątek: wejście na kolumnę celu nigdy nie jest odrzucane
  jako ślepy zaułek — dotarcie do slotu kończy misję.
- **Hooki wokół pętli** (`beforeToolCall` / `afterToolResult` / `beforeFinish`, jak w przykładzie
  `03_03_language`): guard przed wysłaniem, doklejanie kontekstu do wyniku, strażnik niepozwalający
  zakończyć pracy przed dotarciem do slotu. Po pierwszym biegu doszedł trzeci wyjątek w `beforeFinish`:
  w przegranej pozycji hook **autoryzuje `reset`** zamiast żądać ruchu — wcześniej agent trzy razy
  z rzędu pytał „mogę zresetować?", bo prompt każe traktować reset jako ostateczność.
- **Sprzeczny sygnał w feedbacku kosztuje trzykrotność kontekstu**: pierwsza wersja raportu pokazywała
  trasę do celu także przy komendach odrzuconych („the floor is sealed RIGHT NOW; goal reachable in 5").
  Agent zrobił się nadmiernie ostrożny — **13 komend i 54K tokenów zamiast 8 i 23K**. Po rozdzieleniu
  (trasa tylko przy komendach, które przejdą) wrócił do 8.
- **47 przypadków testowych offline** (`--tests`), bez sieci i klucza. Wśród nich **prawdziwa plansza
  z przegranego biegu**: trzy kolejne stany przepisane z logu potwierdzają, że `BoardProjection`
  przewiduje reaktor **co do kratki**, że dokładnie ten ruch, który zgubił robota, jest teraz odrzucany,
  że pozycja, w której bieg utknął, faktycznie nie ma wyjścia, i że z pozycji startowej istniała trasa
  w 10 komendach.
- `SimulatedReactorApi` odpowiada w tym samym kształcie JSON co podgląd, więc parser, guard i pętla
  agenta działają na nim bez zmian — `--run --offline` to pełna próba generalna bez `/verify` i bez
  ryzyka. Deterministyczny `RehearsalPilot` (`--simulate`) przechodzi planszę na sześciu układach, co
  dowodzi, że guard zostawia drogę i nie zakleszcza robota.
- **Rezultat (zaliczony)**: drugi bieg, **9 komend** — `start`, 3× `right`, 2× `wait`, 3× `right`.
  Stan reaktora po stronie Huba **wygasa** po kilkunastu minutach, więc zablokowana plansza z pierwszego
  biegu zniknęła sama i reset nie był potrzebny.
- Tryby: `--tests`, `--simulate [--seed N]` (offline, bez LLM), `--board [--offline]` (odczyt, **nie
  przesuwa reaktora**), `--command <cmd>…` (ręcznie, przez ten sam guard), `--run --offline` (agent
  przeciw symulatorowi), `--run` (prawdziwy reaktor). Transkrypt w `reactor-cache/run-<data>/`,
  żądania w `reactor-log.jsonl` z kluczem zredagowanym.

## Zadanie S03E04 — "negotiations" (szczegóły)

- Zadanie **odwraca role**: agenta ma centrala, a ja dostarczam mu narzędzia. Automat wysyła POST
  `{"params": "..."}` w **języku naturalnym** na mój publiczny adres i oczekuje `{"output": "..."}`.
  Ograniczenia: maksymalnie **2 narzędzia**, odpowiedź **4–500 bajtów**, **10 kroków**, 3 przedmioty,
  a **brak odpowiedzi przerywa jego pracę**. Zgłoszenie to lista URL-i z opisami (POST `/verify`,
  task `negotiations`), weryfikacja **asynchroniczna** przez `answer: {action: "check"}`.
- Dane: `cities.csv` (50 miast — 48 metropolii plus **Domatowo** i **Skolwin**, dwie wsie wciśnięte
  między nie), `items.csv` (2137 przedmiotów), `connections.csv` (5349 par, **1–4 miasta** na przedmiot).
  Katalog jest w 99,7 % szumem; sygnał to **ostatnie 6 pozycji**: turbina / inwerter / akumulator
  w wersjach 48 V i 24-12 V.
- **Pułapka jest napięciowa**: komplet ma wspólne miasta tylko w spójnym zestawie 48 V → **Domatowo
  i Skolwin** (te same wsie, które odstają w `cities.csv`). Każdy mix napięć daje pustkę, a akumulator
  kwasowy 12 V nie jest oferowany przez **żadne** miasto.
- Dwa narzędzia: `/api/offers` (miasta dla jednego przedmiotu, ze wszystkimi pasującymi wariantami)
  i `/api/cities-with-all` (miasta mające całą listę naraz). Dopasowanie NL→przedmiot robi **kod**,
  nie model: cisza kończy misję agenta, więc endpoint ma odpowiadać zawsze, szybko i tak samo.
- Rdzeń dopasowania (`Catalog/`): wspólna normalizacja obu stron (diakrytyki, `48 V`/`48V`/`48 woltów`
  → `48v`), scoring ważony **IDF** (`wiatrowa` 2 razy na 2137 nazw, `dioda` 351), **token z cyfrą musi
  zgadzać się dokładnie** (1N4001 ≠ 1N4007, 48V ≠ 480V), sprzeczny pomiar to kara ×0,35. Splitter tnie
  zdanie po `,` `;` `oraz` `i`, a fragment niosący sam parametr dokleja się do poprzedniej pozycji.
- **Narzędzie nie steruje agenta na 48 V** — lookup pokazuje warianty, a puste przecięcie wraca ze
  wskazówką „sprawdź zgodność parametrów". Pozycja wieloznaczna jest raportowana jako
  `turbina wiatrowa (2 warianty)`, bo miasta pochodzą z **sumy** wariantów.
- Gwarancje w kodzie: każda ścieżka HTTP kończy się **200 z polem `output`** (zepsuty JSON, brak pola,
  wyjątek), `params` czytane tolerancyjnie; budżet liczony **„na drucie"** (`\n` i `"` jako 2 znaki, bo
  nie wiadomo, którą długość mierzy centrala) z `UnsafeRelaxedJsonEscaping`; klucz i flaga redagowane
  w `negotiations-log.jsonl`. **49 testów offline**, bez sieci i klucza.
- **Drugie nieudokumentowane ograniczenie**: `description` ma limit **300 znaków**. Pierwsze zgłoszenie
  (opisy ~720 i ~640 znaków) dostało HTTP 400, kod **-875** — *„Field description can contain a maximum
  of 300 characters in tool #1"* — i nic się nie zarejestrowało, więc `--check` uparcie zwracał `-500`
  „No results yet". Limit sprawdza teraz `ToolCatalog.Validate` **przed wysłaniem**. Skrócenie do ~290
  znaków niczego nie kosztowało: zostały format `params`, przykład, odesłanie do drugiego narzędzia
  i ostrzeżenie o napięciach.
- **Przebieg (zaliczony)**: agent zapukał **sekundę po zgłoszeniu** i zadał **trzy pytania, wszystkie do
  narzędzia pierwszego** — „turbina wiatrowa mająca 48V i moc 400W", „akumulator pod 48V dowolna
  pojemność", „inwerter który pasuje pod 48V". **Narzędzia drugiego nie użył ani razu** (przecięcie zrobił
  sam, 3 kroki z 10), **spójne napięcie wybrał sam**, a pytał pełnymi zdaniami z odmianą i diakrytykami —
  dokładnie tym kształtem, pod który pisana była normalizacja. Zero nietrafionych zapytań; centrala
  odpowiedziała `cities: ["Domatowo", "Skolwin"]` i flagą w pierwszym `--check` po ~70 s.
  **Wniosek: opis narzędzia jest sugestią, nie sterowaniem** — agent zignorował odesłanie do drugiego
  narzędzia, bo własna ścieżka też mieściła się w budżecie kroków.
- Tryby: `--tests`, `--query "<opis>"` / `--common "<lista>"` (lokalny podgląd odpowiedzi z licznikiem
  bajtów), `--serve [--port 3000]`, `--submission --base-url <adres>` (podgląd zgłoszenia, klucz
  zamaskowany, bez wysyłki), `--submit --base-url <adres>`, `--check`.
- Drobiazgi operacyjne: pinggy wymaga jawnego `free@` i **niepustego hasła** (puste Enter = `Connection
  closed`); uruchomiony `--serve` blokuje `.exe` w `bin\Debug`, więc przebudowa w trakcie sesji idzie
  przez konfigurację `Release` (`dotnet run -c Release --no-build -- --submit ...`).

## Zadanie S03E05 — "savethem" (szczegóły)

- Zadanie: wytyczyć trasę posłańca z bazy do Skolwina po planszy **10×10** i wysłać ją jako
  `answer: ["<pojazd>", "right", "up", ...]` (POST `/verify`, task `savethem`). Budżet: **10 paliwa
  i 10 jedzenia**, zużywane wyłącznie za ruch. Podgląd: `savethem_preview.html`.
- **Narzędzi nie ma w kodzie ani w prompcie** — znany jest tylko `/api/toolsearch`, który dopasowuje
  po słowach kluczowych i zwraca **3 najlepsze** trafienia. Rejestr ma trzy pozycje: `maps` (przyjmuje
  **nazwę miasta**, `-716 "I don't have maps for such a city"`), `wehicles` (**nazwę pojazdu**,
  `-616` wypisuje dozwolone), `books` (pełnotekstowe archiwum notatek).
- **Pułapka terenowa**: rzeka w kolumnach 7–8 dzieli planszę, a jedyne dwa suche pola po jej drugiej
  stronie (`1,8` i `8,7`) to **ślepe zaułki**. Wodę trzeba przejść pieszo albo koniem. Rakieta sama
  nie doleci (11 ruchów × 1.0 > 10 paliwa), koń sam nie dojdzie (11 × 1.6 = 17.6 jedzenia), samochód
  tonie. Zostaje **`rocket` 8 ruchów → `dismount` → 3 pieszo**: 11 ruchów (= dystans Manhattan),
  paliwo 8.2, jedzenie 8.3. Drzewo `T` dokłada **+0.2 paliwa** tylko trybom z napędem.
- **Zapasów 10/10 nie ma w archiwum** — `books` opisuje tempo spalania i brak stacji paliw, ale nie
  stan początkowy. To dana z centrali, więc siedzi w briefingu agenta, a nie wśród rzeczy do odkrycia.
- Rozwiązanie: `03_05_zadanie` — agent z pięcioma narzędziami: `search_tools`, `ask_tool` (odmawia
  nazwy, która nie wróciła z wyszukiwarki), `register_world` (agent **zapisuje reguły**, kod sprawdza
  kształt, nigdy prawdziwość), `plan_route`, `submit_route`. Warstwa `Llm/` i `AgentLoop` z S03E03.
- **Podział pracy**: model szuka i interpretuje notatki, kod liczy. `RoutePlanner` nie zna pojęcia
  „najkrótsza trasa" — dwa zasoby drenują się w różnym tempie zależnie od trybu, więc trzyma wszystkie
  niezdominowane etykiety `(paliwo, jedzenie, ruchy)` na pole i dopiero na końcu przykłada budżet.
  `RouteSimulator` odgrywa każdą trasę przed wysyłką (odrzucenie kosztuje turę, nie próbę).
- **Nieudokumentowane ograniczenia**: `query` narzędzia ma limit **80 znaków** (`-617`, wyszukiwarka
  limitu nie ma — zmierzone binarnie), rate limit ~30 żądań pod rząd i minuta ciszy (`429`, `-9999`),
  a podgląd (`savethem_backend.php`, POST `key=`) **istnieje dopiero po pierwszej wysyłce** (`-980`),
  więc jest post-mortem z timeline'em, nie darmowym odczytem przed startem jak w S03E03.
- **Pierwszy bieg agenta poległ** (33 iteracje, 34 żądania, zero danych) i każdy powód dał poprawkę:
  nie znalazł `books`, bo nie użył słowa z rodziny *notes* → **rozpoznanie startowe w kodzie**
  (rzeczowniki briefingu odpytuje kod, jak `BootstrapAsync` w S03E02); siedem razy dostał `-716`
  i wysyłał opisy zamiast nazwy miasta, mając **Skolwin** w briefingu → licznik odrzuceń w wyniku
  narzędzia i akapit „narzędzie odpowiada na wartość, nie na prośbę"; zarejestrował atrapę świata
  (`map: ["S G"]`, budżety 0) → walidator wymaga **sklasyfikowania każdego znaku mapy** i dodatnich
  zapasów. Do tego pamięć powtórzonych pytań i `Agent.MinSecondsBetweenRequests: 5` (429 od OpenAI).
- **Drugi bieg (zaliczony): 12 iteracji, 15 żądań do huba.** Ładnie widać pętlę sprzężenia: `register_world`
  odrzuciło nieznane `R` → agent poszedł po legendę do `books`; `plan_route` powiedziało „żaden tryb
  nie dojdzie", bo wszystkim wpisał `can_enter_water: false` → poszedł po regułę wody i poprawił
  rejestrację. **Reguła, której nie odnalazł, wyszła jako brak trasy, a nie jako ciche założenie.**
  W iteracji 8 ogłosił, że misja jest niewykonalna — zawrócił go hook `BeforeFinish`.
- **Czego agent nie znalazł**: notatki `trees-and-burn`, więc zarejestrował `tree_extra_fuel: 0`
  i poprowadził trasę przez drzewo `(5,3)`, licząc paliwo 8.0 zamiast 8.2. Margines uratował bieg —
  przy ciaśniejszym budżecie to samo przeoczenie byłoby porażką.
- Tryby: `--tests` (73 przypadki offline, w tym kontrola krzyżowa planera z symulatorem), `--plan`
  i `--check "<trasa>"` (offline, z pliku świata), `--bootstrap`, `--tools "<query>"`,
  `--ask <tool|/path> "<query>"`, `--preview`, `--run` (bez wysyłki), `--run --submit`,
  `--submit-route <pojazd> <ruch>...`. Transkrypt w `savethem-cache/run-<data>/` (tam też `world.json`,
  który agent zarejestrował), żądania w `savethem-log.jsonl` z kluczem zredagowanym.
- Easter egg pominięty świadomie: podgląd zdradza `beaver_spot` `{row:2, col:7}` i osobną flagę za
  znalezienie bobrów (notatka `beavers-north`) — misja poboczna, nie liczy się do zaliczenia.

## Zadanie S04E01 — "okoeditor" (szczegóły)

- Zadanie: wprowadzić zmiany w **Centrum Operacyjnym OKO** przez tylne wejście `okoeditor`
  (POST `/verify`, akcje odkryte przez `help`). Panel operatora (`https://oko.ag3nts.org/`, login
  `Zofia`) jest **wyłącznie do czytania** — fabularnie jedno dotknięcie interfejsu zdradza operatorom
  obecność i odcina dostęp. Trzy zmiany, potem akcja `done` (flaga tylko przy komplecie): (1)
  reklasyfikacja incydentu o **Skolwinie** z „pojazdy + ludzie" na **zwierzęta**; (2) zadanie o
  Skolwinie na **wykonane** + treść o bobrach; (3) **decoy** — raport o ruchu ludzi w okolicach
  **Komarowa**, który (brak akcji `create`) powstaje przez **nadpisanie incydentu o Domatowie**.
- API `okoeditor` ma **trzy akcje**: `help`, `update` (`page` = `incydenty|notatki|zadania`, `id` =
  32 hex, opcjonalnie `content`/`title`/`done`; `done` tylko dla `zadania`; wymagane co najmniej jedno
  z `content`/`title`; `uzytkownicy` read-only) oraz `done`. Brak `create` przesądza, że decoy to
  nadpisanie istniejącego incydentu, nie nowy rekord.
- **Panel jest read-only i egzekwuje to kod, nie prompt** (`OkoPanelGuard`): `OkoPanelClient` puszcza
  wyłącznie ścieżki z whitelisty, a jedyny POST, jaki umie, to logowanie. Linki `/edit/<id>` i
  `/delete/<id>` to **zwykłe GET-y bez potwierdzenia** (status zadania w widoku szczegółu też jest
  linkiem `/edit/`, tuż obok treści do przeczytania), więc najdroższy błąd — skasowanie rekordu i
  spalenie dostępu — jest oddalony o jedno naiwne „pójdę za linkiem". Guard to uniemożliwia zamiast
  prosić model, żeby nie klikał. To ta sama filozofia co `CommandGuard` w S03E02.
- **Identyfikatory są wspólne dla stron**: te same 32 znaki adresują inny rekord na `incydenty`,
  `notatki` i `zadania`. `UpdateGuard` odrzuca parę `page`+`id`, której agent w tym biegu **nie
  odczytał na tej stronie** — pomyłka w `page` nie zwróciłaby błędu, tylko cicho przepisała cudzy wpis.
- **Kodeks klasyfikacji odkrywa się z notatki, kod pilnuje tylko spójności**: nigdzie w kodzie nie ma
  zapisane, że zwierzęta to `04`. Agent czyta notatkę „Metody kodowania incydentów" i rejestruje tabelę
  (`register_codebook`); kod sprawdza **kształt** (4 litery + 2 cyfry, każdy wpis z opisem, min. dwie
  rodziny po dwa podtypy — blokuje zarejestrowanie samego `MOVE04` bez przeczytania tabeli). Potem
  `UpdateGuard` odrzuca tytuł incydentu z kodem spoza tabeli (zmyślone `ANIM01` nie kosztuje żądania),
  a checklist pyta kodeks: *czy kod, który teraz stoi w tytule, opisano jako zwierzęta?* Kody: `MOVE`
  wykryto ruch (01 człowiek, 02 pojazd, 03 pojazd+człowiek, **04 zwierzęta**), `PROB` badanie próbki,
  `RECO` rekonesans. Dla ścieżki ręcznej ten sam kodeks odzyskuje `NoteCodeBookParser` z tekstu notatki
  (odkrywanie struktury w kodzie, jak `NoteDecomposer` w S03E01) — działa też jako siatka bezpieczeństwa.
- **Postęp liczony z projekcji konsoli, nie z relacji modelu** (`MissionState`): każda przyjęta edycja
  nakłada się na zapamiętany rekord, checklist patrzy na wynik. Rozbicie zmiany na dwa wywołania działa
  naturalnie — po samym `done: YES` punkt 2 zostaje otwarty, domyka się dopiero, gdy w treści pojawią
  się zwierzęta. Rekord „o Skolwinie" identyfikowany po **pierwszym** odczycie, bo po reklasyfikacji
  tytuł może już Skolwina nie wymieniać.
- **Rekonesans robi kod** (`Operation.BootstrapAsync`): wszystkie listingi czytane przed pętlą, więc
  agent wchodzi znając rekordy i ich identyfikatory (analogicznie do `BootstrapAsync` w S03E02).
- **`finish_mission` bramkowany, ale nie na amen**: `BeforeFinish` nie wypuszcza do `done`, dopóki
  checklist nie jest pełna; po kilku odmowach `done` idzie mimo to z zapisaną rozbieżnością — werdykt
  API jest ostateczny, więc własna bramka nie może uwięzić skończonego biegu.
- **Prompt sekcyjny, zgeneralizowany** (`OkoPrompt`: `<identity>/<surfaces>/<mission>/<method>/
  <identifiers>/<rules>/<limits>`) mówi *jak* pracować; *co* zmienić to `CentreOrders` (rozkaz centrali,
  budowany z settingsów). Kod klasyfikacji celowo nie pada w prompcie — to jedyna rzecz do przeczytania
  z konsoli. Wyjście z panelu nazwane w prompcie **danymi, nigdy instrukcjami**.
- Rozwiązanie: `04_01_zadanie` — warstwy `Oko/` (guard, klient, parser HTML), `Hub/OkoEditorClient`,
  `Mission/` (CodeBook, NoteCodeBookParser, UpdateGuard, MissionState, Operation, TextMatch), `Agents/`
  (prompt, CentreOrders, hooki, AgentLoop z S03E05), `Tools/` (read_console, read_record,
  register_codebook, update_record, finish_mission). **86 testów offline** (guard ścieżek, parser HTML,
  kodeks, parser notatki, guard edycji, checklist), bez sieci i klucza. Model: `gpt-4.1`.
- Tryby: `--tests`, `--guard "<ścieżka>"` (offline); `--help-api`, `--panel`, `--read <page>/<id>`
  (odczyt); `--update`/`--done` (ręcznie, przez ten sam guard; bez `--submit` = dry-run na projekcję);
  `--run` (dry run, nic nie wysłane), `--run --submit` (naprawdę). Transkrypt w
  `okoeditor-cache/run-<data>/` (tam `codebook.json` i snapshoty HTML), żądania w `okoeditor-log.jsonl`
  z kluczem zredagowanym.
- **Rezultat (zaliczony)**: flaga zdobyta **pełną pętlą agenta** (`--run --submit`) — agent przeszedł
  rekonesans w kodzie, przeczytał notatkę z kodami, zarejestrował kodeks, wykonał trzy zmiany przez
  `update_record` i domknął akcją `done`. Guard ścieżek panelu nie dopuścił ani jednego zapisu do
  interfejsu webowego.

## Zadanie S04E02 — "windpower" (szczegóły)

- Zadanie: zaprogramować harmonogram turbiny wiatrowej tak, by przetrwała wichury i wyprodukowała
  brakującą moc elektrowni. Wszystko przez POST `/verify`, `task: "windpower"`, w **oknie serwisowym
  trwającym 40 sekund** od akcji `start`. Treść zadania mówi wprost: *„liniowe wykonywanie wszystkich
  akcji nie umożliwi Ci ukończenia zadania"*.
- **Pierwsze zadanie w kursie bez modelu językowego.** Reguły turbiny są w dokumentacji, prognoza jest
  tabelą liczb, decyzja to porównanie dwóch wartości — nie ma pytania, na które kod by nie odpowiedział.
  Pętla agenta kosztowałaby sekundy z czterdziestu i nic by nie wniosła.
- **Zmierzone koszty kolejki** (`--recon`, okno wydane wyłącznie na pomiar): `powerplantcheck` ~10 s,
  `turbinecheck` ~12 s, **`weather` ~24 s**, `unlockCodeGenerator` ~2 s, żądanie HTTP ~35 ms.
  Rozwiązanie nie polega na szybszym wykonywaniu kroków, tylko na ustawieniu ich według tych pomiarów:
  wszystko zamawiane jest w pierwszej sekundzie, a to, co tanie, dzieje się w cieniu tego, co drogie.
- **`documentation` jest jedynym raportem dostępnym bez sesji** — reszta zwraca `-905`/`-915`/`-925`
  (`--probe` potwierdził to bez otwierania okna). Cała krzywa mocy jest więc znana przed zegarem,
  a `TurbineModel` **czyta ją z dokumentacji**, zamiast mieć ją zaszytą.
- **Prognoza jest losowana per sesja**: 73 z 84 odczytów zmieniło wartość między dwoma oknami, deficyt
  przeszedł z `4-5` na `2-3` kW. To pogrzebało pierwotny pomysł podpisywania punktów z cache'u przed
  otwarciem okna. Ale **wichury są stałe** (te same trzy godziny, 25 / 22 / 28 m/s), a **podpis nie jest
  związany z sesją** — ten sam punkt dostał w dwóch oknach bit w bit ten sam kod.
- **Kolejka gubi zamówienia.** Dwa żądania wysłane 3 ms od siebie: jedno dostało `code 14 queued`
  i nie wróciło nigdy. Stąd zamówienia idą **sekwencyjnie** (żądanie to i tak 35 ms, więc kolejka jest
  swoim własnym odstępem), prognoza i deficyt są zamawiane **po dwa razy**, a podpis, który nie wróci
  w 3 s, jest zamawiany ponownie. Osobno: **dwa równoległe pollery dostały ten sam raport dwukrotnie**,
  więc `getResult` odpytuje **jeden** wątek — skoro serwer potrafi wydać element dwa razy, potrafi go
  pewnie i zgubić.
- **`signedParams` przesądza o równoległości podpisów**: odpowiedź generatora echem powtarza
  podpisane parametry, więc cztery kody zamówione naraz da się przypisać do ich punktów. Bez tego echa
  odpowiedź jest samym hashem i trzeba by zamawiać pojedynczo. `QueuePump` to jeden poller w tle
  rozdzielający elementy po `sourceFunction` (raporty) i po `signedParams` (podpisy); reszta biegu
  **czeka na element po nazwie**, zamiast się o niego ścigać.
- **Trzy decyzje modelowania**, każda wymuszona przez dane: (1) **interpolacja** między kotwicami tabeli
  wydajności — wariant kubełkowy nie działa w żadnej zaobserwowanej sesji; (2) **granica 14 m/s liczona
  jako wichura** — tabela daje tam jeszcze 100%, a reguła bezpieczeństwa mówi „uszkodzenie", więc
  wątpliwość rozstrzyga się na „zabezpiecz"; (3) kryterium godziny produkcyjnej to **„jest w stanie
  pokryć deficyt"** (górny wydatek ≥ górny deficyt) — ostrzejsze odrzuciłoby jedyne dwie godziny
  w tygodniu przy deficycie 2-3 kW, czyli zwróciłoby „brak rozwiązania" tam, gdzie rozwiązanie istnieje.
- `ScheduleValidator` stoi **przed** jedynym `config` w oknie: odrzuca niezabezpieczoną wichurę,
  produkcję w wichurze, kąt za słaby na deficyt, godzinę późniejszą niż potrzeba, punkt podpisany na
  wiatr niepotwierdzony prognozą, punkt bez kodu i godzinę niepełną. `done` nie leci, gdy `config`
  zostanie odrzucony — walidowałoby pusty harmonogram. **23 testy offline**, w tym odtworzenie obu
  zaobserwowanych sesji (6,6 m/s przy 4-5 kW i 4,9 m/s przy 2-3 kW).
- **Rezultat (zaliczony)**: 4 punkty konfiguracji — ochrona `pitch 90` + `idle` na 22.09 18:00 (25 m/s),
  25.09 18:00 (22 m/s) i 26.09 18:00 (28 m/s), produkcja `pitch 0` + `production` na **22.09 20:00**
  (4,9 m/s, pierwsza możliwa godzina, 2 h po pierwszej wichurze). `config` przyjął `storedPoints: 4`,
  `done` zwróciło flagę i `elapsedSeconds: 26.28` przy limicie 40. Zero ponowień, zero pomyłek.
- Tryby: `--tests`, `--plan` (harmonogram z cache'u, offline), `--help-api`, `--doc`, `--probe`
  (zamówienia bez okna), `--recon` (okno wydane na pomiar), `--rehearsal` (pełna choreografia z danych
  sesji, zatrzymana przed `config`/`done`), `--run` (całość). Surowe odpowiedzi w
  `windpower-cache/run-<data>/`, żądania w `windpower-log.jsonl` z kluczem zredagowanym; oba gitignored,
  bo zawierają flagę.

## Zadanie S04E03 — "domatowo" (szczegóły)

- Zadanie: odnaleźć partyzanta w ruinach Domatowa i wezwać helikopter na pole, na którym zwiadowca
  potwierdził człowieka. Mapa **11×11**, do 4 transporterów i 8 zwiadowców, **300 punktów akcji**.
  Wszystko przez POST `/verify`, `task: "domatowo"`, akcje z `help`: `create`, `move`, `inspect`,
  `dismount`, `callHelicopter`, `reset` oraz darmowe `getMap`, `searchSymbol`, `getObjects`, `getLogs`,
  `expenses`, `actionCost`. Ceny: zwiadowca 5, transporter 5 + 5/pasażer, ruch zwiadowcy **7/pole**,
  ruch transportera 1/pole, inspekcja 1, `dismount` i `callHelicopter` 0.
- **Drugie zadanie bez modelu językowego** (po S04E02). Wpisy `getLogs` to zróżnicowana proza fabularna
  („kot uciekający przez wybite okno", „szczur większy niż kot") bez podpowiedzi kierunkowych, a o trafieniu
  rozstrzyga `human_found`/`human_found_at` z serwera. Model jako interpreter logów nie miałby na co odpowiadać.
- **Rekonesans bez punktów**: podgląd `domatowo_preview` ma **całą siatkę wpisaną w HTML** po stronie serwera,
  bez klucza; backend `domatowo_backend.php` (`action=pull`, wymaga nagłówka `Origin`/`Referer` huba) oddaje
  za darmo punkty, `human_found`, jednostki i kolejkę animacji. Ale **pozycje w `pull` stoją w miejscu, dopóki
  strona podglądu nie potwierdzi kolejki** (`ack`), więc źródłem prawdy o pozycjach jest `getObjects` (0 pkt).
- Sygnał „jeden z najwyższych bloków" → symbol `B3` (odczyt operatora, `TargetSymbol` w konfiguracji):
  14 pól w trzech skupiskach `F1–G2`, `A10–C11`, `H10–I11`, każde styka się z ulicą (przystanki `E2`;
  `B9`/`C9`; `H9`/`I9`). Spawn jednostek na `A6 → D6`, trasy liczy serwer (transporter ulicami, zwiadowca
  najkrótszą ortogonalną), `inspect` bada **tylko pole, na którym zwiadowca stoi**.
- **Planer** (`SearchPlanner`): wszystkie porządki skupisk × wszystkie podziały między transportery
  (24 kandydatów), trasa zwiadowcy po skupisku jako najtańsza permutacja (≤ 6 pól). Kryterium: najniższy
  **koszt oczekiwany** przy jednostajnym rozkładzie partyzanta, pod warunkiem że najgorszy przypadek plus rezerwa
  lądowania mieści się w 300. Wygrał wariant: transporter z 2 zwiadowcami `A6 → C9 → H9`, a dopiero gdy pusto,
  **drugi** transporter z 1 zwiadowcą do `E2` (oczekiwane 77,0 vs 80,3 dla jednego konwoju z trzema, bo trzeci
  pasażer nie jest opłacany z góry).
- **Wykonawca** (`Operation` + `Tactician`): jedna akcja na raz, po każdej trzy darmowe odczyty. Preferencje idą
  za cennikiem: `inspect` tam, gdzie zwiadowca stoi → `dismount` z transportera na przystanku nieobsadzonego
  skupiska → krok zwiadowcy po swoim skupisku → przejazd załadowanego transportera → dopiero zakup jednostek.
  Zwiadowca dostaje skupisko tylko, gdy dojście pieszo nie jest droższe niż dowiezienie świeżego. `ActionGuard`
  przed każdą wysyłką: budżet po projekcji, transporter tylko na osiągalną ulicę, limity 4/8, `dismount` tylko
  gdy wokół są wolne pola, `callHelicopter` tylko na `human_found_at`, `reset` wyłącznie z `--allow-reset`.
- **`OperationLedger`** (`domatowo-cache/ledger.json`) pamięta to, czego hub nie oddaje na żądanie: ile zwiadowców
  siedzi w transporterze (`crew[]` z `create`, `dismounted[]` z `dismount`, dla jednego transportera wnioskowanie
  z liczników) i które pola obejrzano (wpisy `getLogs` **wygasają po kilku minutach**, w obrębie biegu powtarzają
  się przy każdym odczycie).
- **Odkryte w biegu, nieobecne w `help`**: `dismount` sadza zwiadowcę **na polu na północ od pojazdu**, także
  gdy to budynek (`H8` = kościół) — reguła to kierunek, nie teren, więc przy blokach na południe/wschód od ulic
  zwiadowca zawsze dochodził pieszo (rezerwa 2 kroki na wizytę); **sloty spawnu zużywają się** (drugi
  transporter na `B6`, choć `A6` było puste); `path_steps` liczy pole startowe; `getObjects` zwraca `typ`
  zamiast `type`.
- **Rezultat (zaliczony)**: 28 akcji płatnych, **155 z 300 punktów**, człowiek na **`G1`** jako 12. z 14 pól
  („Udało się. Mężczyzna w wieku około 30 lat chował się za workami z cementem."). Zero odrzuceń guarda,
  zero błędów huba. Pierwszy odcinek ręcznie przez `--action` (nauka reguł), reszta wykonawcą w trzech biegach
  (`--max-actions 6`, `10`, `18`). **Pierwsze zadanie, w którym akcje gry wykonywał Claude** (wyjątek w zasadach
  poniżej), `callHelicopter destination=G1` wykonałem ja. Uwaga procesowa: edycja `CLAUDE.md` i wysyłka akcji
  w tej samej turze została raz zablokowana przez klasyfikator uprawnień jako „self-modification".
- **54 testy offline** (`--tests`), w tym prawdziwa plansza z połowy odcinka (transporter na `C9`, zwiadowca na
  `C8`) jako scenariusz wznowienia taktyka. Tryby: `--tests`, `--plan` (offline), `--help-api`, `--get-map`,
  `--state`, `--action <nazwa> [k=v]` (przez guard), `--next` (wybór bez wysyłki), `--step` (jedna akcja),
  `--run [--max-actions N]`. Odpowiedzi w `domatowo-cache/`, żądania w `domatowo-log.jsonl` z kluczem
  zredagowanym; oba gitignored, bo zawierają flagę.

## Zasady pracy w tym repo

- **Nie uruchamiać wysyłki odpowiedzi do Huba (`/verify`)** — ani bezpośrednio, ani przez uruchomienie
  agenta, który ją wysyła. Zadanie kończy się na gotowym, zbudowanym kodzie + instrukcji uruchomienia.
  Rozwiązanie uruchamiam i odpowiedź wysyłam **ja sam** — chcę prześledzić proces i się uczyć.
  (Pomocnicze wywołania endpointów *danych* Huba, np. `/api/location`, przy debugowaniu są OK.)
  - **Wyjątek dla zadań, w których każda akcja gry idzie przez `/verify`** (jak `domatowo` w S04E03),
    ustalony 2026-09-24: Claude może sam wykonywać akcje rekonesansu i planu (`move`, `dismount`,
    `inspect`, `getLogs`, `getObjects` itp.) — zawsze przez guard w kodzie, z pełnym logiem i relacją
    z każdej odpowiedzi. **Akcję zamykającą misję i zwracającą flagę** (np. `callHelicopter`) wykonuję
    ja sam. `reset` (losuje partyzanta od nowa) nigdy bez mojej jawnej zgody.
- Język komunikacji ze mną: polski. Komentarze w kodzie: angielski (moja globalna zasada).
- Nie commitować sekretów ani flag. Przed commitem sprawdzić, czy `appsettings.Development.json` nie wpadł do stage.
- Nie ruszać folderów przykładów z upstreamu — ułatwia to przyszłe merge z oryginalnym repo.
