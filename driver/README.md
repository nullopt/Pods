# PodsAap driver

A small kernel driver that opens Apple's accessory protocol (AAP) channel to paired AirPods, so Pods
can send commands (noise control, and so on) and read exact battery and both earbuds' ear state.
Windows has no user-mode API for this kind of Bluetooth channel (L2CAP), so a driver is the only way.

It is Microsoft's Bluetooth Echo sample client with three changes: it binds to devices offering
Apple's AAP service, opens the channel on AAP's fixed number (PSM `0x1001`) instead of asking the
device for it, and registers Pods' own interface. Pods finds that interface by the device's
container ID and opens it: one write sends one AAP frame, one read returns one. The driver knows
nothing about AAP itself. License: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Why Test Mode

Windows loads a kernel driver only if Microsoft has signed it. That needs a code-signing
certificate (usually EV, issued to a registered business) and Microsoft's attestation signing. A
free project has no way to pay for that, so the driver is signed with a self-made test certificate
instead, which Windows accepts only with test signing on ("Test Mode", shown as a watermark on the
desktop). Test signing in turn needs Secure Boot off. If someone ever funds proper signing, this
requirement goes away; until then it stays.

> **Status: experimental and unproven.** The driver builds and test-signs, and the app-side code is
> unit-tested, but the end-to-end exchange has never been run against real AirPods. The AAP frame
> formats come from reverse-engineering (MagicPods static analysis, cross-checked with LibrePods'
> macOS captures); they may be wrong. Expect to read the raw `r:`/`s:` frames in the logs and fix
> the parser. Enabling Test Mode and running an unproven kernel driver is at your own risk.

## Build (no admin)

```powershell
.\build.ps1        # restores the WDK NuGet packages, builds bin\x64\Release\PodsAap\
.\sign-test.ps1    # creates the "Pods Driver Test" certificate once, signs PodsAap.sys and the catalog
```

Needs Visual Studio 2026 (or its Build Tools) with the C++ workload. Nothing else is installed:
the WDK comes from NuGet, and if Visual Studio's "Windows Driver Kit" component is missing,
`build.ps1` downloads its small build-rules package (checked by hash) into `obj\`.

## One-time setup on your PC

Do these in order, in an **admin PowerShell**:

1. **Suspend BitLocker for two restarts**, or the boot changes below make Windows ask for your
   BitLocker recovery key:
   `manage-bde -protectors -disable C: -RebootCount 2`
2. **Turn Secure Boot off** in the firmware (UEFI/BIOS) setup: `shutdown /r /fw /t 0` restarts
   straight into it (or press F2, F10, Del or Esc while the PC starts). Find Secure Boot (usually
   under Boot or Security), set it to Disabled, save and exit.
   (Windows refuses the next step while Secure Boot is on.)
3. Back in Windows, **turn test signing on**: `bcdedit /set testsigning on`
4. **Restart.** "Test Mode" appears in the corner of the desktop. BitLocker protection resumes by
   itself after this restart.
5. **Install the driver:** `.\install.ps1`

If MagicPods' MagicAAP driver is installed, uninstall it first: it binds to the same devices.

## Undo it all

1. `.\uninstall.ps1` (admin): removes the driver and the test certificate.
2. Suspend BitLocker for two restarts again: `manage-bde -protectors -disable C: -RebootCount 2`
3. `bcdedit /set testsigning off` (admin), then restart into the firmware setup and turn Secure Boot
   back on, save, and let Windows start.

## Shipping

To run on ordinary PCs (Secure Boot on, no Test Mode) the driver package must be signed through
Microsoft's Partner Center: an EV code-signing certificate, then attestation signing (or WHQL).
Nothing in the code changes for that; only the signing step does.
