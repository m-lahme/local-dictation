"""
Local Dictation Tool — hold Ctrl+Win, speak, release to paste transcription.
Supports English and German with auto-detection.
"""

import ctypes
import ctypes.wintypes
import json
import re
import os
import sys
import threading
import time
import tkinter as tk
from collections import deque
from pathlib import Path

import keyboard
import numpy as np
import sounddevice as sd
from faster_whisper import WhisperModel


def _fix_stdio():
    """Redirect stdout/stderr to devnull when running under pythonw.exe."""
    if sys.stdout is None:
        sys.stdout = open(os.devnull, "w")
    if sys.stderr is None:
        sys.stderr = open(os.devnull, "w")


_fix_stdio()

# ── Config ───────────────────────────────────────────────────────────────────

DEFAULT_CONFIG = {
    "hotkey": "strg+linke windows",
    "language": None,          # None = auto-detect
    "model": "large-v3-turbo",
    "device": "auto",          # "auto", "cuda", or "cpu"
    "initial_prompt": "",
    "max_duration": 60,
    "cancel_key": "escape",
    "keep_in_clipboard": False,
    "overlay_position": "bottom-center",  # "bottom-center", "bottom-right", "bottom-left", "top-right", "top-left", or [x, y]
}

def load_config() -> dict:
    config = dict(DEFAULT_CONFIG)
    config_path = Path(__file__).parent / "config.json"
    if config_path.exists():
        try:
            with open(config_path, encoding="utf-8") as f:
                user = json.load(f)
            config.update({k: v for k, v in user.items() if k in DEFAULT_CONFIG})
            print(f"Loaded config from {config_path}")
        except Exception as e:
            print(f"Warning: failed to load config.json: {e}")
    return config

# ── Win32 Clipboard ──────────────────────────────────────────────────────────

CF_UNICODETEXT = 13

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

OpenClipboard = user32.OpenClipboard
OpenClipboard.argtypes = [ctypes.wintypes.HWND]
OpenClipboard.restype = ctypes.wintypes.BOOL

CloseClipboard = user32.CloseClipboard
CloseClipboard.argtypes = []
CloseClipboard.restype = ctypes.wintypes.BOOL

EmptyClipboard = user32.EmptyClipboard
EmptyClipboard.argtypes = []
EmptyClipboard.restype = ctypes.wintypes.BOOL

GetClipboardData = user32.GetClipboardData
GetClipboardData.argtypes = [ctypes.wintypes.UINT]
GetClipboardData.restype = ctypes.wintypes.HANDLE

SetClipboardData = user32.SetClipboardData
SetClipboardData.argtypes = [ctypes.wintypes.UINT, ctypes.wintypes.HANDLE]
SetClipboardData.restype = ctypes.wintypes.HANDLE

GlobalAlloc = kernel32.GlobalAlloc
GlobalAlloc.argtypes = [ctypes.wintypes.UINT, ctypes.c_size_t]
GlobalAlloc.restype = ctypes.wintypes.HANDLE

GlobalLock = kernel32.GlobalLock
GlobalLock.argtypes = [ctypes.wintypes.HANDLE]
GlobalLock.restype = ctypes.c_void_p

GlobalUnlock = kernel32.GlobalUnlock
GlobalUnlock.argtypes = [ctypes.wintypes.HANDLE]
GlobalUnlock.restype = ctypes.wintypes.BOOL

GMEM_MOVEABLE = 0x0002


def clipboard_get() -> str | None:
    """Read Unicode text from clipboard, or None on failure."""
    if not OpenClipboard(None):
        return None
    try:
        handle = GetClipboardData(CF_UNICODETEXT)
        if not handle:
            return None
        ptr = GlobalLock(handle)
        if not ptr:
            return None
        try:
            return ctypes.wstring_at(ptr)
        finally:
            GlobalUnlock(handle)
    finally:
        CloseClipboard()


def clipboard_set(text: str) -> bool:
    """Write Unicode text to clipboard. Returns True on success."""
    if not OpenClipboard(None):
        return False
    try:
        EmptyClipboard()
        encoded = text.encode("utf-16-le") + b"\x00\x00"
        h = GlobalAlloc(GMEM_MOVEABLE, len(encoded))
        if not h:
            return False
        ptr = GlobalLock(h)
        if not ptr:
            return False
        ctypes.memmove(ptr, encoded, len(encoded))
        GlobalUnlock(h)
        SetClipboardData(CF_UNICODETEXT, h)
        return True
    finally:
        CloseClipboard()

# ── Audio Recorder ───────────────────────────────────────────────────────────

SAMPLE_RATE = 16000  # whisper expects 16 kHz


class AudioRecorder:
    """Records audio from the default input device into a memory buffer."""

    def __init__(self, max_duration: float = 30):
        self._chunks: deque[np.ndarray] = deque()
        self._stream: sd.InputStream | None = None
        self._max_samples = int(max_duration * SAMPLE_RATE) if max_duration else None

    def _callback(self, indata: np.ndarray, frames: int, time_info, status):
        if status:
            print(f"Audio: {status}", file=sys.stderr)
        self._chunks.append(indata[:, 0].copy())

    def start(self):
        self._chunks.clear()
        self._stream = sd.InputStream(
            samplerate=SAMPLE_RATE,
            channels=1,
            dtype="float32",
            callback=self._callback,
        )
        self._stream.start()

    def stop(self) -> np.ndarray:
        """Stop recording and return audio as a float32 numpy array."""
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        if not self._chunks:
            return np.array([], dtype=np.float32)
        audio = np.concatenate(list(self._chunks))
        # Enforce max duration
        if self._max_samples is not None and len(audio) > self._max_samples:
            audio = audio[: self._max_samples]
        return audio

# ── Transcriber ──────────────────────────────────────────────────────────────


class Transcriber:
    """Wraps faster-whisper model loading and transcription."""

    def __init__(self, model_name: str, device: str, language: str | None,
                 initial_prompt: str, max_duration: int = 60):
        self.language = language
        self.initial_prompt = initial_prompt or None
        self.max_duration = max_duration

        # Resolve device
        if device == "auto":
            device, compute_type = self._auto_detect()
        elif device == "cuda":
            compute_type = "float16"
        else:
            device, compute_type = "cpu", "int8"

        print(f"Loading model '{model_name}' on {device} ({compute_type})...")
        t0 = time.perf_counter()
        self.model = WhisperModel(model_name, device=device,
                                  compute_type=compute_type)
        dt = time.perf_counter() - t0
        print(f"Model loaded in {dt:.1f}s")

    @staticmethod
    def _auto_detect() -> tuple[str, str]:
        try:
            import ctranslate2
            if ctranslate2.get_cuda_device_count() > 0:
                print("CUDA detected — using GPU with float16")
                return "cuda", "float16"
        except Exception:
            pass
        print("No CUDA — falling back to CPU with int8")
        return "cpu", "int8"

    def transcribe(self, audio: np.ndarray) -> str:
        segments, info = self.model.transcribe(
            audio,
            beam_size=5,
            temperature=0.0,
            condition_on_previous_text=False,
            language=self.language,
            initial_prompt=self.initial_prompt,
            vad_filter=True,
            chunk_length=self.max_duration if self.max_duration else 30,
        )
        text_parts = [seg.text for seg in segments]
        text = re.sub(r" {2,}", " ", " ".join(text_parts).strip())
        if info.language:
            print(f"  Detected language: {info.language} "
                  f"(prob {info.language_probability:.0%})")
        return text

# ── Overlay Status Indicator ─────────────────────────────────────────────────

OVERLAY_STATES = {
    "loading":      {"bg": "#2c3e50", "text": "Loading...", "width": 180},
    "idle":         {"bg": "#3a3a3a", "text": "Ready    ",      "width": 80},
    "recording":    {"bg": "#c0392b", "text": "Recording...", "width": 180, "bg_dark": "#7a2219"},
    "transcribing": {"bg": "#e67e22", "text": "Transcribing...", "width": 180},
    "done":         {"bg": "#27ae60", "text": "Done!",       "width": 180},
    "cancelled":    {"bg": "#7f8c8d", "text": "Cancelled",   "width": 180},
    "error":        {"bg": "#3a3a3a", "text": "Error", "fg": "#e74c3c", "width": 180},
}


class OverlayIndicator:
    """Small always-on-top floating status bar using tkinter."""

    MAX_WIDTH, HEIGHT = 180, 32
    CORNER_RADIUS = 12

    MARGIN = 16
    TASKBAR_OFFSET = 60

    def __init__(self, position="bottom-center"):
        self._position = position
        self._root: tk.Tk | None = None
        self._canvas: tk.Canvas | None = None
        self._rect_id = None
        self._fill_rect_id = None
        self._fill_color = None
        self._text_id = None
        self._close_id = None
        self._current_width = OVERLAY_STATES["loading"].get("width", self.MAX_WIDTH)
        self._state_gen = 0  # incremented on every state change
        self._ready = threading.Event()
        t = threading.Thread(target=self._run, daemon=True)
        t.start()
        self._ready.wait(timeout=3)

    def _resolve_position(self, screen_w, screen_h, width):
        pos = self._position
        if isinstance(pos, (list, tuple)) and len(pos) == 2:
            return int(pos[0]), int(pos[1])
        positions = {
            "bottom-center": ((screen_w - width) // 2,
                              screen_h - self.HEIGHT - self.TASKBAR_OFFSET),
            "bottom-right":  (screen_w - width - self.MARGIN,
                              screen_h - self.HEIGHT - self.TASKBAR_OFFSET),
            "bottom-left":   (self.MARGIN,
                              screen_h - self.HEIGHT - self.TASKBAR_OFFSET),
            "top-right":     (screen_w - width - self.MARGIN,
                              self.MARGIN),
            "top-left":      (self.MARGIN,
                              self.MARGIN),
        }
        return positions.get(pos, positions["bottom-center"])

    def _run(self):
        root = tk.Tk()
        self._root = root
        root.overrideredirect(True)
        root.attributes("-topmost", True)
        root.attributes("-transparentcolor", "magenta")
        root.config(bg="magenta")

        w = self._current_width
        x, y = self._resolve_position(root.winfo_screenwidth(),
                                      root.winfo_screenheight(), w)
        root.geometry(f"{w}x{self.HEIGHT}+{x}+{y}")

        canvas = tk.Canvas(root, width=w, height=self.HEIGHT,
                           bg="magenta", highlightthickness=0)
        canvas.pack()
        self._canvas = canvas

        self._rect_id = self._rounded_rect(canvas, 0, 0,
                                           w, self.HEIGHT,
                                           self.CORNER_RADIUS, fill="#2c3e50")
        self._text_id = canvas.create_text(
            w // 2, self.HEIGHT // 2,
            text="Loading...", fill="white",
            font=("Segoe UI", 10, "bold"),
        )

        # Drag-to-move bindings
        canvas.bind("<ButtonPress-1>", self._start_drag)
        canvas.bind("<B1-Motion>", self._on_drag)

        # Close button
        self._close_id = canvas.create_text(
            w - 14, self.HEIGHT // 2 - 2,
            text="\u00d7", fill="white",
            font=("Segoe UI", 13, "bold"),
        )
        canvas.tag_bind(self._close_id, "<Button-1>",
                        lambda e: os._exit(0))
        canvas.tag_bind(self._close_id, "<Enter>",
                        lambda e: canvas.itemconfigure(self._close_id,
                                                       fill="#F74545"))
        canvas.tag_bind(self._close_id, "<Leave>",
                        lambda e: canvas.itemconfigure(self._close_id,
                                                       fill="white"))

        self._ready.set()
        root.mainloop()

    def _start_drag(self, event):
        self._drag_x = event.x
        self._drag_y = event.y

    def _on_drag(self, event):
        x = self._root.winfo_x() + event.x - self._drag_x
        y = self._root.winfo_y() + event.y - self._drag_y
        self._root.geometry(f"+{x}+{y}")

    @staticmethod
    def _rounded_rect(canvas, x1, y1, x2, y2, r, **kwargs):
        points = [
            x1+r, y1,   x2-r, y1,   x2, y1,   x2, y1+r,
            x2, y2-r,   x2, y2,     x2-r, y2,  x1+r, y2,
            x1, y2,     x1, y2-r,   x1, y1+r,  x1, y1,
        ]
        return canvas.create_polygon(points, smooth=True, **kwargs)

    def set_state(self, state: str):
        if self._root is None:
            return
        info = OVERLAY_STATES.get(state, OVERLAY_STATES["idle"])
        self._root.after(0, self._apply, state, info)

    def set_progress(self, fraction: float):
        if self._root is None:
            return
        self._root.after(0, self._apply_progress,
                         min(1.0, max(0.0, fraction)))

    def _apply(self, state: str, info: dict):
        if self._canvas is None:
            return
        self._state_gen += 1
        new_width = info.get("width", self.MAX_WIDTH)
        bg_dark = info.get("bg_dark")
        bg_color = bg_dark if bg_dark else info["bg"]

        # Clear any existing fill rect
        if self._fill_rect_id is not None:
            self._canvas.delete(self._fill_rect_id)
            self._fill_rect_id = None

        if new_width != self._current_width:
            cx = self._root.winfo_x() + self._current_width // 2
            cy = self._root.winfo_y()
            nx = cx - new_width // 2
            self._current_width = new_width
            self._canvas.delete(self._rect_id)
            self._rect_id = self._rounded_rect(
                self._canvas, 0, 0, new_width, self.HEIGHT,
                self.CORNER_RADIUS, fill=bg_color)
            self._canvas.tag_raise(self._text_id)
            self._canvas.tag_raise(self._close_id)
            self._canvas.coords(self._text_id, new_width // 2, self.HEIGHT // 2)
            self._canvas.coords(self._close_id, new_width - 14, self.HEIGHT // 2 -2)
            self._canvas.configure(width=new_width)
            self._root.geometry(f"{new_width}x{self.HEIGHT}+{nx}+{cy}")
        else:
            self._canvas.itemconfigure(self._rect_id, fill=bg_color)

        self._fill_color = info["bg"] if bg_dark else None
        self._canvas.itemconfigure(self._text_id, text=info["text"],
                                   fill=info.get("fg", "white"))
        if state == "done":
            gen = self._state_gen
            self._root.after(1000, lambda: self._revert_to_idle(gen))
        elif state in ("error", "cancelled"):
            gen = self._state_gen
            self._root.after(1500, lambda: self._revert_to_idle(gen))

    def _revert_to_idle(self, gen: int):
        if gen == self._state_gen:
            self._apply("idle", OVERLAY_STATES["idle"])

    def _apply_progress(self, fraction: float):
        if self._canvas is None or self._fill_color is None:
            return
        if self._fill_rect_id is not None:
            self._canvas.delete(self._fill_rect_id)
            self._fill_rect_id = None
        min_w = 6
        fill_w = min_w + int((self._current_width - min_w) * fraction)
        self._fill_rect_id = self._rounded_rect(
            self._canvas, 0, 0, fill_w, self.HEIGHT,
            self.CORNER_RADIUS, fill=self._fill_color)
        self._canvas.tag_raise(self._text_id)
        self._canvas.tag_raise(self._close_id)


# ── Dictation App ────────────────────────────────────────────────────────────

MIN_AUDIO_DURATION = 0.1  # seconds — ignore taps shorter than this
TOGGLE_THRESHOLD = 0.4   # seconds — tap shorter than this enters toggle mode


class DictationApp:
    def __init__(self, config: dict):
        self.config = config
        self.overlay = OverlayIndicator(position=config["overlay_position"])
        self.recorder = AudioRecorder(max_duration=config["max_duration"])
        self.transcriber = Transcriber(
            model_name=config["model"],
            device=config["device"],
            language=config["language"],
            initial_prompt=config["initial_prompt"],
            max_duration=config["max_duration"],
        )
        self._recording = False
        self._toggle_mode = False
        self._busy = False
        self._cancelled = False
        self._recording_start = 0.0
        self._lock = threading.Lock()

    def _on_press(self):
        with self._lock:
            if self._busy:
                return
            if self._recording and self._toggle_mode:
                # Second press in toggle mode — stop recording
                self._recording = False
                self._toggle_mode = False
            elif not self._recording:
                # Start recording
                self._recording = True
                self._cancelled = False
                self._toggle_mode = False
            else:
                return
        if self._recording:
            self.overlay.set_state("recording")
            print("Recording...")
            self.recorder.start()
            self._recording_start = time.perf_counter()
            threading.Thread(target=self._recording_progress_loop,
                             daemon=True).start()
        else:
            self._stop_and_transcribe()

    def _recording_progress_loop(self):
        max_dur = self.config["max_duration"]
        while self._recording and not self._cancelled:
            if max_dur:
                elapsed = time.perf_counter() - self._recording_start
                self.overlay.set_progress(min(1.0, elapsed / max_dur))
                if elapsed >= max_dur:
                    break
            else:
                self.overlay.set_progress(1.0)
            time.sleep(0.05)

    def _on_release(self):
        with self._lock:
            if not self._recording:
                return
            elapsed = time.perf_counter() - self._recording_start
            if elapsed < TOGGLE_THRESHOLD:
                # Short tap — enter toggle mode, keep recording
                self._toggle_mode = True
                return
            self._recording = False
            if self._cancelled:
                return
        self._stop_and_transcribe()

    def _stop_and_transcribe(self):
        audio = self.recorder.stop()
        duration = len(audio) / SAMPLE_RATE if len(audio) > 0 else 0
        if duration < MIN_AUDIO_DURATION:
            print(f"Too short ({duration:.2f}s), skipping")
            self.overlay.set_state("idle")
            return
        self.overlay.set_state("transcribing")
        print(f"Captured {duration:.1f}s of audio, transcribing...")
        with self._lock:
            if self._busy:
                print("Already transcribing, skipping")
                return
            self._busy = True
        thread = threading.Thread(target=self._process_audio, args=(audio,),
                                  daemon=True)
        thread.start()

    def _cancel(self):
        with self._lock:
            if not self._recording and not self._busy:
                return
            self._cancelled = True
            was_recording = self._recording
            self._recording = False
            self._toggle_mode = False
        if was_recording:
            self.recorder.stop()
        print("Cancelled.")
        self.overlay.set_state("cancelled")

    def _process_audio(self, audio: np.ndarray):
        try:
            t0 = time.perf_counter()
            text = self.transcriber.transcribe(audio)
            with self._lock:
                if self._cancelled:
                    print("  Transcription cancelled.")
                    self.overlay.set_state("idle")
                    return
            dt = time.perf_counter() - t0
            if not text:
                print("  (no speech detected)")
                self.overlay.set_state("idle")
                return
            print(f"  Transcribed in {dt:.2f}s: {text}")
            self._paste_text(text)
            self.overlay.set_state("done")
        except Exception as e:
            print(f"  Transcription error: {e}", file=sys.stderr)
            self.overlay.set_state("error")
        finally:
            with self._lock:
                self._busy = False

    def _paste_text(self, text: str):
        old_clipboard = None
        if not self.config["keep_in_clipboard"]:
            old_clipboard = clipboard_get()

        clipboard_set(text)
        time.sleep(0.05)
        keyboard.send("ctrl+v")

        if old_clipboard is not None:
            time.sleep(0.15)
            clipboard_set(old_clipboard)

    def run(self):
        hotkey = self.config["hotkey"]
        print(f"Registering hotkey: {hotkey}")

        keyboard.add_hotkey(
            hotkey,
            callback=self._on_press,
            suppress=True,
            trigger_on_release=False,
        )

        # Register release handler for each key in the combo.
        # We detect release by hooking the individual keys and checking
        # if the full combo is no longer held.
        combo_keys = [k.strip() for k in hotkey.split("+")]

        def on_key_release(event):
            if event.event_type == "up" and event.name in combo_keys:
                if self._recording:
                    self._on_release()

        keyboard.hook(on_key_release)

        cancel_key = self.config["cancel_key"]
        if cancel_key:
            keyboard.on_press_key(cancel_key, lambda _: self._cancel(),
                                  suppress=False)

        self.overlay.set_state("idle")
        print("=" * 50)
        print(f"Ready! Hold [{hotkey}] to dictate.")
        if cancel_key:
            print(f"Press [{cancel_key}] to cancel.")
        print("Press Ctrl+C to exit.")
        print("=" * 50)

        try:
            keyboard.wait()
        except KeyboardInterrupt:
            print("\nExiting...")

# ── Entry Point ──────────────────────────────────────────────────────────────


def main():
    if "--debug" in sys.argv:
        print("DEBUG MODE: Press keys to see their names. Ctrl+C to exit.")
        print("-" * 50)
        def on_event(event):
            print(f"  {event.event_type:4s}  name={event.name!r}  scan_code={event.scan_code}")
        keyboard.hook(on_event)
        try:
            keyboard.wait()
        except KeyboardInterrupt:
            print("\nDone.")
        return

    config = load_config()
    app = DictationApp(config)
    app.run()


if __name__ == "__main__":
    main()
