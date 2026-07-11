# AiMeter

A lightweight Windows desktop app that keeps your AI service usage limits visible at a
glance. AiMeter lives in the system tray and shows a small always-on-top widget that
tracks remaining quota and reset timers across AI providers.

It currently tracks **Claude** (claude.ai session/weekly limits) and **OpenCode**
(opencode.ai Go rolling/weekly/monthly usage), side by side, on an extensible provider
architecture designed for OpenAI, Gemini, Grok, OpenRouter, DeepSeek, and others.

![Widget – detailed](docs/image1.png)
![Widget – compact](docs/image2.png)
![Settings](docs/settings.png)

## Download

**Latest release: v1.0.0** —
[AiMeter-v1.0.0-win-x64.zip](https://github.com/LanternOfDarkness/AiMeter/releases/download/v1.0.0/AiMeter-v1.0.0-win-x64.zip)

```
SHA-256: 7EFCD780F88F4F33B34E52A5B417A754CCE9BC1B62389DF481D86A978A4239C5
```

Framework-dependent build for **win-x64** — needs the .NET 9 Desktop Runtime and the WebView2
Runtime (see [Requirements](#requirements)). Extract the zip and run `AiMeter.exe`.
Verify the download with `Get-FileHash AiMeter-v1.0.0-win-x64.zip -Algorithm SHA256`.

## Features

- **Multiple providers** — track Claude and OpenCode together; log into each under
  Settings → Accounts. Deselect any metric you don't want on the widget.
- **Floating widget** — always-on-top, draggable, remembers its position (and can sit over
  the taskbar).
  - **Detailed** layout: circular rings per metric with remaining %, name, and reset time.
  - **Compact** layout: a slim strip of thin bars with % and a short reset label (`5h`, `2d 3h`).
- **Widget controls** — hover the widget for Refresh / Settings / Switch Layout / Hide (they
  fade in at the top-right), or right-click anywhere on it for the same menu.
- **Reset countdowns** that stay live and format long windows as days + hours.
- **System tray** app: show/hide widget, open settings, refresh, exit.
- **Launch at startup** — optionally start AiMeter when you sign into Windows.
- **Stay over fullscreen** — keeps the widget above borderless-fullscreen games (toggle in
  Settings → General). True exclusive-fullscreen apps can't be overlaid by any window.
- **Adjustable opacity** with an on-hover dim, or turn opacity off entirely.
- **Configurable notifications** for low, exhausted, and reset quotas.
- **Pick which metrics are shown** — deselected metrics stay listed so you can re-enable them.

## Quota color states

| Remaining | Color |
|-----------|-------|
| 75–100%   | Green |
| 50–75%    | Blue |
| 25–50%    | Orange |
| 10–25%    | Red |
| 0–10%     | Dark red |
| Unknown   | Gray |

## Getting started

### Requirements
- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — preinstalled
  on Windows 11 and current Windows 10; both providers log in and fetch through it.

### Build & run
```powershell
dotnet build src/AiMeter/AiMeter.csproj
dotnet run --project src/AiMeter/AiMeter.csproj
```

On first run, open **Settings → Accounts** and log into Claude.ai and/or opencode.ai to
fetch your usage limits — each provider has its own login row. Authentication runs in an
embedded browser; only a lightweight "logged in" marker is stored locally (the real session
lives in the WebView2 profile). Log into just the providers you use.

## Configuration

Settings are stored as JSON at:
```
%AppData%\AiMeter\settings.json
```
This includes widget opacity, layout mode, saved position, selected metrics, polling
interval, and notification preferences. The embedded browser session lives alongside it in
`%AppData%\AiMeter\WebView2`.

Diagnostic logs are written daily to `%AppData%\AiMeter\logs\aimeter-{yyyy-MM-dd}.log` and
capture provider fetch failures, session expiry, and settings load/save errors — useful when
metrics stop updating with no visible explanation.

## Architecture

- **.NET 9 · WPF · MVVM** with `CommunityToolkit.Mvvm` and `Microsoft.Extensions.Hosting` DI.
- `IProvider` implementations fetch usage; `ProviderManager` polls, caches last-good data,
  filters by selection, and raises quota alerts.
- The widget and settings share a single live `AppConfig` singleton, persisted by
  `SettingsManager`.

See [docs/design.md](docs/design.md) for the full design document.

## License

See repository for license details.
