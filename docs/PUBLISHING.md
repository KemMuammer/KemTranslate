# Publishing KemTranslate to GitHub

This guide shows how to prepare the repository, push it to GitHub, and create a Windows release build from PowerShell.

## 1. Verify the project locally

From the repository root:

```powershell
dotnet build .\KemTranslate.csproj -c Release
dotnet run --project .\KemTranslate.Tests\KemTranslate.Tests.csproj
```

## 2. Initialize git if needed

```powershell
git init
git branch -M main
git add .
git commit -m "Initial release"
```

## 3. Create the GitHub repository

Create an empty repository on GitHub, then connect the local repository:

```powershell
git remote add origin https://github.com/<your-user>/<your-repo>.git
git push -u origin main
```

If the repository already exists, skip `git init` and only add the remote if required.

## 4. Publish a Windows build

Example self-contained single-file publish:

```powershell
dotnet publish .\KemTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```

Published files will be written to:

- `publish\win-x64`

## 5. Test the published build

Before creating a GitHub release, verify:

- the application starts correctly
- tray behavior works as expected
- `Start with Windows` works as expected
- translation settings load correctly
- OCR works if bundled

## 6. Create a GitHub release

On GitHub:

1. open the repository
2. go to `Releases`
3. choose `Draft a new release`
4. create a tag such as `v1.0.0`
5. attach a zip of `publish\win-x64`
6. publish the release

## 7. Optional zip step

```powershell
Compress-Archive -Path .\publish\win-x64\* -DestinationPath .\publish\KemTranslate-win-x64.zip -Force
```

## 8. Optional GitHub CLI flow

If GitHub CLI is installed and authenticated:

```powershell
gh repo create <your-repo> --public --source . --remote origin --push
gh release create v1.0.0 .\publish\KemTranslate-win-x64.zip --title "v1.0.0" --notes "Initial release"
```

## Notes

- `kemsettings.json` is ignored by git on purpose
- if you bundle Tesseract in `ocr/`, include its required notices
- avoid committing personal server URLs, API keys, or machine-specific settings
- this repository includes an MIT `LICENSE`
- if you distribute bundled OCR binaries, include the upstream notices described in `THIRD-PARTY-NOTICES.md`
