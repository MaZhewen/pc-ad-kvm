# Wireless Connection Implementation Plan

> **For agentic workers:** Implement tasks in order, using test first for each behavior.

**Goal:** Add Android 11+ wireless debugging pairing and a single, identity-checked KVM session while keeping USB mode available.

**Architecture:** Centralize ADB execution and device selection, then move connection ownership to one coordinator. The existing loopback transport carries a versioned session handshake and remains the sole input path.

**Tech Stack:** C# 5 / .NET Framework WinForms, Java 8 Android injector, PowerShell offline test harnesses.

---

### Task 1: ADB and identity foundation

- [ ] Add offline tests for device parsing, endpoint validation, argument quoting, pairing stdin, timeout/cancellation, and target selectors.
- [ ] Implement `AdbClient`, `AdbDevice`, `WirelessDiscovery`, and connection fields in `Config`.
- [ ] Route `DeviceLauncher` commands through the selected target and structured ADB results.
- [ ] Run the new tests and existing `adb-timeout` and agent logic suites.

### Task 2: Session safety

- [ ] Add failing transport tests for fragmented/wrong handshakes, disconnect once, and closing a session without stopping the listener.
- [ ] Implement token verification in `Transport`; pass the token to `Injector`.
- [ ] Add Java tests for session preamble and bounded frame reads, then add injector lease, cleanup, and process lock.
- [ ] Run transport and injector tests.

### Task 3: Connection lifecycle and UI

- [ ] Add tests for coordinator selection, identity mismatch, retry, and cancellation.
- [ ] Move all initial connect and reconnect work into `ConnectionCoordinator`; keep `Watchers` for heartbeat, geometry, and foreground only.
- [ ] Add `ConnectionForm`, tray entry, profile persistence, state display, and safe shutdown.
- [ ] Run all offline suites and build both artifacts.

### Task 4: Acceptance

- [ ] Review requirements in the design against the implementation.
- [ ] Validate ADB/network and real Android behavior when a device and pairing code are available; report unverified hardware cases explicitly.
- [ ] Update README with wireless instructions and troubleshooting.
