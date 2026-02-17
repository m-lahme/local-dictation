# Local Dictation

A local, offline speech-to-text dictation tool for Windows. Hold a hotkey, speak, release, and the transcribed text is pasted into whatever application has focus. Powered by [faster-whisper](https://github.com/SYSTRAN/faster-whisper) — no cloud services, no API keys.

## Requirements

- Windows 10/11
- Python 3.10+
- A working microphone
- Administrator privileges (required by the `keyboard` library for global hotkeys)

### CUDA (optional)

If you have an NVIDIA GPU with CUDA support, the tool will automatically use it for faster transcription. Otherwise it falls back to CPU.

## Installation

1. Clone or download this repository.

2. Install the dependencies:

   ```
   pip install keyboard numpy sounddevice faster-whisper
   ```

3. Run the tool as Administrator:

   ```
   python dictation.py
   ```

   On first launch the Whisper model will be downloaded automatically (this may take a few minutes depending on your connection).

### Desktop Installation (optional)

Run the install script to set up shortcuts so you can launch the tool without a terminal:

```
python install.py
```

It will ask you for each option individually:

- **Desktop shortcut** — launch from your desktop
- **Start Menu shortcut** — find and pin the app via Windows Search
- **Autostart at logon** — start the tool automatically when you log in

Shortcuts launch the app with `pythonw.exe` (no console window). To quit, click the **×** button on the overlay.

To remove all shortcuts:

```
python install.py --uninstall
```

## Usage

1. Launch the script. A small status overlay appears and shows **"Loading..."** while the model loads.
2. Once the overlay switches to **"Ready"**, hold down the hotkey (default: `Ctrl+Win`).
3. The overlay turns red with **"Recording..."** — speak into your microphone.
4. Release the hotkey. The overlay turns amber with **"Transcribing..."**.
5. Once complete, the transcribed text is pasted into the active window and the overlay briefly flashes green with **"Done!"**.
6. To exit, click the **×** on the overlay or press `Ctrl+C` in the terminal.

The overlay is always on top and can be repositioned by clicking and dragging it.

### Debug mode

To identify the correct key names for your keyboard layout, run:

```
python dictation.py --debug
```

This prints every key press/release event with its name and scan code. Use these names when configuring the `hotkey` option.

## Configuration

Create a `config.json` file in the same directory as `dictation.py`. All options are optional — any omitted option uses its default value.

### Example

```json
{
  "hotkey": "strg+linke windows",
  "language": null,
  "model": "large-v3-turbo",
  "device": "auto",
  "initial_prompt": "",
  "max_duration": 60,
  "cancel_key": "escape",
  "keep_in_clipboard": false,
  "overlay_position": "bottom-center"
}
```

### Options

| Option              | Type             | Default                | Description                                                                                                                                                                                                                |
| ------------------- | ---------------- | ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `hotkey`            | string           | `"strg+linke windows"` | The key combination to hold while dictating. Key names depend on your OS keyboard layout — use `--debug` mode to find the correct names. For a standard US layout you would use `"ctrl+left windows"`.                     |
| `language`          | string or null   | `null`                 | Language code for transcription (e.g. `"en"`, `"de"`). Set to `null` for automatic language detection.                                                                                                                     |
| `model`             | string           | `"large-v3-turbo"`     | The Whisper model to use. Options include `"tiny"`, `"base"`, `"small"`, `"medium"`, `"large-v3"`, `"large-v3-turbo"`. Larger models are more accurate but slower and use more memory.                                     |
| `device`            | string           | `"auto"`               | Compute device. `"auto"` detects CUDA automatically. Set to `"cuda"` to force GPU or `"cpu"` to force CPU.                                                                                                                 |
| `initial_prompt`    | string           | `""`                   | An optional text hint passed to Whisper to bias transcription toward specific vocabulary or formatting style. See [Initial Prompt](#initial-prompt) below for details.                                                     |
| `max_duration`      | number           | `60`                   | Maximum recording duration in seconds. Recording is automatically capped at this length.                                                                                                                                   |
| `cancel_key`        | string or null   | `"escape"`             | Key to cancel an in-progress recording or transcription. The overlay reverts to "Ready" and no text is pasted. Set to `null` to disable.                                                                                   |
| `keep_in_clipboard` | boolean          | `false`                | If `true`, the transcribed text stays in the clipboard after pasting, so you can paste it again or access it via clipboard history (Win+V). If `false`, the clipboard is restored to its previous content after pasting.   |
| `overlay_position`  | string or [x, y] | `"bottom-center"`      | Starting position of the status overlay. Named positions: `"bottom-center"`, `"bottom-right"`, `"bottom-left"`, `"top-right"`, `"top-left"`. Or provide exact pixel coordinates as a two-element array, e.g. `[500, 300]`. |

### Initial Prompt

The `initial_prompt` option is a **vocabulary and style hint** for the Whisper model — it is _not_ an instruction. Whisper is a speech-to-text model, not a chatbot, so it will not follow commands like "output as Markdown" or "only return to-do items".

Internally, Whisper treats the `initial_prompt` text as if it were the transcript of speech that came right before your recording. This biases the model's predictions in two ways:

- **Vocabulary**: Words and terms in the prompt are more likely to be recognized correctly. This is especially useful for uncommon proper nouns, technical jargon, or domain-specific terms that Whisper might otherwise misspell or miss.
- **Style**: Whisper mimics the formatting style of the prompt. If the prompt uses proper punctuation and capitalization, the output tends to as well. If the prompt is all lowercase without punctuation, the output follows suit.

#### Examples

| Goal                 | `initial_prompt` value                                                  |
| -------------------- | ----------------------------------------------------------------------- |
| Programming terms    | `"TypeScript, useState, async, await, Kubernetes, PostgreSQL"`          |
| Proper nouns         | `"Anthropic, Claude, OpenAI, Hugging Face"`                             |
| Medical terminology  | `"Patient presents with dyspnea, tachycardia, and bilateral crackles."` |
| German dictation     | `"Heute besprechen wir die Ergebnisse der letzten Besprechung."`        |
| Enforce punctuation  | `"Hello, how are you? I'm doing well. Let's get started."`              |
| Suppress punctuation | `"hello how are you i am doing well lets get started"`                  |
| Default (no bias)    | `""`                                                                    |

#### Common mistake

Do **not** write instructions like:

```json
"initial_prompt": "parse todo items from the transcript and output only the to-dos in Markdown."
```

Whisper will ignore the instruction and may hallucinate words from it (e.g. inserting "to-do" or "Markdown" into unrelated transcriptions). Instead, put example text that resembles what you expect the output to look like, or list the specific words you want Whisper to recognize.

## Status Overlay

The floating overlay shows the current state of the tool at a glance:

| State        | Color                        | Text              |
| ------------ | ---------------------------- | ----------------- |
| Loading      | Dark blue                    | "Loading..."      |
| Idle         | Dark gray                    | "Ready"           |
| Recording    | Red                          | "Recording..."    |
| Transcribing | Amber                        | "Transcribing..." |
| Done         | Green (1s)                   | "Done!"           |
| Cancelled    | Gray (1.5s)                  | "Cancelled"       |
| Error        | Red text on dark gray (1.5s) | "Error"           |

The "Done" and "Error" states automatically revert to "Ready" after a short delay. The overlay can be dragged to any position on screen.
