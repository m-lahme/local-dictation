# Lossless and race-safe Windows Clipboard insertion

Research findings for [GitHub issue #13](https://github.com/m-lahme/local-dictation/issues/13), based only on Microsoft documentation. This report separates documented platform facts, engineering conclusions, and product decisions that remain with the owner.

## Executive finding

Windows has the primitives needed for a conservative Clipboard transaction, but it does **not** provide a universal, atomic "snapshot, paste, and restore" operation. A broadly compatible implementation can be lossless only for Clipboard states that it can fully materialize and independently reproduce before changing the Clipboard. It can be race-safe by combining the Clipboard's exclusive-open lock, its sequence number, and an unguessable transaction marker, and by refusing to restore when another producer has replaced the temporary content.

The required V1 boundary should therefore be fail-closed: if every advertised format and storage medium cannot be cloned under a bounded policy, Local Dictation must not change the Clipboard. It should preserve the transcription as a not-inserted History entry and route the user to manual copy/insertion. A prototype still has to validate the candidate allowlist and paste-consumption handshake; this research does not make the remaining product choices listed at the end.

## Documented platform facts

### Clipboard locking, ownership, and change detection

- `OpenClipboard` prevents other applications from modifying the Clipboard until `CloseClipboard`; it fails while another window has it open. Calling `EmptyClipboard` transfers ownership to the window associated with the open Clipboard. [OpenClipboard](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openclipboard), [Clipboard operations](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations)
- `SetClipboardData` transfers ownership of a supplied object to the system. A memory object must use `GMEM_MOVEABLE`, and the producer must neither modify nor free it after a successful call. Passing `NULL` advertises a delayed-rendered format; the owner must then handle `WM_RENDERFORMAT` and `WM_RENDERALLFORMATS`. [SetClipboardData](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclipboarddata)
- With delayed rendering, the owner remains responsible for producing the data while it owns the Clipboard. A request triggers `WM_RENDERFORMAT`; destruction of the owner triggers `WM_RENDERALLFORMATS`, after which unrendered formats cease to be available. [Clipboard operations: delayed rendering](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations), [WM_RENDERALLFORMATS](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-renderallformats)
- `GetClipboardSequenceNumber` returns a per-window-station `DWORD` that increments when the Clipboard changes or is emptied. Delayed rendering is a documented special case: its increment happens when the change is rendered. A return value of zero can instead mean the caller lacks `WINSTA_ACCESSCLIPBOARD`. [GetClipboardSequenceNumber](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber)
- `AddClipboardFormatListener` posts `WM_CLIPBOARDUPDATE` when content changes. This is a notification, not a lock, compare-and-swap operation, or indication that a paste consumer finished reading. [AddClipboardFormatListener](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)

### Format classes and their lifetimes

- The Clipboard can contain multiple ordered representations of the same logical item. `EnumClipboardFormats` enumerates the currently available formats in preference order and also enumerates some system-synthesized conversions. [Clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats), [EnumClipboardFormats](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumclipboardformats)
- Standard formats are not all the same handle type. Text, DIB, HDROP, locale, audio, and many other flat formats are memory objects; `CF_BITMAP`, `CF_PALETTE`, `CF_METAFILEPICT`, and `CF_ENHMETAFILE` use specific GDI/metafile handle types. `CF_OWNERDISPLAY` has no transferable payload: its owner must service drawing and sizing messages. [Standard Clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/standard-clipboard-formats), [About the Clipboard](https://learn.microsoft.com/en-us/windows/win32/dataxchg/about-the-clipboard)
- Registered formats let applications exchange application-defined data by a shared registered name. Private formats in `CF_PRIVATEFIRST..CF_PRIVATELAST` have owner-managed handles that the system does not automatically free. Formats in `CF_GDIOBJFIRST..CF_GDIOBJLAST`, despite the name, use `GlobalAlloc(GMEM_MOVEABLE)` data and are automatically freed by the system. [Clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats)
- The system can synthesize text, bitmap/DIB, palette, and metafile variants on demand. Therefore, an enumerated list can include representations that were not independently supplied by the original producer. [Synthesized Clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#synthesized-clipboard-formats)

### OLE data objects add dimensions that a format ID alone cannot capture

- OLE describes an offered representation with `FORMATETC`: format ID, optional target device, aspect/view, index, and accepted storage medium. File-content transfers can use multiple `lindex` values. [FORMATETC](https://learn.microsoft.com/en-us/windows/win32/api/objidl/ns-objidl-formatetc)
- The storage medium can be `HGLOBAL`, a disk file, `IStream`, `IStorage`, a bitmap, a metafile picture, or an enhanced metafile. `STGMEDIUM.pUnkForRelease` determines whether the receiver owns the medium or merely releases a provider-owned object. [TYMED](https://learn.microsoft.com/en-us/windows/win32/api/objidl/ne-objidl-tymed), [ReleaseStgMedium](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-releasestgmedium)
- `IDataObject::EnumFormatEtc(DATADIR_GET)` enumerates representations that may be requested, but Microsoft explicitly says the list is a hint: formats can change over time, and some OLE 1 objects enumerate only a subset. A `tymed` field may offer multiple media. [IDataObject::EnumFormatEtc](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-idataobject-enumformatetc)
- `IDataObject::GetData` renders one requested `FORMATETC` into one `STGMEDIUM`; the caller must release the result according to its ownership metadata. It can execute application code and fail for a particular format, index, aspect, target device, or medium. [IDataObject::GetData](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-idataobject-getdata)
- `OleGetClipboard` may return an object that forwards calls to the original producer over RPC. Microsoft recommends holding it only briefly. For non-OLE producers, the OLE wrapper can lack accurate medium metadata for application-defined formats even when a `GetData` request would succeed. [OleGetClipboard](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olegetclipboard)
- `OleSetClipboard` keeps the supplied `IDataObject` alive and offers all its formats through delayed rendering. `OleFlushClipboard` materializes the formats it can preserve after the source exits, but its documented flush set excludes `TYMED_FILE`; it converts eligible global-handle and structured media under stated conditions rather than promising an identity clone of every possible data object. [OleSetClipboard](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-olesetclipboard), [OleFlushClipboard](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleflushclipboard)

### Clipboard history and Cloud Clipboard controls

Windows recognizes three registered formats that apply to the whole Clipboard item:

| Registered format | Documented effect |
| --- | --- |
| `ExcludeClipboardContentFromMonitorProcessing` | Any payload prevents inclusion in Clipboard history **and** synchronization to other devices. |
| `CanIncludeInClipboardHistory` | A serialized `DWORD` zero prevents history inclusion; one requests inclusion. It does not control roaming. |
| `CanUploadToCloudClipboard` | A serialized `DWORD` zero prevents roaming; one requests roaming. It does not control local history. |

These controls are documented in [Cloud Clipboard and Clipboard history formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats). The WinRT equivalent is `Clipboard.SetContentWithOptions` with `ClipboardContentOptions.IsAllowedInHistory` and `.IsRoamable`; both option defaults are `true`. [SetContentWithOptions](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard.setcontentwithoptions), [IsAllowedInHistory](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboardcontentoptions.isallowedinhistory), [IsRoamable](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboardcontentoptions.isroamable)

The documented history APIs manipulate history items; they are not a lossless store for arbitrary Win32/OLE formats. Nothing in the cited APIs identifies an earlier history item as a byte-for-byte snapshot suitable for restoring the live Clipboard.

### Paste dispatch is not paste completion

- `WM_PASTE` is defined only for edit controls and combo boxes and has no return value; its documentation describes the `CF_TEXT` path, so it is not a universal insertion protocol. [WM_PASTE](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-paste)
- `SendInput` serially inserts keyboard events into the input stream and reports only how many events were inserted. It is subject to UIPI and can inject only into an equal- or lower-integrity target; UIPI blocking is not positively identified by its return value or `GetLastError`. It also does not reset already-held keys. [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- None of the documented Clipboard listener, `SendInput`, or `WM_PASTE` contracts reports that an arbitrary target has finished retrieving or applying Clipboard data.

## Engineering conclusions from those facts

The following are conclusions and proposed boundaries, not additional claims made by Microsoft.

### 1. A universal identity clone is impossible to promise

An arbitrary OLE object can vary output by target device, aspect, index, medium, and time; its format enumeration is not guaranteed complete or stable. Owner-display content depends on callbacks to the original owner. Private-format handles are intentionally opaque and owner-managed. File media can refer to mutable external paths. A provider-owned stream/storage or delayed renderer can fail, block, exit, or change its result.

Therefore, retaining an `IDataObject` pointer is not an independent snapshot, copying only text loses data, and enumerating format IDs plus blindly copying handles is not safe. "All relevant formats" must mean **every representation admitted by an explicit, tested clone policy**; any representation outside that policy makes the entire Clipboard state unsafe for temporary replacement.

### 2. Candidate clone-policy boundary for a prototype

The prototype should admit a Clipboard state only when all of these checks succeed before `EmptyClipboard` is ever called:

1. Open and exclusively lock the Clipboard with a real owner window.
2. Enumerate the stable format set and materialize delayed representations while the original owner still exists. Re-enumerate after materialization because rendering and synthesis can affect the available set and sequence number.
3. Deep-copy each admitted payload into Local Dictation-owned storage:
   - flat `HGLOBAL` formats, preserving exact bytes, registered format names, and original order;
   - standard bitmap, palette, metafile-picture, and enhanced-metafile formats only through their type-specific duplication paths;
   - OLE `IStream`/`IStorage` representations only if they can be fully copied into independent storage for every admitted `FORMATETC` and index.
4. Reject `CF_OWNERDISPLAY`, `CF_PRIVATEFIRST..CF_PRIVATELAST`, `TYMED_FILE`, unknown handle kinds, target-device-dependent representations, indeterminate index ranges, provider-owned/lazy media that cannot be made independent, and any enumeration/render/copy error or time-budget breach.
5. Record the final sequence number and a format/order fingerprint only after materialization has stabilized. Close the Clipboard, then reacquire it immediately before replacement and verify both again while the exclusive lock is held. A mismatch means another producer won; discard the snapshot and retry or fail without mutation.

The allowlist is deliberately a prototype hypothesis. In particular, virtual-file groups (`CFSTR_FILEDESCRIPTOR` plus indexed `CFSTR_FILECONTENTS`), embedded OLE objects, palette-dependent graphics, zero-length custom payloads, and provider-owned media must stay rejected until tests demonstrate an independent, semantically equivalent clone and restore.

### 3. Proposed race-safe transaction

Use a single STA transaction component with a hidden message window and a per-transaction cryptographically random marker format/payload.

1. **Prepare without mutation.** Build the independent snapshot under the allowlist above. If anything is unsafe, busy beyond a bounded retry budget, or changes during preparation, do not touch the Clipboard.
2. **Commit temporary content under the lock.** Reopen the Clipboard; while it is exclusively locked, verify the snapshot sequence and fingerprint, then empty it and publish:
   - transcription as `CF_UNICODETEXT` (and only additionally required, tested text formats), preferably delayed so the component can observe the target's request;
   - the random transaction marker;
   - `ExcludeClipboardContentFromMonitorProcessing` so the temporary text is neither added to Win+V history nor roamed.
   Record every sequence-number change caused by the component, including delayed rendering, before releasing the lock.
3. **Request paste.** After verifying the intended insertion target still has focus, send the target's normal paste chord. `SendInput` success proves only input injection, not paste success. Treat an observed request for the delayed transcription format as a consumption signal, not proof that the target applied the text.
4. **Restore only if still owner of the transaction.** Reopen the Clipboard and, while holding the lock, require both the exact random marker and the last sequence number attributable to this transaction. If either differs, close without writing: a user/application copy is newer and must never be overwritten. If both match, replace the temporary item with independently cloned original formats in original preference order.
5. **Suppress monitoring of the restore.** Publish the restoration with the documented exclusion format so the act of restoring does not add a duplicate Win+V/cloud item. This necessarily leaves that additional control format on the live restored item; the prototype must test its visibility and compatibility, and the owner must accept or reject that tradeoff.
6. **Finalize status.** Mark insertion successful only after the chosen consumption criterion. On any pre-commit failure, record a not-inserted History entry. After commit, if a concurrent producer wins, preserve that newer Clipboard and never restore over it; History must accurately represent that insertion/restoration outcome.

The sequence number alone is insufficient because delayed rendering legitimately changes it and a `DWORD` can eventually wrap. The marker alone is insufficient because a third party could copy the marker along with other formats. Requiring both, while inspecting and restoring under `OpenClipboard`, makes the compare-and-restore decision race-safe with respect to other well-behaved Clipboard writers.

### 4. Safe fallback boundary

Local Dictation must skip automatic insertion before the first Clipboard mutation when any of the following is true:

- the format set includes any non-allowlisted or ambiguously typed representation;
- any delayed representation cannot be rendered, copied, bounded, or independently owned;
- format enumeration, order, or sequence fails to stabilize;
- the Clipboard cannot be acquired within the retry/time budget;
- the insertion target is missing, has changed unexpectedly, or is at a higher integrity level;
- the implementation cannot install both history/cloud exclusion and the transaction marker.

After temporary content is committed:

- if the marker/sequence check shows a newer copy, never restore; preserve the newer Clipboard;
- if paste consumption is not observed by the timeout and the marker/sequence still match, restore the snapshot and treat insertion as unconfirmed/not inserted;
- if restoration itself fails while the marker still matches, preserve the transcription and snapshot in application-owned recovery state and show an actionable History entry; do not loop destructively.

## Rejected alternatives

| Alternative | Why it does not meet the contract |
| --- | --- |
| Save and restore only Unicode text | Destroys every non-text representation and formatting format. |
| Keep the original `IDataObject` pointer and put it back | It can forward to the original producer over RPC, is recommended only for brief retention, and is not an independent immutable snapshot. |
| Enumerate IDs and duplicate every value as `HGLOBAL` | Standard, private, GDI, metafile, OLE, and owner-display formats have different handle and lifetime contracts. |
| Use Win+V history as the backup | History is filtered, optional, and not specified as a lossless store of all native/OLE formats. Restoring a history item also changes current Clipboard content rather than providing compare-and-swap. |
| Restore after a fixed sleep | Scheduling time is not evidence that the target read or applied the Clipboard. |
| Restore whenever the sequence number still equals the value after `SetClipboardData` | Delayed rendering itself can increment the sequence number; attribution and the transaction marker are required. |
| Hold `OpenClipboard` while sending paste | The target cannot open/read the Clipboard while Local Dictation holds the exclusive lock. |
| Send `WM_PASTE` to every target | It is a narrow edit/combo-control message, not a universal application paste contract. |
| Treat `SendInput` success as insertion success | It reports queued input events, is constrained by UIPI, and does not acknowledge Clipboard consumption or text insertion. |
| Delete the temporary item from Clipboard history afterward | The requirement is to avoid creating/altering history, and deletion cannot undo prior cloud synchronization or observers that already processed it. |

## Prototype and test matrix

Each case must assert (a) inserted text, (b) exact admitted-format bytes/semantics and preference order after restore, (c) no overwrite of a newer copy, (d) no new/deleted/reordered Win+V item, (e) no Cloud Clipboard propagation for temporary/restore updates, and (f) correct History status and recovery UI.

| Axis | Required cases |
| --- | --- |
| Flat formats | Empty Clipboard; `CF_UNICODETEXT`; ANSI/OEM plus synthesized Unicode; HTML; RTF; CSV; zero-length and large registered `HGLOBAL`; `CF_HDROP`; locale and audio formats. |
| Graphics | `CF_BITMAP`; `CF_DIB`; `CF_DIBV5`; `CF_PALETTE` combinations; `CF_METAFILEPICT`; `CF_ENHMETAFILE`; display variants; synthesized conversions. |
| OLE/Shell | Embedded `IStorage`; `IStream`; multiple supported media for one format; provider-owned `pUnkForRelease`; virtual files with multiple `lindex` values; application-defined formats. |
| Mandatory rejection | `CF_OWNERDISPLAY`; private-format range; `TYMED_FILE`; unknown handle; target-device-specific representation; incomplete enumeration; failed deep copy. |
| Delayed rendering | Fast owner; slow owner; hung owner; owner exits; rendering changes sequence; rendering adds/synthesizes a format; `WM_RENDERALLFORMATS` path. |
| Contention | Clipboard busy at snapshot/commit/restore; user or application copies before commit, during paste, after text render, and immediately before restore; repeated transactions; sequence changes generated only by this transaction. |
| Paste targets | Classic edit control; WinUI/UWP field; browser contenteditable; Office-style rich editor; terminal; application with asynchronous paste; target that never requests data; focus change; elevated target. |
| History/cloud | History off/on; cloud off/on; both on; pre-existing duplicate item; pre-existing exclusion formats; second device online/offline; verify temporary and restoration writes do not create or modify history entries. |
| Failure/recovery | `SendInput` partial/zero return; UIPI block; target closes; timeout before consumption; restore allocation/set failure; Local Dictation exits during each phase. |
| System context | Windows 11 supported builds; multiple desktops/window stations; RDP connect/disconnect; clipboard redirection enabled/disabled; high-frequency clipboard manager installed. |

Automated fixtures should include first-party test owners/consumers implementing each handle type, delayed-rendering message, `IDataObject` medium, failure, and race schedule. Real-application tests are still required because paste timing and accepted formats are target-specific. Run destructive/history/cloud cases in isolated test accounts and devices.

## Remaining risks

- A delayed renderer or COM provider can execute foreign code paths and hang; the safe timeout/cancellation architecture needs a prototype because forcibly abandoning an STA/RPC call has lifecycle risks.
- A data object can be stateful or target-dependent even when its bytes can be materialized once; semantic identity cannot be inferred from byte equality alone.
- Observing a request for transcription data does not prove that an arbitrary target committed it to its document.
- The history/cloud exclusion behavior of the restore operation, especially the extra registered control format left on the current item, requires Windows 11 black-box validation.
- Third-party clipboard managers and RDP redirection are observers/producers outside the base API contract and can introduce additional changes or materialization requests.
- Input injection cannot cross upward integrity boundaries under UIPI; an elevated helper would widen the security model and is not implied by this report.

## Open product decisions — owner input required

This research deliberately does not decide:

1. Whether V1 accepts the conservative allowlist (and therefore sometimes skips insertion) or relaxes the original "preserve all formats" requirement.
2. Whether leaving `ExcludeClipboardContentFromMonitorProcessing` on the restored current item is acceptable in exchange for not duplicating it in history/cloud.
3. What bounded wait/retry budgets apply to opening, materializing, paste consumption, and restoration.
4. Which paste-consumption criterion is sufficient for History to say "inserted": first text request, target-specific acknowledgement where available, or a more conservative unconfirmed state.
5. Whether an unconfirmed paste needs a new History status distinct from `not inserted`.
6. Whether higher-integrity targets should always fail safely or whether a separately designed elevated component is in scope.
7. The initial admitted application/format compatibility matrix for V1 and which rejected states should offer a one-click manual copy action.

## Proposed decision pointer

Windows supports a race-safe, history-suppressed Clipboard transaction only behind a conservative deep-clone allowlist: snapshot every admitted representation before mutation, tag temporary content, restore only when marker and sequence still match, and fail without touching the Clipboard for any unsupported or unstable state. Universal lossless cloning and universal paste completion are not platform guarantees, so the remaining compatibility and timeout tradeoffs require owner decisions plus a prototype.
