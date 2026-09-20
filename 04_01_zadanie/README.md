# S04E01 — "okoeditor"

Wprowadzenie zmian w **Centrum Operacyjnym OKO** przez tylne wejście udostępnione przez centralę.
Panel operatora (`https://oko.ag3nts.org/`) służy **wyłącznie do czytania** — każda zmiana idzie
przez API `okoeditor` (POST `/verify`). Fabularnie: jedno dotknięcie interfejsu webowego zdradza
operatorom, że ktoś tam był, i odcina dostęp.

## Zadanie

Trzy zmiany z listy centrali, potem akcja `done` (zwraca flagę tylko, gdy komplet):

1. Incydent o **Skolwinie** przeklasyfikować z „pojazdy i ludzie" na **zwierzęta**.
2. Zadanie o Skolwinie oznaczyć jako **wykonane** i wpisać w treści, że widziano zwierzęta (bobry).
3. Odwrócić uwagę operatorów: na liście incydentów ma się pojawić **raport o ruchu ludzi w okolicach
   Komarowa**. API nie ma akcji `create`, więc powstaje przez **nadpisanie istniejącego incydentu**
   (wybrany: raport o **Domatowie**).

## API `okoeditor` (z akcji `help`)

Trzy akcje: `help`, `update` (`page` = `incydenty|notatki|zadania`, `id` = 32 hex, opcjonalnie
`content`, `title`, `done` = `YES|NO` — `done` tylko dla `zadania`; wymagane co najmniej jedno z
`content`/`title`; strona `uzytkownicy` jest read-only) oraz `done`.

## Sedno zadania i podział pracy

**Panel jest tylko do czytania — i to egzekwuje kod, nie prompt.** `OkoPanelClient` puszcza wyłącznie
ścieżki z whitelisty `OkoPanelGuard`; jedyny POST, jaki potrafi wykonać, to logowanie. Linki
`/edit/<id>` i `/delete/<id>` to zwykłe GET-y bez potwierdzenia (status zadania w widoku szczegółu
też jest linkiem `/edit/`), więc najdroższy błąd — skasowanie rekordu i spalenie dostępu — jest
oddalony o jedno naiwne „pójdę za linkiem". Guard to uniemożliwia zamiast prosić model, żeby nie
klikał.

**Identyfikatory są wspólne dla stron.** Te same 32 znaki adresują inny rekord na `incydenty`,
`notatki` i `zadania`. `UpdateGuard` odrzuca parę `page`+`id`, której agent w tym biegu nie odczytał
na **tej** stronie — pomyłka w `page` nie zwróciłaby błędu, tylko cicho przepisała cudzy wpis.

**Kodeks klasyfikacji odkrywa się z notatki, kod pilnuje tylko spójności.** Nigdzie w kodzie nie ma
zapisane, że zwierzęta to `04`. Agent czyta notatkę „Metody kodowania incydentów" i rejestruje tabelę
(`register_codebook`); kod sprawdza **kształt** (4 litery + 2 cyfry, każdy wpis z opisem, min. dwie
rodziny po dwa podtypy — to blokuje zarejestrowanie samego `MOVE04` bez przeczytania tabeli). Potem
`UpdateGuard` odrzuca tytuł incydentu z kodem spoza zarejestrowanej tabeli — zmyślone `ANIM01` nie
kosztuje żądania. Checklist pyta kodeks: *czy kod, który teraz stoi w tytule, opisano jako zwierzęta?*

**Postęp liczy się z projekcji konsoli, nie z relacji modelu.** Każda przyjęta edycja nakłada się na
zapamiętany rekord, a `MissionState` sprawdza wynik. Rozbicie zmiany na dwa wywołania działa
naturalnie: po samym `done: YES` punkt 2 zostaje otwarty, domyka się dopiero, gdy w treści pojawią
się zwierzęta. Rekord „o Skolwinie" jest identyfikowany po **pierwszym** odczycie — po przeklasyfikowaniu
tytuł może już Skolwina nie wymieniać.

## Architektura

```
Oko/     OkoPanelGuard    whitelist ścieżek; /edit/ i /delete/ nazwane wprost jako zakazane
         OkoPanelClient   read-only klient panelu (jedyny POST = logowanie), cache HTML
         RecordParser     HTML → rekordy (listingi i widoki szczegółu)
         OkoRecord        model + odczyt kodu klasyfikacji z tytułu
Hub/     OkoEditorClient  POST /verify: help/update/done, budżet, retry 429/503, log z redakcją klucza
Mission/ CodeBook         kodeks (kształt, nie prawdziwość) + wyszukiwanie po opisanym znaczeniu
         NoteCodeBookParser  odzyskanie tabeli z tekstu notatki (ścieżka ręczna + siatka bezpieczeństwa)
         UpdateGuard      co może pójść na /verify
         MissionState     projekcja konsoli + checklista trzech zmian + flaga z regexu
         Operation        spina panel+API+state, rekonesans w kodzie przed pętlą
         TextMatch        dopasowanie polskiego tekstu niezależnie od diakrytyków
Agents/  OkoPrompt        prompt sekcyjny („jak")
         CentreOrders     rozkazy centrali („co"), budowane z settingsów
         OkoHooks         flaga + checklista po każdym wywołaniu; BeforeFinish nie wypuszcza do done
         AgentLoop        pętla function-calling z hookami (przeniesiona z S03E05)
Tools/   read_console · read_record · register_codebook · update_record · finish_mission
Llm/     warstwa klienta OpenAI-compatible (przeniesiona z S03E05)
Tests/   OfflineTests     86 przypadków, bez sieci i klucza
```

## Decyzje projektowe

- **Prompt sekcyjny, zgeneralizowany**: `<identity>/<surfaces>/<mission>/<method>/<identifiers>/
  <rules>/<limits>` mówią *jak* pracować (nie pisać do panelu, brać id ze strony, którą się edytuje,
  zarejestrować kodeks przed zmianą tytułu). *Co* zmienić to `CentreOrders` — rozkaz centrali, dana
  wejściowa. Kod klasyfikacji celowo nie pada w prompcie: to jedyna rzecz, którą model ma przeczytać
  z konsoli.
- **Gwarancje w kodzie, nie w prompcie**: whitelista ścieżek, para `page`+`id` z odczytanego listingu,
  kodeks sprawdzany co do kształtu, `done` tylko dla `zadania`, odrzucenie edycji, która niczego nie
  zmienia (przy budżecie żądań to nie kosmetyka). Odrzucenie w guardzie kosztuje turę agenta i **zero**
  żądań; zła edycja kosztuje żądanie, żywy rekord i ślad w obserwowanym panelu.
- **`finish_mission` bramkowany, ale nie na amen**: `BeforeFinish` nie wypuszcza do `done`, dopóki
  checklista nie jest pełna; po kilku odmowach `done` idzie mimo to, z zapisaną rozbieżnością —
  werdykt API jest ostateczny, więc własna bramka nie może uwięzić skończonego biegu.
- **Rekonesans robi kod** (`Operation.BootstrapAsync`): wszystkie listingi czytane przed pętlą, więc
  agent wchodzi znając rekordy i ich identyfikatory, zamiast marnować tury (i żądania) na odkrycie,
  że konsola w ogóle coś ma. Analogicznie do `BootstrapAsync` z S03E02.
- **Ścieżka ręczna przez ten sam guard**: `--update`/`--done` walidują tak samo jak narzędzia agenta.
  Bez modelu kodeks odzyskuje `NoteCodeBookParser` z tekstu notatki — struktura notatki jest regularna,
  więc jej *kształt* czyta kod, a *znaczenie* kodów zostaje słowami notatki (jak `NoteDecomposer` w
  S03E01). Ten sam parser działa jako siatka bezpieczeństwa.
- **Wysyłka opt-in**: tu każda akcja agenta *jest* żądaniem na `/verify` (jak w S01E05), więc nie da
  się rozdzielić „pracy" od „wysyłki". `--run` bez `--submit` to pełna próba generalna — edycje
  przechodzą przez guard i nakładają się na lokalną projekcję konsoli, nic nie opuszcza maszyny.

## Tryby uruchomienia

```
Offline (bez sieci i klucza):
  --tests                    kontrole guarda ścieżek, parsera HTML, kodeksu, parsera notatki, guarda edycji (86)
  --guard "<ścieżka>"...     werdykt guarda dla ścieżki panelu, bez pobierania

Czytanie (panel + dokumentacja API, bez zmian):
  --help-api                 własny help API okoeditor
  --panel                    wszystkie listingi konsoli
  --read <page>/<id>...      jeden rekord w całości

Edycja ręczna (przez ten sam guard co agent; bez --submit nic nie leci na /verify):
  --update --page <p> --id <id> [--title <t>] [--content <c>] [--set-done YES|NO] [--submit]
  --done [--submit]

Pętla agenta:
  --run                      pełna pętla, dry run — edycje oceniane i nakładane na projekcję, nic nie wysłane
  --run --submit             pełna pętla, edycje i weryfikacja naprawdę wysłane na /verify
```

Transkrypt biegu w `okoeditor-cache/run-<data>/` (tam też `codebook.json` zarejestrowany przez
agenta i snapshoty HTML). Wszystkie żądania do API w `okoeditor-log.jsonl` z kluczem zredagowanym na
`***`.

## Konfiguracja

`appsettings.json` (commitowany, bez sekretów) + `appsettings.Development.json` (**gitignored**):
`AI_DevsApiKey`, `Agent:ApiKey` (OpenAI) oraz `OkoEditor:PanelPassword`. Model: `gpt-4.1`.

## Status

Zbudowane, `--tests` 86/86, ścieżki czytające i ręczny dry-run zweryfikowane na żywym panelu
(bez `/verify`). Pętla agenta i wysyłka na `/verify` — do uruchomienia ręcznie (zasada repo:
odpowiedź do Huba wysyła autor, żeby prześledzić proces).
