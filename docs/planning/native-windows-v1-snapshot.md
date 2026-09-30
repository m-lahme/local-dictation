# Native Windows V1 — consolidated planning snapshot

> Status: Consolidated summary of the pre-map conversation and the Wayfinder decisions resolved through 2026-07-18. The canonical detail and rationale live in the [Wayfinder map](https://github.com/m-lahme/local-dictation/issues/1) and its linked decision tickets; a later ticket resolution takes precedence over this summary.

## Destination

Produce a decision-complete product and architecture specification plus an implementation roadmap for version 1 of a rebuilt native Windows application. Version 1 covers the Overlay, system tray integration, Management window, Transcription history, dictionary, settings, local Whisper and NVIDIA Parakeet backends, packaging, storage, and the core Windows integration architecture.

Cloud transcription providers are considered as a future extension seam but are not implemented in version 1.

## Product posture

- Optimize version 1 for the owner's daily use.
- Avoid architectural dead ends that would make a later open-source or commercial release expensive.
- Officially develop and test on Windows 11.
- Keep Windows 10 compatibility technically plausible, but do not test, promise, or implement explicit Windows 10 support in version 1.

## Visual direction

- Use a strongly Fluent-inspired, modern, calm, and uncluttered interface.
- Base the layout on the Fluent prototype, not the more experimental alternatives.
- Prefer icons in the top-level Management window navigation. Settings category subnavigation remains icon-free. Exact top-level glyphs can be refined during implementation without reopening the information architecture.
- Use neutral gray surfaces with one red accent color.
- Reuse the same red for selected navigation, active toggles, recording feedback, and the app icon.
- Support `System`, `Light`, and `Dark` themes; default to `System`.
- Use subtle Mica transparency in the Management window where appropriate.
- Consider Acrylic for transient menus and possibly the small Overlay.
- Avoid gradients and large accent-colored surfaces.
- App icon direction: four vertical bars of different heights representing an audio level.
- Exact colors, bar geometry, and animation timing remain to be refined later.
- Static design sketches were removed after the native implementation replaced them; they remain available in Git history.

## Application surfaces and lifecycle

### Overlay

- Is topmost and visible by default, but can be hidden while Local Dictation is idle.
- Starting a Dictation temporarily reveals a hidden Overlay for capture and transcription feedback. It remains visible through processing and the final feedback state; after returning to idle, it restores the prior hidden state.
- Is intentionally very small: a narrow horizontal pill containing only five bars/dots.
- Contains no visible text, icons, or buttons.
- Dictation is controlled through global hotkeys; interacting with the Overlay is not required to dictate.
- Left-click without dragging opens the Application menu.
- Right-click opens the same Application menu.
- Left-button movement past a drag threshold temporarily repositions the Overlay.

### Overlay states

- Idle/Ready: five short muted-neutral bars with deliberately low prominence.
- Recording: red bars that react to the live microphone level.
- Transcribing: amber bars moving as a calm travelling wave.
- Model loading: the same travelling-wave language in blue.
- Success: four active green segments form a check, hold for 1.6 seconds, then return to idle.
- Cancellation returns directly to idle without a separate cancelled animation.
- Error: a deliberately broken red bar pattern; any preserved text routes to its History entry.

### Overlay home position

- Settings define the startup/home position; changing the setting moves the Overlay immediately.
- Available anchors should include top/bottom center and the four screen corners.
- A monitor can be selected explicitly; the primary monitor is the default.
- Focus changes between monitors never move the Overlay.
- Manual dragging is temporary for the current session and does not alter the configured home position.
- On restart, the Overlay returns to its configured monitor and anchor.
- If that monitor is unavailable, use the primary monitor for that session without overwriting the stored choice.
- Placement must account for DPI, work area, resolution, and taskbar position.

### Shared Application menu

The Overlay and tray icon expose the same menu:

- Transcription history
- Dictionary
- Show or hide Overlay
- Settings
- Quit Local Dictation

### Management window and tray

- One conventional Management window hosts settings, Transcription history, dictionary, and other management surfaces.
- Its stable top-level sidebar uses icons for History, Dictionary, Settings, and Quit. Entering Settings replaces that sidebar with icon-free category navigation and a Back action.
- It appears in the taskbar while open.
- Closing it with `×` hides only the window; Overlay, hotkeys, and tray icon keep running.
- The app can also be quit from a clearly discoverable command inside the Management window.
- Only an explicit quit command stops Local Dictation.
- On the first close of the Management window, show this one-time explanation:

  > Local Dictation läuft im Hintergrund weiter. Zum vollständigen Beenden öffne das Kontextmenü des Overlays oder des Symbols im Infobereich per Rechtsklick.

## Dictation interaction

- Support both modes in version 1:
  - Push-to-talk: capture while its hotkey is held.
  - Start/stop: one hotkey activation starts capture, another stops it.
- Both modes operate through configurable global hotkeys, but neither requires a dedicated key. One shared key may start capture immediately, treat a short tap as Start-stop dictation, and treat holding beyond a configurable threshold as Push-to-talk dictation whose release ends capture. The exact threshold remains open.
- Escape cancels by default, can be rebound, and can be disabled.
- Cancellation is available whenever the Dictation is not idle: during capture, model wait, or transcription.
- Cancellation discards audio and partial results, creates no History entry, and returns the Overlay directly to idle.
- A background model load already in progress may continue after cancellation.

## First-run setup

Provide a short guided setup:

1. Select and test the microphone.
2. Configure push-to-talk and start/stop hotkeys.
3. Analyze hardware, recommend a model, and download it.
4. Optionally enable start with Windows.

- Steps can be skipped where practical and changed later in Settings.
- Start-with-Windows is opt-in, not silently enabled.
- Autostart opens only the Overlay and tray icon, not the Management window.

## Local transcription models

- Version 1 supports local Whisper and NVIDIA Parakeet engines.
- Analyze hardware and recommend a model, but require a conscious user choice.
- Prefer Parakeet on systems without a suitable GPU.
- Recommend a suitable Whisper model when sufficient GPU resources exist.
- Never switch models silently during a Dictation.
- Allow models to be downloaded, selected, managed, and removed independently.

### Model lifecycle

- Capture readiness and transcription readiness are independent.
- Allow recording as soon as the audio pipeline is ready, even if the model is still loading.
- Load the active model in the background at application startup.
- Do not show background model loading in the Overlay unless it actually blocks processing after capture ends.
- If an unloaded model is needed, begin loading it as soon as recording starts.
- If capture finishes first, show a loading/waiting state until transcription can begin.
- Keep the model loaded by default.
- Setting `Unload model after inactivity` offers:
  - Never (default)
  - 5 minutes
  - 15 minutes
  - 30 minutes
  - 1 hour
- The inactivity timer begins after the last transcription completes.

## Audio pipeline and device recovery

- Optimize for minimal delay between hotkey activation and audio capture.
- Prepare/warm the capture pipeline at startup without recording or retaining samples while idle.
- Do not treat an existing stream object as proof that the microphone still provides valid samples.
- Monitor actual sample delivery and Windows device changes.
- Recreate the audio stream after device invalidation, USB/Bluetooth interruptions, docking changes, default-device changes, and RDP/Remote Desktop disconnect/reconnect.
- Treat RDP as one example of generic audio device churn rather than building a protocol-specific workaround.
- Keep the model and non-audio pipeline warm independently of the capture stream.

### Interruption during capture

- Give a briefly unavailable microphone a short recovery window; exact duration remains to be decided.
- If recovery fails, transcribe the audio captured so far.
- Mark the resulting History entry as incomplete.
- Do not automatically insert incomplete text.
- Open the Management window directly on that History entry and show a concise explanation.
- Provide an immediate copy action.

## Text output

Three independent settings:

| Setting | Default | Behavior |
| --- | --- | --- |
| Insert transcribed text automatically | On | Insert at the Current insertion target when processing completes. |
| Remember insertion target from Dictation start | Off | Restore the Remembered insertion target before insertion. |
| Keep transcribed text in the Windows clipboard | Off | Leave the final text as current clipboard content and allow it in Windows clipboard history. |

- By default, insert wherever the text cursor is focused when transcription completes.
- Do not compare start and end applications in the default mode.
- If remembered-target mode is enabled, remember and restore the application/input focused at Dictation start.
- If auto-insert and keep-in-clipboard are both off, the text remains accessible only from Transcription history.

## Clipboard transaction

- Automatic insertion may use the Windows clipboard temporarily for broad application compatibility.
- When `Keep transcribed text in the Windows clipboard` is off:
  - Preserve all existing clipboard formats, not just text.
  - Exclude temporary transcription text from Windows clipboard history and Cloud Clipboard synchronization.
  - Restore the original clipboard after paste.
  - Do not alter existing `Win+V` history entries.
  - Detect concurrent clipboard changes and never overwrite a newer user/application copy operation.
- Do not rely on correlating or restoring a `Win+V` history item; history is incomplete and supports only a subset of formats.
- Before overwriting, inspect whether every existing format can be restored safely.
- If any format is unsafe to preserve:
  - Do not change the Windows clipboard at all.
  - Save the transcription as a not-inserted History entry.
  - Open the Management window directly on that entry.
  - Explain why insertion was skipped and offer copy/manual insertion actions.
- Research established a fail-closed, deep-clone allowlist approach with a transaction marker and Clipboard sequence checks. A dedicated prototype must still validate supported formats, concurrent changes, history/cloud exclusion, and target paste timing.

## Transcription history

- Store locally in a small database; SQLite is preferred over CSV as primary storage.
- CSV may be offered later as an export format.
- Retain entries for 30 days in version 1.
- Retention is not configurable in version 1.
- Store:
  - transcribed text,
  - completion timestamp,
  - transcription model,
  - completion/output status,
  - detected language only if the backend already supplies it reliably.
- Do not run extra language detection merely for history metadata.
- Do not retain audio files in version 1.
- Support complete, incomplete, and not-inserted entries.
- Failed or cancelled Dictations that produced no useful text create no regular History entry.
- Re-transcribing retained audio with a better model is a possible later feature, not version 1.

## Dictionary

The Dictionary has two explicitly different concepts:

### Recognition terms

- User-provided words or phrases offered as vocabulary bias to backends that support it.
- They are hints, not guaranteed replacements.
- One global, unordered collection applies to every supporting backend. Unsupported backends do not reinterpret Recognition terms as replacements.
- Runtime-specific capacity, serialization, and effectiveness remain part of backend validation.

### Replacement rules

- Deterministic post-processing after transcription.
- Each rule maps source text to exact target text.
- Match is case-insensitive by default while output always uses the configured target spelling.
- Each rule may optionally require case-sensitive matching.
- Each rule selects one match mode:
  - complete word or phrase (default),
  - also match within other words.
- The visible numbered list is the execution order. It runs once; later rules see earlier output, while a rule never reprocesses text it inserted itself.
- Matching is Unicode-aware and canonically equivalent, with locale-independent full case folding for case-insensitive rules.
- V1 allows up to 1,000 positions and caps every intermediate result at 10 MiB. Exceeding the cap aborts post-processing and preserves the original backend text as not inserted.
- Versioned JSON import/export keeps Recognition terms and Replacement rules separate. The complete semantics and examples live in [Specify dictionary and replacement semantics](https://github.com/m-lahme/local-dictation/issues/8).

## Confirmed platform behavior

- Windows 11 is the visual reference and only tested/supported OS for version 1.
- Use native Windows technology; current leading direction is WinUI 3 / Windows App SDK, but the final framework and process architecture still require an explicit decision.
- Windows 10 should not be deliberately blocked when inexpensive compatibility is possible.
- Windows 10 may receive simplified visual fallbacks for system-only effects, but no version 1 support promise is made.

## Out of scope for version 1

- Cloud transcription provider implementation and API-key management.
- Official Windows 10 testing/support.
- Persistent audio history.
- Re-transcription of old Dictations from retained audio.
- Configurable History retention.
- Final commercial licensing, pricing, and support operations.

## Remaining decisions

The live ticket graph, current blockers, and completed decision index are maintained only in the [Wayfinder map](https://github.com/m-lahme/local-dictation/issues/1) to avoid duplicating a list that becomes stale as parallel sessions finish.
