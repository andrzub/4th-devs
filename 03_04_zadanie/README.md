# S03E04 — "negotiations"

Zadanie odwraca role z poprzednich odcinków: to **nie ja mam agenta**, tylko centrala. Ja dostarczam mu
narzędzia. Automat wysyła POST-em `{"params": "..."}` w języku naturalnym na mój publiczny adres i oczekuje
`{"output": "..."}`. Ograniczenia z treści zadania: maksymalnie **2 narzędzia**, odpowiedź **4–500 bajtów**,
agent ma **10 kroków**, szuka miast oferujących **wszystkie 3 przedmioty naraz**, a **brak odpowiedzi
przerywa jego pracę**.

## Dane i zagadka

Trzy pliki z `https://hub.ag3nts.org/dane/s03e04_csv/` (kopia w `data/`, commitowana):

| Plik | Zawartość |
|---|---|
| `cities.csv` | 50 miast — 48 metropolii oraz **Domatowo** i **Skolwin**, dwie wsie wciśnięte między nie |
| `items.csv` | 2137 przedmiotów — katalog elektroniki |
| `connections.csv` | 5349 par `itemCode,cityCode`; każdy przedmiot ma **1–4 miasta** |

Katalog jest w 99,7 % szumem. Sygnał to **ostatnie 6 pozycji**: turbina wiatrowa 400 W w wersji 48 V i 24 V,
inwerter DC/AC 48 V 3000 W i 12 V 1500 W, akumulator AGM 48 V 150 Ah i kwasowy 12 V 200 Ah.

**Pułapka jest napięciowa.** Tylko spójny zestaw 48 V ma wspólne miasta — przecięcie to **Skolwin
i Domatowo**, czyli te same dwie wsie, które odstają w `cities.csv`. Każdy mix napięć daje pustkę,
a akumulator kwasowy 12 V nie jest oferowany przez **żadne** miasto.

## Podział pracy

Dopasowanie opisu do przedmiotu robi **kod**, nie model. Powód jest w regułach zadania: cisza narzędzia
kończy misję agenta, więc endpoint musi odpowiedzieć zawsze, szybko i tak samo. Zewnętrzne wywołanie LLM
w tej ścieżce to dodatkowa latencja, koszt i niedeterminizm w miejscu, które ma być najbardziej przewidywalne.

| Warstwa | Rola |
|---|---|
| `Catalog/TextNormalizer.cs` | Obie strony przez ten sam młynek: diakrytyki, `48 V` / `48V` / `48 woltów` → `48v`, słowa-wypełniacze precz |
| `Catalog/Synonyms.cs` | Tylko tam, gdzie języki się rozjeżdżają (`battery`→`akumulator`, `inverter`→`inwerter`); `turbine`→`turbina` załatwia już wspólny prefiks |
| `Catalog/ItemMatcher.cs` | Scoring ważony IDF — `wiatrowa` występuje 2 razy na 2137 nazw, `dioda` 351 razy |
| `Catalog/QuerySplitter.cs` | Zdanie → lista pozycji (`,` `;` `oraz` `i` `+`); fragment niosący sam parametr dokleja się do poprzedniego |
| `Catalog/OfferFinder.cs` | Lookup i przecięcie; pozycja o kilku wariantach wnosi **sumę** ich miast |
| `Tools/` | Formatowanie odpowiedzi, budżet bajtów, opisy narzędzi i payload zgłoszenia |
| `Api/` | Minimal API i log wszystkich żądań |
| `Tests/OfflineTests.cs` | **49 przypadków** bez sieci i bez klucza |

## Decyzje projektowe

- **Token z cyfrą musi zgadzać się dokładnie.** `1N4001` to nie `1N4007`, a `48V` to nie `480V` — przy
  zwykłym dopasowaniu prefiksowym obie pary by się skleiły. Sprzeczny pomiar (zapytanie mówi 48 V, nazwa
  mówi 24 V) to dodatkowo kara ×0,35, więc wariant o złym napięciu wypada z wyników zamiast z nimi konkurować.
- **Narzędzie nie steruje agenta na 48 V.** Lookup pokazuje wszystkie warianty z ich miastami, a puste
  przecięcie wraca ze wskazówką „sprawdź, czy pozycje są wzajemnie zgodne (np. to samo napięcie)".
  Zaszycie odpowiedzi w narzędziu byłoby kruche i rozwiązywałoby zadanie za agenta.
- **Pozycja wieloznaczna nie udaje konkretnej.** Przy `turbina wiatrowa, inwerter, akumulator` odpowiedź
  mówi `turbina wiatrowa (2 warianty)`, bo miasta pochodzą z sumy obu — nazwanie jednego obiecywałoby
  więcej, niż sprawdzono.
- **Każda ścieżka HTTP kończy się 200 z polem `output`** — zepsuty JSON, brak pola, wyjątek. Dodatkowo
  `params` jest czytane tolerancyjnie (czysty tekst, wartość nie-stringowa, alternatywne nazwy pola),
  bo narzędzie odpowiadające tylko na idealnie ukształtowane wywołania to narzędzie, które kiedyś zamilknie.
- **Budżet liczony „na drucie".** Nie wiadomo, czy centrala mierzy odkodowany string, czy surowy JSON,
  w którym `\n` i `"` to po dwa znaki — `ToolOutput.WireLength` liczy tę droższą wersję, a linie są dodawane,
  dopóki się mieszczą (najpierw treść, potem wskazówki). Odpowiedzi serializują się z
  `UnsafeRelaxedJsonEscaping`, więc cudzysłów kosztuje 2 znaki, a nie 6 (`"`).
- **Klucz i flaga są redagowane w logu** (`***`), a zapytania agenta lądują tam w całości — przychodzą raz
  i są jedynym dowodem na to, jak on naprawdę formułuje pytania.

## Dwa nieudokumentowane ograniczenia

1. **500 bajtów na odpowiedź** — jest w treści zadania, ale bez wskazania, jak liczone; stąd wariant ostrożny.
2. **300 znaków na `description`** — nie ma tego nigdzie. Pierwsze zgłoszenie (opisy po ~720 i ~640 znaków)
   dostało HTTP 400, kod **-875**: *„Field description can contain a maximum of 300 characters in tool #1"*.
   Limit jest teraz sprawdzany w `ToolCatalog.Validate` **przed wysłaniem** i pilnowany testem, więc za długi
   opis kosztuje odmowę lokalną, a nie żądanie do centrali.

Skrócenie opisów z ~700 do ~290 znaków niczego nie kosztowało: zostały format `params`, przykład
z parametrami, odesłanie do drugiego narzędzia i ostrzeżenie o napięciach. Wypadły zdania, które agent
i tak wyczytuje z pierwszej odpowiedzi.

## Przebieg (zaliczony)

Agent centrali zapukał **sekundę po zgłoszeniu** i zadał **trzy pytania — wszystkie do narzędzia pierwszego**:

```
"turbina wiatrowa mająca 48V i moc 400W"  → Turbina wiatrowa 400W 48V: Domatowo, Rzeszow, Skolwin
"akumulator pod 48V dowolna pojemność"    → Akumulator AGM 48V 150Ah: Domatowo, Jaworzno, Skolwin
"inwerter który pasuje pod 48V"           → Inwerter DC/AC 48V 3000W: Bydgoszcz, Domatowo, Skolwin
```

Trzy wnioski z tego biegu:

- **Narzędzia drugiego nie użył ani razu.** Przecięcie trzech list zrobił sam, mimo że opis narzędzia
  pierwszego wprost odsyłał do `cities-with-all`. Przy trzech pozycjach i limicie 10 kroków to nie kosztowało
  go nic — użył 3 z 10 kroków — ale pokazuje, że opis narzędzia jest sugestią, nie sterowaniem.
- **Spójne napięcie wybrał sam**, we wszystkich trzech zapytaniach, bez podpowiedzi z mojej strony.
- **Pytał pełnymi zdaniami z odmianą i diakrytykami** („mająca", „pod 48V dowolna pojemność", „który pasuje
  pod 48V") — dokładnie ten kształt, pod który pisana była normalizacja. Zero nietrafionych zapytań.

Odpowiedź centrali: `cities: ["Domatowo", "Skolwin"]` plus flaga, w pierwszym `--check` po ~70 sekundach.

## Tryby uruchomienia

| Tryb | Co robi |
|---|---|
| `--tests` | 49 testów offline: normalizacja, dopasowanie, budżet bajtów, limit opisu (bez sieci i klucza) |
| `--query "<opis>"` | Odpowiedź narzędzia dla jednego przedmiotu, z licznikiem bajtów |
| `--common "<lista>"` | Odpowiedź narzędzia dla całej listy |
| `--serve [--port 3000]` | Publiczne API obu narzędzi |
| `--submission --base-url <adres>` | Podgląd zgłoszenia z zamaskowanym kluczem, **bez wysyłki** |
| `--submit --base-url <adres>` | Zgłoszenie narzędzi do centrali |
| `--check` | Odpytanie centrali o wynik i flagę |

Klucz: `appsettings.Development.json` (gitignored, `Negotiations:AI_DevsApiKey`) albo zmienna `AI_DEVS_API_KEY`.

## Wystawienie na świat

```bash
dotnet run -- --serve --port 3000
```

```bash
ssh -p 443 -R0:localhost:3000 free@a.pinggy.io
```

Użytkownika (`free@`) trzeba podać jawnie, a przy pytaniu o hasło wpisać **dowolny niepusty znak** — puste
Enter kończy się `Connection closed`. Darmowy tunel żyje 60 minut, jeden na adres IP. Bez limitu czasu:
`cloudflared tunnel --url http://localhost:3000`.

Uruchomiony `--serve` blokuje plik `.exe` w `bin\Debug`, więc przebudowa w trakcie sesji wymaga albo
zatrzymania serwera, albo pracy na konfiguracji `Release` (`dotnet run -c Release --no-build -- --submit ...`).
