# Pods

AirPods companion for Windows: battery, ear detection and quick connect for Apple AirPods
and Apple-chip Beats, as a lightweight tray app. No drivers, no admin rights.

> **Status: early development.** The BLE decoding and device-tracking core is done and
> tested against real AirPods Pro 2 adverts; the Windows app itself is not built yet.
> See `PLAN.md` for the roadmap.

## Planned for v1

- Left / right / case battery with charging state, in the tray and a lid-open popup
- Ear detection: pause when you take an AirPod out, resume when you put it back
- Auto-connect when you open the case; audio output switching
- Global hotkeys, low-battery toasts, notification read-out into the earbuds

Noise-control modes, gesture remapping and exact 1% battery need a kernel driver and are
planned for v2.

## Docs

| Path | What it is |
|---|---|
| `SPEC.md` | v1 behaviour spec |
| `PLAN.md` | roadmap, stack, estimates, risks |
| `docs/protocol-apple.md` | Apple proximity-advert measurements on real hardware |
| `docs/superpowers/plans/` | executable, test-first implementation plans |

## Building from source

Requires the .NET 10 SDK on Windows 10 2004 or later (x64). No Visual Studio, Windows SDK,
Developer Mode or certificate is needed: Pods is an ordinary unpackaged app.

```
dotnet run --project src/Pods.App     # the tray app
dotnet test Pods.slnx                 # unit tests
```

Settings and logs live in `%LOCALAPPDATA%\Pods`.

Watch live adverts from AirPods near the PC (open the case):

```
dotnet run --project spikes/AdvertSpike            # stop with Enter
dotnet run --project spikes/AdvertSpike -- --seconds 120
```

## Licence

MIT, see `LICENSE`.

"AirPods" and "Beats" are trademarks of Apple Inc., used only to describe the hardware
this app works with. This project is not affiliated with or endorsed by Apple.
