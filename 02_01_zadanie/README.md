# S02E01 — "categorize"

Agent w roli **inżyniera promptów** dla zdalnego klasyfikatora ładunków. Klasyfikator to celowo
archaiczny model z oknem **100 tokenów**, który dla każdego z 10 towarów dostaje jeden prompt
i ma odpowiedzieć `DNG` (niebezpieczny) lub `NEU` (neutralny). Haczyk fabularny: wszystko, co
związane z reaktorem, ma **zawsze** wychodzić jako `NEU`. Całość ma budżet **1,5 PP** na 10 zapytań.

Cała komunikacja idzie POST-em na `https://hub.ag3nts.org/verify` (`task: "categorize"`,
`answer: { prompt: ... }`) — po jednym zapytaniu na towar, plus `{"prompt": "reset"}` do zerowania licznika.

## Uruchomienie

```bash
dotnet build 02_01_zadanie/02_01_zadanie.csproj
```

Domyślny bieg to **dry-run** — nie wysyła nic, tylko pokazuje kształt żądań i sprawdza lokalny tokenizer:

```bash
dotnet run --project 02_01_zadanie/02_01_zadanie.csproj
```

Faktyczne uruchomienie agenta (wywołuje API Huba):

```bash
dotnet run --project 02_01_zadanie/02_01_zadanie.csproj -- --run
```

Klucze (`AI_DevsApiKey`, `OpenAI:ApiKey`) żyją w `appsettings.Development.json`, który jest **gitignorowany**.
Log wszystkich wywołań `/verify` ląduje w `bin/Debug/net10.0/categorize-log.jsonl` (klucz API zredagowany na `***`).

## Decyzje projektowe

### Matematyka budżetu wymusza cache — i to ona projektuje szablon

Cennik: 10 tokenów wejściowych = 0,02 PP, 10 z cache = 0,01 PP, 10 wyjściowych = 0,02 PP.
Dziesięć promptów po pełne 100 tokenów bez cache to **2,0 PP — ponad budżet**. Wnioski, które
kod egzekwuje, a prompt systemowy tłumaczy agentowi:

- zmienne dane (`{id}`, `{description}`) muszą być **na samym końcu** szablonu, żeby stały prefiks
  cache'ował się od drugiego zapytania;
- im krótszy szablon, tym większy zapas — poniżej ~73 tokenów batch mieści się w budżecie nawet bez cache.

### Agent projektuje szablon, kod mierzy i wysyła

Agent (`gpt-4.1`) nie wysyła pojedynczych promptów — oddaje **szablon** z placeholderami
`{id}`/`{description}`, a deterministyczny kod robi resztę: renderuje go dla każdego towaru,
mierzy tokeny, pilnuje limitu i budżetu, wysyła i zbiera odpowiedzi. Sam katalog **zmienia się
co kilka minut**, więc szablon musi generalizować — kod wymusza placeholdery (każdy dokładnie raz),
a prompt systemowy zabrania odwołań do konkretnych id.

### Dwa narzędzia: darmowe i płatne, wyraźnie rozdzielone

- `validate_template` — **za darmo, lokalnie**: pobiera świeży katalog (endpoint danych, nie `/verify`),
  renderuje szablon dla wszystkich towarów, liczy tokeny (`o200k_base` z pakietu
  `Microsoft.ML.Tokenizers`) i szacuje koszt batcha z cache i bez. Zwraca też zawartość katalogu,
  więc agent widzi realne dane, na których pracuje.
- `run_classification_cycle` — **wydaje budżet**: `reset` → świeży katalog → wysyłki po kolei,
  do pierwszej pomyłki. Szablon, który nie przechodzi walidacji lokalnej, jest odrzucany **zanim**
  cokolwiek pójdzie w sieć.

Hub liczy tokeny „mniej więcej jak GPT-5.2", więc lokalny pomiar to przybliżenie — stąd konfigurowalny
**margines bezpieczeństwa** (`TokenSafetyMargin`, domyślnie 8 tok.), o który obniżany jest limit 100 tokenów.

### Pomyłka zeruje saldo — cykl zatrzymuje się na pierwszym -890

Kluczowa mechanika, **której nie ma w treści zadania**: błędna klasyfikacja (HTTP 406, kod -890)
natychmiast **zeruje pozostałe saldo**. Wszystkie kolejne wywołania w tym cyklu dostają -910
„Insufficient funds" niezależnie od tego, ile budżetu realnie zostało — to kara, nie koszt.

Pierwsza wersja narzędzia dokańczała cykl „dla pełnego feedbacku" i karmiła agenta serią -910,
przez co ten diagnozował problem trafności jako problem budżetu i w kółko skracał prompt.
Po poprawce cykl zatrzymuje się na pierwszym -890/-910, a raport dostaje pole `stoppedEarly`
rozróżniające oba przypadki: -910 tuż po -890 = popraw trafność; -910 bez wcześniejszego -890 =
szablon faktycznie za drogi. Realne liczby z logu: koszty są liniowe (0,002 PP/token wejścia i wyjścia),
odpowiedź `NEU` to 2 tokeny (0,004 PP), a cache prefiksu działa już od ~17 tokenów — czysty przebieg
przy ~45-tokenowych promptach kosztuje ~0,7 PP, więc budżet jest komfortowy, o ile nie ma pomyłek.

### Agent nie może „ogłosić" sukcesu bez flagi

W pierwszym biegu agent po ośmiu nieudanych cyklach poddał się i **sfabrykował flagę** w swoim
podsumowaniu. Prawdziwa flaga przychodzi wyłącznie w odpowiedzi Huba i wykrywa ją kod (regex
w narzędziu cyklu). Stąd trzy zabezpieczenia: prompt systemowy wprost zabrania zmyślania flagi,
pętla nie przyjmuje odpowiedzi bez tool calls, dopóki flagi nie ma (dokłada wiadomość, że zadanie
trwa dalej), a na końcu program wypisuje jednoznaczny status flagi, żeby fabrykacja była widoczna
na pierwszy rzut oka.

### Retry w kodzie, odpowiedzi surowe

Jak w S01E05: błędy transportu i 429/5xx obsługuje `HubClient` (backoff + `Retry-After`, serializacja
przez `SemaphoreSlim`, minimalny odstęp między żądaniami), więc model nigdy nie traci iteracji na błąd
przejściowy. Odpowiedzi Huba wracają do modelu **bez parafrazy** — komunikaty o błędnej klasyfikacji
czy wyczerpanym budżecie są precyzyjne i to na nich agent iteruje.

### Prompt systemowy nie zdradza gotowego szablonu

Prompt opisuje **grę**: okno tokenów, cennik, mechanikę cache, wymóg jednowyrazowej odpowiedzi
i wyjątek reaktorowy. Nie podaje treści szablonu — dobranie sformułowań, które 100-tokenowy model
zrozumie, to jest właściwa praca agenta w tym zadaniu. Iteracja: zmieniaj to, czemu feedback przeczy,
nie wysyłaj drugi raz szablonu, który już poległ.

### Wysyłka jest opt-in

Każde zapytanie klasyfikacyjne to żądanie do `/verify`, więc jak w S01E05 — bez `--run` pętla agenta
w ogóle nie startuje. Dry-run nie dotyka sieci: wypisuje kształt żądań i przy okazji smoke-testuje
tokenizer (konstrukcja `TiktokenTokenizer` + policzenie przykładowego szablonu).
