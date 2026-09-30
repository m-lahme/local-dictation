# Tests

Run from the repository root with PowerShell 7:

```powershell
.\tests\run-native-regression.ps1
```

The default suite builds the C# audit executable and runs domain, storage, correction-learning, audio-conversion, power-routing, overlay-description and hotkey state-machine tests. It uses a new data directory under `work/tests/` on every run. It does not start microphone capture, send keys or alter the user's clipboard. Hotkey tests include delayed event delivery after cold audio startup.

The optional `-Visual` switch opens WPF windows and writes render images and generated assets under `work/audit/visuals/`. Run this only when desktop interaction is acceptable.

The audit executable also exposes explicit integration modes: `--clipboard-test`, `--clipboard-test-disposable`, `--meter-test`, `--capture-test` and `--input-test`. They access real hardware, windows or the clipboard; they are not part of the default run and some retain assumptions from earlier hardware investigations. Inspect the corresponding test before using it, and pass an isolated data directory as the second argument.

`audio/` contains synthetic speech, generated from the fixed sentences in `create_test_audio.ps1`, not user recordings. Regenerating these clips requires Windows PowerShell with System.Speech; output depends on the installed speech voice. Model inference and physical hotkeys remain separate hardware checks.
