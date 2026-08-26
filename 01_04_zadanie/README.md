# S01E04 — "sendit"

Agent wypełniający deklarację transportu w Systemie Przesyłek Konduktorskich (SPK).

Zadanie ćwiczy temat lekcji — **multimodalność i załączniki**. Dokumentacja SPK jest rozsypana po
kilkunastu plikach powiązanych markerami `[include file="..."]`, a część danych istnieje **wyłącznie
w grafice**. Agent nie dostaje niczego z góry: sam decyduje, czego mu brakuje, i sięga po dokumenty
oraz po vision przez narzędzia.

## Uruchomienie

```bash
dotnet run --project 01_04_zadanie
```

Domyślnie jest to **dry-run** — gotowa deklaracja zostaje wypisana na konsolę i zapisana do
`deklaracja.txt` w katalogu wyjściowym, ale **nie leci do Huba**. Żeby faktycznie wysłać:

```bash
dotnet run --project 01_04_zadanie -- --submit
```

W trybie `--submit`, jeśli Hub odrzuci odpowiedź, treść błędu wraca do agenta i ten poprawia
deklarację w kolejnej iteracji (Hub podpowiada w komunikacie, co jest nie tak).

## Konfiguracja

`appsettings.json` jest szablonem bez sekretów. Klucze idą do `appsettings.Development.json`
(gitignored):

```json
{
  "AI_DevsApiKey": "...",
  "OpenAI": { "ApiKey": "..." }
}
```

Model: `gpt-4.1` zarówno dla pętli agenta, jak i dla vision (`OpenAI:VisionModel` pozwala rozdzielić).

## Struktura

| Element | Rola |
|---|---|
| `Program.cs` | Pętla agenta z Function Calling, limit 25 iteracji |
| `Mission/DeclarationAgentPrompt.cs` | Instrukcja systemowa — cel, ograniczenia, wzorce (bez odpowiedzi) |
| `Mission/ShipmentBrief.cs` | Dane przesyłki z treści zadania |
| `Documents/DocumentLibrary.cs` | Zamiana nazwy pliku na treść albo na `data:` URL + cache na dysku |
| `Tools/FetchDocumentTool.cs` | `fetch_document` — czyta plik tekstowy dokumentacji |
| `Tools/AnalyzeImageTool.cs` | `analyze_image` — pyta model vision o grafikę |
| `Tools/SubmitDeclarationTool.cs` | `submit_declaration` — zapis + opcjonalny POST na `/verify` |
| `Llm/` | Klient OpenAI na `HttpClient`, przeniesiony z S01E02 i rozszerzony o obrazy |

## Decyzje projektowe

**Prompt bez odpowiedzi.** Zgodnie z lekcją instrukcja agenta podaje cel, limity i uniwersalne
wzorce, ale **nie** mówi, że kategoria to A ani że trasa ma kod X-01 — to agent ma wydedukować
z regulaminu. Dlatego jest to agent, a nie workflow: przy sztywnym procesie wystarczyłby workflow.

**Vision jako narzędzie, nie jako część głównej pętli.** Agent prowadzący rozmowę nigdy nie widzi
obrazów — przekazuje nazwę pliku do `analyze_image` i dostaje odpowiedź tekstem. Podpięty pod
narzędzie model vision ma osobną, krótką instrukcję: transkrybuj wiernie, a gdy czegoś nie widać —
powiedz to, zamiast dopowiadać. To zabezpieczenie przed halucynacją zawartości tabeli.

**`detail: "high"` w części obrazowej.** Grafika w dokumentacji to gęsta tabela; niski tier
downsampluje ją poniżej czytelności.

**`fetch_document` odmawia obsługi grafik.** Zwraca komunikat kierujący do `analyze_image`, zamiast
oddać nieczytelne bajty — inaczej agent mógłby cicho pominąć dane, których nie ma nigdzie indziej.

**Sanityzacja nazw plików.** Nazwa pliku pochodzi od modelu, więc `DocumentLibrary` dopuszcza tylko
czystą nazwę z katalogu dokumentacji — bez `..`, bez podkatalogów, bez absolutnych URL-i.

**Wysyłka jest opt-in.** Bez `--submit` narzędzie kończące zapisuje deklarację i mówi modelowi, że
nic nie zostało wysłane. Zgodnie z zasadą repo odpowiedź do Huba wysyłam ręcznie.
