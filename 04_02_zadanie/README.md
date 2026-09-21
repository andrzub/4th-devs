# S04E02 — „windpower"

Harmonogram pracy turbiny wiatrowej ustawiony przez API centrali w **oknie serwisowym trwającym
40 sekund**. Konsolowa aplikacja .NET, bez modelu językowego: wszystko, co decyduje o odpowiedzi,
jest arytmetyką na danych, które API samo publikuje.

## Zadanie

Elektrownia stoi w trybie StandBy i zgłasza niedobór mocy. Trzeba:

1. zabezpieczyć turbinę na każdą wichurę w prognozie (łopaty bez oporu, produkcja wyłączona),
2. znaleźć **pierwszą** godzinę, w której da się wyprodukować brakującą moc,
3. podpisać każdy punkt konfiguracji kodem z generatora, zapisać przez `config`, domknąć `done`.

Komunikacja: POST `/verify`, `task: "windpower"`, akcja w `answer.action`.

## API (z akcji `help`)

| akcja | charakter | uwagi |
|---|---|---|
| `start` | natychmiast | otwiera okno serwisowe, `sessionTimeout: 40` |
| `get param=documentation` | **natychmiast** | jedyny raport dostępny **bez sesji** |
| `get param=weather` | kolejka, **~24 s** | prognoza: 84 odczyty co 2 h przez 7 dni |
| `get param=powerplantcheck` | kolejka, ~10 s | `powerDeficitKw` jako zakres, np. `"2-3"` |
| `get param=turbinecheck` | kolejka, ~12 s | wymagany przed `done` |
| `unlockCodeGenerator` | kolejka, **~2 s** | podpis nad `startDate`, `startHour`, `windMs`, `pitchAngle` |
| `getResult` | natychmiast | zwraca **jeden** gotowy element i go zdejmuje, w losowej kolejności |
| `config` | natychmiast | forma wsadowa: wszystkie punkty w jednym żądaniu |
| `done` | natychmiast | walidacja i flaga |

## Sedno zadania

Liniowo się nie da i o to chodzi. Prognoza — bez której nie ma ani wichur, ani godziny
produkcyjnej — zjada 24 z 40 sekund, a generator podpisów stoi za nią w łańcuchu zależności, bo
podpisuje **prędkość wiatru** z tej właśnie prognozy.

Rozwiązanie nie polega na szybszym wykonywaniu kroków, tylko na ustawieniu ich według **zmierzonego**
kosztu: wszystko zamawiane jest w pierwszej sekundzie, a to, co tanie, dzieje się w cieniu tego, co
drogie. Realny przebieg zaliczonego biegu:

```
 0,2 s  start
 0,3 s  5 zamówień (weather ×2, powerplantcheck ×2, turbinecheck)
10,2 s  deficyt        12,2 s  turbinecheck        24,2 s  prognoza → plan
24,3 s  4 zamówienia podpisów      26,2 s  podpisy
26,4 s  config (4 punkty)          26,4 s  done → flaga
```

`done` zaraportowało `elapsedSeconds: 26.28` przy limicie 40. Flaga za pierwszą wysyłką.

## Podział pracy: co liczy kod

Model językowy nie bierze udziału w tym zadaniu — nie ma tu pytania, na które kod by nie odpowiedział.
Reguły turbiny są w dokumentacji, prognoza jest tabelą liczb, a decyzja to porównanie dwóch wartości.

`TurbineModel` **czyta krzywą mocy z dokumentacji**, nie ma jej zaszytej:

```
moc = 14 kW × yield(windMs) × pitch(0° = 100%, 45° = 65%, 90° = 0%)
yield:  <4 m/s nic | 4 → 10-15% | 6 → 30-40% | 8 → 60-70% | 10 → 90-100% | 12-14 → 100% | 14+ uszkodzenie
```

Trzy decyzje modelowania, każda wymuszona przez dane:

- **Interpolacja między kotwicami tabeli.** Prognoza podaje 6,6 i 4,9 m/s, tabela zna tylko 4, 6, 8,
  10. Wariant kubełkowy (zaokrąglenie w dół) nie działa w **żadnej** zaobserwowanej sesji, więc
  interpolacja jest tym, co autor miał na myśli. Oba końce opublikowanego zakresu interpolowane są
  osobno, żeby niepewność, do której dokumentacja się przyznaje, dotrwała do decyzji.
- **Granica 14 m/s jest traktowana jako wichura.** Tabela daje przy 14 m/s jeszcze 100% wydajności,
  a reguła bezpieczeństwa nazywa `14+` uszkodzeniem — źródła są sprzeczne dokładnie w tym punkcie.
  Zabezpieczenie godziny nie kosztuje nic, utrata łopat kosztuje misję; rozbieżność ląduje w notatce.
- **Kryterium godziny produkcyjnej: „jest w stanie pokryć deficyt"** (górny wydatek ≥ górny deficyt),
  z wypisaniem marginesu pesymistycznego. Kryterium ostrzejsze (dolny wydatek ≥ górny deficyt)
  odrzuciłoby jedyne dwie godziny w tygodniu przy deficycie 2-3 kW — zwróciłoby „brak rozwiązania"
  tam, gdzie rozwiązanie istnieje.

## Co zostało zmierzone, zanim cokolwiek zostało wysłane

Cztery okna wydane na rozpoznanie, bo każda z tych rzeczy inaczej kosztowałaby zaliczenie:

- **`documentation` działa bez sesji** (`--doc`), reszta zwraca `-905`/`-915`/`-925` (`--probe`).
  Cała krzywa mocy jest więc znana przed otwarciem zegara.
- **Czasy kolejki** (`--recon`): 10 / 12 / 24 s dla raportów, ~2 s dla podpisu. Bez tego pomiaru
  kolejność zamówień jest zgadywaniem.
- **Prognoza jest losowana per sesja** — 73 z 84 odczytów zmieniło wartość między dwoma oknami,
  a deficyt przeszedł z 4-5 na 2-3 kW. To pogrzebało pierwotny pomysł podpisywania z cache'u.
  Co ciekawe, **wichury są stałe**: te same trzy godziny, te same 25 / 22 / 28 m/s.
- **Podpis nie jest związany z sesją** — ten sam punkt dostał w dwóch oknach bit w bit ten sam kod.
- **Kolejka potrafi zgubić zamówienie.** Dwa żądania wysłane 3 ms od siebie: jedno dostało
  `code 14 queued` i nie wróciło nigdy. Stąd zamówienia idą **sekwencyjnie** (żądanie to i tak 35 ms,
  więc kolejka jest swoim własnym odstępem), prognoza i deficyt są zamawiane **po dwa razy**,
  a podpis, który nie wróci w 3 s, jest **zamawiany ponownie**.
- **Dwa równoległe pollery dostały ten sam raport dwukrotnie**, więc `getResult` odpytuje **jeden**
  wątek — skoro serwer potrafi wydać element dwa razy, potrafi go pewnie i zgubić.

## Architektura

| Warstwa | Zawartość |
|---|---|
| `Analysis/` | `TurbineModel` (krzywa mocy z dokumentacji), `WeatherForecast`, `SchedulePlanner`, `ScheduleValidator`, `ValueRange` |
| `Hub/` | `WindPowerClient` — jedyne wyjście na `/verify`, log do `windpower-log.jsonl` z kluczem zredagowanym |
| `Mission/` | `QueuePump` (drenaż kolejki), `SignatureCollector`, `WindowRun` (choreografia okna), `DataCache`, `Recon` |
| `Tests/` | `OfflineTests` — 23 przypadki bez sieci i klucza |

`QueuePump` to jeden poller w tle, który rozdziela elementy po `sourceFunction`, a podpisy po
`signedParams`; reszta biegu **czeka na element po nazwie**, zamiast się o niego ścigać. To dzięki
`signedParams` — echu podpisanych parametrów w odpowiedzi — cztery podpisy można zamówić naraz:
bez niego odpowiedź generatora jest samym hashem i nie da się jej przypisać do punktu.

`ScheduleValidator` stoi **przed** jedynym `config` w oknie i odrzuca: niezabezpieczoną wichurę,
produkcję w wichurze, kąt za słaby na deficyt, godzinę późniejszą niż potrzeba, punkt podpisany na
wiatr, którego prognoza nie potwierdza, punkt bez kodu i godzinę niepełną. Bieg zatrzymuje się też,
gdy prognoza nie dotrze w terminie albo gdy `config` zostanie odrzucony — `done` nie leci wtedy
w ogóle, bo walidowałoby pusty harmonogram.

## Tryby uruchomienia

```
--tests      23 kontrole offline: bez sieci, bez klucza
--plan       harmonogram z cache'owanych raportów, offline
--help-api   opis API (akcja "help")
--doc        dokumentacja bez otwierania okna
--probe      zamówienia BEZ okna — dowód, że kolejka wymaga sesji
--recon      okno wydane na pomiar: zamawia wszystko, nic nie konfiguruje
--rehearsal  pełna choreografia z danych tej sesji, zatrzymana przed config i done
--run        całość: start, plan, podpisy, config, done
```

Tylko `start` otwiera okno; `--tests`, `--plan`, `--help-api` i `--doc` nie ruszają zegara.

## Konfiguracja

`appsettings.json` (commitowany, bez sekretów) + `appsettings.Development.json` (gitignored,
`AI_DevsApiKey`). `windpower-cache/` i `windpower-log.jsonl` są ignorowane — trzymają surowe
odpowiedzi łącznie z flagą.

## Status

✅ Zaliczone. Flaga pełnym biegiem `--run`, za pierwszą wysyłką, w 26,28 s z 40 dostępnych.
