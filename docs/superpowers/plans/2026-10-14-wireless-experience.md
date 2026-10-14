# Wireless Experience Implementation Plan

**Goal:** Reduce stale pointer catch-up and click delay; integrate a truthful, session-scoped Wi-Fi low-latency request.

**Architecture:** Keep the authenticated ADB protocol and diagnostics. Coalesce adjacent positions on Android, preserve discrete barriers, and pace only replaceable PC movements. Add a reflection adapter for the Android Wi-Fi lock with independent cleanup and explicit activation limits.

**Tech Stack:** .NET Framework C#, Java 8 reflection, Android app_process, PowerShell regressions.

**Spec:** `docs/superpowers/specs/2026-10-14-wireless-experience-design.md`

## Task 1: Android input queue

Files: `src/injector/SessionLink.java`, new `src/injector/InputFrameQueue.java`, `tests/session/TestMain.java`, `tests/session/run.ps1`.

- [x] Write and run failing queue tests: pointer(epoch1,seq1), pointer(epoch1,seq2), buttonDown, pointer(epoch1,seq3), buttonUp must dequeue seq2, down, seq3, up. Different epochs must not replace. Key releases and scroll are barriers. A capacity-full adjacent pointer can replace, while an additional reliable event must reject.
- [x] Implement a synchronized bounded deque with `offer`, timed `poll`, and adjacent same-epoch coalescing. Wake pollers immediately on enqueue. Integrate it without changing heartbeat/geometry handling.
- [x] Add timestamped frames and periodic Android queue/process summaries without payload logging; provide a processing completion hook for Injector.
- [x] Run `tests/session/run.ps1`, `tests/double-click/run.ps1`, and check concurrent queue stress terminates with ordered discrete events.

## Task 2: PC dispatch

Files: `src/agent/FrameQueue.cs`, `src/agent/FrameWriter.cs`, optional new `src/agent/FrameDispatchPolicy.cs`, `tests/transport/Main.cs`, `tests/transport/run.ps1`.

- [x] Write a failing deterministic scheduling test showing pointer→button bypasses the remaining interval while two ordinary pointer sends preserve four-millisecond pacing. Test key, wheel, leave, and geometry barriers.
- [x] Expose only the queue inspection needed to decide whether its head pointer precedes a discrete frame. Apply pacing to an isolated pointer; dispatch pointer plus following discrete event immediately, preserving FIFO order.
- [x] Test actual FrameWriter bytes preserve pointer/button/key order and run transport plus absolute-pointer regressions.

## Task 3: Wi-Fi latency guard

Files: new `src/injector/WifiLatencyGuard.java`, `src/injector/Injector.java`, `src/agent/DeviceLauncher.cs`, `src/agent/ConnectionCoordinator.cs`, new `tests/wifi-latency/TestMain.java`, new `tests/wifi-latency/run.ps1`, relevant launcher tests.

- [x] Add failing lifecycle tests: successful acquisition is released once; partial acquisition failure still cleans up; failure is reported without propagating; disabled sessions never acquire. Fake only the Android API boundary.
- [x] Implement session-scoped acquire/cleanup and reflection backend using the shell package context and public WifiLock methods. Log held request versus unsupported, never assert activated from isHeld.
- [x] Pass the optional flag only for wireless launch, retain legacy invocation overload, and integrate cleanup independently from UHID release.
- [x] Run guard/connection/coordinator/session/double-click tests. Build both artifacts. Inspect target-device lock and foreground eligibility after launch.

## Task 4: Integration and real validation

- [x] Document queue behavior, latency request restrictions, and trial executable location.
- [x] Run seven core regressions, Android double-click, Wi-Fi tests, and `git diff --check`; compile matching jar and exe.
- [x] Obtain fresh code review, resolve material findings, and preserve final results in this plan.
- [x] Launch trial build after normal tray exit; collect comparable wireless input and feedback. Report any unavailable Wi-Fi activation separately from the two software optimizations.

## Execution record

- Android queue: tests first failed on retaining newest pre-click coordinates and timing summaries; 10,000-pointer/5,000-reliable concurrent stress and real-socket burst tests pass.
- PC dispatch: the unchanged original scheduler failed the pointer-before-button test; the corrected scheduler and actual recording stream ordering pass.
- Wi-Fi: lifecycle and launch flag tests failed before implementation, then pass; normal low-latency lock acquisition succeeds on the Android 16 tablet. Latest system `is_low_latency_activated=false` confirms that held does not mean activated. No global/root command was executed with a mutation argument.
- Fresh review found a diagnostic sample mismatch when Android coalesces a sampled PC sequence. Reproducing test failed, then passed after bounded timestamps were retained for all dispatched pointer frames. Only acknowledged positions contribute to the distribution, so before/after sample populations differ and cannot prove an improvement alone.
- Android timing and Wi-Fi status originally went to an ignored process output stream. Callback delivery test failed, then passed after routing only these bounded diagnostic prefixes into the PC log.
- Real user validation found a held-left drag froze the visible cursor until release. Directly adding UHID position reports while held then broke interface scrolling. Replace that approach with `DragCursor.java`, an independent noninteractive SurfaceControl visual layer. Regression first failed on an extra held mouse report, then on missing drag feedback, and now passes with touch MOVE plus visual-only movement. `tests/drag-cursor/run.ps1` covers reflection, partial failures and independent cleanup. A target-device probe successfully showed the arrow at logical (600,600), moved it, and removed it; screenshot verified display coordinates. Final real dragging/double-click validation remains pending.
- Further user validation confirmed scrolling works but two cursors are visible: the compositor cursor moves while the native cursor remains at the previous position, then updates after release. The UHID InRange=false report did not hide the native sprite. A new regression rejects that report on touch DOWN. Investigating the device-specific pointer icon API with real input-channel tokens before changing the implementation again; no completion claim until single-cursor dragging is validated.
- Exact absolute descriptor and relative probe mice both accepted TYPE_NULL and TYPE_DEFAULT using raw input tokens; both probes closed their own device and restored the icon. NativeCursor owns hide/restore lifecycle, gates compositor display on successful hide, and retries temporary restoration failures on ordinary movement. Compile-only Android classes were verified absent from dex class definitions.
- Fresh review identified an initial-window snapshot publication race. The empty-callback regression failed before synchronizing callback and initial publication with a receivedCallback flag, then passed; closed listeners ignore late callbacks. Review confirmed Android 16 touch events on device 0 do not clear another mouse device's hover state, so no forced hover was added to stationary clicks.
- User confirmed a full PC-KVM restart produces one cursor, but its appearance differed. DragCursor now loads the system default cursor bitmap, preserves its pixel dimensions and hotspot, and retains the previous arrow only as an unsupported-platform fallback. Bitmap/hotspot/reuse tests failed before implementation and now pass. A real AndroidLayer probe loaded, displayed, moved and removed the system image successfully. Final user confirmation of matching appearance, dragging and double-click selection is pending.
- The first system-image trial rendered no cursor. A real pixel probe found the vendor PointerIcon bitmap entirely transparent, whereas the matching framework pointer_arrow resource contains visible pixels. Added alpha validation and a same-dimension resource fallback, each with a failing test before implementation. The updated real probe was visually compared with the native cursor and cleaned up. After reloading this final jar, the user answered “正常了” to the combined cursor visibility/style, interface dragging, and double-click selection check. Fresh focused review found no remaining blocking issue.
- All seven core regressions, double-click, Wi-Fi, drag-cursor and native-cursor suites passed; meaningful regression cases cover queue barriers, sender pacing, coalesced ACK measurements, native icon lifecycle, listener snapshot races, image hotspots and transparent vendor resources. The matching trial remains at G:\pc-kvm\dist\wireless-experience. Wi-Fi request is held but target activation remains unavailable; network jitter is not claimed solved and no numerical latency improvement is claimed from these samples.
