# Local Dictation

Local Dictation is a Windows application for capturing speech, transcribing it, and inserting the result into the application the user is currently working in.

## Language

**Dictation**:
A single interaction that begins with speech capture and ends when its transcription is inserted into the user's active application or deliberately discarded.
_Avoid_: Recording, job

**Push-to-talk dictation**:
A dictation whose speech capture remains active only while its assigned global hotkey is held.
_Avoid_: Hold mode

**Start-stop dictation**:
A dictation whose speech capture starts and stops on separate activations of its assigned global hotkey.
_Avoid_: Toggle recording

**Overlay**:
The compact topmost surface that communicates the current Dictation state and provides access to application actions. It is visible by default but can be hidden while Local Dictation is idle. Starting a Dictation temporarily shows a hidden Overlay for capture and transcription feedback; after the Overlay returns to its idle state, it restores the user's prior hidden state. Dictation is controlled through global hotkeys and does not require interaction with the Overlay.
_Avoid_: Widget, toolbar

**History entry**:
The local record of a Dictation that produced transcribed text, consisting of that text, its completion time, transcription model, and completion status. It may also include the detected language when the transcription backend already provides it; recorded audio is not part of a History entry. An entry can be marked incomplete or not inserted when Local Dictation preserved text after an audio or output failure.
_Avoid_: Log entry, transcript file

**Transcription history**:
The collection of History entries retained by Local Dictation for the previous 30 days.
_Avoid_: Archive, log

**Recognition term**:
A user-provided word or phrase offered to a transcription backend as a vocabulary hint when that backend supports biasing. It can improve recognition but does not guarantee a particular output.
_Avoid_: Dictionary entry, replacement

**Replacement rule**:
A deterministic post-processing rule that replaces a specified source text with its configured target text after transcription. Each rule chooses whether the source must match a complete word or phrase, or may also match within other words.
_Avoid_: Recognition term, correction

**Current insertion target**:
The text input that has focus when a completed Dictation is ready to be inserted. It is the default destination and may differ from the input that was focused when the Dictation began.
_Avoid_: Active application

**Remembered insertion target**:
The application and text input that had focus when a Dictation began, retained so Local Dictation can restore it before inserting the completed text when the corresponding option is enabled.
_Avoid_: Original window, locked application

**Management window**:
The single conventional application window that hosts settings, Transcription history, the dictionary, and other management surfaces. Closing it does not stop Local Dictation.
_Avoid_: Settings window, dashboard

**Application menu**:
The shared command menu available from both the Overlay and the system tray icon. It opens sections of the Management window, shows or hides the Overlay, and provides the command that stops Local Dictation completely.
_Avoid_: Context menu, tray menu

**Overlay home position**:
The configured monitor and screen anchor at which the Overlay appears when Local Dictation starts and to which it moves immediately when the configuration changes. Moving the Overlay manually is temporary and does not change its home position.
_Avoid_: Last position, saved drag position

**Capture readiness**:
The state in which Local Dictation can begin receiving microphone samples with minimal delay while no samples are retained or transcribed before a Dictation begins.
_Avoid_: Listening, background recording

**Transcription readiness**:
The state in which the selected transcription model is loaded and can process captured audio. A Dictation may begin before transcription readiness is reached; model loading becomes visible only when it delays processing after speech capture ends.
_Avoid_: App readiness, capture readiness

**Audio input availability**:
The selected microphone's demonstrated ability to deliver valid audio samples. An existing or nominally active stream alone does not establish availability after device changes, interruptions, or Remote Desktop reconnections.
_Avoid_: Stream state, device connection
