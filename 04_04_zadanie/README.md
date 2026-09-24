# S04E04 — „filesystem"

Notatki Natana o handlu wymiennym między miastami trafiają do wirtualnego systemu plików Huba
w trzech katalogach: `/miasta`, `/osoby`, `/towary`. Konsolowa aplikacja .NET, komunikacja jak zawsze:
POST `/verify`, `task: "filesystem"`, akcja (lub tablica akcji) w polu `answer`.

## Stan prac

| krok | zakres | stan |
|---|---|---|
| 1 | szkielet, warstwa `Llm/` i `AgentLoop` z 04_01, klient Huba z logiem, notatki w repo (`--notes`, `--help-api`) | gotowy |
| 2 | baza wiedzy (`workspace/`: mapa treści + 3 szablony), projekcja systemu plików, guard zapisu, walidator krzyżowy, builder paczki, testy offline (`--tests`, `--validate`) | gotowy |
| 3 | agent: prompt, hooki, 7 narzędzi na lokalnej projekcji, `--run` (nic nie wysyła), `--submit` (reset + paczka + `done`), `--list` | gotowy |
| 4 | po pierwszej wysyłce: guard osób wymaga dwóch **różnych** słów i odsyła do dziennika, heurystyka liczby mnogiej (`-i`/`-y`) dla towarów i kluczy, 96 testów | **zaliczone** w drugim biegu |

## Uruchomienie

Klucze w `appsettings.Development.json` (gitignored):

```json
{ "AI_DevsApiKey": "...", "Agent": { "ApiKey": "..." } }
```

```bash
dotnet run -- --tests
dotnet run -- --workspace
dotnet run -- --prompt
dotnet run -- --run
dotnet run -- --validate filesystem-cache/run-<data>/plan.json
dotnet run -- --submit filesystem-cache/run-<data>/plan.json
dotnet run -- --list /miasta
```

| tryb | model | dotyka `/verify` | uwagi |
|---|---|---|---|
| `--tests`, `--notes`, `--workspace`, `--prompt`, `--validate <plan>` | nie | nie | offline, bez kluczy |
| `--run` | tak | **nie** | agent buduje lokalną projekcję; zapisuje `plan.json`, `validation.txt`, `batch.json` w `filesystem-cache/run-<data>/` |
| `--help-api`, `--list [ścieżka]` | nie | tak | odczyt, po jednym żądaniu |
| `--reset` | nie | tak | czyści system plików Huba |
| `--submit <plan>` | nie | tak | odmawia, gdy walidator odrzuca plan; potem `reset` → paczka → `done`, flaga wykryta regexem |

Wszystkie żądania do Huba lądują w `filesystem-log.jsonl` z kluczem zredagowanym na `***`; transkrypty
biegów w `filesystem-cache/`. Oba gitignored, bo mogą zawierać flagę.

## API (z akcji `help`)

| akcja | parametry | uwagi |
|---|---|---|
| `createFile` | `path`, `content` | nadpisuje istniejący plik; „tylko markdown", **linki muszą wskazywać istniejące pliki** |
| `createDirectory` | `path` | rodzic musi istnieć |
| `deleteFile`, `deleteDirectory` | `path` | katalog znika z zawartością |
| `listFiles` | `path` (domyślnie `/`) | nazwy, znaczniki czasu, rozmiary; **nie działa w batchu** |
| `reset` | — | czyści wszystko; **działa też w batchu** |
| `done` | — | walidacja końcowa; tylko osobnym żądaniem |

Limity: nazwy `^[a-z0-9_]+$` (**tylko małe litery**, cyfry, podkreślenie), plik ≤ 20 znaków, katalog ≤ 30,
głębokość ≤ 3, **nazwy unikalne w całym drzewie** (towar i miasto nie mogą się nazywać tak samo).
Podgląd `filesystem_preview.html` czyta `filesystem_backend.php` GET-em na sesji cookie (bez klucza
zwraca `-990 Invalid request origin`) i po `done` pokazuje flagę jako **plik** w drzewie.

## Podział pracy: model czyta, kod pilnuje

Dane to cztery pliki (4 KB): tablica ogłoszeń z zapotrzebowaniem (liczby), dziennik rozmów (osoby)
i ledger transakcji `sprzedawca -> towar -> kupujący` (sprzedawcy). Trudność nie jest w objętości,
tylko w języku: odmiana nazw miast („z Pucka", „w Darzlubiu"), dopełniacz liczby mnogiej w ogłoszeniach
(„6 mlotkow" → `mlotek`), oraz **osoby rozbite między wpisami** (nazwisko w jednym, imię w drugim).
To robi model. Kod nie zna polskiej fleksji i nie próbuje jej udawać.

Za to kod egzekwuje wszystko, co da się sprawdzić bez gramatyki:

- **Baza wiedzy jak w lekcji** (`workspace/`): `index.md` to mapa treści (katalogi, źródło prawdy per
  katalog, limity API, kolejność pracy), a `templates/` mówi, jak wygląda notatka każdego typu.
  Przykłady w szablonach są zmyślone (Komarowo, koparka), żeby nie podpowiadały odpowiedzi.
  Mapa treści trafia do promptu **w oryginale**; szablony i notatki agent czyta narzędziami.
- **„Rozejrzyj się, zanim zapiszesz" jest w hooku, nie w prośbie**: `write_file` jest odrzucany, dopóki
  agent nie przeczytał wszystkich notatek, oraz dopóki nie przeczytał szablonu katalogu, do którego pisze.
- **`WriteGuard`** ocenia każdy zapis do lokalnej projekcji: tylko trzy katalogi, nazwa wg limitów API
  (małe litery, ≤ 20 znaków, unikalna globalnie, bez rozszerzenia), treść ASCII; miasto = obiekt JSON
  z małymi kluczami i dodatnimi liczbami całkowitymi; osoba = pełne imię i nazwisko + dokładnie jeden
  link do **istniejącego** miasta; towar = linki tylko do istniejących miast, bez powtórzeń.
- **`PlanValidator`** ocenia całość przed wysyłką: każde miasto ma dokładnie jedną osobę, każda liczba
  w JSON miasta występuje na tablicy ogłoszeń, każda nazwa miasta i osoby ma rdzeń w notatkach
  (nie da się zmyślić Gdyni ani Kowalskiego), a `/towary` odzwierciedla ledger sprzedawca po sprzedawcy.
  Pozycje ledgera dopasowuje do plików po dokładnej nazwie albo wspólnym rdzeniu (`ziemniaki` trafia
  w `ziemniak`, ale `maka` nie trafia w `makaron`). Ten sam walidator jest bramką `check_plan`,
  hooka `BeforeFinish` i trybu `--submit`.
- **Agent nigdy nie dotyka Huba.** Buduje projekcję, `--run` zapisuje `plan.json`, a `--submit` wysyła
  go w trzech żądaniach z osobnymi werdyktami: `reset`, paczka (katalogi, potem pliki — link musi
  wskazywać istniejący plik, więc kolejność w paczce ma znaczenie), `done`. Flagę rozpoznaje regex.
- **Testy** (`--tests`) pracują na syntetycznym świecie trzech miast, więc odpowiedź do zadania nie jest
  zaszyta w kodzie.

## Decyzje

- **Liczby z ogłoszeń bez korekt z dziennika.** Dziennik mówi o Darzlubiu „wode juz sobie jakos
  wykombinowali", ale tablica ogłoszeń jest jedynym źródłem liczb i tak jest opisana w mapie treści.
- **Klucze w JSON miasta w liczbie pojedynczej**, tak jak nazwy w `/towary` — zadanie wymaga tego
  wprost tylko dla towarów, ale spójność nazw jest warta więcej niż dosłowność.
- **Notatki Natana w repo** (`natan-notes/`, 4 KB, publiczne), żeby testy i tryby offline działały
  bez sieci.

## Przebieg

**Bieg 1** (56 wywołań narzędzi, 44 zapisy, 12 nadpisań, 1 odmowa, 3 `check_plan`): agent przeczytał
cztery notatki i trzy szablony, zapisał 8 miast, potem osoby. Przy Brudzewie zapisał `/osoby/kisiel`
z samym nazwiskiem, guard odrzucił z komunikatem o kształcie `firstname_surname`, a agent
**powtórzył nazwisko** (`kisiel_kisiel`, „Kisiel Kisiel"), zamiast wrócić do dziennika po „Rafal".
W towarach najpierw pomylił kupujących ze sprzedawcami (`ziemniaki` z czterema miastami) i założył
`/towary/woda`; `check_plan` wskazał oba błędy i agent je poprawił. Plan przeszedł lokalną walidację,
paczka 32 akcji przeszła w całości (kody 10/20), a `done` odrzucił: `-805` *„Some people are missing
in /osoby files"*, `missing: ["Rafał Kisiel"]`.

Dwie luki w kodzie, obie moje: (1) guard żądał dwóch słów, ale komunikat nie mówił, skąd wziąć
brakujące, więc **sam sprowokował fałszerstwo**; (2) `ziemniaki` w liczbie mnogiej przeszło, bo ledger
też pisze `ziemniaki` i walidator uznał dokładną kopię za dopasowanie. Hub zgłasza jeden błąd naraz,
więc liczba mnoga wyszłaby dopiero w kolejnej wysyłce.

**Poprawka**: imię i nazwisko muszą być różnymi słowami, a każda odmowa dotycząca osoby mówi wprost,
że druga połowa nazwiska jest w innym wpisie dziennika o tym samym mieście; nazwy towarów i klucze
JSON kończące się na `-i`/`-y` są odrzucane jako liczba mnoga (ze wzorcem `koparki -> koparka`, bez
podawania odpowiedzi); prompt dostał zasadę „odmowy nie załatwia się powtórzeniem słowa". Stary plan
pada teraz w dokładnie czterech miejscach.

**Bieg 2** (54 wywołania, 43 zapisy, 11 nadpisań, 2 odmowy, 3 `check_plan`): to samo `/osoby/kisiel`,
ale po nowym komunikacie agent zapisał `rafal_kisiel` z „Rafal Kisiel"; druga odmowa to pusta treść
przy `/towary/woda`. `ziemniak` w pojedynczej od razu. `--submit`: `reset` → paczka 32 akcji →
`done` z **flagą**. Łącznie 7 żądań do `/verify` (`help` + dwa razy `reset`/paczka/`done`).
