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

## Zasady pracy w tym repo

- **Nie uruchamiać wysyłki odpowiedzi do Huba (`/verify`)** — ani bezpośrednio, ani przez uruchomienie
  agenta, który ją wysyła. Zadanie kończy się na gotowym, zbudowanym kodzie + instrukcji uruchomienia.
  Rozwiązanie uruchamiam i odpowiedź wysyłam **ja sam** — chcę prześledzić proces i się uczyć.
  (Pomocnicze wywołania endpointów *danych* Huba, np. `/api/location`, przy debugowaniu są OK.)
- Język komunikacji ze mną: polski. Komentarze w kodzie: angielski (moja globalna zasada).
- Nie commitować sekretów ani flag. Przed commitem sprawdzić, czy `appsettings.Development.json` nie wpadł do stage.
- Nie ruszać folderów przykładów z upstreamu — ułatwia to przyszłe merge z oryginalnym repo.
