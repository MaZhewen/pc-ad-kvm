# Double Edge and Shortcut Switching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Require two deliberate edge pushes in both directions and add a configurable global shortcut to toggle control.

**Architecture:** EdgeTracker retains the transition state and emits its existing entry and exit events. MessageHost registers the shortcut with Windows. Config and SettingsForm persist a validated combination; Program connects these pieces and preserves takeover safety checks.

**Tech Stack:** C# 5, .NET Framework 4, WinForms, Windows Raw Input and RegisterHotKey.

---

### Task 1: Double edge state machine

**Files:** `src/agent/EdgeTracker.cs`, `tests/edge-tracker/Main.cs`, `src/agent/Program.cs`.

- [ ] Add tests proving first push never switches, inward travel of 12 pixels and a second push within 1,200 ms does, a late push restarts the attempt, and left/right directions mirror. Keep the phone's 40 mickey requirement for each push. Use explicit timestamps in test calls.
- [ ] Run `tests/edge-tracker/run.ps1` and confirm those tests fail for missing behavior.
- [ ] Implement pending attempt state, timestamp comparison, inward travel, and reset on abort/geometry/side change. Production mouse events use the monotonic tick read by EdgeTracker.
- [ ] Rerun `tests/edge-tracker/run.ps1` and confirm zero failures.

### Task 2: Shortcut transition and registration

**Files:** `src/agent/EdgeTracker.cs`, `src/agent/MessageHost.cs`, `src/agent/Program.cs`, `tests/edge-tracker/Main.cs`.

- [ ] Add tests proving shortcut entry and exit use the same events, do not require the edge attempt, and clear any pending attempt.
- [ ] Run `tests/edge-tracker/run.ps1` and confirm the new tests fail.
- [ ] Add explicit tracker transition methods. Register a no-repeat global hotkey in MessageHost and dispatch one toggle event. Wire Program to the existing entry safety checks and exit cleanup.
- [ ] Rerun edge tests and compile the agent.

### Task 3: Configurable shortcut

**Files:** `src/agent/Config.cs`, `src/agent/SettingsForm.cs`, `src/agent/TrayUi.cs`, `src/agent/Program.cs`, `tests/agent-logic/Main.cs`.

- [ ] Add tests for Ctrl+Alt+Space default, valid custom combination, invalid fallback, emergency key rejection, and Save/Load round trip.
- [ ] Run `tests/agent-logic/run.ps1` and confirm the new tests fail.
- [ ] Add a shortcut value and parser, settings recorder, and atomic runtime application: register new combination before saving; on conflict keep the previous one and show a nonmodal explanation.
- [ ] Rerun agent logic tests and compile the agent.

### Task 4: Documentation and final verification

**Files:** `README.md`, `tests/README.md`.

- [ ] Explain double edge timing, shortcut use and customization, and the emergency exit.
- [ ] Run `tests/edge-tracker/run.ps1`, `tests/agent-logic/run.ps1`, and relevant existing harnesses. Compile the Windows agent directly to avoid the build script's forced shutdown of a running copy; record exact results.
- [ ] Review the diff against the approved design and identify any device-specific checks still needed.

## Verification record

### Settings extension: enable or disable edge switching

- Add a persisted `EnableEdgeSwitch` setting, defaulting to true, and a checkbox in Settings.
- Let `EdgeTracker` enforce the setting in both directions, clearing a pending attempt when disabled; keep shortcut switching independent.
- Test default, invalid value, save/load, disabled edge behavior in both directions, and shortcut behavior while disabled. Rebuild the Windows executable.

- `tests/edge-tracker/run.ps1`: 53 passed, 0 failed.
- `tests/agent-logic/run.ps1`: 44 passed, 0 failed.
- `tests/message-host/run.ps1`: Settings checkbox, registration, conflict, handle recreation, input-readiness safety, and visibility checks passed; foreground takeover check skipped because the tool session has no interactive foreground window.
- `tests/transport/run.ps1`: passed.
- Windows agent compiled with the repository's .NET Framework C# compiler into `dist/pc-kvm.exe` after the previously running copy exited. The regular build script was not run because it force-stops an existing `pc-kvm` process.
- Interactive PC and Android switching remains a device check for the user; this session did not capture live input.
