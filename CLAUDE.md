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

## Zasady pracy w tym repo

- **Nie uruchamiać wysyłki odpowiedzi do Huba (`/verify`)** — ani bezpośrednio, ani przez uruchomienie
  agenta, który ją wysyła. Zadanie kończy się na gotowym, zbudowanym kodzie + instrukcji uruchomienia.
  Rozwiązanie uruchamiam i odpowiedź wysyłam **ja sam** — chcę prześledzić proces i się uczyć.
  (Pomocnicze wywołania endpointów *danych* Huba, np. `/api/location`, przy debugowaniu są OK.)
- Język komunikacji ze mną: polski. Komentarze w kodzie: angielski (moja globalna zasada).
- Nie commitować sekretów ani flag. Przed commitem sprawdzić, czy `appsettings.Development.json` nie wpadł do stage.
- Nie ruszać folderów przykładów z upstreamu — ułatwia to przyszłe merge z oryginalnym repo.
