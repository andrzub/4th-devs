# S03E03 — „reactor"

Przeprowadzenie robota transportowego z modułem chłodzenia przez halę reaktora: z pierwszej kolumny
najniższego wiersza do slotu `G` w kolumnie siódmej. Po drodze poruszają się bloki rdzenia — dotknięcie
któregokolwiek niszczy robota. Sterowanie to `POST https://hub.ag3nts.org/verify`, `task: "reactor"`,
`answer: {command}`, **jedna komenda na żądanie**: `start`, `reset`, `left`, `wait`, `right`.

## Odkrycie, które zmienia ekonomię zadania

Podgląd graficzny (`hub.ag3nts.org/reactor_preview.html`) pobiera stan planszy z osobnego endpointu
**`POST /reactor_backend.php`** (`application/x-www-form-urlencoded`, pole `key` = klucz AI_devs).
Ten odczyt **nie jest komendą**: nie rusza bloków i nie zużywa budżetu. A skoro bloki poruszają się
wyłącznie na komendę, to znaczy, że **patrzenie na reaktor jest darmowe, a kosztuje dopiero ruch**.

Odpowiedź ma pełny stan i to na niej stoi cała reszta projektu:

| Pole | Znaczenie |
|---|---|
| `board` | 5 wierszy × 7 kolumn, wartości `.` / `P` / `G` / `B`; wiersz 1 to góra |
| `blocks[]` | `{col, top_row, direction}` — kolumna, górne pole bloku i **kierunek następnego ruchu** |
| `player` | `{col, row}` |
| `reached_goal`, `is_crushed` | statusy końcowe |
| `message`, `crush_message`, `updated_at` | komunikaty i znacznik czasu |

`direction` to kierunek **następnego** ruchu, więc stan planszy po najbliższym ticku jest w pełni
przewidywalny bez wysyłania czegokolwiek. To jest fundament guarda.

## Podział pracy: co liczy kod, co decyduje agent

Kod odpowiada na pytanie **„czy ta komenda zabije robota?"** — to jest geometria, a nie osąd.
Agent odpowiada na pytanie **„którą z komend, które przeżyją, opłaca się teraz wydać?"**.

`MoveGuard` odrzuca komendę **przed wysłaniem**, gdy kolumna docelowa jest zamknięta *teraz* albo
zamknie się *w tym samym ticku*. Sprawdzenie obu stanów jest celowe: dokumentacja nie mówi, czy na
serwerze najpierw rusza się robot, czy bloki, więc werdykt musi być poprawny przy obu kolejnościach.
Odrzucenie kosztuje jedną turę agenta i **zero żądań**; zgniecenie kosztuje cały bieg i zapasowego
robota, o którego oszczędzanie wprost prosi fabuła.

Jedyne wyjście awaryjne: gdy wszystkie trzy komendy ruchu są zabójcze, guard mówi wprost, że pozycja
jest przegrana i został `reset`. `reset` nie jest blokowany nigdy, żeby guard nie mógł zakleszczyć biegu.

### Ślepe zaułki, czyli dlaczego jeden tick to za mało

Pierwszy prawdziwy bieg zgubił robota **bez zgniecenia** — guard zatrzymał go w pozycji, z której nie
było już wyjścia. Powód: bloki w kolumnach 2, 3 i 4 jadą **zsynchronizowane**, więc schodzą na parter
wszystkie naraz i zamykają trzy kolumny w jednym ticku. Robot stojący w środku nie ma dokąd uciec, a
błąd zapadł **dwa ticki wcześniej** — przy wejściu w kolumnę, która wtedy była jeszcze otwarta.

Ruch bloków jest w pełni deterministyczny i **okresowy**: parter kolumny jest zajęty lub wolny jako
funkcja samego ticku, a okres to `2 × (maxTop − minTop)` = 6 dla planszy 7×5. Cała przyszłość to więc
graf stanów `(kolumna, tick mod 6)` — 42 stany, czyli rozmiar, który da się **rozstrzygnąć w całości**
zamiast zgadywać krok po kroku. Robi to `RouteFinder`:

- **`CanSurviveFrom`** — największy punkt stały: start od wszystkich stanów, w których robot może
  legalnie stać, i wykreślanie tych, z których nie ma dokąd pójść, aż zbiór przestaje się kurczyć.
  To, co zostaje, to dokładnie stany niebędące pułapkami. Guard odrzuca ruch prowadzący poza ten zbiór.
- **`TicksToGoalFrom`** — BFS po tym samym grafie: najkrótsza liczba komend do slotu albo `null`, gdy
  cel jest odcięty. To **nie jest** zakaz, tylko informacja w raporcie — decyzja zostaje przy agencie.

Wyjątek: wejście na kolumnę celu **nigdy** nie jest odrzucane jako ślepy zaułek. Dotarcie do slotu
kończy misję, więc to, co bloki zrobią potem, nie ma już znaczenia (osobny przypadek testowy).

## Feedback kontekstowy (`BoardAdvice`)

Temat lekcji. Każdy wynik narzędzia wraca do agenta nie jako surowy JSON, lecz jako sytuacja:

```
      c1  c2  c3  c4  c5  c6  c7
  r4   .  B^   .  Bv  B^   .   .
  r5   .   P   .   .   .   .   G
  robot: column 2, row 5   ^ = block moves up next tick, v = down

STATUS: robot in column 2, 5 column(s) short of the goal in column 7.

Columns within reach:
  left  -> column 1      no block in this column, safe indefinitely; goal reachable in 8 more command(s)
  wait  -> column 2      block at rows 1-2 moving down; seals the floor in 3 ticks; goal reachable in 5 more command(s)
  right -> column 3      block at rows 3-4 moving down; seals the floor on the NEXT tick; REFUSED, this move would destroy the robot

SURVIVABLE COMMANDS: left, wait. Anything else is refused before it is sent.
```

Linia trasy pojawia się **tylko przy komendach, które przejdą**. Pierwsza wersja pokazywała ją także
przy odrzuconych („the floor is sealed RIGHT NOW; goal reachable in 5") i to wystarczyło, żeby agent
zrobił się nadmiernie ostrożny: w biegu offline zaczął stać w miejscu i przeszedł planszę w 13
komendach zamiast 8, zużywając 54K tokenów zamiast 23K. Sprzeczny sygnał w jednym wierszu kosztował
trzykrotność kontekstu.

Strzałka kierunku jedzie **przy komórce**, a nie w osobnej liście obok planszy — model nie musi
zszywać dwóch reprezentacji. Liczba ticków do zamknięcia kolumny jest policzona w kodzie
(`BoardProjection`), bo to jest dokładnie ta arytmetyka, którą model musiałby powtarzać co turę
i od czasu do czasu pomylić.

## Hooki wokół pętli (`Agents/`)

Lekcja pokazuje hooki jako pełnoprawny element logiki agenta, nie tło. Trzy punkty, te same co
w przykładzie `03_03_language`:

- **`beforeToolCall`** — przepuszcza komendę przez guard. Odmowa wraca jako wynik narzędzia razem
  z aktualną sytuacją, a żądanie nigdy nie wychodzi.
- **`afterToolResult`** — dokłada kontekst, którego w wyniku nie ma: informację o fladze, o zniszczeniu
  robota, a po **dwóch odmowach z rzędu** uwagę, że lista bezpiecznych komend jest w raporcie wyżej
  i nie ma sensu zgadywać dalej.
- **`beforeFinish`** — strażnik procesu. Agent nie może zakończyć pracy, dopóki robot nie stoi
  w slocie; próba zakończenia wraca jako polecenie „popatrz na planszę i zrób następny krok"
  (do trzech razy). Wyjątki: robot zniszczony albo wyczerpany budżet — wtedy nie ma czego pilnować.
  **Trzeci wyjątek dopisany po pierwszym biegu**: w przegranej pozycji hook nie żąda pracy, tylko
  autoryzuje `reset` (do `MaxResets` razy). Wcześniej agent trzy razy z rzędu napisał „mogę zresetować,
  czy mam to zrobić?" — zachował się rozsądnie, bo prompt każe traktować `reset` jako ostateczność,
  a hook uparcie kazał mu pracować dalej. Pytanie bez adresata to koszt, nie bezpieczeństwo.

Guard siedzi także w `ReactorSession.Screen`, przez co ta sama reguła obowiązuje w trybie ręcznym
(`--command`), który hooków nie ma.

## Narzędzia agenta

Tylko dwa, bo tylko dwie rzeczy można zrobić z reaktorem:

- **`look`** — czyta planszę. Darmowe, nie rusza bloków. Prompt mówi wprost, że wynik `send_command`
  już zawiera świeży raport, więc `look` zaraz po komendzie niczego nie wnosi (bez tej linijki agent
  wołał `look` po każdym ruchu — 12 iteracji i 28,6K tokenów zamiast 9 i 18,3K).
- **`send_command`** — wysyła **jedną** komendę i przesuwa reaktor o jeden tick.

## Gwarancje w kodzie

- **Flagę wykrywa regex** w `MissionState`, a pętla kończy się na `FlagReceived`, nie na deklaracji
  modelu. Prompt zakazuje zmyślania flagi.
- **Parser jest tolerancyjny** (`BoardStateParser`): szuka `board` *gdziekolwiek* w odpowiedzi,
  przyjmuje `top_row` i `topRow`, wiersze jako tablice i jako stringi, a pozycję robota bierze z pola
  `player` albo — gdy go nie ma — ze znacznika `P` na planszy. Kształt odpowiedzi `/verify` nie był
  znany przy pisaniu kodu, więc zamiast zgadywać jeden wariant, obsłużone są wszystkie sensowne.
- **Odpowiedź na komendę nie musi nieść planszy.** Gdy jej nie niesie, sesja dociąga stan darmowym
  odczytem, żeby guard nigdy nie oceniał następnego ruchu na nieaktualnym obrazie.
- **Bieg rozpoczęty w innym procesie jest rozpoznawany**: plansza istnieje tylko wtedy, gdy zadanie
  jest wystartowane, więc jej pojawienie się ustawia `Started`. Dzięki temu `--run` uruchomiony po
  ręcznym `start` nie wyśle drugiego `start` na cudzy postęp.
- **Budżet komend** jest pilnowany w kliencie, nie przez model; jego wyczerpanie kończy bieg.
- Każde żądanie ląduje w `reactor-log.jsonl` z kluczem zredagowanym na `***`.

## Sprawdzone offline

- **`--tests`: 47 przypadków**, bez sieci i bez klucza — guard (kolumna zamknięta teraz / zamykająca
  się w ticku / blok odjeżdżający / krawędzie planszy / stany końcowe / ślepe zaułki), odbicia bloków
  na krańcach, liczenie ticków do zamknięcia i parser w czterech kształtach odpowiedzi.
- **Prawdziwa plansza z przegranego biegu jest przypadkiem testowym.** Trzy kolejne stany reaktora
  zostały przepisane z logu; testy sprawdzają, że `BoardProjection` przewiduje je co do kratki (model
  ruchu bloków zgadza się z reaktorem), że **dokładnie ten ruch, który zgubił robota, jest teraz
  odrzucany**, że pozycja, w której bieg utknął, faktycznie nie ma wyjścia, i że z pozycji startowej
  istnieje trasa — **10 komend**.
- **`--simulate`**: `SimulatedReactorApi` odpowiada w tym samym kształcie JSON co podgląd, więc parser,
  guard i pętla agenta działają na nim bez żadnej zmiany. Deterministyczny `RehearsalPilot`
  przechodzi planszę na sześciu układach — to dowód, że guard zostawia drogę i nie zakleszcza robota.
- **`--run --offline`**: pełna pętla agenta przeciw symulatorowi, bez `/verify` i bez ryzyka.
  Wyniki: seed 0 i 3 — 7 komend, same `right`; seed 2 — 8 komend (`right`, `wait`, 5× `right`).
  Zero odmów guarda, zero zniszczonych robotów.
- **Stan reaktora po stronie Huba wygasa.** Plansza z zablokowanym robotem zniknęła po kilkunastu
  minutach (`-980` z podglądu), więc kolejny bieg zaczyna od czystego `start`. Sesja i tak nie zakłada
  żadnej konkretnej fazy bloków — `RouteFinder` liczy wszystko z tego, co zwróci API.

## Tryby uruchomienia

| Polecenie | Co robi |
|---|---|
| `--tests` | 36 kontroli offline. Bez sieci, bez klucza, bez LLM |
| `--simulate [--seed N]` | Próba przejścia na symulatorze stałym pilotem. Bez sieci, bez LLM |
| `--board [--offline]` | Odczyt planszy. **Nie przesuwa reaktora** |
| `--command <cmd> [<cmd>…]` | Ręczne komendy przez ten sam guard. Każda to jeden tick |
| `--run --offline [--seed N]` | Pętla agenta przeciw symulatorowi. Bez `/verify` |
| `--run` | Pętla agenta przeciw prawdziwemu reaktorowi |

Transkrypt biegu: `reactor-cache/run-<data>/transcript.txt`. Log żądań: `reactor-log.jsonl`.

## Konfiguracja

`appsettings.json` jest szablonem; klucze żyją w `appsettings.Development.json` (gitignored):
`AI_DevsApiKey` oraz `Agent:ApiKey`. Model: `gpt-4.1`. Wymiary planszy i kolumna celu **nie są**
w konfiguracji — czyta się je z planszy zwróconej przez API, żeby błędny opis w briefingu nie mógł
wprowadzić guarda w błąd.
