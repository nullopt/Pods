<p align="center"><img src=".github/logo.png" width="128" alt="Pods logo"></p>

# Pods

[![test](https://github.com/nullopt/Pods/actions/workflows/test.yml/badge.svg)](https://github.com/nullopt/Pods/actions/workflows/test.yml)

AirPods companion for Windows: battery, ear detection and quick connect for Apple AirPods,
as a lightweight tray app. No drivers, no admin rights.

> **Early development.** Pods runs in the tray, lists your paired AirPods with their battery
> and connection state, and connects or disconnects them. The features below are in progress.

## Features

- Left / right / case battery with charging state, in the tray and a lid-open popup
- Ear detection: pause when you take an AirPod out, resume when you put it back
- Connect from the tray, a hotkey or by opening the case; audio output switching
- Low-battery notifications

## Supported devices

Pods recognises these models from their Bluetooth adverts once they are paired with the PC.
Every model shows battery and connection state and can be connected or disconnected from Pods.
AirPods Max show a single battery.

| Model | Ear detection | Case-open popup | Auto connect |
|---|:-:|:-:|:-:|
| AirPods (1st, 2nd, 3rd generation), AirPods 4, AirPods 4 (ANC), AirPods 5 | ✓ | ✓ | ✓ |
| AirPods Pro, AirPods Pro 2 (Lightning and USB-C), AirPods Pro 3 | ✓ | ✓ | ✓ |
| AirPods Max (Lightning and USB-C), AirPods Max 2 | ✓ | ✓ ¹ | |

¹ Off by default: the AirPods Max signal also fires when you take them off your head.

Beats headphones are not supported.

### Tested on real hardware

Most models above are supported from their documented advert format and have not been tried yet.
These have:

| Device | Checked |
|---|---|
| AirPods Pro 2 (Lightning) | battery and charging, case-open popup, in-ear state, auto connect, connect |
| AirPods Max (Lightning) | battery and charging, on-head state, connect and disconnect, low-battery notification |

If you try Pods with another model, please open an issue saying what worked and what didn't.

### Optional: noise control and exact battery (experimental)

With the optional [PodsAap driver](driver/README.md), Pods also talks Apple's accessory protocol
to connected AirPods: noise control (Noise Cancellation, Adaptive, Transparency, Off) from the
tile, battery to the exact percent, and ear detection for both earbuds. The driver is not signed by
Microsoft yet, so it needs Windows Test Mode, which needs Secure Boot off. Without it, Pods works as
above.

## Building

Requires the .NET 10 SDK on Windows 10 2004 or later (x64). No Visual Studio, Windows SDK,
Developer Mode or certificate is needed.

```
dotnet run --project src/Pods.App
dotnet test
```

Settings and logs live in `%LOCALAPPDATA%\Pods`.

## Licence

MIT, see `LICENSE`. The bundled Inter font is under the SIL Open Font License
(`src/Pods.App/Assets/Fonts/Inter-OFL.txt`).

"AirPods" is a trademark of Apple Inc., used only to describe the hardware
this app works with. This project is not affiliated with or endorsed by Apple.
