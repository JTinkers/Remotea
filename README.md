# Remotea

Share one mouse and keyboard between two Windows PCs on a LAN. Push the cursor off the configured edge of the **controller** PC and it appears on the **receiver** PC; from then on all mouse and keyboard input goes there until you push the cursor back out through the edge it came in from.

Traffic is authenticated and encrypted (AES-256-GCM, key derived from a shared secret), so keystrokes aren't readable on the network.

## Install

Extract `Remotea.zip` on each PC and double-click **Install.cmd**. On first install Notepad opens `remotea.json` — set `role`, `host` and `secret` (see below), save and close it. The installer then:

- copies Remotea to `%LOCALAPPDATA%\Programs\Remotea` (no admin needed; an existing `remotea.json` is kept on reinstall/upgrade)
- adds Start Menu entries **Remotea**, **Remotea Settings** and **Uninstall Remotea**
- starts Remotea minimized at every logon (`Install.cmd -NoAutostart` to skip)
- on a receiver, adds the inbound firewall rule (one admin prompt)
- starts it right away

To upgrade, run the new `Install.cmd` again. After editing settings, restart Remotea from the Start Menu.

## Build

The app targets .NET 8. Building the `Remotea.slnx` solution needs .NET SDK 9.0.200 or later.

```bash
dotnet build
dotnet test
powershell -ExecutionPolicy Bypass -File installer/Package.ps1
```

`Package.ps1` produces `share/Remotea.zip` with the self-contained single-file exe, the installer scripts and this README.

```
src/Remotea/          the app
  Controller/         captures local input and forwards it while the cursor is on the receiver
  Receiver/           injects the received input
  Protocol/           message framing and the encrypted channel
  Native/             Win32 interop
tests/Remotea.Tests/  xUnit tests
installer/            Install/Uninstall scripts and Package.ps1
```

## Configure

Edit `remotea.json` next to the exe on each PC. `secret` must be the same on both, and Remotea refuses to start with the placeholder `change-me`.

```jsonc
{
  "role": "controller",        // or "receiver"
  "host": "192.168.31.20",     // receiver's IP (controller only)
  "port": 13333,
  "edge": "right",             // controller edge that leads to the receiver
  "secret": "a long random passphrase",
  "releaseHotkey": "Ctrl+Alt+F12"
}
```

Any setting can be overridden on the command line (`controller`/`receiver` for the role, `--host`, `--port`, `--edge`, `--secret`, `--hotkey`, `--config`, `--verbose`), and the secret can come from the `REMOTEA_SECRET` environment variable instead.

## Run manually (without installing)

Receiver first (allow TCP 13333 through its firewall once, from an elevated prompt):

```bash
netsh advfirewall firewall add rule name="Remotea" dir=in action=allow protocol=TCP localport=13333
```

```bash
Remotea.exe receiver
```

Then on the controller:

```bash
Remotea.exe controller --host 192.168.31.20
```

The controller reconnects automatically if the receiver restarts or the network drops. If the connection is lost while you're on the receiver, control snaps back to the controller and the receiver releases any held keys and buttons.

**Getting back:** move the cursor out through the receiver edge it entered from (with `edge: right`, that's the receiver's left edge), or press the release hotkey (default `Ctrl+Alt+F12`).

## Limits

- `Ctrl+Alt+Del`, `Win+L` and the UAC prompt live on the secure desktop and can't be captured or injected.
- Windows blocks injection into elevated windows from a non-elevated process. Run the receiver as administrator if you need to control elevated apps.
- The edge is the outer bound of the whole virtual desktop. With monitors of different sizes, parts of that bound may not be reachable.
- While you're on the receiver, the controller's cursor stays parked in the centre of its screen.
- One controller and one receiver; no clipboard or file sharing.
