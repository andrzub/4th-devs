# S03E01 — "evaluation"

Znalezienie anomalii w 9999 plikach JSON z odczytami czujników elektrowni i odesłanie do Centrali
identyfikatorów wszystkich plików, które trzeba sprawdzić ponownie
(POST `/verify`, `task: "evaluation"`, `answer: {recheck: [...]}`).

Zadanie definiuje cztery rodzaje anomalii: wartość poza normą, kanał nieaktywny raportujący dane,
notatka operatora zapewniająca, że wszystko jest w porządku przy błędnych danych, oraz notatka
zgłaszająca awarię przy danych poprawnych.

## Podział pracy: co rozstrzyga kod, a co model

Trzy z czterech definicji anomalii to arytmetyka na zadeklarowanych zakresach — `sensor_type`
mówi, które kanały są aktywne, aktywny kanał musi mieścić się w normie, nieaktywny musi zwracać
dokładnie `0`. Model nie jest o to pytany ani razu.

Model dostaje **jedno pytanie**, na które kod nie potrafi odpowiedzieć: *czy ta wypowiedź operatora
twierdzi, że sprzęt jest sprawny, czy że coś jest nie tak?* Nic w plikach nie mówi, co operator miał
na myśli, więc nie ma tu deterministycznego źródła prawdy — i właśnie dlatego ta jedna decyzja
dostała warstwę ewaluacji (`Evals/`), a nie kolejny `if`.

| Warstwa | Odpowiada za |
|---|---|
| `Sensors/` | model danych, zakresy, walidacja odczytu, pobranie i cache archiwum |
| `Notes/` | dekompozycja notatek, klasyfikator wsadowy, parsowanie odpowiedzi, cache wyroków |
| `Anomalies/` | złożenie danych z notatką w werdykt na plik i listę `recheck` |
| `Observability/` | licznik tokenów i kosztu, log przebiegu, zapis pełnych interakcji |
| `Evals/` | oznaczony zbiór testowy, metryki klasyfikatora, testy offline całej otoczki |
| `Hub/` | jedyne wyjście na świat — wysyłka odpowiedzi |

## Redukcja kosztu: 9999 → 2032 → 325

Wskazówka z lekcji mówi wprost, że wrzucenie wszystkiego do modelu będzie drogie i że w danych
mnóstwo informacji się powtarza. Powtórzenia są tu na dwóch poziomach:

1. **Deduplikacja notatek.** 9999 notatek to tylko **2032 unikalne treści** (20,3 % archiwum).
2. **Deduplikacja klauzul.** Notatki są szablonowe: 2031 z 2032 składa się z dokładnie trzech
   klauzul rozdzielonych przecinkami, dobranych z trzech pul (43 otwarcia, 38 środków, 243
   zakończenia). Unikalnych klauzul w całym archiwum jest **325**. Ocena puli klauzul zamiast puli
   notatek to ~11× mniej wejścia (7,4 tys. vs 84 tys. tokenów, `--analyze` liczy oba warianty).

Struktura klauzul jest **odkrywana w kodzie** (`NoteDecomposer`, podział po przecinkach), a nie
zaszyta na sztywno. Notatka, która się nie rozkłada — jak jedyna ręcznie napisana notatka w
archiwum — jedzie do modelu jako całość, więc nikt nie kroi na fragmenty tekstu, który sensu we
fragmentach nie ma. Werdykt notatki to złożenie werdyktów klauzul: **jedna klauzula zgłaszająca
problem decyduje o całej notatce**, co jest jednocześnie regułą biznesową i zabezpieczeniem przed
notatką o mieszanym tonie („wartości wyglądają dobrze, ale przebieg dwa razy skoczył").

Trzeci poziom to **kształt odpowiedzi**. Output kosztuje kilka razy więcej niż input, a
przytłaczająca większość wypowiedzi jest rutynowa, więc model nie wypisuje werdyktu dla każdej
pozycji — zwraca **tylko numery wyjątków**:

```
PROBLEM: 14,37
UNCLEAR:
```

Wszystko, czego nie wymienił, jest liczone jako `Ok`. Odpowiedź na 60-elementowy batch to
kilkanaście tokenów zamiast kilkuset. Do tego wyroki lądują w `ToneCache` na dysku (klucz zawiera
nazwę modelu), więc powtórny bieg całego pipeline'u nie kosztuje nic.

## Cena tego kształtu odpowiedzi i zabezpieczenie

Format „wypisz tylko wyjątki" ma wadę wprost proporcjonalną do swojej zalety: **odpowiedź ucięta,
leniwa albo pusta wygląda identycznie jak „nic do zgłoszenia"**. Milczenie modelu jest tu poprawną
odpowiedzią w 98 % przypadków, więc nie da się go odróżnić od awarii.

Dlatego do **każdego batcha** wstrzykiwane są dwie **wypowiedzi kontrolne** o znanym werdykcie —
jedna oczywiście awaryjna, jedna oczywiście rutynowa — na pozycjach losowanych z ziarna równego
numerowi batcha (powtarzalnie, żeby logi dwóch biegów dały się porównać). Jeśli model przestanie
oznaczać podstawioną awarię albo zacznie oznaczać podstawioną rutynę, batch jest odrzucany i
powtarzany (do 3 prób), a nie cicho przyjmowany. Teksty kontrolne są napisane poza słownikiem
elektrowni, więc nie mogą się zderzyć z prawdziwą notatką. To ewaluacja **online**, w trakcie
biegu, w odróżnieniu od zbioru testowego niżej.

Parsowanie odpowiedzi jest twarde: brak którejś linii, numer poza zakresem, numer w obu liniach
naraz — każde z tych zdarzeń to błąd batcha, nie „przymknięcie oka".

## Observability

Warstwa obserwacji jest zbudowana na tej samej hierarchii, którą lekcja opisuje na przykładzie
Langfuse, tylko lokalnie:

| Pojęcie z lekcji | Odpowiednik tutaj |
|---|---|
| session | bieg (`runId`, katalog `sensors-cache/run-<data>/`) |
| trace | jedno logiczne przejście po danych (`classify:clauses`, `eval:clauses`, `report`) |
| generation | jedno wywołanie modelu z własnymi tokenami, kosztem i czasem |
| event | zdarzenie aplikacji (`run-started`, `classification-planned`, `answer-built`) |

Wszystko trafia do `classification-log.jsonl`, a pełna treść każdej interakcji (prompt systemowy,
prompt użytkownika, surowa odpowiedź, decyzja o przyjęciu batcha) do plików w katalogu biegu —
czyli materiał do odtworzenia stanu interakcji i wklejenia do Playgroundu, gdyby coś poszło nie tak.

`UsageMeter` liczy tokeny i pieniądze **per trace** i sumarycznie, razem z tokenami obsłużonymi
z cache prefiksu providera (`prompt_tokens_details.cached_tokens`) wycenianymi po niższej stawce.
Cennik siedzi w `appsettings.json`, bo taryfy zmieniają się częściej niż kod. Raport podaje też
koszt na 1000 sklasyfikowanych wypowiedzi — jedyną liczbę, którą da się sensownie ekstrapolować.

## Evals

`Evals/note-tone.labeled.json` to 30 ręcznie oznaczonych wypowiedzi, zaprojektowanych według trzech
zasad z lekcji:

- **Pokrycie** — wszystkie trzy werdykty plus tryby porażki, które faktycznie tu grożą: negacja
  („nothing suggests a fault condition" jest zdrowe, choć nazywa awarię), słownictwo awaryjne w
  zdrowym kontekście („kontrolka awarii przetestowana, działa"), notatki o tonie mieszanym,
  tekst nieorzekający o niczym.
- **Różnorodność** — połowa przypadków to prawdziwe notatki z archiwum wzięte dosłownie, połowa jest
  napisana pod konkretną pułapkę. Dane syntetyczne są dobrym punktem startu, ale same nie pokazują,
  jak wygląda prawdziwy szum.
- **Balans** — 12 `Ok`, 12 `Problem`, 6 `Unclear`, żeby klasyfikator nie dostał wysokiego wyniku
  za faworyzowanie jednej klasy.

`--evals` liczy macierz pomyłek oraz precision / recall / F1 na klasę i zwraca kod wyjścia różny od
zera poniżej progu z konfiguracji (domyślnie 95 %). Zbiór idzie przez **dokładnie tę samą ścieżkę**
co dane produkcyjne, razem z wybranym tierem, więc mierzy to, co realnie zdecyduje o odpowiedzi.

Najważniejsze: `--report` **uruchamia ewaluację przed zbudowaniem odpowiedzi i odmawia jej
zbudowania**, jeśli klasyfikator nie przejdzie progu. To sedno lekcji — regresja w ocenie notatek
nie jest widoczna w wyniku (lista identyfikatorów wygląda tak samo), a przesądza o zaliczeniu.
`--skip-evals` istnieje jako świadome obejście, nie jako domyślne zachowanie.

Obok tego `--selftest` sprawdza **offline, bez klucza i bez sieci**, całą otoczkę klasyfikatora:
parsowanie odpowiedzi, działanie wypowiedzi kontrolnych, dekompozycję i składanie werdyktów oraz
wszystkie cztery definicje anomalii na syntetycznych odczytach. 34 przypadki, zero tokenów.

## Co pokazują dane

Bieg `--analyze` (bez modelu):

- **46 plików ma błędne dane** — 22 wartości poza normą i 24 przypadki, w których nieaktywny kanał
  coś raportuje. Najczęstszy: kanał wilgotności zwracający odczyt w czujniku, który wilgotności nie
  mierzy (12 plików).
- Niektóre „duchy" są podstępne, bo mieszczą się w normie *swojego* kanału — `3123` to
  `humidity/pressure/temperature`, który raportuje `voltage_supply_v = 230.5`, czyli wartość
  całkowicie zdrową dla napięcia. Sam zakres tego nie wyłapie; wyłapuje to porównanie z `sensor_type`.
- Notatek negatywnych jest w archiwum garść — pule klauzul „awaryjnych" mają po kilka wystąpień,
  gdy pozytywne po kilkadziesiąt. Większość z nich siedzi na plikach, które i tak są anomalią z
  powodu danych, więc **realnie o wyniku decyduje kilka plików** ze zdrowymi danymi i zaniepokojoną
  notatką. Tym bardziej opłaca się mieć na tę ocenę ewaluację.
- Plik `2137.json` ma jedyną nieszablonową notatkę w całym archiwum: *„The report looks completely
  normal. I will go to check status of all other devices."* Dane są zdrowe, notatka pozytywna, więc
  **nie jest anomalią** — to żart, nie pułapka. Trafiła do zbioru testowego jako przypadek
  brzegowy, bo jest jedyną notatką, która nie rozkłada się na klauzule.

Wstępny odczyt tonacji notatek zrobiony offline w trakcie analizy danych sugerował **~52 pliki**
łącznie (46 z danych + ~6 z notatek). To oczekiwanie do weryfikacji, nie wynik — ostateczną listę
buduje `--report` na podstawie werdyktów modelu.

## Uruchomienie

```
dotnet build
dotnet run -- --fetch                 # pobranie i rozpakowanie archiwum (~3,6 MB)
dotnet run -- --analyze               # cała warstwa deterministyczna + porównanie kosztu tierów
dotnet run -- --selftest              # 34 testy offline otoczki klasyfikatora
dotnet run -- --evals                 # zbiór oznaczony: macierz pomyłek, precision/recall
dotnet run -- --classify              # klasyfikacja notatek, rozkład werdyktów, koszt
dotnet run -- --report                # evals jako bramka, potem pełna lista anomalii -> answer.json
dotnet run -- --submit                # jedyna wysyłka na /verify
```

Opcje: `--tier clauses|notes` (domyślnie `clauses`), `--refresh` (ponowne pobranie archiwum),
`--skip-evals` (zbudowanie odpowiedzi bez bramki jakości).

Klucze w `appsettings.Development.json` (gitignored): `AI_DevsApiKey` oraz `Classifier:ApiKey`.
Domyślny model to `gpt-4.1-mini` — pytanie o tonację jednej wypowiedzi nie wymaga większego, a przy
takiej liczbie wywołań różnica w cenie jest widoczna.

Artefakty biegu (wszystkie gitignored): `sensors-cache/` (archiwum, cache wyroków, katalogi biegów),
`classification-log.jsonl`, `evaluation-log.jsonl` (wysyłki z zredagowanym kluczem), `answer.json`.

## Świadome decyzje

- **Model nie jest agentem.** Warstwa `Llm/` została przeniesiona z S02E05 **bez function callingu** —
  nie ma pętli, narzędzi ani `ToolCall`. Tu nie ma nic do eksplorowania: pliki są lokalne, pytanie
  jest jedno i takie samo dla każdej wypowiedzi. Pętla agenta byłaby kosztem bez zysku.
- **Klasyfikowane są wszystkie notatki, nie tylko z plików o zdrowych danych.** Do samej odpowiedzi
  wystarczyłyby notatki plików bez wykrytych usterek (plik z błędnymi danymi jest anomalią
  niezależnie od notatki), co oszczędziłoby kilka procent. Ale wtedy raport nie potrafiłby wskazać
  plików, w których operator **podpisał błędny odczyt jako zdrowy** — a to jest dokładnie ten dowód
  nierzetelności, o który prosi fabuła. Kilka procent za tę informację to dobra cena.
- **`Unclear` nie wchodzi do odpowiedzi.** Notatka nieorzekająca o niczym przy zdrowych danych nie
  pasuje do żadnej z czterech definicji anomalii, więc nie jest zgłaszana — ale ląduje w osobnej
  sekcji raportu „do spojrzenia przez człowieka", zamiast zniknąć.
- **Wysyłka jest osobnym trybem.** `--report` buduje i pokazuje listę, `--submit` ją wysyła. Zadanie
  zalicza jedno poprawne żądanie, więc lista przechodzi przez oczy człowieka po drodze.
- **Estymacja tokenów w `--analyze` to przybliżenie ~4 znaki/token**, nie tokenizer. Do wyboru
  między tierami, gdzie różnica jest jedenastokrotna, dokładność tokenizera nic nie zmienia; realny
  koszt raportuje potem `UsageMeter` z danych zwróconych przez API.
