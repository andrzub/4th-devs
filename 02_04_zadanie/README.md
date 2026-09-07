# S02E04 — "mailbox"

Skrzynka jednego z operatorów Systemu. Trzeba z niej wyciągnąć trzy wartości i wysłać je razem
na `/verify` (task `mailbox`):

| Pole | Co to |
|---|---|
| `date` | dzień, w którym dział bezpieczeństwa planuje atak na naszą elektrownię, format `YYYY-MM-DD` |
| `password` | hasło do systemu pracowniczego, wciąż leżące w skrzynce |
| `confirmation_code` | kod potwierdzenia z ticketa działu bezpieczeństwa, `SEC-` + 32 znaki = 36 znaków |

Punkt wyjścia: Wiktor z ruchu oporu wysłał donos z anonimowego adresu w domenie `proton.me`,
nie znamy nazwiska i prawdopodobnie nie ograniczył się do jednego maila. Skrzynka jest cały czas
w użyciu, więc w trakcie pracy wpływają nowe wiadomości.

## Architektura: koordynator + delegowani badacze

Lekcja jest o organizowaniu kontekstu dla wielu wątków, więc rozwiązanie jest **orchestratorem**
z narzędziem `delegate`, a nie jednym agentem z narzędziami do skrzynki.

```
                 coordinator (gpt-4.1)
                 delegate | mission_status | submit_answer
                          |
        +-----------------+-----------------+
        |                 |                 |
  researcher#1       researcher#2       researcher#3      (gpt-4.1-mini, świeży kontekst każdy)
  (date)             (password)         (confirmation_code)
  search_mail | get_inbox | get_thread | get_messages | report_finding
        |                 |                 |
        +-----------------+-----------------+
                          |
              blackboard: MissionState + MessageStore
```

- **Koordynator nie ma dostępu do skrzynki.** Nie ma ani jednego narzędzia pocztowego, więc każdy
  fakt musi przyjść przez badacza. To wymusza delegowanie zamiast zaglądania po cichu samemu.
- **Każdy badacz startuje z pustym kontekstem** i widzi wyłącznie briefing koordynatora. To dokładnie
  ta „degradacja komunikacji", o której mówi lekcja: jednolinijkowy briefing daje jednolinijkowy
  wysiłek, dlatego prompt koordynatora wprost wymaga briefingu samowystarczalnego.
- **Zysk jest kontekstowy, nie tylko czasowy.** W teście dymnym trzej badacze przeczytali po ~3900
  tokenów wejścia każdy, a koordynator zużył 1163 tokeny na całą turę — treści maili nigdy nie wchodzą
  do jego okna kontekstu, wchodzą tylko wnioski.
- **Równoległość jest prawdziwa.** Kilka wywołań `delegate` w jednej turze modelu leci przez
  `Task.WhenAll`, a klient LLM zwalnia swoją bramkę, gdy nie skonfigurowano odstępu między żądaniami.
  Dostęp do skrzynki pozostaje zserializowany (`SemaphoreSlim` w `ZmailClient`), więc równoległość
  nigdy nie przebije budżetu zapytań.

### Blackboard i konflikty

`MissionState` to wspólny stan: aktualna wartość każdego faktu, cytat-dowód, `messageID` źródła,
wartości odrzucone przez Huba i historia wszystkich zgłoszeń. Findingi **nie są nadpisywane** —
jeśli dwóch badaczy poda różne wartości dla tego samego faktu, oba lądują w historii, a `delegate`
zwraca `CONFLICT` i każe koordynatorowi to rozstrzygnąć. To wprost strategia „historia zmian +
agent zarządzający" z lekcji: kod wykrywa konflikt, decyzję podejmuje koordynator.

`MessageStore` to druga część współdzielonego kontekstu — cache treści po `messageID`. Dwóch badaczy
idących przez ten sam wątek płaci za jedno pobranie. Cache jest bezpieczny, bo skrzynka jest dla nas
tylko do czytania; `refresh: true` wymusza ponowne pobranie.

## Czego nauczyło API (i co z tego wynikło w kodzie)

`help` zwraca sześć akcji: `getInbox`, `getThread`, `getMessages`, `search`, `reset` i samo `help`.
Zamiast parafrazować składnię operatorów w prompcie, **kod woła `help` na starcie biegu i wkleja jego
surową odpowiedź do promptu każdego badacza** — gramatyka wyszukiwarki pochodzi od tego, kto ją
implementuje. To też punkt 1 z instrukcji zadania.

Trzy rzeczy odkryte przez sondowanie API, każda zamieniona w zabezpieczenie w kodzie:

1. **`rowID` nie jest stabilny.** Ta sama wiadomość wystąpiła raz jako `rowID 127`, chwilę później
   jako `130` — skrzynka żyje i pozycje się przesuwają. Stabilny jest tylko 32-znakowy `messageID`.
   Dlatego cache kluczuje po `messageID`, `report_finding` odrzuca dowód wskazany przez `rowID`,
   a prompt badacza mówi o tym wprost.
2. **Pod `rowID 0` siedzi podstawiona wiadomość.** `ids` przyjmuje też numeryczne `rowID`, więc
   identyfikator z 32 zer trafia w wiadomość, **której nie ma w żadnym listingu**, a której kod
   potwierdzenia ma 35 znaków zamiast 36. Treść pod tym `rowID` się zmienia między wywołaniami.
   `MessageStore` porównuje więc każdą zwróconą wiadomość z tym, o co pytano, i wszystko poza tym
   oddaje w osobnej sekcji z ostrzeżeniem „nie traktuj tego jako odpowiedzi na swoje pytanie".
3. **API liczy zapytania** (pole `request` w odpowiedzi `getMessages`, akcja `reset` je zeruje).
   Klient prowadzi własny licznik z budżetem (`Mailbox:MaxZmailRequests`), pokazuje go modelowi przy
   każdym wyniku narzędzia i przerywa bieg, gdy budżet się skończy, zamiast dobijać się do API.

## Co pilnuje kod, a nie prompt

- **Formaty.** `AnswerValidator` sprawdza `YYYY-MM-DD` jako realną datę oraz `SEC-` + dokładnie
  36 znaków. Kod z pułapki (35 znaków) nie ma szans dojść do Huba — `report_finding` go odrzuca
  i badacz szuka dalej.
- **Dowód.** Zgłoszenie bez cytatu z treści, bez `messageID` albo z wartością już odrzuconą przez
  Huba jest **odrzucane w narzędziu**, a badacz nie kończy pracy. Prosta prośba w prompcie by tego
  nie dała.
- **Wysyłka.** `submit_answer` wysyła to, co leży na blackboardzie, a nie to, co model przepisze
  w argumentach — wartość nie może się „przekręcić" między znalezieniem a wysłaniem. To narzędzie
  ma `IsParallelSafe => false`, więc nigdy nie leci równolegle z niczym innym.
- **Flaga.** Wykrywana regexem w `MissionState`, nigdy nie brana z tekstu modelu. Pętla odmawia
  zakończenia bez prawdziwej flagi (max 3 ponaglenia), a prompt zakazuje jej zmyślania.
- **Pominięte wywołania też dostają wynik.** Gdy misja kończy się w połowie tury, pozostałe
  `tool_calls` dostają wynik „Skipped", bo API odrzuca kolejne żądanie z niedopowiedzianym wywołaniem.

## Czego świadomie nie ma

Lekcja opisuje też narzędzie **`message`** do dwukierunkowej komunikacji, które wstrzymuje pętlę
badacza do czasu odpowiedzi koordynatora. Tutaj by się nie odpracowało: skrzynka jest tylko do
czytania, briefingi są samowystarczalne, a jedyny realny przypadek „brakuje mi informacji" to
*nie znalazłem* — i on wraca do koordynatora jako zwykły raport `found=false` z listą prób.
Koordynator decyduje, czy ponowić za chwilę, bo poczta mogła właśnie dojść.

## Uruchamianie

Klucze w `appsettings.Development.json` (gitignored): `AI_DevsApiKey`, `Coordinator:ApiKey`,
`Researcher:ApiKey`.

Czytanie skrzynki ręcznie, bez LLM i bez `/verify`:

```bash
dotnet run -- --help-api
dotnet run -- --inbox 1
dotnet run -- --search "from:proton.me"
dotnet run -- --thread 62045
dotnet run -- --read <messageID> [<messageID>...]
dotnet run -- --reset
```

Agenci:

```bash
dotnet run -- --draft
```

Pełny bieg koordynatora i badaczy, ale `submit_answer` **nic nie wysyła** — zapisuje odpowiedź do
`mailbox-cache/run-*/draft-answer.json` i kończy bieg.

```bash
dotnet run -- --run
```

To samo, ale każde `submit_answer`, które przejdzie kontrole formatu, to prawdziwy POST na `/verify`.

Dokończenie ręczne, jedno żądanie, bez LLM:

```bash
dotnet run -- --submit --date 2026-03-23 --password haslo --code SEC-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Każdy bieg zostawia `mailbox-cache/run-<data>/` z osobnym transkryptem na agenta
(`coordinator.txt`, `researcher-1-date.txt`, …), a każde żądanie do zmail i do `/verify` ląduje
w `mailbox-log.jsonl` z kluczem API zredagowanym na `***`.

Strojenie w `appsettings.json`, sekcja `Mailbox`: `MaxZmailRequests`, `MaxMessageBodyChars`,
`CoordinatorMaxIterations`, `ResearcherMaxIterations`.
