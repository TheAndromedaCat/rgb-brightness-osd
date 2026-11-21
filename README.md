# RGB Brightness OSD

A lightweight Windows system tray utility that displays a floating brightness meter popup when you press your RGB brightness keys (default `F21` for up, `F20` for down). Designed for use with any keyboard that has a customizable button, roller, or dial layout (e.g., Razer keyboards with Synapse) paired with any RGB software that supports global brightness up/down hotkeys (e.g., SignalRGB), without blocking or consuming the keypress.

## Features
- **Non-blocking global key hook:** Listens for `F21` / `F20` (or custom keys) while passing them through to your RGB software.
- **Auto-syncs with Windows theme:** Matches your Windows Dark/Light mode, accent color, and taskbar color setting (`ColorPrevalence`).
- **Click-through & non-focus-stealing:** Floats above the taskbar and fades out automatically without interrupting games or active windows.
- **Customizable from the system tray:**
  - **Show Meter:** Preview the popup.
  - **Set Level:** Quickly sync the meter (`0%`–`100%`) with your current RGB brightness.
  - **Step Size:** Choose `1%`, `2%`, `5%`, or `10%` per press.
  - **Position:** `Bottom Left`, `Bottom Center`, or `Bottom Right`.
  - **Keybinds:** Rebind **Brightness Up** and **Brightness Down** to any key or dial input, or reset to `F21` / `F20`.
  - **Start with Windows:** Toggle automatic launch at Windows sign-in.

## Usage
1. Run `RgbBrightnessOSD.exe`.
2. Right-click (or double-click) the sun icon in the Windows system tray to adjust settings or keybinds.

Settings are saved to `%AppData%\RgbBrightnessOSD\settings.ini`.

## Building from Source
Requires Windows with .NET Framework 4.x (built into Windows 10/11). Run:
```powershell
.\build.ps1
```
