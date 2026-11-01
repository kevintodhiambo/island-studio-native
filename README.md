# Island Studio

A living **notch for Windows** — a native desktop companion inspired by the macOS/iPhone Dynamic Island. It sits at the top of your screen and expands on hover to show now‑playing media with synced lyrics, a control centre (volume + brightness), weather, a focus timer, a water reminder, a to‑do list and a drag‑and‑drop file shelf. It also ships an optional app **dock**, an animated **water‑ripple wallpaper**, and a minimal‑notch mode.

Built in **C# / WPF (.NET 8)** with per‑pixel transparency, so the notch blends into the top bezel with no rendering artifacts.

## Features

- **Notch** – blended shouldered shape, idle "eyes", hover to expand.
- **Media** – now playing with artwork, transport controls, progress, equalizer and synced lyrics (via LRCLIB).
- **Control centre** – volume and brightness sliders.
- **Weather** – temperature + conditions for your location (key‑less).
- **Widgets** – focus timer, water reminder, to‑do list, file shelf.
- **Dock** – app launcher with running indicators, hover magnification, drag‑to‑add, and import of your pinned taskbar apps.
- **Water wallpaper** – an optional animated ripple wallpaper that reacts to the cursor.
- **Settings** – start with Windows, idle eyes, minimal‑notch mode, hide the Windows taskbar, and more.

## Install

Download the latest `Island.Studio.*.zip` from the [Releases](https://github.com/kevintodhiambo/island-studio-native/releases) page, extract it, and run `IslandStudio.exe`. It is self‑contained (no .NET install needed) and runs on Windows 10/11 (64‑bit).

## Build from source

Requires the .NET 8 SDK.

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -o publish-folder
```

The app is published as a folder you can zip and run.

## License

[MIT](LICENSE) © Kevin Odhiambo
