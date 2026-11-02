# Tests

Run from the repository root with Windows PowerShell 5.1 or PowerShell 7:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-native-regression.ps1
```

The default suite builds the C# audit executable and runs domain, storage, correction-learning, audio-conversion, power-routing, overlay-description and hotkey state-machine tests. It uses a new data directory under `work/tests/` on every run. It does not start microphone capture, send keys or alter the user's clipboard. Hotkey tests include delayed event delivery after cold audio startup. The same suite runs in GitHub Actions on `windows-latest` (`.github/workflows/ci.yml`).

The optional `-Visual` switch opens WPF windows and writes render images and generated assets under `work/audit/visuals/`. Run this only when desktop interaction is acceptable.

The audit executable also exposes explicit integration modes: `--clipboard-test`, `--clipboard-test-disposable`, `--meter-test`, `--capture-test` and `--input-test`. They access real hardware, windows or the clipboard; they are not part of the default run and some retain assumptions from earlier hardware investigations. Inspect the corresponding test before using it, and pass an isolated data directory as the second argument.

`--history-benchmark` opens the history page several times with the `history.json` from the given data directory, scrolls through it and prints the timings. To measure realistic sizes, copy a real history to `work/history-benchmark/` (ignored by git) and pass that folder; the benchmark also saves a screenshot of that history there.

## Manual model tests

`audio/` contains synthetic speech, generated from the fixed sentences in `create_test_audio.ps1` with the local Windows speech voice, not user recordings. Regenerating these clips requires Windows PowerShell with System.Speech; output depends on the installed voice. After `build.ps1`, the worker transcribes them directly:

```powershell
.\bin\native\LocalDictation.Worker.exe --engine parakeet --device cpu --file "tests\audio\de_kurz.wav|tests\audio\en_short.wav"
.\bin\native\LocalDictation.Worker.exe --engine whisper --device gpu --file "tests\audio\de_fachbegriffe.wav"
```

The worker reads the models from `%LOCALAPPDATA%\LocalDictation\native-models` (or `--models <folder>`); start the app once to download them. Physical hotkeys remain a separate hardware check.
