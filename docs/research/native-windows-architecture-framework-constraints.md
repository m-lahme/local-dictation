# Native Windows architecture and framework constraints

Research findings for GitHub issue #3, current as of 2026-07-17.

## Question and source policy

This note evaluates native Windows UI/framework choices and the architecture constraints imposed by Local Dictation's Overlay, system tray icon, global hotkeys, Application menu, Management window, accessibility requirements, and Windows 11 deployment. It does not select the product's framework or distribution model.

Only first-party sources are used: Microsoft Learn/API documentation and Microsoft-owned repositories. Statements under **Sourced facts** report those sources. **Reasoned conclusions** are engineering inferences from those facts and the repository's product constraints. **Open product decisions** are deliberately left to the owner.

## Executive synthesis

### Sourced facts

- Microsoft recommends Windows App SDK with WinUI 3 for new native Windows desktop applications. The same SDK can also add non-UI capabilities such as app lifecycle and windowing to WPF, Windows Forms, and Win32 applications. The current Windows App SDK version table lists 2.2, released 2026-06-09, with Windows 10 version 1809 as its minimum OS; Microsoft distinguishes stable, preview, and experimental channels and describes stable as production-supported. [Windows app development](https://learn.microsoft.com/en-us/windows/apps/) · [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/) · [versioning overview](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)
- All relevant desktop frameworks ultimately expose an `HWND`. Windows App SDK's `AppWindow` is a 1:1 high-level abstraction over a top-level `HWND` and can be used with WinUI, WPF, Windows Forms, or Win32, while framework-specific and Win32 APIs remain available alongside it. [Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows) · [Retrieve a window handle](https://learn.microsoft.com/en-us/windows/apps/develop/ui/retrieve-hwnd)
- The Windows notification-area API is still `Shell_NotifyIcon`, which requires an application-defined callback message and a continuously running message pump. The Windows App SDK repository's modern tray proposal remains an open backlog item; it is not a shipping replacement API. [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw) · [Microsoft Windows App SDK issue #713](https://github.com/microsoft/WindowsAppSDK/issues/713)
- `RegisterHotKey` provides system-wide activation notifications through `WM_HOTKEY`, but it reports a hotkey activation, not the complete key-down/key-up stream needed by push-to-talk or modifier-only bindings. A `WH_KEYBOARD_LL` hook reports down and up events, requires a message loop, must return quickly, and can be silently removed after a timeout; Microsoft says raw input is preferable in most monitoring scenarios. [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) · [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- Windows App SDK/WinUI provides direct Mica and Desktop Acrylic backdrops. Microsoft positions Mica as a persistent foundation material and Acrylic as a transient material for flyouts and context menus. Win32/WPF can use Mica via Windows App SDK interop, but that path requires explicit controller/runtime integration. [System backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops) · [Mica in Win32](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-mica-win32)

### Reasoned conclusion

There is no credible zero-interop framework choice for this product. The decision should therefore optimize the Management window and long-term codebase, while deliberately containing unavoidable Shell/User32 interop behind a small Windows integration boundary.

The recommendation-ready shortlist is:

1. **WinUI 3 + Windows App SDK, C#/.NET, with a narrow generated P/Invoke/WinRT interop layer.** This best matches the stated Fluent visual direction and Microsoft's new-app guidance.
2. **WPF on a currently supported .NET release + Windows App SDK/Win32 interop.** This is a lower-framework-risk alternative with explicit, mature application lifetime behavior, but it needs more work to reach the intended Windows 11 appearance.
3. **WinUI 3 + Windows App SDK, C++/WinRT.** This has the same WinUI capabilities and limitations as option 1 while making native API access direct; its tradeoff is a materially higher implementation and memory-safety burden.

Raw Win32 should remain an implementation tool for the shell/Overlay boundary, not the default Management UI framework. Windows Forms, .NET MAUI, UWP, web shells, and cross-platform UI frameworks do not improve the product's Windows-only constraints enough to justify their mismatch or extra abstraction.

## Capability constraints

### Tiny, persistent, topmost Overlay

#### Sourced facts

- `AppWindow` supports positioning, sizing, z-order changes, and presenters. `OverlappedPresenter` exposes standard window behavior; `CompactOverlayPresenter` is a picture-in-picture-oriented always-on-top preset whose size can be overridden. `AppWindow.IsShownInSwitchers` controls appearance in representations such as Alt+Tab and the taskbar. [Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows) · [AppWindow.IsShownInSwitchers](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.isshowninswitchers)
- Win32's `WS_EX_TOPMOST` keeps a window above non-topmost windows; `WS_EX_TOOLWINDOW` removes it from the taskbar and Alt+Tab; `WS_EX_NOACTIVATE` prevents foreground activation on click and explicitly says the window should not be activated through keyboard navigation by accessibility technology. [Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) · [Window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
- Per-Monitor V2 DPI awareness provides top-level and child window DPI notifications plus automatic non-client, common-control, menu, and dialog scaling. Microsoft recommends PMv2 for desktop applications. [DPI awareness context](https://learn.microsoft.com/en-us/windows/win32/hidpi/dpi-awareness-context) · [High-DPI desktop development](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)

#### Reasoned conclusions

- Use a normal top-level Overlay window with an `OverlappedPresenter`, no title bar, no resize/minimize/maximize affordances, `IsAlwaysOnTop`, and `IsShownInSwitchers = false`; do not use `CompactOverlayPresenter` merely because its name resembles the domain term. Its picture-in-picture semantics are unnecessary and could introduce behavior that must then be undone.
- Retrieve the Overlay's `HWND` and apply only the styles not represented by `AppWindow`. The key unresolved tension is `WS_EX_NOACTIVATE`: it protects the Current insertion target when the Overlay is clicked, but it also prevents ordinary accessible keyboard activation. The product needs an explicit accessibility design rather than treating that flag as a free win.
- Implement placement in physical pixels at the native boundary, converting from effective pixels using the target monitor's DPI and work area. React to `WM_DPICHANGED`, display topology changes, work-area changes, and taskbar movement. This is framework-independent.
- Overlay dragging and left-click-versus-drag classification are custom input behaviors in every shortlisted framework. They require a tested pixel/DPI-aware drag threshold and should not be inferred from framework defaults.

### System tray icon and shared Application menu

#### Sourced facts

- `Shell_NotifyIcon` adds, modifies, and deletes notification-area icons. After `NIM_ADD`, an application must call `NIM_SETVERSION`; version 4 behavior includes keyboard selection messages and `WM_CONTEXTMENU`. `NIM_SETFOCUS` returns focus to the notification area after a tray UI operation is dismissed. [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)
- Microsoft's notification icon sample uses `Shell_NotifyIcon`, `Shell_NotifyIconGetRect`, a context menu, and a flyout window. [NotificationIcon sample](https://learn.microsoft.com/en-us/windows/win32/shell/samples-notificationicon)
- `CreatePopupMenu` creates an `HMENU`; `TrackPopupMenu`/`TrackPopupMenuEx` displays it. A detached menu must be destroyed by the application. [CreatePopupMenu](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createpopupmenu)
- WinUI's `MenuFlyout` is a XAML control whose styling/template comes from WinUI resources. It supports commands, separators, toggles, radio items, and submenus. It is not an `HMENU`. [MenuFlyout](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.menuflyout) · [WinUI menus](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/menus)

#### Reasoned conclusions

- Every shortlisted framework needs a small Win32 tray host, normally a hidden/message-only `HWND` on the UI/message-pump thread. This host owns the notification icon, handles taskbar recreation, dispatches tray events, and removes the icon during explicit quit.
- Model the Application menu once as framework-neutral command data. Render that model either (a) as an `HMENU` for both Overlay and tray, giving literally the same native menu, or (b) as WinUI/WPF menu UI at the Overlay and an `HMENU` at the tray, sharing behavior but not rendering technology.
- Option (a) most directly satisfies “the same Application menu” and inherits standard Win32 menu keyboard/accessibility behavior. Option (b) better matches Fluent Acrylic styling but doubles menu-surface testing and makes “same” semantic rather than literal.

### Global hotkeys and push-to-talk

#### Sourced facts

- `RegisterHotKey` fails when a combination is already registered in typical cases, supports modifier flags plus a virtual key, offers `MOD_NOREPEAT`, reserves F12 for the debugger, and reserves Windows-key combinations for the OS. [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- `WH_KEYBOARD_LL` receives `WM_KEYDOWN`, `WM_KEYUP`, `WM_SYSKEYDOWN`, and `WM_SYSKEYUP` in the installing thread's context. The installing thread needs a message loop; callbacks must hand work off and return promptly. [LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
- Global hooks are shared system resources and Microsoft says they should be restricted to special-purpose applications. Hook architecture and process boundaries matter for injected hook types; low-level keyboard callbacks are delivered back to the installing process rather than being injected into every target. [SetWindowsHookEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw) · [Hooks overview](https://learn.microsoft.com/en-us/windows/win32/winmsg/about-hooks)

#### Reasoned conclusions

- Start-stop hotkeys that consist of a conventional modifier-plus-key combination fit `RegisterHotKey` well and gain deterministic conflict detection.
- Push-to-talk requires observing release, and modifier-only or left/right-specific choices exceed `RegisterHotKey`'s abstraction. A dedicated input component should therefore support two strategies: `RegisterHotKey` for conventional activation bindings, and a carefully bounded low-level hook or raw-input design for hold/release and advanced bindings.
- Do not perform audio, model, or UI work inside a hook callback. Enqueue a tiny immutable input event to the app state machine and return immediately.
- Elevated targets, secure desktops/UAC, Remote Desktop transitions, injected events, keyboard layouts, left/right modifiers, auto-repeat, and conflicts require an executable prototype and test matrix. The source material does not justify promising identical behavior across integrity boundaries.

### Mica, Acrylic, themes, and native menus

#### Sourced facts

- WinUI 3 can assign `MicaBackdrop` or `DesktopAcrylicBackdrop` directly to `Window.SystemBackdrop`. Mica is intended for persistent foundation layers; Acrylic is intended for transient light-dismiss surfaces. [System backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops) · [Window.SystemBackdrop](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window.systembackdrop)
- WPF gained a Fluent theme with Light, Dark, and System modes in .NET 9. This modernizes standard WPF control appearance, but it is not the WinUI control set. [What's new in WPF for .NET 9](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)
- WinUI theme resources cover Light, Dark, and HighContrast, and `{ThemeResource}` values re-evaluate when the theme changes. [XAML theme resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources)

#### Reasoned conclusions

- WinUI 3 has the shortest supported path to the requested Management-window Mica, Fluent controls, and system theme behavior.
- WPF can meet the visual direction, but only with a deliberate combination of the current Fluent theme, Windows App SDK/Win32 backdrop interop, custom styling, and visual QA. It should not be described as visually equivalent without a prototype.
- Acrylic on the tiny Overlay is technically possible, but Microsoft positions Acrylic as transient. A continuously visible Acrylic Overlay is therefore a product-specific deviation whose readability, power use, contrast, and transparency-disabled fallback must be prototyped.
- A literal native `HMENU` follows system menu rendering rather than the app's WinUI Acrylic design. The owner must decide whether “native menu” means the Win32 system menu or a Fluent XAML menu built on the native Windows UI framework.

### Multiple windows and application lifetime

#### Sourced facts

- WinUI supports multiple `Window` objects on the same UI thread. `AppWindow` has a 1:1 lifetime relationship with its top-level `HWND`. [Multiple windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/multiple-windows) · [Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows)
- Windows App SDK desktop apps are not subject to UWP's automatic background suspension/termination model; they continue until closed or the process exits, subject to system-wide power/resource policy. [App lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/launch/app-lifecycle)
- Current WinUI exposes `Application.DispatcherShutdownMode`: `OnLastWindowClose` exits the event loop when the last XAML window closes, whereas `OnExplicitShutdown` leaves the pump running. [DispatcherShutdownMode](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.dispatchershutdownmode)
- WPF's `Application.ShutdownMode` has an established `OnExplicitShutdown` mode for applications whose lifetime is not tied to their main or last window. [WPF ShutdownMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.shutdownmode)
- Windows App SDK desktop apps are multi-instance by default; `AppInstance.FindOrRegisterForKey` and activation redirection implement single-instance behavior. [Multi-instance apps](https://learn.microsoft.com/en-us/windows/apps/develop/launch/multi-instance-apps)

#### Reasoned conclusions

- The shell host, not the Management window, owns application lifetime. Set explicit-shutdown behavior, intercept Management-window close to hide it after the one-time explanation, and terminate only through a single quit coordinator that unregisters input, removes the tray icon, stops capture/inference, flushes storage, and exits the dispatcher.
- Enforce one application instance before creating XAML. Redirect later activations to the existing instance and route them to a command such as “show Management window”. Without this, duplicate tray icons and competing global hotkeys are expected.
- One process is sufficient for Overlay, tray, Management window, storage orchestration, and the domain state machine. A separate inference worker is optional isolation, not a UI-framework requirement.

### Accessibility

#### Sourced facts

- WinUI and WPF both integrate with Microsoft UI Automation. Built-in controls provide automation peers; custom controls need an appropriate custom `AutomationPeer`, properties, patterns, and state-change events. [WinUI custom automation peers](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/custom-automation-peers) · [WPF custom-control automation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control)
- WinUI high-contrast behavior depends on theme resources; hard-coded colors and styles that block resource lookup defeat that behavior. Microsoft also requires non-color alternatives for meaningful state. [Accessibility overview](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview) · [Accessibility best practices](https://learn.microsoft.com/en-us/windows/win32/winauto/accessibility-best-practices)
- `UISettings` exposes whether animations and advanced visual effects are enabled and raises change events. [UISettings](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings) · [AnimationsEnabled](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.animationsenabled)

#### Reasoned conclusions

- The five bars/dots are a custom state visualization and therefore need a custom automation representation if the Overlay is present in the UI Automation tree. Expose a stable accessible name and a state/value description, and raise state-change events for Idle, Recording, Transcribing, Success, Loading, and Error.
- Do not encode state only through neutral/red/amber/green. The animated geometry supplies a second visual cue, but reduced-motion mode removes much of that cue; the tray/menu and Management window must expose equivalent textual status.
- Respect high contrast, transparency disabled, animation disabled, text scaling, and DPI scaling. Backdrops and custom animation must have opaque/static fallbacks.
- If `WS_EX_NOACTIVATE` is chosen, validate the entire Overlay interaction with Narrator, keyboard-only operation, magnification, and the tray alternative. The Win32 documentation's accessibility warning makes this a product decision, not merely an implementation detail.

### Windows 11 deployment and packaging

#### Sourced facts

- WinUI 3 projects are packaged by default, but Windows App SDK supports packaged, packaged-with-external-location, and unpackaged applications. MSIX/package identity enables Windows extension points and clean install/update behavior; unpackaged apps retain unrestricted filesystem, registry, elevation, API, and process-model access but must supply their own installer/update mechanism. [Packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/) · [What is MSIX?](https://learn.microsoft.com/en-us/windows/msix/overview)
- Packaged WinUI 3 desktop applications normally run medium-integrity/full-trust rather than in AppContainer unless explicitly configured for AppContainer. [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- Packaged application payload is read-only after deployment. Mutable models, SQLite data, settings, and logs therefore belong in an application-data location, never beside the installed executable. [How packaged desktop apps run](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- Windows App SDK deployment can be framework-dependent or self-contained. Framework-dependent is smaller and serviced centrally but adds a runtime dependency; self-contained gives version control and xcopy-style isolation but is larger, can load slower/use more memory when unpackaged, and makes the application responsible for SDK servicing. [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview) · [Self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- Framework-dependent unpackaged apps must deploy the Windows App SDK runtime and initialize it through the bootstrapper/dynamic-dependency machinery. C# applications also need an appropriate .NET deployment choice, and unpackaged Windows App SDK deployment requires the Visual C++ redistributable. [Deploy unpackaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
- MSIX packages must be signed. [What is MSIX?](https://learn.microsoft.com/en-us/windows/msix/overview)

#### Reasoned conclusions

- Both MSIX/full-trust and unpackaged installer paths can support tray, hotkeys, native inference libraries, and downloaded models. AppContainer is not a viable default because the application needs broad desktop integration.
- Package mutable data separately from binaries from day one, even if the first build is unpackaged. This keeps MSIX viable and avoids migrations when model files become large.
- A framework-dependent MSIX is the cleanest mainstream deployment candidate; an unpackaged self-contained installer is the strongest fallback when dependency bootstrap, enterprise sideload/signing, model/runtime layout, or native-library probing conflicts with MSIX. This must be validated with the chosen Whisper/Parakeet runtimes before selection.
- Pin an explicitly supported stable Windows App SDK release. Do not base version 1 on preview or experimental APIs merely to remove a small interop seam.

## Framework comparison

| Candidate | Strong fit | Awkward or unsupported without interop | Deployment shape | Status |
| --- | --- | --- | --- | --- |
| WinUI 3 + C#/.NET | Microsoft's recommended new native Windows UI; direct Fluent controls, theme resources, Mica/Acrylic, AppWindow, UI Automation; productive managed domain/UI code | Tray icon, `HMENU`, advanced hotkey capture, special window styles, some picker/window ownership paths require HWND/P/Invoke; exact Overlay accessibility needs custom peer | Packaged by default; packaged or unpackaged; framework-dependent or self-contained | **Shortlist, leading direction** |
| WPF + C#/.NET + Windows App SDK | Mature XAML/data binding; explicit lifetime behavior; broad desktop interop; current Fluent theme; WinForms `NotifyIcon` can be reused if literal Shell interop is not desired | No direct WinUI control set; Mica/Acrylic and AppWindow are interop; achieving the prototype's exact visual direction needs more styling; tray/hotkeys still native boundaries | Traditional installer or MSIX; Windows App SDK optional but required for its modern APIs/backdrops | **Shortlist, lower-framework-risk alternative** |
| WinUI 3 + C++/WinRT | Same modern UI surface as WinUI C#; direct Win32/COM; natural ABI for native ML libraries; no managed/native P/Invoke declarations | Same missing tray/advanced-hotkey abstractions; substantially more manual ownership, lifetime, and memory-safety work; application/domain development slower | Same Windows App SDK packaging matrix; native redistributable/runtime decisions remain | **Shortlist only if native-language benefits are valued** |
| Raw Win32/Direct2D/C++ | Maximum control over HWND, menus, tray, hooks, rendering, and process model; minimal abstraction at the shell boundary | Management controls, navigation, themes, localization, UI Automation providers, and responsive layout become application work | Maximum installer freedom | **Use as interop/Overlay tool; reject as whole-app UI default** |
| Windows Forms | Mature desktop lifetime and built-in [`NotifyIcon`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon); simplest utility UI | Visual direction and modern responsive management UI are a poor fit; Acrylic/Mica and advanced windowing still interop | Traditional installer or MSIX | **Rejected as primary UI** |
| .NET MAUI | Uses WinUI 3 on Windows and supports multiple windows | Its value is cross-platform UI abstraction, while this product is Windows-only and immediately needs Windows-specific tray, HWND, hook, menu, and packaging work; adds a layer without removing constraints | MAUI/WinUI packaging toolchain | **Rejected for version 1** ([MAUI architecture](https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui)) |
| UWP | XAML/Fluent lineage | Microsoft describes UWP as maintenance mode; its lifecycle/sandbox model conflicts with a continuously running tray/hotkey desktop utility | MSIX/AppContainer-oriented | **Rejected for new work** ([Windows app development](https://learn.microsoft.com/en-us/windows/apps/)) |
| Electron/WebView-first/cross-platform UI | Familiar web or cross-platform tooling | Not the requested native Windows technology; does not remove Win32 shell/hotkey/native-model boundaries and increases runtime footprint/bridge surface | Framework-specific bundles | **Rejected unless the native-Windows requirement changes** |

## Architecture boundaries implied by the facts

### Reasoned reference shape, not a selected design

```text
Single per-user, medium-integrity desktop application
├── Shell host (UI/message-pump thread)
│   ├── WinUI or WPF Management window
│   ├── Overlay HWND + renderer
│   ├── hidden/message-only HWND
│   ├── Shell_NotifyIcon + HMENU adapter
│   └── hotkey/input adapter
├── Application/domain state machine
│   ├── explicit lifecycle + single-instance routing
│   ├── Dictation orchestration
│   └── framework-neutral Application-menu commands
├── Platform adapters
│   ├── audio/device notifications
│   ├── clipboard/output/focus
│   ├── DPI/display/work-area
│   └── persistence/autostart/packaging identity
└── Transcription engine boundary
    ├── in-process runtime, or
    └── optional child worker over versioned IPC
```

The shell host must live in the interactive user's session. Microsoft states that services cannot interact directly with users and says interactive-service techniques should not be used in new code. A Windows service is therefore the wrong owner for the Overlay, tray, hotkeys, or Current insertion target. [Interactive Services](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)

Keep framework objects out of the domain/application layer. Make Shell, hotkey, windowing, audio, clipboard, model runtime, and persistence dependencies narrow interfaces so that choosing WinUI versus WPF does not also select the core architecture. This additionally permits prototype replacement and deterministic tests of the Dictation state machine.

A separate inference process is justified only if the selected native runtime demonstrates one of these needs: crash containment, incompatible native dependency graphs, GPU context cleanup that cannot be made reliable in-process, independent restart/update, or materially better model unloading. Otherwise, a single process avoids IPC, duplicated runtime memory, shutdown races, and packaging complexity. A Windows service is not the fallback.

## Necessary interop inventory

The following is required or highly likely regardless of shortlisted UI framework:

| Boundary | Native APIs/concepts | Why |
| --- | --- | --- |
| Tray | `Shell_NotifyIcon`, `NOTIFYICONDATA`, callback message, taskbar-recreated handling | No shipping Windows App SDK tray replacement |
| Native Application menu | `CreatePopupMenu`, `InsertMenuItem`/`AppendMenu`, `TrackPopupMenuEx`, `DestroyMenu`, focus restoration | Literal shared system menu and tray keyboard behavior |
| Conventional global hotkey | `RegisterHotKey`, `UnregisterHotKey`, `WM_HOTKEY` | Conflict-aware system-wide activations |
| Push-to-talk/advanced binding | `WH_KEYBOARD_LL` or a validated raw-input design | Key-up, modifier-only, and left/right-specific state |
| Overlay | HWND retrieval, styles, `SetWindowPos`, DPI/display/work-area messages | Non-activating/topmost/tool-window behavior and placement |
| Accessibility | XAML/WPF automation peers or Win32 UI Automation provider | Custom five-bar state and events |
| Theme/effects fallback | `UISettings` and framework theme resources | Animation/transparency/high-contrast compliance |
| Single instance/lifecycle | `AppInstance` activation redirection or framework-equivalent mutex/IPC; explicit dispatcher shutdown | One tray icon/hotkey owner and background lifetime |

For C#, use generated, typed interop declarations rather than a hand-maintained collection of `DllImport` signatures where feasible. Microsoft's own WinUI interop example uses `WindowNative.GetWindowHandle` plus generated P/Invoke APIs. [WinUI and Win32 interop](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/desktop-winui3-app-with-basic-interop) · [CsWin32](https://github.com/microsoft/CsWin32)

## Remaining risks and required prototypes

These items are not resolved by documentation and should block a final framework/architecture ADR where noted:

1. **Overlay spike (blocks framework selection):** build the real pill dimensions in WinUI 3 C# and WPF, then verify topmost behavior, no taskbar/Alt+Tab entry, click-versus-drag, no focus theft, Per-Monitor V2 moves, taskbar/work-area changes, Mica/Acrylic/opaque fallback, idle resource use, and Narrator/keyboard behavior.
2. **Hotkey spike (blocks input architecture):** verify conventional and modifier-only bindings, push-to-talk release, left/right modifiers, conflict reporting, repeat, injected input, elevated target, UAC/secure desktop, lock/unlock, RDP reconnect, and hook recovery. Do not decide low-level hook versus raw input from documentation alone.
3. **Tray/menu spike:** verify Explorer restart recovery, DPI/theme/high contrast, keyboard invocation, menu dismissal/focus restoration, RTL/localization, and whether one `HMENU` opened from both surfaces meets the visual bar.
4. **Packaging spike (blocks distribution choice):** package a representative UI plus actual chosen native inference libraries and a downloaded model. Test clean install/update/uninstall, signing, offline install, framework bootstrap, model persistence across update, autostart, file associations if any, and repair after partial model download.
5. **Native dependency graph:** the framework decision is not complete until Whisper and Parakeet runtime candidates establish architectures (x64/ARM64), C/C++ runtime, CUDA/DirectML/ONNX dependencies, DLL search/isolation behavior, and licensing. These can force a worker process or installer choice.
6. **Accessibility design:** decide whether the Overlay itself is operable or intentionally passive, how Dictation state is announced without stealing focus, and which reduced-motion/static states replace the animations.
7. **Windows App SDK servicing risk:** select and pin a stable release only after checking its current release notes and known issues against windowing, input, and packaging prototypes.

## Open product decisions

The research narrows but does not answer these owner decisions:

1. Choose WinUI 3/C#, WPF/C#, or WinUI 3/C++/WinRT after the Overlay spike; decide how much visual fidelity versus framework maturity/native-language control is worth.
2. Define “native Application menu”: one literal Win32 `HMENU` from Overlay and tray, or one command model rendered as Fluent XAML on the Overlay and `HMENU` at the tray.
3. Decide whether clicking the Overlay may activate it. If not, approve the accessibility behavior and alternate keyboard/screen-reader path implied by `WS_EX_NOACTIVATE`.
4. Decide which advanced hotkey forms version 1 promises: modifier-only, left/right modifiers, and operation while another application is elevated.
5. Choose MSIX/full-trust versus unpackaged installer, framework-dependent versus self-contained, distribution/update channel, signing identity, and autostart mechanism after the packaging spike.
6. Choose one UI process versus UI plus inference child process after native runtime research. Do not use a Windows service for interactive integration.
7. Decide whether the continuously visible Overlay may use Acrylic despite Microsoft's transient-surface guidance, and approve its opaque/transparency-disabled fallback.
8. Decide target architectures (x64 only versus x64 plus ARM64) once inference backend support is known.

## Decision-ready recommendation envelope

The owner can safely make a final choice within this envelope:

- **If the Fluent prototype and direct Windows 11 materials dominate:** choose WinUI 3. Prefer C# unless a measured native-runtime constraint justifies C++/WinRT. Accept and isolate the unavoidable Win32 shell/input boundary.
- **If framework maturity, explicit lifetime, and desktop interop dominate:** choose WPF/C#, but require the visual/Overlay spike to prove the intended Windows 11 quality before committing.
- **In either case:** use one per-user medium-integrity shell process, explicit lifetime, single-instance redirection, a hidden/message-only HWND for Shell/User32 events, a framework-neutral domain state machine and command model, and an optional inference child only when runtime evidence justifies it.
- **Do not choose based on tray or hotkeys:** both leading frameworks need the same native APIs there. Choose based on UI fit, team/language preference, accessibility prototype results, native-runtime integration, and deployment evidence.
