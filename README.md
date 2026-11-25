# Notka

Prywatna aplikacja na Windows 11: taski i sticky notes, sterowana także z Claude Desktop przez MCP.

## Wymagania

- Windows 11
- .NET SDK 10

## Budowanie i testy

```
dotnet build Notka.sln
dotnet test --solution Notka.sln
```

## Konfiguracja API

| Klucz | Znaczenie |
|---|---|
| `Notka:DbPath` | plik SQLite, domyślnie `%LOCALAPPDATA%\Notka\notka.db` |
| `ConnectionStrings:Notka` | pełny connection string, nadpisuje `Notka:DbPath` |

Baza migruje się automatycznie przy starcie. API odrzuca żądania z obcym `Host` lub z nagłówkiem `Origin`.

Każde żądanie wymaga nagłówka `Authorization: Bearer <token>`. Token powstaje przy każdym uruchomieniu aplikacji
desktopowej w pliku `%LOCALAPPDATA%\Notka\api-token` (`Notka:TokenPath` zmienia ścieżkę) i jest usuwany przy
zamknięciu; aplikacja i serwer MCP korzystają z niego automatycznie. Adres API serwera MCP (`Notka__ApiUrl`)
musi być literalnym adresem IP z pętli zwrotnej, np. `http://127.0.0.1:5123`.

## API

- `/api/tasks` - lista (`?filter=All|Today|Overdue|Upcoming|Done`), `search?q=`, get, create, update, `{id}/complete`, delete
- `/api/notes` - lista, create, update, `{id}/pin`, `{id}/unpin`, delete
- `/hubs/changes` - SignalR, zdarzenia `TaskChanged` i `NoteChanged`

## Aplikacja desktopowa

```
dotnet run --project src/Notka.Desktop -p:Platform=x64
```

Aplikacja uruchamia API na `127.0.0.1:5123` (zmiana: zmienna środowiskowa `Notka__Port`), pokazuje zadania i notatki i działa w trayu:
zamknięcie okna chowa je, a `Zakończ` w menu traya lub w ustawieniach wyłącza aplikację.
Zębatka w pasku tytułu otwiera stronę ustawień (uruchamianie z Windows, zamknięcie aplikacji); Esc lub strzałka wracają do głównego widoku.
Przycisk widoku kompaktowego w pasku tytułu zmniejsza okno do wąskiej listy notatek (jak w Sticky Notes: dodawanie, wyszukiwanie,
kolor i przypinanie w menu „…”) z przełącznikiem Notatki | Zadania; zadania mają wyszukiwarkę (po tytule, w bieżącej zakładce)
także w widoku pełnym. W widoku kompaktowym pinezka włącza „zawsze na wierzchu”. Tryb i rozmiar każdego widoku są zapamiętywane.
Obok zegara (na lewo od obszaru powiadomień, przy przycisku Widgets omijanym automatycznie) wyświetla się pasek z liczbą
zadań na dziś, najbliższym zadaniem i liczbą zaległych; kliknięcie otwiera aplikację, najechanie pokazuje listę
zadań na dziś, a treść jest też w podpowiedzi ikony w trayu. Pasek ukrywa się przy pełnym ekranie i gdy brakuje miejsca,
a wyłącza go przełącznik „Pasek przy zegarze” w ustawieniach.
Tylko jedna instancja: ponowne uruchomienie pokazuje okno działającej aplikacji. Logi: `%LOCALAPPDATA%\Notka\logs`.

## Serwer MCP dla Claude Desktop

```
dotnet publish src/Notka.Mcp -c Release -o artifacts/mcp
```

W `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "notka": {
      "command": "C:\\sciezka\\do\\artifacts\\mcp\\Notka.Mcp.exe"
    }
  }
}
```

Adres API to domyślnie `http://127.0.0.1:5123`; zmienia go zmienna środowiskowa `Notka__ApiUrl`.
Narzędzia: `add_task`, `list_tasks`, `search_tasks`, `complete_task`, `update_task`, `create_note`, `list_notes`.
Gdy aplikacja Notka nie działa, narzędzia zwracają czytelny komunikat.

## Struktura

- `src/Notka.Core` - modele i DTO
- `src/Notka.Api` - biblioteka API: EF Core, endpointy, SignalR
- `src/Notka.Desktop` - aplikacja WinUI 3
- `src/Notka.Desktop.Core` - klient API, klient SignalR i ViewModele aplikacji desktopowej
- `src/Notka.Mcp` - serwer MCP dla Claude Desktop
- `tests/Notka.Tests` - testy xUnit

Plan i postęp: [docs/PLAN.md](docs/PLAN.md). Zasady projektu: [CLAUDE.md](CLAUDE.md).
