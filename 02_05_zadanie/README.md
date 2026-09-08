# S02E05 — "drone"

Dron `DRN-BMB7` ma polecieć misję zarejestrowaną przeciwko elektrowni w Żarnowcu (`PWR6132PL`),
ale jedyny ładunek, jaki niesie, ma spaść na **tamę obok niej** — żeby doprowadzić wodę do systemu
chłodzenia rdzeni zamiast zrównać budynek z ziemią.

Zadanie ma dwie połowy o zupełnie różnym charakterze:

- **odczyt terenu** — sektor tamy jest wyłącznie na zdjęciu lotniczym, nigdzie w tekście,
- **rozgryzienie API** — dokumentacja drona jest celowo pełna kolidujących nazw, a jedynym
  źródłem prawdy o tym, czego chce dron, są komunikaty błędów.

Pierwsza połowa jest **nieodwracalna** (jeden ładunek, jeden sektor), druga jest **iteracyjna**.
Cały projekt jest zbudowany wokół tego rozróżnienia: to, czego nie da się cofnąć, rozstrzyga kod;
to, co można poprawić po komunikacie błędu, robi agent.

## Przeciążony `set(...)` — sedno zadania

Dokumentacja opisuje jedną nazwę jako **sześć różnych funkcji**, rozpoznawanych po *kształcie*
argumentu:

| Wywołanie | Znaczenie | Rozpoznanie po |
|---|---|---|
| `set(3,4)` | sektor lądowania na mapie obiektu (`x` = kolumna, `y` = wiersz, lewy górny róg `1,1`) | dwa argumenty liczbowe |
| `set(4m)` | wysokość lotu 1–100 m | sufiks `m` |
| `set(1%)` | moc silników 0–100% | sufiks `%` |
| `set(engineON)` / `set(engineOFF)` | silniki | literał |
| `set(video)` / `set(image)` / `set(destroy)` / `set(return)` | cele misji, można kilka naraz | literał |

Podstęp z fabuły działa dlatego, że dokumentacja **rozdziela dwie rzeczy, które intuicyjnie są jednym**:
`setDestinationObject(ID)` ustala *obiekt docelowy lotu* (to widzi System i to odznacza jako
zniszczone), a `set(x,y)` ustala *sektor lądowania na mapie tego obiektu* (tam faktycznie spada
ładunek). Cel = elektrownia, sektor = tama.

## Odczyt mapy: dwa niezależne odczyty, które muszą się zgodzić

Mapa (`/data/<klucz>/drone.png`, 1920×929) to zdjęcie lotnicze ruin z narysowaną czerwoną siatką.

**Kod liczy siatkę, nie model.** Liczenie kolumn i wierszy to dokładnie ta czynność, w której modele
vision są zawodne — obraz w proporcji 2:1 z dwiema liniami pionowymi bardzo chętnie zostaje opisany
jako „3×3" z samego pattern-matchingu. Siatka jest tu **3 kolumny × 4 wiersze**.
`MapGridDetector` znajduje ją po tym, czego w zdjęciu lotniczym nie ma: czystej czerwieni
(`R > 150` i `R − max(G,B) > 70`) pokrywającej ponad 60% wiersza lub kolumny. Kafelki są wycinane
**ściśle pomiędzy** liniami, żeby czerwień nie weszła w kadr i nie rozpraszała modelu.

**Podbita woda jest sygnałem, nie scenerią.** Treść zadania mówi wprost, że przy tamie celowo
podbito intensywność koloru wody. `WaterSignalAnalyzer` liczy piksele wyraźnie nasycone
niebiesko-zielono (`B ≥ R+25`, `G ≥ R+25`, nasycenie HSV > 0,35) w każdym sektorze. Na tej mapie
wynik jest binarny: **9321 z 9322 trafień w `col2-row4`**, jedno w tle. Sygnał uznaje się za
rozstrzygający dopiero przy ≥500 pikselach i ≥90% udziale — inaczej bieg staje.

**Gemini czyta to samo niezależnie.** `DamLocator` wysyła wszystkie 12 kafelków w **jednym**
zapytaniu (darmowy tier liczy zapytania na dobę, więc pytanie o każdy sektor osobno byłoby
rozrzutnością) i pyta wyłącznie o to, który sektor zawiera tamę — z opisem, czym tama jest
(przelewy, kładka, spiętrzona woda) i czym nie jest (proste ściany, wyschnięte koryta).
Model **nie wie**, co wyszło z analizy pikselowej.

Dopiero **zgodność obu odczytów** daje sektor. Rozbieżność przerywa bieg i wypisuje oba werdykty —
nic nie leci.

## Co pilnuje kod, a nie prompt

`InstructionValidator` sprawdza każdą listę **zanim** dotknie Huba:

- **Sektor.** Lista zawierająca `flyToLocation` musi zawierać dokładnie jeden `set(x,y)` i musi to
  być sektor tamy, oraz `setDestinationObject(PWR6132PL)` i nic innego jako cel. To nie jest rzecz,
  którą model ma zapamiętać poprawnie — jeden ładunek nie daje drugiego podejścia.
- **Formaty z dokumentacji.** ID obiektu `[A-Z]{3}[0-9]+[A-Z]{2}`, właściciel dokładnie dwa słowa,
  LED jako `#RRGGBB`, wysokość 1–100 m, moc 0–100%, sektor w granicach siatki. Odrzucenie lokalne
  **nie kosztuje wysyłki** — model dostaje powód i poprawia.
- **Nieznane metody przechodzą.** Dokumentacja może być niepełna, a sondowanie jest tu legalną
  strategią; od mówienia „takiej metody nie ma" jest Hub, którego komunikaty są precyzyjne.

Poza tym: **flagę wykrywa regex w kodzie** (`MissionState`) z odpowiedzi Huba, a nie deklaracja
modelu; pętla odmawia zakończenia bez niej. Budżet wysyłek jest twardy i pilnowany w `HubClient`,
nie w prompcie.

> Guard jest przetestowany offline na 17 przypadkach (poprawna misja, zamienione kolumna/wiersz,
> sektor spoza siatki, brak wysokości, obcy obiekt docelowy, złe formaty…) — bez sieci i bez `/verify`.

## Konsekwencja projektowa: jedna lista = cała misja

Dron **utrzymuje konfigurację między żądaniami** (inaczej `hardReset` nie miałby sensu). Gdyby
pozwolić budować misję przyrostowo — sektor w jednym żądaniu, lot w drugim — walidator nie miałby
jak sprawdzić, na co naprawdę leci ładunek, bo połowa stanu byłaby po stronie drona.
Dlatego każda lista jest wysyłana jako kompletna misja, a prompt mówi o tym wprost i wskazuje
`hardReset` jako sposób na pozbycie się resztek po wcześniejszej próbie.

## Prompt

To odcinek o **projektowaniu instrukcji agenta**, więc prompt jest sekcyjny, wg anatomii z lekcji:

| Sekcja | Rola |
|---|---|
| `<identity>` | operator drona, nie pokładowe AI; charakter: dosłowność, jedna zmiana naraz, wiara w komunikat maszyny |
| `<protocol>` | praca w próbach; odpowiedź drona jest jedynym źródłem prawdy; nie trzeba rozumieć całej dokumentacji przed pierwszą próbą |
| `<mission>` | podstęp, sektor tamy jako **fakt ustalony przed briefingiem**, jeden ładunek |
| `<api>` | surowa dokumentacja z `drone.html` |
| `<rules>` | przeciążone nazwy, konfigurować tylko to, co potrzebne, stan utrzymuje się między próbami, zakaz zmyślania flagi |
| `<limits>` | budżet wysyłek, darmowe odrzucenia lokalne, „jeśli walidator marudzi na sektor, to lista jest zła, a nie sektor" |

Dokumentacja trafia do promptu **w oryginale**, nie jako moja parafraza — kolidujące nazwy metod są
substancją zadania, a streszczenie rozwiązywałoby je za model. `DroneManual` zamienia HTML na tekst
zachowując strukturę tabeli (wiersz = linia komórek oddzielonych `|`) i przykłady JSON bez zmian.

## Czego świadomie nie ma

- **Narzędzia „przeczytaj dokumentację" i „podaj sektor".** Oba dane są statyczne i znane przed
  startem pętli, więc siedzą w prompcie. Zostaje jedna prawdziwa akcja: wyślij listę, przeczytaj błąd.
- **Trybu `--draft`.** W poprzednich zadaniach symulowana wysyłka miała sens, bo agent pracował też
  poza nią. Tu jedyną akcją agenta *jest* żądanie do Huba, więc symulacja nie dałaby mu czego czytać.
- **Równoległych wywołań narzędzi.** Jedno narzędzie zmienia stan prawdziwego drona i zużywa
  reglamentowaną wysyłkę; wyścig nic nie daje.

## Uruchamianie

Klucze w `appsettings.Development.json` (gitignored): `AI_DevsApiKey`, `Agent:ApiKey` (OpenAI),
`Vision:ApiKey` (Google AI Studio). Modele: `gpt-4.1` w pętli, `gemini-3.6-flash` w vision.

Odczyt terenu, **bez** `/verify`:

```bash
dotnet run -- --map
```

```bash
dotnet run -- --locate
```

```bash
dotnet run -- --manual
```

```bash
dotnet run -- --prompt
```

(`--refresh` przy każdym z nich wymusza ponowne pobranie zamiast cache'u)

`--prompt` robi pełny odczyt terenu jak `--run`, ale zamiast uruchomić pętlę wypisuje dokładnie ten
prompt systemowy i tę pierwszą wiadomość, które dostanie agent — podgląd przed wydaniem wysyłek.

Pełny bieg — **każde `send_instructions` to prawdziwe żądanie na `/verify`**:

```bash
dotnet run -- --run
```

Dokończenie ręczne, bez modelu, z tymi samymi kontrolami lokalnymi:

```bash
dotnet run -- --submit "hardReset" "setDestinationObject(PWR6132PL)" "set(2,4)" "set(30m)" "set(destroy)" "flyToLocation"
```

Każdy bieg zapisuje transkrypt w `drone-cache/run-<data>/`, kafelki sektorów w `drone-cache/`,
a wszystkie żądania do Huba w `drone-log.jsonl` z kluczem zredagowanym na `***`.
