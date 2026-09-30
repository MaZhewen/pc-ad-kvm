# Wireless Port Scan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Find an Android wireless debugging connection port from one user-entered IPv4 address and fill the verified endpoint into the connection UI.

**Architecture:** Keep TCP discovery separate from ADB identity checks. The form starts and cancels one background scan; the coordinator verifies candidates before the form uses them. Scanning never deploys the injector.

**Tech Stack:** C# 5 / .NET Framework, WinForms, existing ADB client, PowerShell test harnesses.

---

### Task 1: Bounded TCP scanner

**Files:** Create `src/agent/WirelessPortScanner.cs`; create `tests/port-scan/Main.cs` and `tests/port-scan/run.ps1`.

- [ ] Write a failing local-listener test for `WirelessPortScanner.Scan(IPAddress.Loopback, new int[] { closed, open }, onOpen, onProgress, token)` that expects only the listener port and no work after cancellation.
- [ ] Run `./tests/port-scan/run.ps1`; expect a missing-type compile failure.
- [ ] Implement strict IPv4 extraction and a fixed pool of at most 128 workers. Each worker attempts TCP connect with a 300 ms deadline; the scan stops on a successful callback, cancellation, or the 180 s overall deadline.
- [ ] Run the scanner test; expect the listener and cancellation checks to pass.

### Task 2: Verify ADB candidates

**Files:** Modify `src/agent/ConnectionCoordinator.cs`; extend `tests/coordinator/Main.cs` and `tests/coordinator/FakeAdb.cs`.

- [ ] Add failing fake-ADB cases: an open port with no exact online ADB endpoint is rejected; a candidate with `ro.serialno` different from saved identity is rejected; a matching endpoint is returned without reverse, push, or injector start.
- [ ] Run `./tests/coordinator/run.ps1`; expect missing verification API.
- [ ] Add `VerifyWirelessEndpoint` that executes target-specific `adb connect`, `devices -l`, and `getprop ro.serialno`, applying the existing identity rule and cleaning up only a scan-created wrong endpoint.
- [ ] Run the coordinator tests and `./tests/connection/run.ps1`; expect all pass.

### Task 3: Nonblocking connection-window flow

**Files:** Modify `src/agent/ConnectionForm.cs` and `README.md`; extend `tests/message-host` or add a focused UI harness if a behavioral assertion is possible without launching KVM.

- [ ] Add a failing UI test for scan action availability in Wireless mode, input validation, cancellation, and completed endpoint fill without starting KVM.
- [ ] Run the UI test; expect scan controls or behavior to be absent.
- [ ] Add Scan and Cancel controls. On a worker thread, verify same-IP mDNS candidates first, then scan only the entered IP; marshal progress and result updates to the UI thread, and ignore callbacks after cancel/close/new input.
- [ ] Document the IP-only scan, pairing prerequisite, timeout, and manual fallback in README.
- [ ] Run all offline suites, build injector and agent, then test with the provided IP when the device is reachable.

### Task 4: Review and delivery

- [ ] Compare code with the spec for scan bounds, identity checks, cancellation, and no automatic KVM deployment.
- [ ] Run `git diff --check` and `git status --short`, then commit the verified changes on `codex/wireless-connection`.
- [ ] Report the discovered port or clear reason it could not be verified; distinguish offline tests from live device validation.
