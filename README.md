# Local Dictation

A native dictation app for Windows 11 x64: start a recording with a hotkey, transcribe it locally and insert the text into the active application. The UI and audio capture are implemented in C# with WPF and WASAPI. A separate C# worker integrates whisper.cpp and sherpa-onnx. Speech is recognized entirely on your own computer; there is no account and no cloud. The UI is available in English and German.

License: [MIT](LICENSE). Notices for the libraries and models used are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Requirements

- Windows 11 x64. Windows 10 and Windows on ARM are untested. On ARM, the x64 build runs under emulation and then only with Parakeet.
- Parakeet (the default model) runs on any x64 CPU. The download on the first dictation is about 640 MiB.
- Whisper requires a dedicated graphics card with a Vulkan driver and the [Microsoft Visual C++ Redistributable 2015–2022 (x64)](https://aka.ms/vc14/vc_redist.x64.exe). The download is about 550 MiB. Integrated graphics are currently not used; without a suitable GPU, Parakeet takes over.
- Windows must allow desktop apps to access the microphone (Settings › Privacy & security › Microphone).

## Installation

From a release ZIP:

1. Unblock the ZIP file before extracting it: right-click › Properties › "Unblock", or in PowerShell `Unblock-File .\LocalDictation-<Version>-win-x64.zip`.
2. Extract the entire ZIP and run `setup.cmd`. The app is installed per user to `%LOCALAPPDATA%\Programs\Local Dictation` and gets a Start menu shortcut. Administrator rights are not required. Alternatively, you can start `LocalDictation.exe` directly from the extracted folder.
3. The program files are not signed, so Windows SmartScreen warns on first launch ("More info" › "Run anyway"). If Smart App Control is on, Windows blocks the app; the only way around that is building from source.

Updates are installed the same way. The previous installation is kept as a fallback folder; older fallback folders are removed.

### Uninstalling

Run `setup.cmd -Uninstall` in the extracted release folder. This quits the app and removes the installation folder, the fallback folder, the Start menu shortcut and the autostart entry. `setup.cmd -Uninstall -RemoveData` also deletes the settings, history, statistics and downloaded models under `%LOCALAPPDATA%\LocalDictation`.

## Usage

When the app starts, the overlay appears. Starting it again opens the main window of the running instance. Left-clicking the tray icon shows or hides the main window. If the overlay is hidden while idle, a normal start opens the main window instead. Starting with Windows does not open the main window.

- A short press of Ctrl + Windows starts a dictation; pressing it again stops it.
- Holding the keys past the hold threshold and then releasing them uses push-to-talk.
- Pressing twice quickly opens the correction of the last dictation, based on the selected, corrected text.
- Ctrl + Shift + Space can be configured as an alternative.
- Escape discards the recording, the model wait or the transcription. While idle, Escape is left to other applications.
- Closing the window keeps the app running. To quit completely, choose Quit in the sidebar, the tray menu or the overlay menu.

## Languages

The UI is available in English and German. By default it follows the Windows display language; any other display language uses English. You can change the language under Settings › Appearance. The UI language does not affect transcription: the active model recognizes the spoken language by itself.

## Models

Whisper 3 Turbo uses whisper.cpp with Vulkan on a dedicated GPU. Parakeet TDT 0.6B v3 uses sherpa-onnx on the CPU. Recording and model preparation run independently. The first time the GPU is used, building the shaders can take a few seconds. If Vulkan or the GPU fails, Parakeet can process the same dictation on the CPU. Whisper on the CPU is disabled.

The optional automatic model switch responds to Windows power events: on supported ASUS systems, Silent means Parakeet/CPU, and Performance or Turbo means Whisper/GPU. On other devices, unambiguous power-saving and high-performance profiles are mapped the same way. Balanced and unknown profiles keep the active model. A switch happens once the profile has been stable for 15 seconds and waits until a dictation in progress has finished. Optionally, Whisper is unloaded after a configurable idle time and loaded again for the next dictation.

The model catalog pins source revisions, sizes and SHA-256 checksums. Downloads are verified before they are activated. Inactive models can be removed in Settings. Without internet access, you can also copy the files listed in `src/LocalDictation/model-catalog.json` to `%LOCALAPPDATA%\LocalDictation\native-models\whisper` or `...\native-models\parakeet` yourself; the app recognizes them by their checksum at startup.

## Microphones

Settings › Audio and hotkeys contains a priority list of all microphones Windows knows about, including those that are currently disconnected. The app uses the highest connected microphone. You can reorder the list by drag and drop or with Alt+arrow keys. Type and connection, such as laptop, headset, webcam, USB or Bluetooth, are derived from the Windows device properties. On first start, a previously selected microphone is placed at the top; the others are sorted by device type. Newly connected devices are added at the end.

The app responds to Windows device events and, while idle, switches to the best available microphone without starting the capture stream. It never switches during a dictation. If the microphone in use fails during a dictation, the dictation is treated as interrupted.

## Dictionary, History and Statistics

Replacement rules are applied in the order shown. Each rule matches whole words or parts of words, with selectable case sensitivity and Unicode normalization. Rules can be searched and reordered by drag and drop. From selected, corrected text, the app can suggest new rules; you review and edit them before they are added.

The History contains dictations from the last 30 days. Recordings remain available for one hour so they can be transcribed again with another model. An optional second Whisper transcription checks for conspicuously missing capitalization or punctuation. An orange check mark flags a result worth reviewing; it is not a reliable indication of whether the text is correct.

Optional voice commands insert bullet points, new lines and paragraphs. They work in English ("bullet point", "new line", "new paragraph") and German ("Stichpunkt", "neue Zeile", "neuer Absatz"). Saying "literally" or "wörtlich" before a command keeps its words as text. Statistics shows words, dictation time and estimated time saved. You can set your typing speed or measure it with a typing test.

## Data and text output

All user data is stored under `%LOCALAPPDATA%\LocalDictation`: settings, dictionary, history, usage statistics, temporarily retained recordings, size-limited diagnostic logs and `native-models`. Older text/TSV settings are migrated; the original files are kept as a migration backup. Automatic cleanup applies to the active data, not to these backups. There is no fixed limit on recording length; memory and runtime limits constrain very long recordings.

"Insert only" sends a single Ctrl+V when every clipboard format can be safely backed up and restored. Supported formats include text, HTML, RTF, image data and known metadata from browsers and code editors. Anything you copy in the meantime takes precedence over the restore. The temporary text and the restored content are excluded from Windows clipboard history and cloud sync. Unknown formats, or formats that cannot be copied safely, are left untouched; the app then falls back to slower Unicode character input.

"Clipboard only" and "Insert and clipboard" deliberately keep the dictation on the clipboard. Target programs running with higher privileges can block input. When the target reads the clipboard, that does not confirm a successful paste in every application; the text is also kept in the History. Partial model results are never inserted.

Environment variables for alternative storage locations:

- `LOCAL_DICTATION_DATA`: data folder instead of `%LOCALAPPDATA%\LocalDictation`
- `LOCAL_DICTATION_MODELS`: model folder instead of `<data folder>\native-models`

## Privacy

- Audio, transcripts and all settings stay on your computer. There is no telemetry, no account and no update check.
- The only network connection is the model download from huggingface.co. As with any download, Hugging Face sees your IP address.
- The History stores dictated text unencrypted in `history.json` for 30 days. Recordings are deleted after one hour.
- `diagnostics.log` is capped at about 2 MiB and contains no dictated text, but it does contain device names (such as "…'s AirPods"), error messages and local paths. Please review it before attaching it to a public issue.
- The global keyboard hook evaluates only the configured hotkeys and Escape and does not record keystrokes. To insert text, the app briefly replaces the clipboard and restores it afterwards.

## Troubleshooting

- Diagnostic log: `%LOCALAPPDATA%\LocalDictation\diagnostics.log`.
- Whisper is not available: check that the graphics driver supports Vulkan and that the Visual C++ Redistributable is installed. Until then, Parakeet takes over.
- No sound, or the microphone is blocked: in Windows Settings under Privacy & security › Microphone, allow desktop apps to access the microphone, and check whether another program is using the microphone exclusively.
- Text does not appear: programs running as administrator do not accept input from an app started normally. The text stays in the History and can be copied from there.

## Building and running

Development prerequisites: Windows 11 x64, .NET SDK 10.0.x (with only a .NET 11 SDK installed, `global.json` is rejected) and Windows PowerShell 5.1 or PowerShell 7. The first build downloads the NuGet packages to `.tools/packages`.

Windows blocks scripts by default. The commands below bypass this for the single call only; alternatively, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once.

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

The build includes the .NET runtime and places the app and the worker in `bin\`. Start the app only from this output: `dotnet run` and Visual Studio build only the app, without the transcription worker in `native\`, so transcription is not possible.

UI text is written in English in the code and translated into German in the tables under `src/LocalDictation/Strings/`. The regression tests fail if a localized text has no German translation.

## Repository structure

```text
LocalDictation.slnx      Solution for app, worker and tests
src/LocalDictation/      WPF app, recording, hotkeys, data and Windows integration
src/NativeWorker/        Isolated transcription process
tests/NativeAudit/       C# regression tests and optional integration tests
tests/audio/             Synthetic speech clips for manual model tests
assets/                  App icon and logo sizes
licenses/                License texts of the dependencies
docs/                    Architecture research, planning and development notes
.github/workflows/       CI: regression tests and build on windows-latest
build.ps1                Publish app and worker to bin/
package.ps1              Package a clean build as a ZIP with a hash manifest and a SHA-256 file
setup.ps1, setup.cmd     Install or uninstall a verified package per user
configure.ps1            Read and change settings from the command line
```

`bin/`, `dist/`, `work/` and local dependency caches are not under version control. The documents under `docs/planning` and `docs/research` describe earlier decisions and research; they are not a complete description of the current feature set.

## Testing and packaging

```powershell
# Logic tests without windows, microphone capture or simulated keyboard input
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-native-regression.ps1

# Optional: also open native windows and render images
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-native-regression.ps1 -Visual

# Create a release ZIP from a fresh build; the running installation is not touched
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

The ZIP and its SHA-256 file are placed in `dist/`. The hash manifest is inside the ZIP. Updates keep the previous installation as a fallback folder. Models and user data are stored outside it. There is no automatic online updater.

Settings can be read and changed without the UI, for example in a session started with `powershell -NoProfile -ExecutionPolicy Bypass`:

```powershell
.\configure.ps1 -Show
.\configure.ps1 -Engine parakeet -Restart
.\configure.ps1 -AutomaticPowerModel $true -Restart
.\configure.ps1 -Language en -Restart
.\bin\LocalDictation.exe --shutdown
```

`-Language` accepts `system`, `de` or `en`. Without `-Restart`, the script refuses to change the settings of a running app. The regression tests use their own data under `work/tests/`. Notes on targeted hardware tests are in [tests/README.md](tests/README.md). Please report security issues as described in [SECURITY.md](SECURITY.md).
