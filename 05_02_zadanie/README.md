# S05E02 — „phonecall"

Rozmowa telefoniczna z operatorem systemu OKO: ustalić, która z dróg `RD224`, `RD472`, `RD820` nadaje się
do przerzutu ludzi do Syjonu, a potem doprowadzić do wyłączenia monitoringu na tej drodze. Komunikacja jak
zawsze przez POST `/verify`, `task: "phonecall"`, ale po akcji `start` **każda tura to nagranie MP3
w Base64** w polu `answer.audio`. Rozmowa jest wieloetapowa i liczy się kolejność — pomyłka pali rozmowę
i trzeba zaczynać od `start`.

Sednem zadania jest to, że **scenariusz siedzi w kodzie, a model tylko czyta, co powiedział operator**.
Etapy rozmowy, kiedy pada hasło i o które drogi wolno poprosić — to wszystko rozstrzyga maszyna stanu
i guard. Model dostaje jedno pytanie na turę („co te słowa znaczą?") i, wyjątkowo, jedno zdanie do
napisania, gdy operator zapyta o coś, czego scenariusz nie przewiduje.

## Stan prac

| krok | zakres | stan |
|---|---|---|
| 1 | szkielet, warstwa `Llm/` bez function callingu, TTS/STT na `HttpClient`, bank nagrań (`--voices`, `--say`) | gotowy; 11/11 wypowiedzi |
| 2 | klient Huba z logiem bez Base64, parser odpowiedzi, rekonesans (`--probe`) | gotowy; 1 żądanie |
| 3 | odczyt operatora, planer, guard, symulowany operator (`--rehearsal`) | gotowy; 5 z 6 scenariuszy z flagą |
| 4 | `--call` | trzy rozmowy **spalone**, po każdej poprawka w kodzie; czwarta przed nami |

## Uruchomienie

Klucze w `appsettings.Development.json` (gitignored). `Interpreter` i `Voice` to jedno konto OpenAI:

```json
{ "AI_DevsApiKey": "...", "Interpreter": { "ApiKey": "..." }, "Voice": { "ApiKey": "..." } }
```

Uruchamiać **z folderu zadania** — cache i log mają ścieżki względne do katalogu bieżącego:

```bash
dotnet run -- --tests
```

| tryb | co robi | sieć |
|---|---|---|
| `--tests` | 60 przypadków: scenariusz, kontrola transkryptu, parser odpowiedzi Huba, log, planer, guard, symulator | nie |
| `--voices` | nagrywa wszystkie wypowiedzi i odsłuchuje każdą dwoma modelami; pisze `voice-bank.json` | OpenAI |
| `--say "<tekst>"` | jedno nagranie i co usłyszał każdy słuchacz — do dobierania wymowy | OpenAI |
| `--probe` | wysyła **tylko** `start` i pokazuje odpowiedź; nic nie mówi operatorowi | Hub |
| `--rehearsal [nazwa]` | cała rozmowa przeciw symulowanemu operatorowi; bez nazwy — wszystkie scenariusze | OpenAI |
| `--call [--step]` | prawdziwa rozmowa; `--step` pokazuje każdą wypowiedź i czeka na Enter | Hub + OpenAI |

Transkrypt biegu w `phonecall-cache/run-<data>/` (tam też nagrania operatora i surowe odpowiedzi),
wszystkie żądania w `phonecall-log.jsonl` z kluczem zredagowanym na `***` i Base64 skróconym do prefiksu.
Oba katalogi są gitignored, bo zawierają flagę.

## Bank nagrań: kontrola w obie strony

Rozwiązanie nie wysyła nagrania, którego samo nie usłyszało. Każda wypowiedź jest syntezowana
(`gpt-4o-mini-tts`), a potem transkrybowana przez **dwóch niezależnych słuchaczy** (`whisper-1`
i `gpt-4o-mini-transcribe`) **bez podpowiedzi** — i przechodzi tylko wtedy, gdy obaj usłyszeli wszystkie
jej kluczowe słowa. Porównanie ignoruje wielkość liter, polskie znaki, spacje i myślniki, więc `RD-224`
liczy się jak `RD224`, ale kod przeczytany słowami („er de dwieście dwadzieścia cztery") już nie.

To nie jest ostrożność na zapas. Przy pierwszym podejściu **3 z 11 wypowiedzi nie przeszły**, a błędy były
dwóch rodzajów:

- **zależny od głosu**: `onyx` i `ash` wymawiają „Zygfryda" tak, że Whisper zapisuje „Siegfrida" — dopisek
  o polskiej wymowie w instrukcji dla syntezatora nic nie dał, pomógł dopiero inny głos (`marin`);
- **losowy per nagranie**: to samo zdanie raz wychodzi z „RD8-220" albo „RD4272", a nagrane ponownie jest
  czyste. Stąd `MaxTakes`: wypowiedź, której słuchacze nie rozpoznali, jest nagrywana od nowa.

Numer próby wchodzi do klucza cache, więc powtórka nie nadpisuje poprzedniej i wszystkie zostają na dysku.
Pytanie o drogi przeszło dopiero w **czwartej** próbie — przy `MaxTakes: 3` bank nigdy by się nie domknął.
Bank jest ładowany przed rozmową i **odmawia startu**, gdy powstał dla innego głosu albo trzyma wypowiedź,
której nikt nie usłyszał.

## Podział pracy: co robi kod, a co model

Model nie prowadzi rozmowy. Dostaje słowa operatora i zwraca je w jednym, stałym kształcie
(`OperatorReading`, structured output): status każdej drogi z cytatem, czy pyta o hasło, czy pyta o cel,
jakie ma inne pytanie, które monitoringi już wyłączył i czy rozmowa jest spalona.

Nad tym stoją trzy warstwy w kodzie:

- **`CallState`** — projekcja tego, co wiemy i co powiedzieliśmy. Status drogi zapisuje się **raz**;
  odpowiedź, która mu przeczy, ląduje w `Contradictions` zamiast go po cichu nadpisać. Rozmowa nie wjedzie
  więc na drogę, którą operator najpierw przepuścił, a potem wycofał.
- **`TurnPlanner`** — wybiera następną turę. Kolejność etapów jest z treści zadania i siedzi tutaj, nie
  w prompcie. Hasło i uzasadnienie mają pierwszeństwo przed dalszym pchaniem rozmowy, bo są bramkami.
- **`TurnGuard`** — ostatnia kontrola przed wypowiedzeniem. Odrzuca prośbę o drogę, której operator nie
  nazwał przejezdną, prośbę pomijającą jedną z przejezdnych, hasło podane komuś, kto o nie nie prosił,
  oraz wypowiedź napisaną w trakcie rozmowy, która próbuje przemycić prośbę o monitoring z pominięciem
  wszystkich powyższych kontroli.

`OperatorReader` dodatkowo **wyrzuca status drogi, której operator nie nazwał** — kod musi wystąpić
w tym, co naprawdę padło. Status zmyślony przez model zamienia się w prośbę o odblokowanie drogi, której
nikt nie przepuścił, czyli dokładnie w spaloną rozmowę.

## Symulowany operator i próba generalna

`SimulatedOperator` odpowiada w tym samym kształcie JSON co Hub, więc czytnik, planer i guard działają na
nim bez zmian. Pięć scenariuszy: `plain`, `password-first`, `suspicious` (żąda hasła i pyta o cel),
`reluctant` (najpierw pyta o numer zlecenia, potem odpowiada po jednej drodze na turę), `all-blocked`.
Symulator pali rozmowę za te dwie pomyłki, przed którymi ostrzega zadanie: prośbę o drogę, której nie
zgłosił jako przejezdnej, i prośbę o monitoring bez hasła.

Próba generalna znalazła **dwa prawdziwe błędy**, których testy jednostkowe nie widziały:

1. **Po uzasadnieniu prośba nie wracała na stół.** Operator pytał „a w jakim celu?", odpowiadaliśmy — i na
   tym się kończyło, bo `DisableRequested` był już ustawiony, więc planer schodził do improwizacji i bieg
   umierał bez flagi. Poprawka: gdy prośba jest złożona i niepotwierdzona, a ostatnią rzeczą, którą
   powiedzieliśmy, **nie była** ta prośba, planer ją **powtarza**. Celem misji jest ta prośba, nie rozmowa.
2. **Pytanie operatora wypierało to, czego rozmowa naprawdę potrzebowała.** Improwizator dostawał
   instrukcję z pytania operatora, więc gdy brakowało statusów dróg, odpowiadał na „coś jeszcze?" zamiast
   dopytać o drogi — a że nie wymieniał wtedy kodów, operator nie miał na co odpowiedzieć. Poprawka:
   **cel improwizacji ustala planer** (`ImproviseGoal`), a nie treść pytania; instrukcja wymienia kody
   dróg, które mają paść w wypowiedzi.

Przy okazji wyszło, że czytnik brał „z jakiego numeru zlecenia pan dzwoni?" za pytanie o cel — prompt
rozróżnia teraz pytanie o **cel transportu** od pytania o **tożsamość i papiery**.

Po poprawkach wszystkie scenariusze, w których droga przejezdna istnieje, kończą się flagą (3–6 tur),
a `all-blocked` uczciwie przerywa zamiast zgadywać.

## Pierwsza prawdziwa rozmowa: spalona w dwóch turach

Przedstawiliśmy się, a operator zapytał: *„W jakiej sprawie dzwonisz?"*. Czytnik uznał to za pytanie
o cel, planer wybrał uzasadnienie i poszła pełna przykrywka — razem z *„ta misja nie może zostać
odnotowana w logach"*. Operator odpowiedział *„Coś kręcisz. Muszę to zgłosić."* i rozmowa umarła, zanim
padło pytanie o drogi.

Błąd nie był w modelu, tylko w projekcie. Treść zadania przypisuje tę wypowiedź do **jednej** sytuacji:
*„Gdyby operator dopytywał, dlaczego chcesz wyłączyć ten monitoring"*. To przykrywka do **prośby
o monitoring**, a nie odpowiedź na powitanie — powiedziana wcześniej jest nieproszonym przyznaniem się,
że chcemy ominąć logi. Planer nie miał tego zapisanego nigdzie, więc wystarczyło jedno nadgorliwe
`asks_why`, żeby spalić rozmowę.

Poprawka jest w kodzie, w dwóch miejscach, bo prompt to za mało: **planer sięga po uzasadnienie dopiero,
gdy prośba o monitoring jest już złożona**, a `TurnGuard` odrzuca je wcześniej niezależnie od tego, co
odczytał model. Pytanie otwierające spada więc do pytania o drogi — które samo z siebie przedstawia
sprawę („ze względu na transport organizowany do jednej z baz Zygfryda"), czyli dokładnie to, o co
operator prosił. Przy okazji czytnik rozróżnia teraz **dopytywanie o powód czegoś, o co już
poprosiliśmy**, od pytania otwierającego.

## Druga rozmowa: prośba bez legitymacji

Tury 1 i 2 poszły wzorowo — czytnik wyłuskał trzy statusy z gadanego zdania („Jedyne co ci zostało to
jechać drogą RD820, tylko się upewnij, że masz terenówkę") i wskazał RD820. Spaliła się tura 3:
`Proszę o wyłączenie monitoringu na drodze RD820.` Operator odpowiedział **tym samym zdaniem** co
poprzednio — to jego standardowa odmowa, gdy coś się nie zgadza.

Tym razem brakowało **autoryzacji**: ani razu nie padło hasło, bo zakodowałem zasadę „hasło tylko na
żądanie", a operator o nie nie poprosił. Tymczasem brief wymienia je **bezwarunkowo** („Tajne hasło
operatorów brzmi: BARBAKAN"), w przeciwieństwie do przykrywki, która jest jawnie warunkowa, i stoi
dokładnie między punktem o prośbie o monitoring a punktem o przykrywce. Wyłączenie monitoringu to
czynność uprzywilejowana — obcy prosi o nią przez telefon i nie legitymuje się niczym.

Prośba dostała więc hasło: `Podaję hasło operatorów. Barbakan. Proszę o wyłączenie monitoringu na
drodze RD820.` **Trzecia rozmowa spaliła się identycznie**, w tej samej turze, tym samym zdaniem.

### Hasło trzeba było przestawić, żeby dało się je usłyszeć

Pierwsze brzmienie prośby — `Hasło operatorów to Barbakan. Proszę o…` — **nie przeszło banku**:
syntezator czytał hasło jak obce słowo i słuchacz konsekwentnie zapisywał **„BARBACAN"** przez C.
29 powtórek, wszystkie tak samo, więc nie był to przypadek pojedynczego nagrania.

Pomogło przestawienie hasła do **własnego zdania**, po etykiecie: `Podaję hasło operatorów. Barbakan.`
Na początku zdania syntezator czyta je jak zwykły polski wyraz. Ta sama reguła wróciła potem przy
„Zygfrydzie" — warto ją zapamiętać: **kruche słowo stawiaj na początku zdania**.

## Hub cały czas mówił, co jest nie tak

Dopiero po trzeciej spalonej rozmowie zajrzałem do **zapisanych odpowiedzi**, zamiast zgadywać dalej.
Odrzucenie niesie trzy rzeczy, których konsola nie pokazywała, bo mój kod je wyrzucał:

- **`hint`** — wprost, czego zabrakło;
- **`transcription`** — jak Hub usłyszał **naszą** wypowiedź;
- **kod etapu** — `110` start → `120` *Identity confirmed* → `150` *Road status delivered* → …,
  a przy odmowie `-810` / `-790`. Rosnące kody są miarą postępu, jak `-872 → -691 → -690 → -689`
  w S03E02.

> **hint przy prośbie:** *„The caller must justify disabling monitoring on the passable road, citing
> a secret operation ordered by Zygfryd."*

Obie odrzucone prośby — bez hasła i z hasłem — dostały **ten sam** hint, więc hasło nie miało z tym nic
wspólnego. Prośba musi nieść **uzasadnienie od razu**, z powołaniem się na tajną operację zleconą przez
Zygfryda. Brzmienie: `Zygfryd zlecił tajną operację. Proszę o wyłączenie monitoringu na drodze RD820.`
Przykrywka o żywności i logach zostaje na wypadek dalszego dopytywania.

To jest prawdziwa nauka z tego zadania: **API samo się dokumentuje, tylko trzeba je czytać**.
`Conversation` wypisuje teraz kod, komunikat, transkrypcję Huba i hint po każdej turze, zapisuje je
w transkrypcie i pokazuje ostatni hint w podsumowaniu biegu.

### Bramka kontrolna nie może być gorsza od Huba

Przy okazji wyszedł fałszywy alarm we własnym zabezpieczeniu. Po dodaniu „Zygfryda" do prośby bank
spadł do 7/11, a **31 z 36 odrzuceń** dotyczyło tego jednego słowa: `whisper-1` zapisywał je jako
„Siegfried", „Zygfyt", „Zegfryda". Tymczasem w polu `transcription` **Hub zapisał „Zygfryda"
poprawnie** i przyjął zdanie z tym słowem (kod `150`). Słuchacz był więc gorszy od rozpoznawania po
drugiej stronie i odrzucał nagrania, które Hub słyszy bez trudu.

`whisper-1` ustąpił miejsca `gpt-4o-transcribe`, który radzi sobie z tą nazwą. Bank wrócił do 11/11
przy 2 powtórkach — bez nagrywania czegokolwiek od nowa, bo zmienił się tylko słuchacz.

Scenariusz `guarded` to teraz operator wszystkich trzech spalonych rozmów: otwiera pytaniem „W jakiej
sprawie dzwonisz?", pali rozmowę za nieproszoną przykrywkę **i** za prośbę, która nie podaje powodu —
ta sama praktyka co prawdziwa plansza z przegranego biegu w S03E03. Jego odmowa niesie ten sam `hint`
co prawdziwa. Przed poprawkami rozwiązanie ginie na nim w drugiej i trzeciej turze, po poprawkach
dostaje flagę w **trzeciej**.

## Wypowiedzi pisane w trakcie rozmowy

Scenariusz ma 11 gotowych wypowiedzi, ale operator może zapytać o coś spoza niego. Wtedy `Improviser`
pisze **jedno zdanie** (prompt zabrania witania się, orzekania o stanie dróg, podawania hasła i proszenia
o monitoring), guard je sprawdza, a bank nagrywa je i odsłuchuje **tą samą kontrolą co resztę**, zanim
pójdzie na Hub. Odrzucona wypowiedź jest pisana od nowa, dwa razy. Na to jest budżet: rekonesans pokazał,
że po `start` mamy **30 minut**, więc nagranie w trakcie rozmowy nie jest problemem.

## Rekonesans

Jedno `start` (uzgodnione), żeby poznać kształt odpowiedzi przed pisaniem parsera:

```
HTTP 200 (code 110)
{"code":110,"message":"Phonecall session started.","msg":"Przez najbliższe 30 minut możesz prowadzić rozmowę."}
```

Trzy rzeczy z tej jednej odpowiedzi: **operator nie odzywa się pierwszy** (rozmowę otwieramy my, zgodnie
z treścią zadania), polski tekst przychodzi w polu **`msg`**, nie `message`, i mamy 30 minut. Parser i tak
szuka nagrania **w dowolnym polu odpowiedzi**, rozpoznając je po bajtach (`ID3`, `FF FB`, `RIFF/WAVE`,
`OggS`, …), a nie po nazwie pola — bo jak operator odsyła audio, wiadomo będzie dopiero w rozmowie.

## Uwagi techniczne

- **Ponawianie**: `start` wolno powtórzyć (otwiera rozmowę, w której nikt jeszcze nic nie powiedział).
  Tura rozmowy jest ponawiana **wyłącznie po 429**, które Hub odrzuca przed obsłużeniem. Po każdym innym
  błędzie operator mógł już usłyszeć wypowiedź, a powiedzenie jej dwa razy potrafi spalić rozmowę.
- **Flagę wykrywa regex w kodzie** (`HubReply.Flag`), jak w poprzednich zadaniach.
- `Llm/` przeniesione z 05_01 **bez function callingu i bez części audio/vision** — tu nie ma pętli agenta
  ani narzędzi, jest jedno pytanie na turę. Audio idzie osobnymi endpointami (`/audio/speech`,
  `/audio/transcriptions`), nie jako część wiadomości czatu.
