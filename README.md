# Suno Library Scrapper

Suno Library Scrapper is a portable Windows application for browsing and organizing a locally downloaded Suno music library.

It is designed to work with libraries downloaded using the [Suno Tracks Exporter ↗ (Chrome Web Store)](https://chromewebstore.google.com/detail/pooodjgdoajgkhcnjfabfbdifeakoocm) browser extension. The application reads the exported metadata, groups songs by their Suno workspace, matches metadata with local WAV files, displays cover artwork and generation details, and helps identify duplicate downloads.

> **Third-party notice:** Suno Tracks Exporter is an independent browser extension created by its respective developer. Suno Library Scrapper is not affiliated with, endorsed by, or maintained by the extension's developer or by Suno.

[![Download SunoScrapper](https://img.shields.io/badge/Download-SunoScrapper.zip-4C9A69?style=for-the-badge&logo=windows)](https://github.com/Damizon/SunoScrapper/releases/download/v1.0.0/SunoScrapper.zip)

[View all releases](https://github.com/Damizon/SunoScrapper/releases)

## Exporting your Suno library

In [Suno Tracks Exporter ↗ (Chrome Web Store)](https://chromewebstore.google.com/detail/pooodjgdoajgkhcnjfabfbdifeakoocm), enable:

> **Include workspace in filename**

This option is required because the export includes the `.txt` metadata files used by Suno Library Scrapper. Keep each metadata file together with its corresponding WAV file.

The files may be organized into workspace folders or placed together in a single folder. Workspace names are read from the exported metadata, not inferred only from folder names.

## Features

- Groups downloaded songs by their Suno workspace.
- Works with workspace folders or a single mixed folder.
- Searches titles, artists, prompts, lyrics, tags, personas and workspaces.
- Displays cover artwork and generation metadata.
- Keeps generated stems out of the song list and groups them by source song in a separate Stems view.
- Preserves multiple Suno stem variants and reuses the original song artwork.
- Opens, copies and reveals local WAV files.
- Opens or downloads the original MP3 from its metadata URL.
- Detects repeated downloads using the Suno track ID.
- Shows duplicates in a separate branch with their local paths.
- Safely removes a selected duplicate after confirmation.
- Provides expand-all, collapse-all and full-screen controls.

## Usage

1. Download [SunoScrapper.zip](https://github.com/Damizon/SunoScrapper/releases/download/v1.0.0/SunoScrapper.zip).
2. Extract `SunoScrapper.exe` from the ZIP archive.
3. Place it in the main folder containing your exported Suno files or workspace folders.
4. Run `SunoScrapper.exe` and allow the initial scan to finish.

SHA-256 for `SunoScrapper.zip`:

```text
159C7890566BA9F196F09F29BA0B0E72862E92940B9C6987E0AD377FBC9955B0
```

The application creates a `scrapper_db` folder next to the executable. It contains the local catalog cache, downloaded artwork and a scan report. Use **Rescan library** after adding or removing exported tracks.

## Windows security notice

Because this is an independently distributed, unsigned application, Windows SmartScreen may display a warning on first launch. Only download the executable from the link above or build it yourself from the source code.

## Development

Requirements: Windows and the .NET 8 SDK.

```powershell
dotnet run --project .\SunoScrapper.csproj -- "D:\Suno"
```

## Build a portable EXE

```powershell
dotnet publish .\SunoScrapper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The resulting self-contained executable includes the required .NET runtime and does not require a separate .NET installation on the target computer.

## License

Suno Library Scrapper is available under the [MIT License](LICENSE).
