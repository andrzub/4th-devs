# S03E05 — "savethem"

Agent planuje podróż posłańca z bazy do Skolwina. Nie dostaje ani mapy, ani listy pojazdów,
ani reguł ruchu — dostaje **jeden adres**: wyszukiwarkę narzędzi. Wszystko inne musi znaleźć sam.

## Podział pracy

Ten sam podział co zwykle, tylko granica biegnie w nowym miejscu:

| Model | Kod |
|---|---|
| Czego w ogóle szukać i jakimi słowami | Kolejkowanie, throttling i budżet zapytań |
| Jak odczytać notatki napisane ludzkim językiem | Geometria planszy i arytmetyka dwóch budżetów |
| Które fakty są regułą, a które tłem fabularnym | Odrzucenie trasy, która nie dojdzie |

Model niczego nie liczy. Zapisuje to, czego się dowiedział (`register_world`), a trasę wylicza
`RoutePlanner`. Dzięki temu reguła, której agent **nie znalazł**, objawia się jako brak trasy,
a nie jako cicho przyjęte założenie.

## Narzędzia agenta

Narzędzia huba **nie są zaszyte w kodzie** — to jest sedno zadania. Agent ma pięć funkcji:

| Narzędzie | Rola |
|---|---|
| `search_tools` | jedyny znany adres; zwraca kilka najlepszych dopasowań, nie cały rejestr |
| `ask_tool` | wywołuje narzędzie, które **wcześniej wróciło z wyszukiwarki** — nazwa zmyślona jest odrzucana bez wysyłania żądania |
| `register_world` | zapisuje mapę, tabelę spalania i reguły terenu; sprawdzany jest **kształt**, nigdy prawdziwość |
| `plan_route` | liczy najtańszą trasę dla **każdego** trybu wyjścia i mówi, dlaczego pozostałe odpadają |
| `submit_route` | jedyne wyjście na `/verify`, za bramką symulatora |

Odpowiedzi narzędzi wracają do modelu **surowe**. Komunikaty błędów tego API są samodokumentujące
(`Unknown vehicle. Allowed values: ...`, `I don't have maps for such a city.`), więc parafraza
mogłaby tylko zgubić informację — ten sam wniosek co w S01E05.

## Rozpoznanie startowe

Przed pierwszą turą kod sam przepytuje rejestr **rzeczownikami z briefingu** (`terrain`, `map`,
`vehicle`, `notes`, `route`, `supplies`) i wkleja surowe odpowiedzi do zadania agenta —
ten sam wzorzec co `BootstrapAsync` w S03E02. To nie jest podpowiedź rozwiązania: rejestr dopasowuje
po słowach kluczowych, więc zgadywanie ich to czysty koszt. Sześć deterministycznych żądań zastępuje
kilkanaście iteracji zgadywania i daje komplet trzech narzędzi (`--bootstrap` pokazuje to bez modelu).

## Gwarancje w kodzie, nie w prompcie

- **`RouteSimulator` odgrywa każdą trasę przed wysłaniem**: teren, woda, zakaz zmiany pojazdu,
  oba budżety, zejście z pojazdu. Odrzucenie kosztuje jedną turę agenta i **zero** żądań,
  odrzucenie przez hub kosztuje jedną z niewielu prób.
- **`RoutePlanner` nie zna pojęcia „najkrótsza trasa"** — dwa zasoby drenują się w różnym tempie
  zależnie od trybu, więc pojedynczy koszt nie istnieje. Szukanie trzyma wszystkie niezdominowane
  etykiety `(paliwo, jedzenie, ruchy)` na pole i dopiero na końcu przykłada budżet. Wybór pojazdu
  jest **wynikiem** tego porównania, nie założeniem przed nim.
- **`HubClient` rozstawia żądania w czasie i je liczy.** Endpointy zamykają się na ~minutę po serii
  zapytań (`429`, kod `-9999`), więc czekanie 3 s przed żądaniem jest tańsze niż limit w środku pętli.
- **Flagę wykrywa regex** w `MissionState`, a pętla nie może się zakończyć na tym, że model ogłosi
  sukces (`isGoalReached` sprawdza kod).
- **Rejestr narzędzi startuje pusty.** Wszystko poza wyszukiwarką musi najpierw pojawić się
  w wyniku wyszukiwania.
- **Limit długości zapytania egzekwowany lokalnie.** Narzędzia przyjmują **80 znaków** (`-617
  "Field query is too long"`), wyszukiwarka nie ma tego limitu — zmierzone, nie zgadnięte.
  Za długie zapytanie jest odrzucane bez wysyłki, czyli za darmo.
- **Powtórzone pytanie nie leci drugi raz.** Identyczna para (narzędzie, wartość) dostaje zapamiętaną
  odpowiedź. A gdy narzędzie odrzuci **drugą** wartość z rzędu, do wyniku dopisywana jest uwaga, że
  jego własny komunikat jest specyfikacją i trzeba wysłać wartość, nie kolejną parafrazę prośby.
- **`register_world` odrzuca świat pozorny**: znak mapy, którego nikt nie sklasyfikował, oraz oba
  budżety równe zeru. Nieznany znak czytałby się jako puste pole — to jedyny błąd, którego symulator
  nie złapie później, bo trasa wyglądałaby na przejezdną.

## Prompt

Sekcyjny (`<identity>`, `<mission>`, `<toolbox>`, `<method>`, `<rules>`, `<limits>`) i celowo
**zgeneralizowany**: nie wymienia narzędzi, nie opisuje terenu, nie sugeruje pojazdu. Mówi tylko,
że oba zasoby drenują się w przeciwnych kierunkach, że wyszukiwarka zwraca kilka trafień (więc inne
sformułowanie może odsłonić inne narzędzie) i że reguła, której się nie odnalazło, **i tak obowiązuje**.

## Tryby uruchomienia

Offline, bez sieci i bez klucza:

```bash
dotnet run -- --tests                 # 70 przypadków: rejestracja świata, reguły, bramka, planer
dotnet run -- --plan                  # plan dla każdego trybu wyjścia z pliku świata
dotnet run -- --check "rocket up up right ..."   # odegranie jednej trasy
```

Przeciw hubowi, ale **bez** `/verify`:

```bash
dotnet run -- --bootstrap             # otwierający przegląd rejestru, od którego startuje agent
dotnet run -- --tools "I need notes about movement rules and terrain"
dotnet run -- --ask books "movement rules"
dotnet run -- --ask /api/maps "Skolwin"
dotnet run -- --preview               # timeline ostatniej wysłanej trasy (istnieje dopiero po wysyłce)
```

Agent:

```bash
dotnet run -- --run                   # pełna pętla, trasy sprawdzane, nic nie wychodzi na /verify
dotnet run -- --run --submit          # pętla z prawdziwą wysyłką
dotnet run -- --submit-route rocket up up right right right up right right dismount right right right
```

`--world <plik>` wskazuje plik reguł dla trybów offline (domyślnie `savethem-cache/world.json`);
bieg agenta zapisuje tam to, co zarejestrował, więc `--plan` i `--check` mogą odtworzyć ten sam świat.

## Konfiguracja

`appsettings.json` (commitowany, bez sekretów) + `appsettings.Development.json` (gitignored):
klucz AI_devs w `AI_DevsApiKey`, klucz OpenAI w sekcji `Agent`. Sekcja `SaveThem` trzyma budżety:
`MaxHubRequests`, `MinSecondsBetweenHubRequests`, `MaxSubmissions`, `MaxIterations`.

Transkrypt biegu ląduje w `savethem-cache/run-<data>/`, wszystkie żądania w `savethem-log.jsonl`
z kluczem zredagowanym na `***`.

## Czego nauczył pierwszy bieg

Pierwsze uruchomienie `--run` (gpt-4.1, 33 iteracje, przerwane) nie zdobyło ani mapy, ani reguł —
i każdy z trzech powodów zamienił się w poprawkę w kodzie:

- **Nie znalazł `books`.** Przez 15 iteracji szukał „available tools", „supplies", „mission" — ani razu
  słowa z rodziny *notes*. Stąd rozpoznanie startowe: słownik briefingu odpytuje kod, nie model.
- **Nie skojarzył, że `maps` chce nazwy miasta.** Dostał siedem razy `-716 "I don't have maps for such
  a city"` i wysyłał kolejne opisy zamiast wartości, choć **Skolwin** miał w briefingu od pierwszej tury.
  Stąd licznik odrzuceń w wyniku narzędzia i akapit w `<toolbox>`: narzędzie odpowiada na **wartość**,
  nie na prośbę, a jego błąd jest specyfikacją.
- **Zarejestrował atrapę świata** (`map: ["S G"]`, budżety `0`), „żeby potwierdzić format" — walidator
  sprawdzał wtedy tylko kształt. Stąd wymóg sklasyfikowania każdego znaku mapy i dodatnich zapasów.

Przy okazji wyszedł limit **80 znaków** na `query` narzędzi, nieobecny w treści zadania, oraz
`429` od OpenAI przy rosnącym kontekście — stąd `Agent.MinSecondsBetweenRequests: 5`.

Osobna obserwacja: liczby zapasów (10 i 10) **nie występują w archiwum** — `books` opisuje tempo
spalania i brak stacji paliw, ale nie stan początkowy. To dana z centrali, więc siedzi w briefingu
agenta, a nie wśród rzeczy do odkrycia.

## Co zweryfikowane

- `--tests`: **73 przypadki offline** — walidacja rejestracji świata, reguły terenu, wszystkie tryby
  porażki bramki oraz kontrola krzyżowa planera z symulatorem (każda trasa zwrócona przez planer jest
  odgrywana i musi zgodzić się co do grosza paliwa i jedzenia).
- `HubClient`, rejestr narzędzi, rozpoznanie startowe i czytnik podglądu sprawdzone na żywym API
  (`--bootstrap` znajduje komplet trzech narzędzi w sześciu żądaniach).
- **Pętla agenta po poprawkach nie została ponownie uruchomiona** — kompiluje się i jest spięta,
  ale biegu z modelem po zmianach nie przeszła.
