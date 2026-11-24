# Notka

Prywatna aplikacja na Windows 11: taski i sticky notes, sterowana także z Claude Desktop przez MCP.

## Wymagania

- Windows 11
- .NET SDK 10

## Budowanie

```
dotnet build Notka.sln
```

## Struktura

- `src/Notka.Core` - modele i DTO
- `src/Notka.Api` - biblioteka API
- `src/Notka.Desktop` - aplikacja WinUI 3
- `src/Notka.Mcp` - serwer MCP dla Claude Desktop
- `tests/Notka.Tests` - testy xUnit

Plan i postęp: [docs/PLAN.md](docs/PLAN.md). Zasady projektu: [CLAUDE.md](CLAUDE.md).
