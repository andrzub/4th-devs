# S04E05 — „foodwarehouse"

Zamówienia w centralnym magazynie Zygfryda dla ośmiu miast z pliku `food4cities.json`: dla każdego
miasta jedno zamówienie z poprawnym twórcą, kodem celu i podpisem, uzupełnione dokładnie tym, czego
miasto potrzebuje. Konsolowa aplikacja .NET, komunikacja jak zawsze: POST `/verify`,
`task: "foodwarehouse"`, narzędzie w polu `answer.tool`.

## Stan prac

| krok | zakres | stan |
|---|---|---|
| 0 | rekonesans API i bazy (13 odczytów, bez zmian stanu) | gotowy |
| 1 | szkielet, warstwa `Llm/` i `AgentLoop` z 04_04, klient Huba z logiem, guard zapytań, parser wyników ze stronicowaniem, zapotrzebowanie, parser zamówień (`--tests`, `--query`, `--orders`) | gotowy |
| 2 | `Mission/`: obserwacje (fakty odczytane z bazy), plan, walidator z ostrzeżeniem o roli (`--validate`) | gotowy |
| 3 | agent: prompt, hooki, 4 narzędzia, `--run` (same odczyty, zero zamówień) | gotowy; dwa biegi po 8 wywołań, plan poprawny za każdym razem |
| 4 | wykonanie: weryfikacja planu na żywo, wykonawca (reset → czyszczenie → podpis/create/append per miasto → kontrola końcowa), `--verify`, `--submit`, `--done` | **zaliczone** w trzeciej wysyłce; dwie pierwsze stanęły na nieusuwalnych seedach |

## Uruchomienie

Klucze w `appsettings.Development.json` (gitignored):

```json
{ "AI_DevsApiKey": "...", "Agent": { "ApiKey": "..." } }
```

```bash
dotnet run -- --tests
dotnet run -- --run
dotnet run -- --verify foodwarehouse-cache/run-<data>/plan.json
dotnet run -- --submit foodwarehouse-cache/run-<data>/plan.json
dotnet run -- --done
```

| tryb | model | dotyka `/verify` | uwagi |
|---|---|---|---|
| `--tests`, `--demand`, `--validate <plan>`, `--prompt` | nie | nie | offline, bez kluczy (`--prompt` bierze `help` z cache) |
| `--fetch` | nie | nie | pobiera `food4cities.json` do `data/` |
| `--help-api`, `--query "<sql>"`, `--orders [id]` | nie | tak, odczyt | zapytanie przechodzi przez ten sam guard co u agenta |
| `--run` | tak | tak, **tylko odczyt** | agent czyta bazę i listę zamówień, rejestruje plan; zapisuje `plan.json`, `observations.json`, `validation.txt` i transkrypt w `foodwarehouse-cache/run-<data>/` |
| `--verify <plan>` | nie | tak, odczyt | 4 żądania: role, zamówienia, cele i użytkownicy z planu, potem walidator |
| `--submit <plan>` | nie | **tak, zmienia stan** | najpierw `--verify`; potem `reset` → usunięcie wszystkich zamówień → per miasto podpis, `create`, `append` batch → `orders get` i porównanie z planem i zapotrzebowaniem. Zatrzymuje się **przed** `done` |
| `--reset`, `--done` | nie | tak | jawne, pojedyncze żądania; flaga wykryta regexem |

Wszystkie żądania do Huba lądują w `foodwarehouse-log.jsonl` z kluczem zredagowanym na `***`; transkrypty
biegów w `foodwarehouse-cache/`. Oba gitignored, bo mogą zawierać flagę.

## API (z akcji `help`)

| narzędzie | parametry | uwagi |
|---|---|---|
| `orders` `get` | `id` (opcjonalny) | lista zamówień: `id`, `title`, `creatorID`, `destination`, `signature`, `items[] {name, items}` |
| `orders` `create` | `title`, `creatorID`, `destination`, `signature` | nowe zamówienie startuje puste; podpis musi pasować do twórcy i celu |
| `orders` `append` | `id`, `name`+`items` albo `items` jako obiekt `{nazwa: ilość}` / tablica | **istniejący towar dostaje ilość dodaną**, nie nadpisaną |
| `orders` `delete` | `id` | całe zamówienie |
| `signatureGenerator` `generate` | `login`, `birthday` (`YYYY-MM-DD`), `destination` | SHA1 w polu `hash` |
| `database` | `query` | tylko `SELECT`, `SHOW TABLES`, `SHOW CREATE TABLE`, `.tables`, `.schema [tabela]`; **strony po 30 wierszy** (`count`, `limit`, `totalTableRows`) |
| `reset` | — | przywraca początkowe 4 zamówienia seed |
| `done` | — | werdykt i flaga |

## Dane (z rekonesansu)

- Trzy tabele: `destinations` (40 miast, `destination_id`, `name`), `roles` (6), `users` (78: `user_id`,
  `login`, `name_surname`, `password`, `birthday`, `role`, `is_active`). Tabela `users` **nie ma klucza
  głównego**: 11 wierszy ma `user_id = NULL` (rola 6 „Vibe Coder", w `name_surname` fragmenty base64,
  jeden z nagłówkiem gzip) — wygląda na misję poboczną, nieruszane.
- Na starcie 4 zamówienia seed do miast spoza misji (Susz, Rewal, Biskupiec, Hel); ich twórcy mają
  wszyscy rolę 2 „Obsługa transportów". **Seedów nie da się usunąć**: `delete` odpowiada „Order deleted"
  i zdejmuje licznik (3, 2, 1, 0), a `orders get` sekundę później pokazuje całą czwórkę. Pierwsza
  wysyłka stanęła właśnie na tym — wykonawca żądał pustego magazynu; teraz czyści tylko zamówienia do
  celów z planu, a seedy zostawia i nie ocenia.
- Podpis zweryfikowany na zamówieniu seed: `login` + `birthday` + `destination` twórcy daje bit w bit
  ten sam SHA1, który stoi na zamówieniu.
- `<` i `>` w zapytaniu dają `-620 "cannot contain HTML tags"`, więc `<>` nie działa (`!=` tak).

## Podział pracy

Model dostaje tylko to, na co kod nie odpowie bez zaglądania do bazy: jaki kod ma miasto i kto ma
podpisać zamówienie. Wszystko inne jest w kodzie:

- **Guard zapytań** (`QueryGuard`) stoi przed wysłaniem: dozwolone kształty z `help`, bez `;`, komentarzy,
  słów zapisu i `<`/`>`. Odmowa kosztuje zero żądań. Identyczne zapytanie jest serwowane z pamięci.
- **Obserwacje** (`Observations`): każdy wiersz, który agent odczytał, jest zapamiętany pod oryginalnymi
  nazwami kolumn (cząstkowe wiersze o tym samym użytkowniku są scalane po loginie). `register_orders`
  odrzuca wpis, którego kod celu albo twórca nie ma obserwacji za sobą — wartość przepisana z pamięci
  modelu nie ma jak wejść do planu. Twórca musi mieć niepusty `user_id` i `is_active = 1`, a login
  i data urodzenia muszą zgadzać się z wierszem.
- **Rola twórcy to ostrzeżenie, nie reguła**: dokumentacja o niej milczy, wniosek „twórcy zamówień seed
  mają rolę 2" należy do agenta. `check_plan` nie akceptuje planu z ostrzeżeniem domyślnie — agent musi
  je świadomie przyjąć (`accept_warnings=true`) albo zmienić plan.
- **Hook**: rejestracja odrzucana, dopóki agent nie obejrzał istniejących zamówień (jedyny przykład
  zamówienia, które magazyn przyjął). `BeforeFinish` zawraca z raportem walidatora.
- **Ilości nigdy nie przechodzą przez model**: plan zawiera tylko miasto, cel i twórcę; towary dokłada
  wykonawca z `food4cities.json`. Podpisy liczy generator na żądanie wykonawcy, nie agent.
- **Wykonawca jest idempotentny i ostrożny**: każdy `--submit` zaczyna od `reset` (zdejmuje nasze
  zamówienia z poprzedniej próby) i usunięcia zamówień do celów z planu, `create` i `append` nie są
  ponawiane po utracie odpowiedzi — najpierw odczyt stanu (powtórzony `append` podwaja ilość), przy
  częściowym wyniku bieg staje. Na końcu jedno `orders get` porównane z planem (cel, twórca, podpis)
  i zapotrzebowaniem (brak / nadmiar / zła ilość); zamówienia do innych celów (seedy) nie są oceniane.
  `done` to osobny krok.
- **`--verify` czyta bazę na nowo**: obserwacje agenta to migawka, a zamówienia idą na stan bieżący.

## Przebieg

- **Agent** (`gpt-4.1`): dwa biegi, oba po 8 wywołań narzędzi i identyczne w strukturze: `.schema` → cele po
  nazwach z pliku (0 wierszy: w bazie nazwy są z wielkiej litery, a `IN` w SQLite rozróżnia wielkość liter) →
  `list_orders` → pełna lista celów i **druga strona** (Domatowo) → użytkownicy 2/5/7/8 → `register_orders`
  z ośmioma miastami naraz → `check_plan`. Zero odmów guarda i hooków. Twórcy = czterej twórcy zamówień seed
  rotacyjnie — wniosek agenta z listingu, nie z promptu.
- **Wysyłka 1 i 2**: wykonawca po `reset` usunął seedy, dostał cztery „Order deleted", a listing dalej pokazywał
  całą czwórkę → stop na kontroli „magazyn pusty". `done` uruchomione wtedy dało `-655` z listą wszystkich 8 miast.
- **Wysyłka 3** (po poprawce: czyścić tylko cele z planu): 31 żądań — 4 weryfikacja na żywo, `reset`, `get`,
  8 × (podpis, `create` z `id` w `order.id`, `append` batch), końcowy `get` zgodny 1:1. `done` → `code 0`, flaga
  i lista 8 zamówień z nazwami miast. Seedy zostały i nie przeszkadzały.

## Testy offline

`--tests`: 134 przypadki bez sieci i klucza — guard zapytań (w tym `<>`), stronicowanie wyników,
zapotrzebowanie, porównanie zamówień, obserwacje (scalanie, aliasy, `NULL`), plan i walidator (każdy
rodzaj błędu i ostrzeżenia), narzędzia i hooki na stanie, parsery odpowiedzi wykonawcy i kontrola końcowa
(podwojona ilość, pozostawiony seed, brak zamówienia). Dane syntetyczne, odpowiedź zadania nie jest
zaszyta w kodzie.
