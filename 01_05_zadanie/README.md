# S01E05 — "railway"

Agent aktywujący trasę kolejową **X-01** przez API, do którego nie ma dokumentacji.
API dokumentuje się samo: akcja `help` zwraca listę akcji, ich parametry i wymaganą kolejność wywołań.

Cała komunikacja idzie POST-em na `https://hub.ag3nts.org/verify` (`task: "railway"`, payload w polu `answer`).

## Uruchomienie

```bash
dotnet build 01_05_zadanie/01_05_zadanie.csproj
```

Domyślny bieg to **dry-run** — nie wysyła nic, tylko pokazuje, jakie pierwsze żądanie poszłoby do Huba:

```bash
dotnet run --project 01_05_zadanie/01_05_zadanie.csproj
```

Faktyczne uruchomienie agenta (wywołuje API Huba):

```bash
dotnet run --project 01_05_zadanie/01_05_zadanie.csproj -- --run
```

Klucze (`AI_DevsApiKey`, `OpenAI:ApiKey`) żyją w `appsettings.Development.json`, który jest **gitignorowany**.
Log wszystkich wywołań ląduje w `bin/Debug/net10.0/railway-log.jsonl` (JSONL, klucz API zredagowany na `***`).

## Decyzje projektowe

### Obsługa 503 i rate-limitów jest w kodzie, nie w modelu

To główna decyzja tego zadania. Lekcja mówi wprost, że zatwierdzanie i obsługa limitów
**musi być deterministyczna i odbywać się przez kod, a nie decyzję LLM** — i tu ma to podwójny sens,
bo każda iteracja modelu zużyta na błąd przejściowy to spalona część budżetu zapytań.

`RailwayClient` bierze na siebie:

- **retry na 503** z wykładniczym backoffem (2 → 4 → 8 … max 30 s) i jitterem — lekcja podkreśla,
  że 503 to symulacja przeciążenia, nie awaria;
- **respektowanie rate-limitów** — po każdej odpowiedzi czyta nagłówki i zapamiętuje stan; jeśli budżet
  jest wyczerpany (`remaining: 0`), **zasypia do czasu resetu jeszcze przed wysłaniem** kolejnego żądania,
  zamiast spalać je na pewne 429;
- **429** — czas oczekiwania bierze z nagłówków (`Retry-After` / `X-RateLimit-Reset`), backoff jest tylko fallbackiem;
- **serializację wywołań** (`SemaphoreSlim(1)`) — nawet gdyby model poprosił o kilka tool calls naraz,
  nie przebiją limitu;
- **minimalny odstęp** między żądaniami (`MinIntervalSeconds`) jako dolna granica ostrożności.

Agent widzi wyłącznie odpowiedzi, które coś znaczą. Prompt mówi mu wprost, że wynik, który do niego dotarł,
jest **ostateczny** — żeby nie próbował „jeszcze raz, może przejdzie".

### Parsowanie nagłówków rate-limitu jest tolerancyjne

Nazewnictwo nagłówków limitów nie jest ustandaryzowane, więc `RateLimitSnapshot` sprawdza kilka wariantów
(`X-RateLimit-*`, `RateLimit-*`, `X-Rate-Limit-*`), a wartość resetu przyjmuje w trzech konwencjach naraz:
delta w sekundach, unix timestamp w sekundach i w milisekundach (rozstrzyga rząd wielkości), plus HTTP-date.
Nie wiedząc, co API zwraca, taniej jest obsłużyć wszystkie kształty niż zgadnąć jeden.

### Jedno narzędzie, `params` wtapiane obok `action`

`call_railway_api(action, params?)` — klucze z `params` są wstawiane **obok** `action` w obiekcie `answer`,
bo taki kształt ma jedyny udokumentowany przykład wywołania (`answer: { action: "help" }`).
Odpowiedź wraca do modelu **surowa**, bez parafrazy: przy zadaniu, w którym komunikaty błędów precyzyjnie
wskazują problem, własnymi słowami można tylko zgubić informację.

### Prompt nie zdradza rozwiązania

Prompt systemowy podaje cel (aktywuj `X-01`), zasady oszczędzania budżetu i regułę „nie zgaduj nazw",
ale **żadnej nazwy akcji, parametru ani kolejności** — te agent musi wyczytać z `help`. Gdyby były w promptcie,
zostałby z tego workflow, a nie agent, i zadanie straciłoby sens.

Prompt osobno zabrania powtórnego wołania `help` — odpowiedź zostaje w kontekście, a ponowne pytanie to
zmarnowane zapytanie z restrykcyjnego budżetu.

### Wysyłka jest opt-in

W tym zadaniu **każda** akcja agenta to żądanie do `/verify`, więc nie da się rozdzielić „pracy" od „wysyłki"
jak w S01E04. Dlatego bez `--run` program w ogóle nie startuje pętli agenta — tylko wypisuje, co by wysłał.
Bieg domyślny nie dotyka sieci.

### Logowanie każdego wywołania

Lekcja wskazuje logowanie jako podstawę debugowania przy limitach i losowych błędach. Każda próba
(łącznie z retry) trafia do JSONL: znacznik czasu, numer próby, payload, status, **wszystkie** nagłówki
odpowiedzi i treść. Nagłówki w całości, bo przy nieznanym API to one są jedynym źródłem wiedzy o limitach.

## Struktura

| Plik | Rola |
|---|---|
| `Program.cs` | Pętla agenta, limit iteracji, licznik tokenów i żądań, tryb dry-run |
| `Railway/RailwayClient.cs` | Transport: retry na 503, respektowanie limitów, serializacja, log |
| `Railway/RateLimitSnapshot.cs` | Odczyt stanu limitu z nagłówków (wiele konwencji nazw i formatów) |
| `Railway/RailwayAgentPrompt.cs` | Prompt systemowy — cel i dyscyplina, bez rozwiązania |
| `Tools/RailwayApiTool.cs` | Narzędzie `call_railway_api` + detekcja flagi `{FLG:...}` |
| `Llm/` | Warstwa klienta LLM przeniesiona z S01E04, bez części vision (brak grafik w tym zadaniu) |

## Model

Domyślnie `gpt-4.1` (`OpenAI:DefaultModel` w `appsettings.json`). Lekcja zwraca uwagę, że przy
restrykcyjnych limitach wybór modelu ma znaczenie — model potrzebujący więcej kroków szybciej wyczerpie
budżet. Zmiana modelu to jedna wartość w konfiguracji, więc warto przetestować kilka.
