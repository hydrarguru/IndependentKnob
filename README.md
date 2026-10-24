# IndependentKnob

Turn your keyboard's volume knob into a per-app volume control.

Normally a keyboard volume knob changes the volume of the whole PC. IndependentKnob runs in the system tray, catches the knob, and changes the volume of one app you choose instead, for example Spotify, Discord or your browser. A small overlay shows which app changed and its new volume.

Windows only for now. Built with C#, .NET 10 and [Avalonia](https://avaloniaui.net/).

## Features

- **Per-app volume and mute** from the knob. If the chosen app isn't playing audio, the knob goes back to normal Windows volume.
- **Three ways to pick the app**:
  - **Selected app**: turning adjusts the selected app, and pressing the knob switches to the next one.
  - **App you're using**: controls the app in front if it's on your list, otherwise a fallback app.
  - **App for the key you hold**: give each app a modifier key (Ctrl, Shift, Alt or none) and hold it while turning.
- **Mute options** in Selected-app mode: double-press, modifier + press, or press and hold.
- **"Only while holding"**: pick Ctrl, Shift or Alt so the knob only controls apps while that key is held. A plain turn stays normal Windows volume.
- **Overlay** with the app's logo, name and volume:
  - Shows at the bottom, top, left or right of the screen, or in the center. Left and right show it upright.
  - Choose its slider colour.
  - Fades in and out, and stays up while you hover it.
  - Drag the dot to set the volume.
  - Never takes focus, so turning the knob doesn't interrupt typing or games.
- **Suggests apps**: when an app that isn't on your list starts playing audio, a small popup asks whether to add it (Add / Not now / Never).
- **Tray app**: no window at startup. The tray tooltip shows which app the knob controls, and starting the app a second time opens the settings window of the copy that's already running.

## Requirements

- Windows 10 or 11
- A keyboard whose knob sends the standard volume keys (Volume Up / Volume Down / Mute). Most do.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build

## Build and run

```sh
dotnet run --project IndependentKnob.csproj
```

The app starts in the system tray (check the `^` overflow area). Click the tray icon to open the settings, and add apps from the "Add an app that is playing audio" list or through the popup.

Run the tests:

```sh
dotnet test IndependentKnob.slnx
```

## Settings

Settings are saved automatically to `%APPDATA%\IndependentKnob\settings.json`. If that file is broken, it is copied to `settings.json.bak` and the app starts with defaults, so nothing is lost.

## Project layout

| Path | What it does |
|------|--------------|
| `Program.cs` | Entry point. Only one copy runs at a time; a second start opens the running copy's window. |
| `App.axaml(.cs)` | Tray icon. Connects knob → router → audio → overlay, and runs the app-suggestion popup. |
| `Platform.cs` | Small interfaces for the OS parts (audio, knob input, foreground app, icons). |
| `Router.cs` | Decides what a knob event does in each mode. No OS code, unit-tested. |
| `PressTiming.cs` | Double-press and press-and-hold timing. Unit-tested. |
| `Settings.cs` | Settings model, loading and saving. |
| `MainWindow`, `OverlayWindow`, `PromptWindow` | Settings window, volume overlay and "add this app?" popup. |
| `Windows/` | Windows code: low-level keyboard hook, per-app audio via [NAudio](https://github.com/naudio/NAudio), foreground app, app icons. |
| `tests/` | xUnit tests. |

## Known limits

- Windows only. The OS code sits behind interfaces so Linux or macOS could be added later.
- Only the default audio output device is controlled.
- The overlay always appears on the main screen.
- Some knobs send the button release right away. On those, press and hold can't work; use double-press or modifier + press instead.

## Credits

Icons on the settings window buttons are from [Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache 2.0).

## License

[MIT](LICENSE)
