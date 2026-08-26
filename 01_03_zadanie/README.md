# S01E03 — zadanie „proxy”

Publiczny endpoint HTTP udający człowieka z dyspozytorni przesyłek kolejowych. Trzyma wątek
rozmowy per `sessionID`, obsługuje paczki przez narzędzia MCP i po cichu przekierowuje przesyłkę
z częściami rdzenia reaktora do Żarnowca (`PWR6132PL`).

## Architektura

Dwa procesy — zgodnie z opcjonalnym wariantem z lekcji narzędzia żyją w osobnym serwerze MCP:

```
operator / Hub ──HTTP POST──►  ProxyServer  ──STDIO (MCP)──►  PackagesMcpServer  ──HTTPS──►  hub.ag3nts.org/api/packages
                              (MCP Host + Client)              (MCP Server: 2 narzędzia)
```

| Projekt | Rola |
|---|---|
| `ProxyServer` | MCP **Host**: endpoint HTTP, pamięć sesji, pętla agenta z Function Calling, klient MCP |
| `PackagesMcpServer` | MCP **Server** (STDIO): narzędzia `check_package` i `redirect_package` |

Host nie zawiera definicji narzędzi — pobiera je przez `tools/list` z serwera MCP i przekazuje
modelowi jako schematy Function Calling. Z perspektywy modelu nie różnią się od narzędzi natywnych.

### Pliki

| Ścieżka | Co robi |
|---|---|
| [ProxyServer/Program.cs](ProxyServer/Program.cs) | Minimal API (`POST /`, `POST /api/proxy`, `GET /health`), DI, wczytanie `mcp.json` |
| [ProxyServer/Mcp/McpToolGateway.cs](ProxyServer/Mcp/McpToolGateway.cs) | Klient MCP: połączenie STDIO, lista narzędzi → `ToolDefinition`, wywoływanie po nazwie |
| [ProxyServer/Agent/ProxyAgent.cs](ProxyServer/Agent/ProxyAgent.cs) | Pętla narzędzi (limit 6 iteracji) dla jednej tury rozmowy |
| [ProxyServer/Agent/OperatorPersona.cs](ProxyServer/Agent/OperatorPersona.cs) | Prompt systemowy (persona „Marek z dyspozytorni” + zadanie specjalne) |
| [ProxyServer/Sessions/SessionStore.cs](ProxyServer/Sessions/SessionStore.cs) | Historia per `sessionID`, blokada na sesję, przycinanie do całych tur |
| [ProxyServer/Mission/ReactorPackageGuard.cs](ProxyServer/Mission/ReactorPackageGuard.cs) | Deterministyczna podmiana `destination` na `PWR6132PL` dla paczki reaktorowej |
| [ProxyServer/Llm/](ProxyServer/Llm/) | Ręczny klient OpenAI na `HttpClient` (przeniesiony z S01E02, `ToolDefinition` zamiast `ITool`) |
| [PackagesMcpServer/PackageTools.cs](PackagesMcpServer/PackageTools.cs) | Narzędzia MCP z opisami po polsku i polem `hint` w odpowiedzi |

### Dlaczego podmiana celu jest też w kodzie, nie tylko w prompcie

Prompt systemowy instruuje model, żeby dla przesyłki z częściami reaktora ustawiał
`destination = PWR6132PL`. Gdyby to była jedyna linia obrony, powodzenie misji zależałoby od tego,
czy model posłucha — a w pierwszym podejściu nie posłuchał. Dlatego `ReactorPackageGuard` przechwytuje każde
wywołanie `redirect_package` i jeśli paczka jest oznaczona jako reaktorowa — nadpisuje `destination`.

Kluczowe jest **skąd** guard wie, co wiezie paczka: API zwraca wyłącznie status i lokalizację, więc jedynym
źródłem tej wiedzy są wiadomości operatora. Guard skanuje je pod kątem numerów `PKG…` i słów kluczowych
(rdzeń, reaktor, paliwo, kasety, radioaktywne), a raz ustawiona flaga nigdy nie jest cofana.
Dla pozostałych paczek argumenty przechodzą bez zmian.

## Konfiguracja

`ProxyServer/appsettings.json` jest szablonem bez sekretów. Klucze wchodzą do
`ProxyServer/appsettings.Development.json` (gitignored):

```json
{
  "AI_DevsApiKey": "...",
  "OpenAI": { "ApiKey": "..." }
}
```

Klucz AI_devs nie leży w `mcp.json` (ten jest commitowany) — host wstrzykuje go procesowi serwera
MCP jako zmienną środowiskową `AI_DEVS_API_KEY`.

Model: `gpt-4.1` (`OpenAI:DefaultModel`). Port: `3000` (`Urls` w `appsettings.json`; dla VPS zmień na
`http://0.0.0.0:3000`).

## Uruchomienie

```bash
dotnet build 01_03_zadanie.slnx
```

Build jest wymagany przed pierwszym startem — `mcp.json` uruchamia serwer MCP przez
`dotnet exec PackagesMcpServer/bin/Debug/net10.0/PackagesMcpServer.dll` (celowo `exec`, a nie
`dotnet run`, żeby output MSBuild nie zaśmiecił strumienia STDIO protokołu). Przy buildzie
w Release trzeba poprawić tę ścieżkę w `ProxyServer/mcp.json`.

```bash
dotnet run --project ProxyServer --no-build
```

Serwer MCP startuje automatycznie jako proces potomny. Sprawdzenie, czy narzędzia się podłączyły:

```bash
curl -s http://localhost:3000/health
```

Powinno zwrócić `{"status":"ok","sessions":0,"tools":["redirect_package","check_package"]}`.

Test rozmowy lokalnie:

```bash
curl -s -X POST http://localhost:3000/api/proxy -H "Content-Type: application/json" -d "{\"sessionID\":\"test-1\",\"msg\":\"Czesc, co tam?\"}"
```

## Wystawienie na świat i zgłoszenie

Tunel (pinggy — nie wymaga instalacji, Windows ma wbudowane `ssh`):

```bash
ssh -p 443 -R0:localhost:3000 free@a.pinggy.io
```

Użytkownika (`free@`) trzeba podać jawnie — inaczej Windows wysyła nazwę konta razem z domeną, czego pinggy
nie akceptuje. Przy pytaniu o hasło wpisz dowolny niepusty znak; puste Enter nie przechodzi. Darmowy tunel żyje
60 minut i dozwolony jest jeden na adres IP. Bez limitu czasu: `cloudflared tunnel --url http://localhost:3000`.

Zgłoszenie do Huba — **wysyłam sam, ręcznie** (zgodnie z zasadami repo agent tego nie robi):

```json
{
  "apikey": "...",
  "task": "proxy",
  "answer": {
    "url": "https://<adres-z-tunelu>/api/proxy",
    "sessionID": "hub-test-1"
  }
}
```

Endpoint odpowiada zarówno na `/` jak i na `/api/proxy`, więc każdy z tych adresów zadziała.

## Co zostało przetestowane lokalnie

- Połączenie MCP przez STDIO, `tools/list` → 2 narzędzia widoczne w `/health`.
- Small talk (persona nie ujawnia AI, także po bezpośrednim pytaniu „jesteś botem?”).
- Pamięć konwersacji w obrębie sesji i izolacja między dwoma `sessionID`.
- Wywołanie `check_package` end-to-end (fikcyjne `PKG00000000` — akcja tylko do odczytu).

## Wynik misji

Zadanie zaliczone. Hub przeprowadził rozmowę, w której operator poprosił o przekierowanie paczki
z rdzeniami do Zabrza (`PWR3847PL`) — przesyłka poszła do `PWR6132PL`, a operator dostał potwierdzenie
wskazujące jego własny kod lokalizacji i nie zorientował się w podmianie.

Pierwsze podejście **nie powiodło się** i warto wiedzieć dlaczego: API paczek nigdy nie zwraca zawartości
przesyłki, tylko `status` i `location`. Jedynym źródłem informacji "to są rdzenie" jest wiadomość operatora.
Guard szukał wtedy słów kluczowych w odpowiedzi API, więc uznawał ładunek za zwykły i przepuszczał
przekierowanie bez zmiany celu. Od poprawki guard analizuje wiadomości operatora, a prompt systemowy jawnie
nakazuje zignorować podany przez niego kod lokalizacji.
