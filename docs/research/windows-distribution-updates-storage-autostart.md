# Windows distribution, updates, storage, and autostart

Research for [GitHub issue #16](https://github.com/m-lahme/local-dictation/issues/16). Sources were checked on 2026-07-17. This note reports platform facts separately from shortlist conclusions and leaves product choices to the owner.

## Facts

### Packaging comparison

| Concern | Full MSIX | Package with external location (sparse package) | Unpackaged |
| --- | --- | --- | --- |
| Package identity | Yes. Identity enables package-only Windows extension points. | Yes. A small signed identity package points at binaries installed elsewhere. Supported from Windows 10 2004 (build 19041). | No. Identity-gated APIs and manifest extensions are unavailable. |
| Installer and payload | MSIX is the installer; installed payload is read-only under `WindowsApps`. A WinUI 3 desktop app normally runs full trust at medium integrity, not in AppContainer. | The existing MSI/EXE installer owns the external payload and registers/unregisters the identity package. | MSI/EXE, ClickOnce, xcopy, or another installer owns everything. |
| Updates | Store or MSIX/App Installer servicing. Package updates are atomic and block-differential. | The existing installer/updater remains responsible for both external payload and identity-package changes. | The application or installer owns updates. |
| Microsoft Store | Direct MSIX submission. | The sparse package itself is not the Store submission; the underlying MSI/EXE can use the Store's unpackaged-app path. | MSI/EXE submission is supported for non-game PC apps if it meets Store requirements. |
| Cleanup | Windows tracks package state and provides the strongest clean-uninstall baseline. | The installer owns cleanup of the external payload and identity-package registration. | The installer owns all cleanup. |

Sources: Microsoft's current [packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [manual external-location packaging guide](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps), [MSIX containerization overview](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview), and [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview).

Full MSIX does not imply an AppContainer for this product. A packaged classic/WinUI 3 desktop process using `runFullTrust`, `packagedClassicApp`, and `mediumIL` has the permissions of a standard desktop app while retaining package identity and some package virtualization. The package install directory remains immutable. [Microsoft's manifest guidance](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion) and [containerization overview](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview) document this distinction.

External-location packaging is not a lighter MSIX installer. It is an identity layer around an independently installed application. The installer must install the external files, build/sign and register the identity package, keep manifest identity metadata aligned with the executable, and unregister it during uninstall. The identity package itself must be trusted on the target computer. [Microsoft's external-location guide](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps) describes those responsibilities.

### Viable distribution and update channels

1. **Microsoft Store with full MSIX.** The Store signs and hosts the package, provides acquisition and automatic package updates, and supports differential delivery. This is the lowest-maintenance public channel for a new packaged app. See [Choose a distribution path](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path) and [MSIX signing guidance](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
2. **Direct full-MSIX distribution with an `.appinstaller` file.** App Installer can check on launch, check silently in the background, prompt, or block activation until a required update is applied. The package and hosting remain the publisher's responsibility. The `ms-appinstaller:` URI protocol has been disabled by default since December 2023; broad distribution should link directly to the `.appinstaller` file for download. See [App Installer overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview), [update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings), and [auto-update/repair](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview).
3. **MSI/EXE plus a product-owned updater.** This is the servicing model for unpackaged and external-location installs. If an MSI/EXE is listed in the Store, the Store makes the current installer available to new customers but does **not** update existing installations; an in-app/installer updater must do that. See [Store MSI/EXE update behavior](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/publish-update-to-your-app-on-store).
4. **WinGet as an additional pull channel.** WinGet can discover, install, upgrade, uninstall, and repair supported installer types. Each release needs a validated manifest and immutable HTTPS installer URL/hash submitted to the public repository. It is a useful discoverability and user/IT-driven upgrade channel, not evidence of an unattended in-app updater. See [WinGet commands](https://learn.microsoft.com/en-us/windows/package-manager/winget/) and [repository submission/validation](https://learn.microsoft.com/en-us/windows/package-manager/package/repository).
5. **GitHub Releases as artifact hosting.** Releases can publish versioned binary assets and release notes and expose a Releases API. GitHub Releases by itself is neither an installation transaction nor an update policy; it can host artifacts consumed by App Installer, WinGet, or a product-owned updater. See [GitHub's release documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

The Windows App SDK runtime also needs a deployment choice. Framework-dependent full-MSIX apps receive framework dependencies through package deployment; framework-dependent external-location and unpackaged apps must deploy/bootstrap the runtime using the Windows App SDK installer or dynamic dependencies. A self-contained deployment increases application size but removes that external runtime prerequisite. See Microsoft's [Windows App SDK deployment overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview) and [external-location/unpackaged deployment guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps).

### Signing, trust, and current costs

- Every MSIX must be signed. For direct distribution, the certificate must be trusted on the target computer and its Subject must match the manifest Publisher. Timestamping should be used so a validly signed package remains verifiable after certificate expiry. See [MSIX signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) and [package-signing certificate requirements](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing).
- A self-signed certificate is suitable only where the publisher can install that certificate into the target's trust store, such as development or managed enterprise deployment. It is not suitable for an ordinary public download. [Microsoft's signing-options comparison](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) states this explicitly.
- A Store-submitted MSIX is re-signed by Microsoft at no certificate cost. A Store-submitted MSI/EXE is different: the installer and **all** included PE files must already be Authenticode-signed with a certificate chaining to a CA in the Microsoft Trusted Root Program. See [MSI/EXE package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements).
- Azure Artifact Signing (formerly Trusted Signing) currently costs **USD 9.99/month** for Basic (5,000 signatures/month) or **USD 99.99/month** for Premium (100,000 signatures/month), then **USD 0.005** per signature above quota. Public Trust is currently available to organizations in the US, Canada, EU, and UK, but to individual developers only in the US and Canada. Thus an individual publisher in Germany is not currently eligible; an eligible legal organization in Germany is. Sources: [Artifact Signing SKU table](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku) and [Artifact Signing FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq).
- Microsoft's current guidance estimates conventional OV certificates at roughly **USD 300–500/year**; actual CA prices and token/cloud-key terms vary. EV no longer receives instant SmartScreen reputation, so paying an EV premium solely for that purpose has no documented benefit. See [MSIX end-to-end signing guidance](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide) and [SmartScreen reputation guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).
- New Microsoft Store Individual and Company accounts have **no registration fee** when onboarding begins through `storedeveloper.microsoft.com`. Microsoft's same page warns that other entry points use the legacy flow, so this price is channel- and time-sensitive and should be rechecked at publication time. See [Open a developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account) (updated 2026-05-07).
- Signing does not guarantee that a new direct-download build will avoid SmartScreen warnings. Microsoft documents publisher and file-hash reputation, and says even valid OV/EV signatures can warn until reputation accumulates; Store-delivered apps are Microsoft-signed and avoid download warnings. See [SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

For a later fully open-source release, SignPath Foundation offers free signing to qualifying projects, but its current terms require an OSI-approved license without commercial dual licensing, no proprietary components, an already released and actively maintained project, documented security/review roles, and SignPath Foundation as certificate publisher. That is a conditional OSS option, not a bridge to an undecided commercial/dual-license model. Sources: [SignPath Foundation](https://signpath.org/) and its [OSS conditions](https://signpath.org/terms.html).

### Storage and separately managed transcription models

The installed MSIX payload is read-only and must contain code/assets, not mutable downloads. Packaged app data exposes `LocalFolder` (eligible for device backup) and `LocalCacheFolder` (persistent across sessions but excluded from backup/restore). Microsoft positions LocalCache for recreatable generated content and caches. See [ApplicationData](https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata?view=winrt-26100) and [LocalFolder](https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata.localfolder?view=winrt-26100).

Large Whisper/Parakeet model files are independently downloadable, removable, and recreatable. Two platform-valid storage policies therefore remain:

- **Package-owned model cache:** put models under the packaged app's `LocalCacheFolder`. It persists across application updates and is excluded from backup; package reset/uninstall removes package-owned app data. This maximizes cleanup consistency.
- **Product-owned external model depot:** use a versioned per-user directory resolved through `FOLDERID_LocalAppData`, outside the package-managed data root. Full-trust packaged, external-location, and unpackaged builds can share this layout. It also permits deliberately preserving multi-gigabyte models across app uninstall/reinstall or a packaging-channel migration, but Windows will not clean it automatically; the product needs explicit remove-model and uninstall policy. Windows documents `%LOCALAPPDATA%` as the per-user local application-data known folder in [KNOWNFOLDERID](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid).

In either policy, model downloads should be separate from the core executable package and tracked with model ID, runtime compatibility/version, source/license, size, and cryptographic hash. This avoids coupling every code update to multi-gigabyte model payloads and lets users manage models independently. MSIX optional packages can technically split large downloadable content from a main package, but they remain package/version/distribution artifacts inside the main app's package container; see [optional packages and related sets](https://learn.microsoft.com/en-us/windows/msix/package/optional-packages). No primary source establishes that optional packages are simpler than a product-owned model catalog for this app.

Settings, the History database, logs, and model binaries need intentionally different retention/backup rules. In particular, `LocalFolder` being backup-eligible makes it a poor accidental default for privacy-sensitive History or multi-gigabyte models; `LocalCacheFolder` or an explicit non-backed-up local directory is the platform-aligned starting point. Exact schema, encryption, backup, and retention policy remain outside this research ticket.

### Autostart

Packaged and external-location apps can declare `windows.startupTask` in the package manifest. A normal packaged desktop app may declare it enabled or call `StartupTask` APIs; the user remains in control through Settings or Task Manager and can disable it. The application ordinarily must have been launched once before the task registers. External-location packages can use package manifest extensions because they have identity. See [desktop integration extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions) and the [StartupTask schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask).

Unpackaged apps can use the per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key (or the user's Startup folder). Microsoft documents that Run values execute at logon, have a 260-character command-line limit, indeterminate ordering, and can be delayed by Windows. `RunOnce` is for transient setup, not persistent application autostart. See [Run and RunOnce Registry Keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).

For Local Dictation, all mechanisms can launch a command-line mode that creates only the Overlay and tray icon, not the Management window. The product requirement that autostart is opt-in should be enforced by first-run/settings UI regardless of what the manifest technically permits. A scheduled task is unnecessary unless a later requirement needs elevation, triggers beyond user logon, or installer-managed recovery; none is established here.

### Tray, Overlay, and packaging effects

`Shell_NotifyIcon` is the desktop API for adding, modifying, and deleting a notification-area icon. `SetWindowPos(..., HWND_TOPMOST, ...)` is the desktop API for keeping a window above non-topmost windows. A full-trust medium-integrity packaged process has standard desktop permissions, so full MSIX, external-location, and unpackaged models can all implement the planned tray icon, shared Application menu, topmost Overlay, and conventional Management window. Sources: [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicona), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos), and [MSIX trust levels](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview).

Packaging does not make a notification-area icon permanently visible; visibility and overflow placement remain user/Shell policy. Also, Microsoft's notification-icon sample warns that a GUID-based icon registration is associated with the calling executable's full path and can fail after that binary moves. Because MSIX versioned install paths change during servicing, stable tray identity across MSIX update, repair, and channel migration needs a small Windows 11 validation test rather than an assumption. See the [NotificationIcon sample](https://learn.microsoft.com/en-us/windows/win32/shell/samples-notificationicon).

No source reviewed establishes a packaging-only difference in topmost HWND behavior for a full-trust app. AppContainer would be a materially different security model and is not the current architecture direction.

### Uninstall and cleanup

Full MSIX uninstall removes the installed package, package-managed AppData, and package-managed/virtualized registry state, while preserving user-created files outside that managed state. Package reset permanently deletes package app data and reinstalls the app; repair retains app data. See [enterprise MSIX uninstall semantics](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-enterprise) and [MSIX reset/repair](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-reset-and-repair).

Files deliberately written outside package-managed state are not covered by that cleanup guarantee. External-location and unpackaged installers must explicitly remove their installed binaries, updater components/services/tasks, Run-key/Startup entries, shortcuts, identity-package registration, and product-owned data selected for deletion. WinGet validation can reject an installer whose uninstall does not clean up completely, and Microsoft Store policy requires the product to communicate and enable clean uninstall. See [WinGet validation](https://learn.microsoft.com/en-us/windows/package-manager/package/repository) and [Microsoft Store Policies 7.18](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).

The user-visible uninstall contract for separately downloaded models is a product decision: delete by default, preserve by default, or offer an explicit choice. Whichever policy is selected must be consistent across Settings' remove-model action, installer uninstall, Store uninstall, reset, and channel migration.

### Microsoft Store and future publication posture

For full MSIX, the Store supplies signing, hosting, acquisition, updates, and SmartScreen-reputable delivery. For MSI/EXE submissions, the publisher must host an immutable versioned HTTPS URL; the installer must be an offline standalone `.msi` or `.exe`, support silent installation, install only the intended product, and arrive with all PE files trusted-signed. Existing users are still updated by the product, not by the Store. See [MSI/EXE package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements) and the current [Win32 Store distribution comparison](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-distribute-your-win32-app-through-microsoft-store).

A direct-download channel and Store channel can coexist, but package identity, publisher identity, model/data paths, update ownership, and migration behavior must be designed before public release. Changing package identity or signing publisher later can turn an update into a separate application identity. Open-sourcing the source does not remove the need to sign public Windows binaries; commercial release adds durable legal-publisher identity and signing-account ownership. Repository licensing and product monetization are independent of whether the binary uses MSIX.

Store policy and prices are external, mutable constraints. Before submission, revalidate the current Store policies, certificate availability/pricing, developer-account onboarding route, and whether post-install model acquisition needs disclosure in listing/certification notes.

## Conclusions and recommendation-ready shortlist

These are implementation-ready candidates, not a product decision.

### Shortlist A: full MSIX first

- Full-trust WinUI 3 desktop app in MSIX.
- Microsoft Store as the lowest-operations public channel; optionally a separately signed `.appinstaller` channel for owner/dev/beta installs.
- `windows.startupTask` for opt-in autostart.
- Models downloaded after first run into either package-owned `LocalCacheFolder` or an explicitly product-owned external depot, according to the uninstall/migration choice.
- Best fit when clean uninstall, Store updates, signing simplicity, and future package-identity features outweigh direct-installer flexibility.
- Required proof before selection: tray GUID persistence across MSIX updates; global hotkeys/clipboard/audio/runtime dependencies under packaged full trust; package identity and data migration between direct and Store builds.

### Shortlist B: conventional MSI/EXE, initially unpackaged

- Per-user conventional installer plus a signed product-owned updater; GitHub Releases may host immutable artifacts and WinGet may add discovery/upgrades.
- HKCU Run for opt-in autostart.
- Explicit per-user LocalAppData storage and an installer-owned uninstall manifest.
- Microsoft Store MSI/EXE listing remains possible later, but it will not take over updates for installed users.
- Best fit only if an MSIX compatibility spike finds a concrete blocker or direct portable/installer control is more important than identity and Store servicing.
- Preserve a packaging seam so a later full MSIX build can use the same logical settings/model catalog with an explicit migration.

### Shortlist C: MSI/EXE plus external-location identity

- Same installer, updater, storage, and cleanup responsibilities as Shortlist B, plus a signed/registered sparse identity package.
- `windows.startupTask` and other identity-only Windows extensions become available.
- Best fit only if a concrete identity-gated feature is required while a concrete constraint prevents full MSIX.
- It is not a simplification for a greenfield app: it combines conventional-installer operations with package identity lifecycle.

Across all candidates, keep downloaded models outside the core code package/update payload, sign every public executable/installer, and define one stable logical model catalog and data-migration contract. The owner must choose the primary channel, model retention policy, publisher/legal identity, and acceptable recurring signing cost.

## Rejected alternatives

- **Unsigned or self-signed public releases:** rejected for ordinary end users because trust installation and SmartScreen friction are unacceptable and Store MSI/EXE submission requires trusted signing.
- **Store MSI/EXE listing as the sole updater:** rejected because the Store does not update existing MSI/EXE installations.
- **GitHub Releases or WinGet as the sole silent updater:** rejected because Releases is artifact hosting and WinGet is a package-manager channel; neither by itself defines the app's unattended update transaction/policy.
- **Embedding all transcription models in the core MSIX/installer:** rejected as the default because independently selectable multi-gigabyte assets would be coupled to code releases and channel package limits. Optional/model packages remain a future alternative if Store-managed model delivery becomes a requirement.
- **External-location packaging merely as a compromise:** rejected as a default. Without a proven need for both a conventional installer and identity-only APIs, it adds identity registration/signing failure modes without gaining full-MSIX servicing and cleanup.
- **EV certificate solely for instant SmartScreen trust:** rejected because Microsoft says EV no longer bypasses reputation building.
- **SignPath Foundation as the assumed commercial bridge:** rejected because its current free-OSS conditions disallow commercial dual licensing and put SignPath Foundation in the publisher certificate. It remains a conditional option only after an unequivocal compatible OSS decision.
- **Scheduled Task autostart without a new requirement:** rejected for V1; StartupTask or HKCU Run already covers opt-in per-user logon startup with less installer complexity.

## Remaining risks and open product decisions

1. Choose the primary V1 distribution path: Store MSIX, direct MSIX/App Installer, or signed MSI/EXE. This determines update ownership, onboarding friction, and recurring signing cost.
2. Decide whether a Store build and direct build must share one upgradeable package identity. Validate publisher/certificate identity and migration before any public release.
3. Decide whether model files are package-owned cache (automatic cleanup) or a product-owned depot (cross-channel/reinstall preservation), and define the uninstall prompt/default.
4. Decide whether the publisher is an individual or legal organization. In Germany this currently determines Azure Artifact Signing Public Trust eligibility; prices and regional availability must be rechecked when release work begins.
5. Decide future publication posture: proprietary/commercial, OSI open source, or dual-license. This changes whether SignPath Foundation is eligible and who can durably own the signing identity.
6. Select updater UX and policy: silent/background vs. prompted, mandatory security updates, rollback/downgrade, metered-network behavior, release rings, and failure recovery.
7. Validate on Windows 11: tray GUID across update/repair; startup enable/disable/re-enable; Overlay topmost behavior; quit/restart during update; model download interruption; full uninstall/reset with both model-storage policies; and direct-to-Store migration.
8. Confirm Store certification treatment of first-run model downloads, model licenses/attribution, privacy disclosure, and any updater behavior against the policies current at submission time.
9. Decide framework-dependent versus self-contained Windows App SDK deployment after measuring installed size, offline behavior, and updater complexity.

## Resolve-when coverage

This research identifies viable packaging and update channels, certificate/trust requirements and current documented costs, storage rules for separately managed large models, autostart options, tray/Overlay implications, uninstall semantics, Microsoft Store constraints, future open-source/commercial consequences, rejected alternatives, remaining risks, and a recommendation-ready shortlist. The unresolved items above are product choices or validation spikes rather than missing platform facts.
