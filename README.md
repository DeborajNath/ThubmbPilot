# ThumbPilot

Personal Android-to-Windows remote touchpad, keyboard and media control over the local network. Developed by Deboraj.

## Run it

The `releases` folder contains the latest Android APK, portable Windows ZIP and upgrade guide. Extract the complete Windows ZIP and double-click `ThumbPilot.exe`. Enable Start with Windows if desired. Closing the window keeps it in the tray; right-click the tray icon and choose Exit to stop it.

Install the APK over the existing app to preserve pairings. Both devices must be on the same reachable LAN with their apps running. Find the other device, request pairing and approve on the receiving device. Saved devices reconnect automatically. No IP/code entry, cloud or subscription is needed.

## Understand the source

Read `CODE-GUIDE.md` for every source file's role, the technology stack and an honest architecture assessment. `PROTOCOL.md` describes the current connection protocol. `CLEANUP.md` records removed generated/obsolete files.

## Development commands

These are developer commands only; ordinary app use does not need PowerShell.

```powershell
cd 'G:\Remote Mouse application'
.\build-mobile.ps1 -JavaHome 'G:\Softwares\Android Studio\jbr'
.\test-mobile.ps1 -JavaHome 'G:\Softwares\Android Studio\jbr'
.\build-desktop.ps1
.\build-desktop.ps1 -Test
```

Android build output: `Mobile app/app/build/outputs/apk/debug/app-debug.apk`.
Windows build output: `Desktop app/bin/ReleaseApp/Release/net10.0-windows/`. This build output is not the bundled portable ZIP; use the provided ZIP for transfer.

The Gradle wrapper and installed local SDKs are retained in `.tools`. Android uses the existing `.tools/android-user/debug.keystore`. Keep that key to sign compatible future APK updates. `Mobile app/local.properties` contains this machine's SDK path. If moving the development project, update that file and supply the new JDK location.

The full Windows protocol tests require a normal Windows account for DPAPI and TLS. The restricted-account `--storage-only` mode uses a test protector. `tests/TrayLifecycle` is a separate WinForms regression project; its fake input and loopback listener test background startup, reopen, close-to-tray and Exit.

## Limits

Windows sign-in auto-start is not a Windows service. UAC/secure desktop and Ctrl+Alt+Delete are unsupported. Pair initially on a trusted LAN. The project has local regression coverage; it has not had an independent security audit or exhaustive physical-device latency testing.
