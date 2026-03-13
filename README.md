# KemTranslate

`KemTranslate` is a Windows desktop translation, OCR, and writing assistant built with WPF on `.NET 10`.

It combines translation, writing assistance, OCR capture, tray integration, and startup options in a single desktop application.

## Features

- Text translation with a configurable LibreTranslate-compatible server
- Writing assistance powered by LanguageTool
- Global hotkey support
- Floating translation workflow
- OCR capture with Tesseract
- Dark theme support
- Minimize to tray
- Start minimized to tray
- Start with Windows
- Recent translation and writing history
- Configurable editor font, logging, and OCR settings

## Requirements

- Windows
- `.NET 10 SDK` for local development and builds
- Optional: Tesseract OCR for OCR capture

## Project structure

- `KemTranslate.csproj` - main WPF application
- `KemTranslate.Tests/` - lightweight test runner project
- `Themes/` - light and dark theme dictionaries
- `ocr/` - bundled OCR runtime files copied to output
- `docs/` - publishing and repository documentation

## Getting started

### Visual Studio

1. Open the project in Visual Studio.
2. Set `KemTranslate` as the startup project.
3. Build and run.

### PowerShell

```powershell
# build the app
dotnet build .\KemTranslate.csproj

# run the app
dotnet run --project .\KemTranslate.csproj

# run the tests
dotnet run --project .\KemTranslate.Tests\KemTranslate.Tests.csproj
```

## Configuration

Settings are stored in `kemsettings.json` next to the application executable.

The app supports configuration for:

- global hotkey behavior
- dark mode
- minimize-to-tray behavior
- startup behavior
- LibreTranslate server URL and API key
- LanguageTool server URL
- OCR executable path and OCR language
- editor font and size
- logging level

The server URL and API key fields intentionally use example placeholders only. No personal endpoints or API keys are stored in the repository defaults.

## OCR setup

If you want to bundle OCR with the app, place the Tesseract runtime files in the `ocr/` folder.

See `ocr/README.txt` for the expected layout.

## Build

```powershell
dotnet build .\KemTranslate.csproj -c Release
dotnet build .\KemTranslate.Tests\KemTranslate.Tests.csproj -c Release
```

## Publish

Example self-contained Windows publish:

```powershell
dotnet publish .\KemTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```

Notes:

- files from `ocr/` are copied to the output when present
- if you bundle Tesseract, include its required license and notice files
- test the published build before creating a GitHub release

## GitHub

For repository setup, pushing, and release publishing, see `docs/PUBLISHING.md`.

## Continuous integration

GitHub Actions is configured in `.github/workflows/build.yml` to build the app and run the test project on Windows.

## Contributing

See `CONTRIBUTING.md`.

## License

This project is licensed under the MIT License. See `LICENSE`.

If you distribute bundled OCR binaries, also review `THIRD-PARTY-NOTICES.md`.