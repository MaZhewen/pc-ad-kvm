# Shared Clipboard Implementation Plan

> **For agentic workers:** Use executing-plans inline in this session; request one fresh code review after implementation. Track completed steps here.

**Goal:** Windows ↔ Android text, image and file clipboard sharing, prioritizing WeChat then file managers, without an APK where device capabilities permit.

**Architecture:** Keep keyboard/mouse wire protocol unchanged. A separately authenticated clipboard socket uses bounded messages and streaming file payloads. Platform adapters capture and publish clipboard snapshots; each connection owns its workers and cancellation. Clipboard changes invalidate in-flight publications.

**Tech Stack:** C# 5 / .NET Framework WinForms, Java 8 reflection against Android 16 system services, ADB reverse, existing PowerShell test harnesses.

## Constraints and initial decisions

- Start from local master 93ce77a, preserving the wireless input and cursor fixes; the native worktree's remote default is older.
- Execute inline as requested by the user. No additional design gate; the design was approved on 2026-10-15.
- Use only marked test data on device; restore clipboard after probes. Do not send WeChat messages.
- Primary app: WeChat 8.0.79. Secondary: Xiaomi File Explorer 8.2.3.2; Android SDK 36.
- If target application cannot paste standard ClipData, report that exact unsupported scenario. No claim that installing an APK fixes application behavior.

## Task 1 — Device capability probe

Files: `probes/05-clipboard/ClipboardProbe.java`, `probes/05-clipboard/run.ps1`.

- [ ] Build a reflection-only probe that obtains `android.content.IClipboard$Stub.asInterface(ServiceManager.getService("clipboard"))`, enumerates supported signatures, and creates a `com.android.shell` context.
- [ ] Save original ClipData; set a unique plain-text marker, assert exact round trip without printing the original, restore in finally.
- [ ] Create marked image/file in a dedicated shared-storage test directory; obtain standard provider URIs, test granting through system clipboard, and remove only owned test data.
- [ ] Record read/write/URI permissions and app versions in the design, including failures. Probe failure is evidence guiding the adapter, not a silent text-only scope reduction.

## Task 2 — Bounded clipboard wire protocol and stale-update gate

Files: `src/agent/ClipboardWire.cs`, `src/injector/ClipboardWire.java`, `tests/clipboard/Main.cs`, `tests/clipboard/TestMain.java`, `tests/clipboard/run.ps1`.

- [ ] Write failing cross-language fixtures for UTF-8 text, item metadata and binary data. Wire uses little-endian integers, length-prefixed UTF-8, versioned preamble, a 32-byte session token, and type/length records.
- [ ] Assert malformed lengths, invalid UTF-8, excessive item counts, unsafe names, early EOF, invalid digest and canceled updates fail before publication. Read bytes with bounded buffers; do not allocate a whole file.
- [ ] Implement the minimal codec and transfer reader/writer that passes fixtures. Limits: text 1 MiB, metadata/name 4 KiB, 128 files, each file 1 GiB, chunks 64 KiB, total update 2 GiB.
- [ ] Add failing state tests: echo is ignored, local updates invalidate remote transfers, old generations cannot publish, failed transfers leave old clipboard intact. Implement synchronization gate and run tests.

## Task 3 — Platform adapters

Files: `src/agent/WindowsClipboard.cs`, `src/injector/AndroidClipboard.java`, platform fixtures under `tests/clipboard/`.

- [ ] Windows: tests capture/publish Unicode, PNG/bitmap and file-drop snapshots; use an STA harness and restore the original clipboard. Read notification via `WM_CLIPBOARDUPDATE`; retry contention asynchronously with bounded delay.
- [ ] Android: tests cover reflection signature selection and item metadata without Android dependencies. Implement the proven device interfaces; obtain standard ClipData text/URIs and stream their contents.
- [ ] Receive into owned cache directories, verify digest, then publish atomically. Images publish image MIME/URI on Android and PNG/bitmap on Windows; files publish URI list and FileDrop respectively. Do not turn opaque URIs into file paths.
- [ ] Cache lifetime tests: current published data stays readable across worker cleanup; failed partials are deleted; newer local clipboard contents prevent old remote publication.

## Task 4 — Session lifecycle and UI

Files: `src/agent/ClipboardService.cs`, `src/injector/ClipboardSession.java`, `src/agent/Program.cs`, `src/agent/Transport.cs`, `src/injector/Injector.java`, `src/agent/Config.cs`, `src/agent/TrayUi.cs`, `src/agent/DeviceLauncher.cs`, `src/agent/ConnectionCoordinator.cs`.

- [ ] Add a clipboard listener on a separate dynamically selected loopback port; authenticate with current session token. Advertise its port to Android over the authenticated input connection and reuse its ADB reverse path; no extra LAN listener or ADB target guessing.
- [ ] Exercise lifecycle with failing tests for wrong token, old session, disabled feature, mid-stream disconnect and cleanup. Start adapter workers only after authentication, cancel on disconnect and generation change.
- [ ] Send changes independently of input. Coalesce pending snapshots and allow newer updates to cancel old transfers; keep bounded socket timeouts and memory.
- [ ] Baseline both clipboards at connect without overwriting; on first takeover transfer the source snapshot to support content copied before connection. Test preconnect text and both transfer directions.
- [ ] Add `EnableClipboard=true` config, a checked tray toggle and a read-only status menu item showing clipboard progress/failure. Saving toggle cancels work when disabled. Bind handle recreate/destroy and app exit safely.

## Task 5 — Regression, device validation and delivery

Files: `README.md`, `tests/README.md`, design validation appendix.

- [ ] Run clipboard fixtures and current transport/session/config/coordinator/message-host/edge/pointer/double-click regressions from this source tree; repair regressions with a failing test first.
- [ ] Build matching exe/jar; keep artifacts in a dedicated trial directory. Verify the shipped binaries, not a separately compiled implementation.
- [ ] Validate marked text both ways, image and file URI round trips, checksums, transfer cancellation and cache validity on the connected tablet. Validate WeChat paste without sending; record unsupported directions honestly.
- [ ] Request a fresh whole-change code review; address important findings and rerun affected checks.
- [ ] Deliver artifact path, tested scenarios, remaining target-app limitations and launch instructions. Do not merge or replace a running installation without explicit authorization.

## Progress

- 2026-10-15: Design approved; device detected as Android 16. Plan created before production edits.
- Task 1 complete: shell clipboard text/listener and externally acquired MediaStore URI publish/read validated without APK. Probe restores original contents.
- Task 2 complete: both codecs64 assertions plus cross-language fixtures; malformed metadata/digests/limits/cancellation covered.
- Task 3 complete: Windows23 assertions previously passed; Android30 assertions plus provider/cache ownership tests pass. Native Windows latest rerun requires unlocked desktop.
- Task 4 complete: service168 assertions, Android session99 assertions, independent authenticated tunnel, switches and configuration integrated; matching binaries build.
- Task 5 in progress: core regressions passed except interactive Windows clipboard/message-host checks blocked by locked desktop. Physical device passed text in both directions and Windows-to-Android PNG digest check. Reverse-image metadata bug fixed with RED→GREEN; full physical rerun and WeChat/file-manager application validation await unlocked devices.
- Review findings addressed: delayed native clipboard callback cannot let old remote data overwrite a new copy; rejected receive directories are correctly owned/deleted; successful materialized Windows text/images are cleaned; old owned file/media caches use reference protection plus24h/2GiB retention.
- Ruling: keep feature on attached `codex/shared-clipboard` branch and deliver a separate trial directory; do not merge or replace the user's running installation during incomplete application validation.
