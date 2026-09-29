# Settings Entry Feedback Implementation Plan

> **For agentic workers:** Execute this plan inline. Preserve the approved Settings UI and existing startup/runtime behavior.

**Goal:** Every launch gesture gives visible feedback. A fresh launch opens Settings after initialization; another launch focuses Settings in the existing process; tray double-click opens the same Settings window.

**Architecture:** Use a named local mutex to elect one process, an auto-reset event for “open Settings” requests, and an auto-reset acknowledgment event and request mutex to serialize and confirm each handled launch request. The primary process listens on a registered wait and marshals requests to the WinForms UI thread. TrayUi.OpenSettings is the single dialog path shared by the tray menu, tray double-click, startup, and IPC. Keep an already-open dialog and activate it instead of creating another.

**Tech Stack:** C# 5, .NET Framework WinForms, PowerShell harness.

---

### Task 1: Specify and test single-instance signaling

**Files:**
- Create: src/agent/SingleInstanceGuard.cs
- Create: tests/single-instance/Main.cs
- Create: tests/single-instance/run.ps1

- [x] Add a harness using unique mutex/event names. Assert the first guard owns the mutex, a competing launch requests Settings, and the callback runs on the UI thread after message pumping.
- [x] Run the harness and confirm it fails because the guard does not exist.
- [x] Implement primary/secondary election, retry signaling for startup races, UI-thread dispatch, a per-request acknowledgment so a signal or stale response is not mistaken for a handled launch, and deterministic cleanup.
- [x] Run the harness and confirm all signaling assertions pass.

### Task 2: Unify settings entry points

**Files:**
- Modify: src/agent/TrayUi.cs
- Modify: src/agent/Program.cs
- Test: tests/single-instance/Main.cs

- [x] Extract the existing menu handler into TrayUi.OpenSettings, including save/apply behavior and modal cleanup.
- [x] Activate an already-open settings dialog instead of opening a duplicate.
- [x] Wire tray icon double-click and the Settings menu item to that shared method.
- [x] Acquire the single-instance guard before initializing raw input, transport, watchers, or the Android helper; a secondary process only signals the primary and exits. Show a MessageBox if it cannot signal the primary.
- [x] Register the primary's UI callback as soon as the hidden MessageHost handle exists; dispatch via that host and use the shared TrayUi entry point once installed. Queue first-launch Settings after device/watchers startup is complete and before entering the application message loop.
- [x] Extend targeted tests to verify duplicate signaling and the UI callback, and review both tray entry points against the shared method.

### Task 3: Verify and build a fresh package

**Files:**
- Test: tests/single-instance/run.ps1
- Test: tests/message-host/run.ps1
- Create: G:\pc-kvm\dist\layout-preview-settings-entry-<unique>\pc-kvm.exe
- Create: G:\pc-kvm\dist\layout-preview-settings-entry-<unique>\pckvm.jar

- [x] Run the single-instance and message-host harnesses with a unique temporary directory.
- [x] Run the offline regressions relevant to the agent UI/runtime; skip the occupied transport port if the existing preview owns it.
- [x] Compile the WinForms executable and place the unchanged current injector jar beside it in a new package directory; do not stop or overwrite the user's running preview.
- [x] Run git diff --check and inspect the final diff for unintended behavior changes.