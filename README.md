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

## API

- `/api/tasks` - lista (`?filter=All|Today|Overdue|Upcoming|Done`), `search?q=`, get, create, update, `{id}/complete`, delete
- `/api/notes` - lista, create, update, `{id}/pin`, `{id}/unpin`, delete
- `/hubs/changes` - SignalR, zdarzenia `TaskChanged` i `NoteChanged`

## Struktura

- `src/Notka.Core` - modele i DTO
- `src/Notka.Api` - biblioteka API: EF Core, endpointy, SignalR
- `src/Notka.Desktop` - aplikacja WinUI 3
- `src/Notka.Mcp` - serwer MCP dla Claude Desktop
- `tests/Notka.Tests` - testy xUnit

Plan i postęp: [docs/PLAN.md](docs/PLAN.md). Zasady projektu: [CLAUDE.md](CLAUDE.md).
