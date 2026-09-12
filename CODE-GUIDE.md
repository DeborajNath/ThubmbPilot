# ThumbPilot — code and architecture guide

## Technology stack

| Part | Actual implementation |
| --- | --- |
| Android app | Native Kotlin, Android SDK, standard Android Views built in code, Gradle Kotlin DSL. Kotlin 2.1.20; Android Gradle Plugin 8.9.2; Gradle 8.11.1. Minimum Android 8 / API 26, compile/target API 35. Java 17 bytecode; builds here used Android Studio's JDK 21. |
| Windows companion | C#, .NET 10, Windows Forms. Native Windows input APIs through P/Invoke. Portable x64 app with a bundled .NET runtime. |
| Communication | Local TCP with TLS and newline-delimited JSON. UDP broadcast for discovery. No HTTP server, WebSocket, cloud service or database. |
| Pairing | User approval; persistent random credentials; PC certificate pinning; signed phone pairing proofs. Android Keystore and AES-GCM protect phone data; Windows DPAPI protects PC identity and pairing data. |
| Tests | Java smoke tests for pure Kotlin input logic, C# console smoke tests for protocol/security/discovery, and a WinForms lifecycle test. |

We did not use Flutter, Compose, React Native, Python for the application, or a web UI. Some one-time development helpers used Python; the running products do not require it.

## Project root

| File/folder | Purpose |
| --- | --- |
| `Desktop app/` | Windows application source and its icon. |
| `Mobile app/` | Android source, resources and Gradle build configuration. |
| `releases/` | Latest installable APK, transferable Windows ZIP, and upgrade guide. These are what you share with another device. |
| `.tools/` | Local development toolchain and dependency caches, explained below. Not required on a PC that only runs the portable ZIP. |
| `tests/TrayLifecycle/` | Windows background/tray lifecycle test. |
| `README.md` | Current quick-start and build instructions. |
| `CODE-GUIDE.md` | This file: code map, execution flow and architecture assessment. |
| `PROTOCOL.md` | Current protocol v8 summary. |
| `CLEANUP.md` | What was removed and preserved. |
| `build-mobile.ps1` | Developer command for building an APK. |
| `build-desktop.ps1` | Developer command for building/running the companion or protocol tests. |
| `test-mobile.ps1` | Compiles/runs input smoke tests against built Kotlin classes. |
| `global.json` | Chooses .NET SDK 10, allowing compatible newer feature bands. |
| `.gitignore` | Excludes generated builds, local tools, machine-specific configuration and signing keys from future Git commits. It is not a backup system. |

## Desktop app

| File/folder | What it does |
| --- | --- |
| `Program.cs` | Entry point. Configures WinForms, prevents duplicate instances, signals an existing instance to reopen, handles `--background`, and runs the main window. |
| `MainForm.cs` | Windows UI, discovery/pairing dialogs, paired-phone management, tray icon/menu, close-to-tray, Exit and startup checkbox. Coordinates the server. |
| `ConnectionServer.cs` | TCP/TLS listener, authentication and handshake, frame parsing, command dispatch, input permission gate, acknowledgements, deadlines and held-button cleanup. |
| `SecurityStore.cs` | Creates/loads the PC certificate and protected pairing store; approves, checks and revokes credentials. Stores token hashes and protects the file with current-user DPAPI. |
| `PairingApproval.cs` | Pairing request data, signature verification, and expiring one-use PC-initiated invitations bound to the phone's key. |
| `DiscoveryServer.cs` | Answers the phone's PC-discovery broadcasts on UDP 45833. |
| `PhoneDiscovery.cs` | Discovers foreground phone apps and sends invitations on UDP 45834. |
| `MouseInput.cs` | `IMouseInput` abstraction plus Windows `SendInput` mouse movement, clicks, dragging and wheel input. |
| `KeyboardInput.cs` | Keyboard interface, validation/encoding, Unicode text, key combinations and system media keys using Windows input APIs. |
| `ActionProtocol.cs` | Allowed PC actions and confirmation requirements. Rejects arbitrary action names. |
| `WindowsActions.cs` | Executes the allowed app launches, lock, sleep, restart and shutdown actions. |
| `StartupSettings.cs` | Reads/writes the current user's Windows Run entry pointing to the EXE with `--background`. |
| `Assets/ThumbPilot.ico` | Multi-size Windows icon, embedded in the EXE and used for the window/tray. |
| `LocalMouse.Desktop.csproj` | .NET project settings, framework, version, resources and icon. The old internal filename is retained; product branding is ThumbPilot. |
| `NuGet.Config` | Clears external package sources; this project uses SDK/platform libraries without third-party NuGet packages. |
| `tests/ProtocolSmoke.csproj` and `tests/Program.cs` | Protocol, input-validation, security and discovery checks with recording/fake input handlers. They do not deliberately control your real mouse or run power actions. |
| `bin/` and `obj/` | Generated executable/compiler output and restore/intermediate files. Removed during cleanup; a build recreates them. |

The application stores your real identity and pairings in `%LOCALAPPDATA%\LocalMouse`, outside this project. The old internal name remains to preserve existing pairings. Cleanup did not touch that folder or the Windows startup registry setting.

## Mobile app

The main code is in `app/src/main/java/dev/localmouse/`.

| Kotlin file | What it does |
| --- | --- |
| `MainActivity.kt` | Main screen, tab switching, connection/pairing dialogs, media/app buttons, preferences and foreground lifecycle. Connects UI callbacks to the network client. |
| `Forest.kt` | Shared colours, rounded button styling and vector navigation icons. |
| `TouchpadView.kt` | Draws the touchpad and passes Android touch events to the gesture engine. |
| `GestureEngine.kt` | Interprets movement, tap/double-tap, drag, two-finger scrolling and right click. Separated from the screen so it can be tested. |
| `KeyboardPanel.kt` | Opens/hides the Android keyboard and provides Copy, Paste and Task Manager buttons. |
| `RemoteKeyboardEditor.kt` | Android input-method connection: typing, composition, deletion and keyboard requests. |
| `LiveComposition.kt` | Tracks the composing suffix already sent to the PC and generates changes as the phone keyboard revises it. Does not read the PC document. |
| `KeyboardCommands.kt` | Validates/splits Unicode text into bounded commands without breaking surrogate pairs or CRLF, and maps keyboard shortcuts. |
| `InputQueue.kt` | Input-command types, bounded queue, movement accumulation/coalescing, ordering, stale-input handling and reconnect clearing. |
| `LanConnection.kt` | Background connection thread, TLS socket, authentication, request/response exchange, heartbeat and reconnect handling. |
| `Discovery.kt` | Finds PCs on the LAN and matches saved PC identities after IP changes. |
| `PhoneDiscovery.kt` | Announces/responds to PC discovery while the app is foregrounded and receives pairing invitations. |
| `PhoneIdentity.kt` | Persistent Android Keystore signing key and pairing proof signatures. |
| `PairingStore.kt` | Encrypted saved-PC credentials and last-connected PC. |
| `PinnedTls.kt` | TLS setup and exact verification of the expected PC certificate fingerprint. |

Other Android files:

| File/folder | Purpose |
| --- | --- |
| `app/src/main/AndroidManifest.xml` | App name/icon, permissions, launch activity and keyboard resize behaviour. |
| `app/src/main/res/values/` | Forest theme and branding colours. |
| `app/src/main/res/drawable-nodpi/thumbpilot.png` | Approved icon artwork. |
| `app/src/main/res/drawable/thumbpilot_foreground.xml` | Icon positioning/padding for launcher masks. |
| `app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml` | Android adaptive launcher icon definition. |
| `app/build.gradle.kts` | App ID, version, Android SDK levels and compiler settings. |
| `build.gradle.kts` | Android/Kotlin build plugin versions. |
| `settings.gradle.kts` | Project modules and dependency repositories. |
| `gradle.properties` | Gradle/JVM/Kotlin build settings. |
| `local.properties` | This computer's Android SDK path; update it if the project moves. |
| `gradlew`, `gradlew.bat`, `gradle/wrapper/` | Gradle wrapper: consistently invokes the selected Gradle version. Keep the wrapper JAR and properties. |
| `tests/InputSmoke.java` | Regression checks for input queue, motion, gestures, typing and composition logic. |
| `.gradle/`, `.kotlin/`, `build/`, `app/build/` | Generated local caches/output. Removed; builds recreate them. |

The Android package ID remains `dev.localmouse`. Changing it would create a separate installation rather than update your existing app.

## What is .tools?

| Folder | Why it stays |
| --- | --- |
| `android-sdk/` | Android compiler/build/platform tools used to build and inspect APKs. |
| `dotnet/` | Local .NET SDK, runtime and targeting packs used to build Windows code. |
| `gradle-home/` | Downloaded Gradle distribution, Android/Kotlin plugins and dependency caches. Retained to avoid large redownloads and support builds from cached dependencies. |
| `android-user/debug.keystore` | The signing key used for APK updates. Keep it: losing/changing it prevents installing an update over the existing signed app. |
| `appdata/`, `dotnet-home/` | Isolated development-tool configuration/state. Small, referenced by build scripts. |

Downloaded SDK ZIP installers, the one-time wrapper bootstrap project, setup helper and compiled scratch tests were removed. Installed tool folders already contain their useful contents.

`.tools` is roughly gigabytes because SDKs and build dependencies are much larger than our application source. It is development infrastructure on your disk, not a paid service or part of the running Android app. The portable Windows release has its own smaller `runtime/` folder, which must stay with its EXE.

## Follow one operation through the code

Mouse movement:

`TouchpadView → GestureEngine → MainActivity callback → InputQueue → LanConnection → TLS/TCP → ConnectionServer → IMouseInput / WindowsMouseInput → Windows cursor`

Typing:

`Android keyboard → RemoteKeyboardEditor → LiveComposition / KeyboardCommands → InputQueue → LanConnection → ConnectionServer → Windows keyboard input`

Pairing:

`LAN discovery → select device → signed pairing request → receiving device's Allow/Decline dialog → save credential and PC identity → authenticated reconnect`

Suggested reading order: `MainActivity.kt`, `TouchpadView.kt`, `GestureEngine.kt`, `InputQueue.kt`, `LanConnection.kt`, then desktop `Program.cs`, `MainForm.cs`, `ConnectionServer.cs`, and input/security classes. Read the tests alongside the corresponding logic.

## Did we maintain proper architecture?

**There is useful separation of responsibilities, suitable for this personal app, but it is not a fully layered Clean Architecture or MVVM implementation.**

Good parts:

- Gesture/typing/queue logic is separated from rendering and can be tested without a phone UI.
- Socket work stays off the Android UI thread.
- Windows input interfaces allow recording/fake implementations in tests.
- Security, discovery, allowed actions and native input are separate files.
- The transport bounds input, preserves ordering and cleans up held buttons on disconnection.

Areas to improve if the project grows:

- `MainActivity` and `MainForm` do too much: UI, lifecycle, connection coordination and dialogs. Extract controllers/view-models and smaller UI components.
- `ConnectionServer` combines transport, authentication and command dispatch. Those can become separate services.
- Wire messages are assembled/parsing by hand in both languages. Central protocol specifications and cross-platform contract fixtures would reduce drift.
- The single-request-in-flight transport is intentionally simple; maximum throughput depends on round-trip latency. Benchmark before changing it.
- Tests are smoke/regression programs, not a full automated device/UI/security test pipeline. Real device latency and Windows sign-in startup have not been exhaustively tested here.
- First pairing uses LAN discovery plus user approval, without an independent out-of-band identity check. That is a documented trusted-LAN limitation.
- Build scripts and SDK paths are local-machine oriented. A reproducible packaging script and automated build pipeline would improve maintainability.
- No Git repository was present at inspection. `.gitignore` alone does not provide change history or recovery.

I would describe it as a small modular native application with pragmatic boundaries. It is working and understandable, with clear next refactoring steps; I would not describe it as enterprise-grade architecture or a completed security audit.
