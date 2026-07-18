# Static UI sketches

Small, non-interactive HTML sketches. Open `index.html` or any variant directly in a browser; no server or build step is required.

- `fluent.html`
- `contrast.html`
- `linear.html`
- `graphite.html`

## Overlay interaction prototype

`overlay-prototype.html?variant=A` is a throwaway interactive prototype for GitHub issue #6. Run it from this directory with `python -m http.server 4176`, then open `http://127.0.0.1:4176/overlay-prototype.html?variant=A`.

The selected baseline is a 64 × 28 DIP visible pill with state colour encoded by its bars. Its success mark uses four equal 7 DIP vertical segments with final offsets `+1 / +6 / 0 / −6` DIP; the in-page editor can still move them in 1 DIP steps. The other variants retain the rejected five- and six-segment constructions as primary-source context. The prototype also simulates smoothed microphone levels with a noise gate, all visible states, click-versus-drag, a localized application menu including Overlay visibility, configured home anchors, monitor fallback, reduced motion, high contrast, and screen-reader announcements. The production Overlay may be hidden while idle; a Dictation temporarily reveals it through capture, transcription, and final feedback, then restores the prior hidden state after returning to idle.
