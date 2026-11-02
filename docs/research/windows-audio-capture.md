# Robust low-latency Windows audio capture

Research findings for [Research robust low-latency Windows audio capture](https://github.com/m-lahme/local-dictation/issues/11). Scope: the Windows 11 V1 capture path, warm capture readiness, observable sample health, and recovery from endpoint churn. Sources are Microsoft documentation and Microsoft-owned samples only.

## Recommendation

Use **direct WASAPI in shared, event-driven mode**, with `IAudioClient3` where available, plus MMDevice and audio-session notifications:

1. At application startup, resolve the selected capture endpoint, activate and initialize `IAudioClient3`, register an event handle, obtain `IAudioCaptureClient`, `IAudioClock2`, and `IAudioSessionControl`, and register both `IMMNotificationClient` and `IAudioSessionEvents`. Leave the stream stopped.
2. On Dictation start, call `IAudioClient::Start`; an MMCSS-registered capture worker waits on the buffer event and drains every available packet with `GetBuffer`/`ReleaseBuffer`.
3. Treat advancing captured-frame/device positions as the evidence for Audio input availability. Endpoint/session callbacks and HRESULTs are early fault signals, not substitutes for sample-flow evidence.
4. On invalidation, stop using the old graph, release all endpoint-bound interfaces, resolve the configured endpoint policy again, and construct a new graph. Never try to reinitialize the old `IAudioClient` instance.

This path fits the product requirement better than the higher-level alternatives because Microsoft documents WASAPI as the option when an application needs more control or lower latency than AudioGraph. Shared mode preserves interoperability, while event-driven buffering avoids a polling timer and `IAudioClient3` exposes the device-supported shared-mode periods. [Low Latency Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio), [`IAudioClient3`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient3), [`IAudioClient::SetEventHandle`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-seteventhandle)

The recommendation deliberately does **not** choose the recovery grace duration, default-device policy, audio period, signal-processing mode, or microphone-indicator UX. Those are product or prototype decisions listed below.

## Established facts

### Warm readiness and privacy boundary

- `Initialize` creates an audio stream; `Start` starts it. With event-driven buffering, `SetEventHandle` must be called after initialization and before `Start`, and Windows signals that handle whenever a buffer is ready. Therefore an initialized, event-bound, stopped graph is the documented seam for warming endpoint/COM setup without starting sample delivery. [`IAudioClient`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient), [`IAudioClient::Start`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-start), [`IAudioClient::SetEventHandle`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-seteventhandle)
- Audio-session disconnect notifications remain available even when a stream is not running. They can reveal that a warmed graph cannot be started, but a stopped stream cannot demonstrate current sample delivery. [Recovering from an Invalid-Device Error](https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error)
- Windows 11 shows a microphone icon while an app is using the microphone. Unpackaged desktop apps are controlled as a group by **Let desktop apps access your microphone**, not by an individual per-app toggle. The documentation does not define whether merely initializing a stopped WASAPI stream counts as “using” the microphone, so indicator behavior must be tested rather than asserted. [Windows camera, microphone, and privacy](https://support.microsoft.com/en-us/windows/privacy/windows-camera-microphone-and-privacy)
- `AUDCLNT_STREAMFLAGS_NOPERSIST` concerns persisted render-session volume/mute state; capture-session settings are never persisted. It does not promise that captured audio is not retained. Non-retention is an application invariant: do not call `Start` before a Dictation, and keep copied samples only in the active Dictation's bounded in-memory buffer. [`IAudioClient::Initialize`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize)

### Latency and buffering

- On Windows 10 and later, `IAudioClient3` can report supported shared-mode engine periods and initialize a shared stream with a chosen period. Microsoft states that the shared-mode default is 10 ms and that updated drivers may support periods below 10 ms. Hardware and driver support determine the periods actually available. [Low Latency Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio), [`IAudioClient3::GetSharedModeEnginePeriod`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient3-getsharedmodeengineperiod), [`IAudioClient3::InitializeSharedAudioStream`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient3-initializesharedaudiostream)
- For event-driven capture, the audio engine signals the registered event when a buffer becomes ready. The processing thread should then drain all available packets. Holding a packet beyond one processing period risks sample loss. [`IAudioClient::SetEventHandle`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-seteventhandle), [`IAudioCaptureClient::GetBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)
- Microsoft documents MMCSS as the mechanism for giving time-sensitive multimedia work prioritized CPU access without starving lower-priority work. A capture worker can register itself with the `Audio` task via `AvSetMmThreadCharacteristics`; priority changes should use MMCSS rather than ad-hoc real-time thread priority. [Multimedia Class Scheduler Service](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service), [`AvSetMmThreadCharacteristics`](https://learn.microsoft.com/en-us/windows/win32/api/avrt/nf-avrt-avsetmmthreadcharacteristicsw)

### Sample-flow health signals

- A successful `GetBuffer` returns the complete next packet, its frame count, device position, and a QPC-correlated capture timestamp. `AUDCLNT_S_BUFFER_EMPTY` means no packet is currently available; it does not by itself prove failure. [`IAudioCaptureClient::GetBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)
- `AUDCLNT_BUFFERFLAGS_SILENT` means the packet represents silence and its data bytes must be ignored. It is still packet delivery and must not be classified as device failure. `DATA_DISCONTINUITY` marks a break/glitch relative to the previous device position, and `TIMESTAMP_ERROR` marks an uncertain packet timestamp. [`AUDCLNT_BUFFERFLAGS`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/ne-audioclient-_audclnt_bufferflags)
- `IAudioClock2::GetDevicePosition` reports the capture device's position in frames and returns `AUDCLNT_S_POSITION_STALLED` before `Start`; after `Start`, a position that continues to advance is an independent health signal. [`IAudioClock2::GetDevicePosition`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock2-getdeviceposition)
- Consequently, the runtime health record should track event wakeups, packet count, total frames, last packet QPC, last advancing device position, buffer flags, and HRESULTs. A watchdog may infer stalled delivery only after a product-selected multiple of the negotiated engine period; amplitude must not be used because valid speech capture can be acoustically silent.

### Device and session events

- `IMMNotificationClient` reports endpoint addition/removal, state changes, property changes, and default-role changes. `OnDeviceStateChanged` distinguishes `ACTIVE`, `DISABLED`, `NOTPRESENT`, and `UNPLUGGED`; `OnDefaultDeviceChanged` is scoped by capture/render flow and console/multimedia/communications role. [`IMMNotificationClient`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient), [`OnDeviceStateChanged`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immnotificationclient-ondevicestatechanged), [`OnDefaultDeviceChanged`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immnotificationclient-ondefaultdevicechanged)
- `IAudioSessionEvents::OnSessionDisconnected` distinguishes device removal, audio-server shutdown, format change, user logoff, Windows Terminal Services session disconnect, and exclusive-mode override. Microsoft explicitly identifies WTS disconnect as an invalidation case, so Remote Desktop recovery belongs in the generic rebuild path rather than a separate RDP audio implementation. [Audio Session Events](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-session-events), [Recovering from an Invalid-Device Error](https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error)
- An unexpected audio-service failure might produce no session-disconnect callback; a later audio API call can instead return `AUDCLNT_E_SERVICE_NOT_RUNNING`. Recovery therefore needs both push notifications and observed call failures. [`IAudioSessionEvents::OnSessionDisconnected`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionevents-onsessiondisconnected), [Recovering from an Invalid-Device Error](https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error)
- Direct WASAPI routing must observe both MMDevice notifications and session-disconnect notifications. Their arrival order is asynchronous and cannot be assumed. A default-device change should not cause a blind rebuild on the first callback; serialize the events and reconcile current endpoint state/policy once. [Relevant Notifications for Stream Routing](https://learn.microsoft.com/en-us/windows/win32/coreaudio/relevant-device-notifications-for-stream-routing)
- Capture operations can return `AUDCLNT_E_DEVICE_INVALIDATED` for unplug/reconfiguration/removal, `AUDCLNT_E_RESOURCES_INVALIDATED` for invalidated stream resources, and `AUDCLNT_E_SERVICE_NOT_RUNNING` when Windows Audio is unavailable. Repeated exclusive-mode buffer errors require stop/reset/release and a new processing loop, but exclusive mode is not recommended here. [`IAudioCaptureClient::GetBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)

### Recovery and ownership constraints

- Microsoft's recovery sequence is to release the old endpoint-bound interfaces, resolve the default or specifically selected endpoint again, and activate a new `IAudioClient`. An `IAudioClient` instance can be initialized only once; failed initialization can also leave it unusable for a retry. [Recovering from an Invalid-Device Error](https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error), [`IAudioClient::Initialize`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize)
- The thread using `IAudioClient` must initialize COM. Microsoft additionally documents that first access to `IAudioClient` should be on an STA thread. `GetBuffer` and its matching `ReleaseBuffer` must execute on the same thread, and service interfaces obtained by `GetService` must be released on the same thread that releases the `IAudioClient`. [`IAudioClient`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient), [`IAudioCaptureClient::GetBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), [`IAudioClient::GetService`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-getservice)
- MMDevice callbacks must be nonblocking, must not wait on synchronization objects, must not register/unregister notification callbacks recursively, and must not release the last MMDevice reference in the callback. The safe architecture is for callbacks to copy the opaque endpoint ID/reason into a lock-free or short-held queue and let the owning audio-control thread rebuild. [`IMMNotificationClient`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)
- Audio-session callbacks have the same architectural consequence: Microsoft requires `IAudioSessionEvents` methods to be nonblocking and warns against unregistering during a callback or releasing the final session-manager reference there. They should enqueue a reason, not rebuild inline. [`IAudioSessionEvents`](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionevents)

## Derived capture state model

The following is a design conclusion from the facts above, not a Windows guarantee:

- **Unprepared**: no endpoint graph.
- **Capture ready**: graph initialized, event and notifications registered, stream stopped, no application audio buffer allocated for samples.
- **Capturing**: stream started; event-driven worker drains packets and advances the sample-health record.
- **Recovering**: a callback, HRESULT, or watchdog requests one serialized rebuild. Previously copied Dictation samples remain valid; no more samples are accepted from the invalid graph.
- **Unavailable**: rebuild attempts have not restored advancing sample delivery. The active Dictation follows the already-planned incomplete-result behavior after the owner-selected recovery grace expires.

Endpoint callbacks, the capture worker, and hotkey commands should all post intents to one audio-control state machine. A generation number on the graph lets late callbacks from an old graph be ignored. Recovery should be idempotent and coalesce a burst of USB, Bluetooth, dock, default-device, property, and session events into one reconciliation pass.

## Rejected alternatives

- **Exclusive-mode WASAPI as the default**: rejected. It can reduce latency, but it takes exclusive endpoint control, can interfere with other apps, may be disabled by the user, and can be preempted or fail depending on device policy. Those tradeoffs conflict with a background dictation utility; shared `IAudioClient3` already supports sub-10-ms periods on capable drivers. [Exclusive-Mode Streams](https://learn.microsoft.com/en-us/windows/win32/coreaudio/exclusive-mode-streams), [Low Latency Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- **AudioGraph for the core capture path**: rejected for V1's robustness prototype. Microsoft generally favors AudioGraph for new development, but documents one extra capture buffer of latency and no option to disable capture effects; WASAPI is the documented choice when more control or lower latency is required. AudioGraph remains a reasonable fallback only if implementation cost later outweighs the control requirements. [Low Latency Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- **Legacy `waveIn`, DirectSound, or an abstraction that hides endpoint/session events**: not selected. Higher-level Windows APIs can implement stream routing automatically, but this ticket explicitly requires observable endpoint invalidation, packet health, and controlled recovery. Direct WASAPI exposes those contracts. This does not rule out a thin, auditable wrapper over WASAPI. [Relevant Notifications for Stream Routing](https://learn.microsoft.com/en-us/windows/win32/coreaudio/relevant-device-notifications-for-stream-routing), [About the Windows Core Audio APIs](https://learn.microsoft.com/en-us/windows/win32/coreaudio/about-the-windows-core-audio-apis)

## Reproducible recovery test matrix

Instrument every run with endpoint ID and role, graph generation, negotiated format/period, command QPC, `Start` return QPC, first-packet QPC/device position, every notification/reason, every non-success HRESULT, discontinuity flags, rebuild attempts, first healthy packet after rebuild, and preserved frame ranges. Run each scenario both from **Capture ready** and, where applicable, mid-Dictation. Repeat on an internal microphone, a USB microphone or dock, a Bluetooth headset, and an RDP redirected microphone when available.

| Scenario | Reproduction | Required evidence and pass condition |
| --- | --- | --- |
| Warm start | Leave graph stopped for 1, 10, and 60 minutes; start 100 short Dictations | No pre-Dictation packets retained; every successful `Start` reaches a first advancing packet; report p50/p95/p99 command-to-first-packet latency and failures. |
| Default endpoint change | Switch the default **capture/multimedia** endpoint in Settings while ready and while capturing | Relevant MMDevice notification is logged; behavior follows the configured selected-device policy; any rebuild is single/coalesced; new graph yields advancing packets. |
| USB/dock removal and return | Unplug microphone or dock mid-Dictation, wait, reconnect to same and then another port | State/session event or invalidation HRESULT is logged; no calls continue against the retired generation; pre-failure frames are preserved; recovery or incomplete-result transition is deterministic. |
| Endpoint disable/enable | Disable and re-enable the selected microphone in Windows Sound settings | `DISABLED`/`ACTIVE` or equivalent removal/addition sequence is logged; complete graph recreation restores packet flow. |
| Format change | Change the endpoint's default format in the classic Sound control panel | `DisconnectReasonFormatChanged` or invalidation is logged; rebuilt graph renegotiates format and produces packets without reusing the old client. |
| Windows Audio restart | Restart the Windows Audio service from an elevated test session | Server-shutdown/service-not-running signal is logged; retries are bounded; capture becomes ready again after service recovery. |
| Bluetooth churn | Start capture, power headset off/on; repeat while changing its active Windows audio profile | Event burst is coalesced; stale endpoint generation is retired; restored endpoint produces advancing packets or the selected-device policy yields a clear unavailable state. |
| RDP disconnect/reconnect | Capture through a redirected microphone; disconnect the RDP/WTS session without logging off, then reconnect | WTS/session-disconnect or device invalidation is logged; generic rebuild path reacquires the post-reconnect endpoint; no RDP-specific bypass is required. |
| Exclusive-mode preemption | Have a test application request exclusive control of the selected microphone | Exclusive override/invalidation is logged; Local Dictation does not fight in a tight reopen loop; it recovers after release or becomes unavailable. |
| Privacy access revoked | Turn off **Let desktop apps access your microphone** before start and, if Windows permits, during capture | Failure HRESULT/event and user-visible unavailable state are recorded; no crash or infinite retry; reenabling access permits a clean rebuild. |
| Silent input | Start with a muted microphone and then an acoustically quiet room | `SILENT` packets and advancing positions count as healthy delivery; amplitude never triggers device recovery. |
| Packet stall/glitch | Under CPU/I/O stress and device churn, delay the capture worker deliberately by controlled multiples of the engine period | `DATA_DISCONTINUITY`, timestamp errors, missed wakeups, or stalled position are recorded; watchdog threshold and false positives are measurable; no invalid memory or cross-generation append occurs. |
| Sleep/resume | Enter connected standby or sleep while ready and while capturing, then resume | Resource/device invalidation is serialized; rebuilt graph returns to stopped capture readiness; active Dictation preserves only confirmed pre-suspend frames. |

The matrix is reproducible at the event/state level. Numeric acceptance thresholds for latency, recovery time, and false-positive stalls must be set by the follow-up prototype against representative owner hardware; this research does not invent them.

## Remaining risks

- Driver behavior varies. A supported minimum period does not guarantee glitch-free operation under load, and Bluetooth endpoint/profile transitions can surface different event sequences across hardware.
- Notifications are asynchronous and can arrive in different orders. Tests must assert final state and generation ownership, not one exact callback order.
- A stopped warm graph proves configuration and receives session invalidation, but cannot prove current sample flow. The first packet after `Start` remains the earliest definitive availability check unless the product explicitly permits an idle probe.
- Windows documentation does not specify the taskbar microphone indicator for an initialized-but-stopped WASAPI stream. This must be observed on supported Windows 11 builds and packaging modes.
- Packaging changes the permission surface: a packaged app declares capabilities, while an unpackaged desktop app is governed by the desktop-app microphone control. Activation/start denial and revocation must be tested in the chosen packaging model. [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations), [Windows camera, microphone, and privacy](https://support.microsoft.com/en-us/windows/privacy/windows-camera-microphone-and-privacy)
- RDP redirection availability is controlled by client, host, and policy configuration. The generic WASAPI recovery path is documented, but representative end-to-end RDP validation remains necessary.
- Samples buffered around normal `Stop` and around a recovery boundary can affect the final word of a Dictation. Windows documents `Stop` and `Reset` separately, but the application's tail-drain and discontinuity policy needs measurement. [`IAudioClient::Stop`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-stop), [`IAudioClient::Reset`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-reset)

## Open product and prototype decisions

These remain for the owner or a follow-up prototype; none is decided by this report:

1. When a specifically selected microphone disappears, should Local Dictation wait only for that endpoint, temporarily use the current default, or require a user choice?
2. What recovery grace applies during an active Dictation, and may multiple short interruptions consume one cumulative budget?
3. Which supported shared-mode period balances command-to-first-packet latency, energy, and glitch rate on the owner's hardware?
4. Should capture effects remain enabled, or should the app request raw processing where supported? This affects device compatibility and transcription quality and requires listening/accuracy tests.
5. Is an explicit, short idle health probe acceptable? It would strengthen Audio input availability before a Dictation but would briefly start microphone use and must not retain samples.
6. What p95/p99 start-latency, recovery-time, and watchdog false-positive thresholds define acceptance?
7. What exact UI explains microphone privacy denial, selected-device loss, and recovery without implying that a merely initialized stream is healthy?
8. What is the normal-stop tail-drain policy, and how are samples on both sides of a recovered interruption joined and marked?
9. Will V1 be packaged or unpackaged for the purposes of microphone capability declaration and privacy diagnostics?

## Primary source index

- Microsoft, [Low Latency Audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- Microsoft, [Capturing a Stream](https://learn.microsoft.com/en-us/windows/win32/coreaudio/capturing-a-stream)
- Microsoft, [Recovering from an Invalid-Device Error](https://learn.microsoft.com/en-us/windows/win32/coreaudio/recovering-from-an-invalid-device-error)
- Microsoft, [Relevant Notifications for Stream Routing](https://learn.microsoft.com/en-us/windows/win32/coreaudio/relevant-device-notifications-for-stream-routing)
- Microsoft, [`IMMNotificationClient`](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)
- Microsoft, [`IAudioCaptureClient::GetBuffer`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer)
- Microsoft, [`IAudioClock2::GetDevicePosition`](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock2-getdeviceposition)
- Microsoft, [Windows camera, microphone, and privacy](https://support.microsoft.com/en-us/windows/privacy/windows-camera-microphone-and-privacy)
