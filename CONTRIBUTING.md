# Contributing

Thank you for contributing to `KemTranslate`.

## Development environment

Use Visual Studio 2026 or the `.NET 10 SDK`.

## Build

```powershell
dotnet build .\KemTranslate.csproj
dotnet build .\KemTranslate.Tests\KemTranslate.Tests.csproj
```

## Run tests

```powershell
dotnet run --project .\KemTranslate.Tests\KemTranslate.Tests.csproj
```

## Pull requests

- keep changes focused
- build the app before submitting
- run the test project before submitting
- update documentation when behavior changes
- avoid unrelated refactoring in feature or bug-fix changes

## Notes

- OCR support depends on Tesseract
- tray startup and Windows autostart behavior are Windows-specific
- do not commit personal server URLs, API keys, or local settings files
- if you add bundled third-party binaries, update `THIRD-PARTY-NOTICES.md` as needed
