# First Pairing UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make first wireless pairing usable from a manually entered IPv4 address and six-digit code, with pairing-port discovery and manual fallback.

**Architecture:** Keep pairing service parsing pure; coordinator performs cancellable mDNS reads and the existing stdin-based ADB pair; WinForms coordinates inputs and state. Pairing does not start KVM deployment.

**Tech Stack:** C# 5 / .NET Framework WinForms, existing AdbClient, PowerShell offline harnesses.

---

### Task 1: Pairing-service discovery

**Files:** Modify `src/agent/WirelessDiscovery.cs`, `src/agent/ConnectionCoordinator.cs`, `tests/connection/Main.cs`, and `tests/coordinator/FakeAdb.cs`.

- [x] Add failing parser tests for combined and separate `_adb-tls-pairing._tcp` records, while asserting connect records stay in the connect list.
- [x] Run `./tests/connection/run.ps1`; confirm parser API is missing.
- [x] Implement `WirelessDiscovery.ParsePairing` and a coordinator `PairingServices(CancellationToken)` wrapper using `adb mdns services`.
- [x] Run connection/coordinator tests; confirm they pass.

### Task 2: Explicit pairing controls and mode switch

**Files:** Modify `src/agent/ConnectionForm.cs`, `tests/connection-form/Main.cs`, and `tests/coordinator/FakeAdb.cs`.

- [x] Add failing UI tests that USB disables wireless controls, Wireless enables them, and manual IP + port + code causes one targeted `adb pair` with leading zero code via stdin.
- [x] Run `./tests/connection-form/run.ps1`; confirm missing controls or behavior.
- [x] Add named IP/port/code controls and handlers, validate strict IPv4 and port, clear code immediately, and keep the existing coordinator pairing callback.
- [x] Run UI and connection tests; confirm they pass.

### Task 3: Auto-discover pairing port

**Files:** Modify `src/agent/ConnectionForm.cs` and `tests/connection-form/Main.cs`.

- [x] Add failing UI tests for same-IP unique mDNS pairing port fill, no-service manual fallback, multiple-service nonselection, and stale lookup cancellation on close/mode change.
- [x] Run focused UI tests; confirm expected failures.
- [x] Query pairing services on a worker thread with cancellation and a bounded deadline; fill/select matching ports on the UI thread. When Pair is clicked with no port, continue only if one candidate exists.
- [x] Run focused tests and update README with the pairing and connect port distinction.

### Task 4: Delivery to current workspace

**Files:** Current managed worktree and `G:\pc-kvm` primary checkout.

- [x] Run the full offline suite and rebuild Windows exe; inspect `git diff --check` and clean status.
- [x] Review and commit the change. Preserve the primary checkout's untracked files, then integrate verified source so the current workspace receives the feature.
- [x] Report the running old preview process and the safe way to open the new binary; avoid force-killing a KVM process that may own input capture.
