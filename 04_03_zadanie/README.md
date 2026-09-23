# S04E03 — „domatowo"

Misja ratunkowa w ruinach Domatowa: odnaleźć partyzanta ukrytego „w jednym z najwyższych bloków"
i wezwać helikopter na pole, na którym zwiadowca potwierdził człowieka. Konsolowa aplikacja .NET,
komunikacja jak zawsze: POST `/verify`, `task: "domatowo"`, akcja w `answer.action`.

## Stan prac

| krok | zakres | stan |
|---|---|---|
| 1 | szkielet, klient huba, log żądań, rekonesans API (`--help-api`, `--get-map`, `--state`, `--action`) | gotowy |
| 2 | model mapy, trasy, skupiska, cennik, guard akcji, planer przeszukania, 39 testów offline (`--tests`, `--plan`) | gotowy |
| 3 | wykonawca planu (`--next`, `--step`, `--run`): obserwacja z darmowych odczytów, taktyk wybiera akcję, guard wycenia, ledger pamięta załogi i inspekcje; **bez modelu językowego** | gotowy |

## Uruchomienie

Klucz AI_devs w `appsettings.Development.json` (gitignored):

```json
{ "AI_DevsApiKey": "..." }
```

```bash
dotnet run -- --tests
dotnet run -- --plan
dotnet run -- --help-api
dotnet run -- --get-map
dotnet run -- --state
dotnet run -- --action getLogs
dotnet run -- --action create type=transporter passengers=2
```

| tryb | dotyka `/verify` | koszt w punktach akcji |
|---|---|---|
| `--tests` | nie | żaden — bez sieci i klucza |
| `--plan [--symbol B3]` | nie | żaden — mapa i cennik z `domatowo-cache/` |
| `--help-api`, `--get-map [--symbols ...]` | tak | żaden (akcje informacyjne) |
| `--state` | **nie** | żaden — backend podglądu `domatowo_backend.php` |
| `--action <nazwa> [k=v ...]` | tak | **zależy od akcji** — jedyny tryb, który tworzy jednostki i wydaje punkty; przechodzi przez ten sam guard co plan |

Tryby można łączyć w jednym wywołaniu. Surowe odpowiedzi lądują w `domatowo-cache/`, każde żądanie
w `domatowo-log.jsonl` z kluczem zredagowanym na `***`. Oba są gitignored, bo mogą zawierać flagę.

## API (z akcji `help` i `actionCost`)

| akcja | cena | uwagi |
|---|---|---|
| `create type=scout` | 5 | jednostki pojawiają się na **kolejnym wolnym slocie `A6 → D6`** |
| `create type=transporter passengers=N` | 5 + 5·N | 1–4 pasażerów; zwiadowcy **nie wracają** na pokład (nie ma akcji wsiadania) |
| `move object where` | transporter 1/pole, zwiadowca 7/pole | **trasę liczy serwer**: transporter tylko ulicami, zwiadowca najkrótszą ortogonalną przez cokolwiek |
| `inspect object` | 1 | bada **wyłącznie pole, na którym stoi** zwiadowca; wpis trafia do `getLogs` |
| `dismount object passengers` | 0 | rozstawia zwiadowców „na wolnych polach wokół pojazdu" |
| `callHelicopter destination` | 0 | tylko po potwierdzeniu człowieka i tylko na to pole |
| `reset` | 0 | czyści planszę **i losuje pozycję partyzanta na nowo** |
| `getMap`, `searchSymbol`, `getObjects`, `getLogs`, `expenses`, `actionCost`, `help` | 0 | informacyjne |

Backend podglądu (`POST domatowo_backend.php`, `action=pull`, wymaga nagłówka `Origin`/`Referer` huba)
zwraca za darmo `stats` (punkty, `human_found`, **`human_found_at`**, `mission_flag`), `objects`
(jednostki z pozycjami) i `tasks` (kolejka animacji). Odczyt nie jest akcją, jak w S03E03.

## Teren i plan

```
     A  B  C  D  E  F  G  H  I  J  K
 1  DR UL UL UL .. B3 B3 DR .. PK PK
 2  DR DR .. UL UL B3 B3 DR UL PK PK
 3  .. .. .. UL PK .. .. DR UL .. ..
 4  B1 B1 .. UL PK SZ SZ SZ UL BS BS
 5  B1 B1 .. UL PK SZ SZ SZ UL BS BS
 6  UL UL UL UL UL UL UL UL UL UL ..
 7  B2 B2 .. UL .. KS KS KS .. DR ..
 8  B2 B2 .. UL .. KS KS KS .. DR ..
 9  .. UL UL UL UL UL UL UL UL UL ..
10  B3 B3 B3 .. DR .. .. B3 B3 DR ..
11  B3 B3 B3 .. DR .. .. B3 B3 DR ..
```

Sygnał mówi „jeden z najwyższych bloków", czyli `B3` (`TargetSymbol` w konfiguracji — to odczyt operatora,
nie kodu). 14 pól w trzech skupiskach, każde styka się z ulicą: `F1–G2` (przystanek `E2`), `A10–C11`
(`B9`/`C9`), `H10–I11` (`H9`/`I9`).

Podział pracy: **kod liczy, serwer jeździ**. `Pathfinder` (BFS po ulicach, Manhattan dla zwiadowcy)
służy do wyceny akcji przed wysłaniem, bo trasę i tak wybiera hub. `SearchPlanner` wycenia wyczerpująco
wszystkie porządki skupisk i wszystkie podziały między transportery (24 kandydatów), a trasę zwiadowcy
po skupisku liczy jako najtańszą ścieżkę przez wszystkie pola (permutacje, ≤ 6 pól). Kryterium:
**najniższy koszt oczekiwany** przy jednostajnym rozkładzie partyzanta po 14 polach, pod warunkiem że
najgorszy przypadek (plus 7 pkt rezerwy na każde wysadzenie, gdyby zwiadowca wylądował obok bloku,
a nie na nim) mieści się w 300.

Wynik `--plan`: transporter z 2 zwiadowcami `A6 → C9` (skupisko lewe dolne) `→ H9` (prawe dolne), a dopiero
gdy tam pusto, **drugi** transporter z 1 zwiadowcą `A6 → E2` (górne). Najgorszy przypadek 136 + 21 rezerwy
= **157 z 300**, koszt oczekiwany **77,0**. Jeden konwój z 3 zwiadowcami ma niższy najgorszy przypadek (135),
ale wyższy oczekiwany (80,3), bo trzeci pasażer jest opłacany z góry także wtedy, gdy partyzant jest
w pierwszym skupisku.

`ActionGuard` stoi przed każdą wysyłką (także ręczną przez `--action`): wycena i budżet, transporter tylko
na ulicę osiągalną ulicami, limity 4/8 jednostek, `inspect` tylko dla zwiadowcy, `dismount` tylko gdy wokół
pojazdu jest dość wolnych pól, `callHelicopter` tylko na pole równe `human_found_at`, `reset` wyłącznie
z `--allow-reset`. Stan do oceny pochodzi z darmowego `pull`, nie z relacji modelu.

## Wykonawca (krok 3)

`Operation` prowadzi przeszukanie **jedną akcją na raz**: trzy darmowe odczyty (`pull` → punkty i `human_found`,
`getObjects` → pozycje, `getLogs` → nowe wpisy), `Tactician` wybiera akcję, `ActionGuard` ją wycenia i sprawdza,
wysyłka, i od nowa. Między krokami nic nie żyje w pamięci poza `domatowo-cache/ledger.json`, więc bieg można
przerwać po dowolnym kroku i wznowić z żywej planszy (`--step` to dokładnie jedna akcja, `--next` tylko pokazuje
wybór). **Bez modelu językowego**: wpisy `getLogs` to zróżnicowana proza („kot uciekający przez wybite okno",
„szczur większy niż kot"), a o znalezieniu rozstrzyga flaga serwera, nie treść wpisu.

Kolejność preferencji taktyka idzie za cennikiem: `inspect` tam, gdzie zwiadowca już stoi (1 pkt) → `dismount`
z transportera stojącego na przystanku nieobsadzonego skupiska (0) → krok zwiadowcy po swoim skupisku (7/pole)
→ przejazd załadowanego transportera do najtańszego skupiska (1/pole) → dopiero zakup nowych jednostek (liczba
pasażerów z planera dla tego, co zostało). Zwiadowca dostaje skupisko tylko wtedy, gdy dojście pieszo nie jest
droższe niż dowiezienie świeżego zwiadowcy, więc po skończonym bloku nie maszeruje przez pół miasta.

Ledger istnieje, bo hub dwu rzeczy nie oddaje na żądanie: **ile zwiadowców siedzi w transporterze** (ani
`getObjects`, ani podgląd tego nie mówią; zapis z `crew[]` przy `create` i `dismounted[]` przy `dismount`,
a dla pojedynczego transportera o nieznanym stanie wnioskowanie z liczników) oraz **które pola obejrzano**
(wpisy `getLogs` wygasają po kilku minutach, więc każdy przyjęty `inspect` i każdy odczytany wpis są zapisywane).

## Przebieg (zaliczony)

Odcinek pierwszy ręcznie przez `--action` (rozpoznanie reguł), reszta wykonawcą w trzech biegach
(`--max-actions 6`, `10`, `18`), łącznie **28 akcji płatnych, 155 z 300 punktów**, człowiek znaleziony na
**`G1`** jako 12. z 14 pól. Zero odrzuceń guarda, zero błędów huba.

| etap | akcje | punkty |
|---|---|---|
| transporter z 2 zwiadowcami `A6 → C9`, zwiadowca ląduje na `C8`, dochodzi na `C10` | create, move, dismount, inspect C8, move | 37 |
| skupisko `A10–C11` (6 pól, pusto) | 5 ruchów + 6 inspekcji | +41 → 78 |
| transporter `C9 → H9`, zwiadowca ląduje na `H8` (kościół), dochodzi na `H10` | move, dismount, move | +19 → 97 |
| skupisko `H10–I11` (4 pola, pusto) | 3 ruchy + 4 inspekcje | +25 → 122 |
| drugi transporter z 1 zwiadowcą spawnuje na **`B6`**, `→ E2`, zwiadowca ląduje na `E1`, dochodzi na `F1` | create, move, dismount, move | +24 → 146 |
| skupisko `F1–G2`: `F1` pusto, **`G1` trafienie** | 1 ruch + 2 inspekcje | +9 → 155 |

Czego nauczył bieg, a czego nie było w `help`:

- **`dismount` sadza zwiadowcę na polu na północ od pojazdu**, także gdy to budynek (`H8` to kościół). Reguła
  to kierunek, nie rodzaj terenu. Wszystkie bloki `B3` leżą na południe albo na wschód od swoich przystanków,
  więc zwiadowca zawsze dochodził pieszo: 2 pola (`C8→C10`, `H8→H10`) albo 1 (`E1→F1`). Planer liczy rezerwę
  2 kroków na wizytę (`SearchPlanner.LandingWalk`).
- **`getObjects` jest źródłem prawdy o pozycjach**; `objects` w backendzie podglądu stoją w miejscu, dopóki
  strona podglądu nie potwierdzi kolejki animacji (`ack`). Punkty i `human_found` w `pull` są aktualne.
- **Sloty spawnu zużywają się**: drugi transporter pojawił się na `B6`, choć `A6` było już puste. Planer zakładał
  ponowne `A6`; różnica to jedno pole drogi.
- `path_steps` w odpowiedzi `move` liczy pole startowe (8 dla 7 przejechanych pól); koszt liczony jest bez niego.
- `getObjects` zwraca `typ` zamiast `type`; parser przyjmuje oba.
- Wpisy `getLogs` znikają po kilku minutach (pusty odczyt 10 min po inspekcji `C8`), ale w obrębie biegu
  powtarzają się przy każdym odczycie. Ledger dedupluje i pamięta.
- Rekord fabularny: wpis z trafienia brzmi „Udało się. Mężczyzna w wieku około 30 lat chował się za workami
  z cementem." Helikopter (`callHelicopter destination=G1`) woła operator ręcznie, wykonawca tylko wypisuje polecenie.
