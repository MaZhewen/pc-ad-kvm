# Android Double-Click Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a mouse double-click in an Android text field act like a finger double-tap, while preventing the second mouse click from reaching WeChat.

**Architecture:** Recognize the second click in a pure Java state machine in the Android injector. Keep the first click as a mouse event, suppress the second mouse press/release, and synthesize two touchscreen taps at the displayed position. Keep mouse drag and other buttons in the existing UHID path.

**Tech Stack:** Java 8 bytecode in an Android `app_process` shell process, UHID, Android `InputManager` and `MotionEvent` through reflection, existing PowerShell test/build scripts.

---

### Task 1: Pure double-click recognizer

**Files:** Create `src/injector/DoubleClickGuard.java`; create `tests/double-click/TestMain.java`, `tests/double-click/run.ps1`.

- [ ] Write tests that feed press/release pairs into `DoubleClickGuard` using deterministic timestamps and screen coordinates. Assert that a second press within 300 ms and 32 px is marked for conversion, while slow, distant, incomplete, dragged, and reset sequences keep ordinary mouse behavior.
- [ ] Run `tests/double-click/run.ps1` and observe the expected missing-class failure.
- [ ] Implement the smallest state machine to pass. Expose separate press, release, move, and reset operations so a held second click that becomes a drag can resume the mouse path.
- [ ] Run the targeted tests and check that the earlier failure is gone.

### Task 2: Touch double-tap injection

**Files:** Create `src/injector/TouchDoubleTap.java`; modify `src/injector/AbsoluteSession.java`; extend `tests/double-click/TestMain.java`.

- [ ] Add a failing integration-level test for the session decision: normal first press/release goes through UHID; recognized second press/release is suppressed and requests exactly one touch double-tap; failed injection never replays the suppressed mouse click; pointer leave/geometry change resets recognition.
- [ ] Run the targeted test and confirm it fails for the missing behavior.
- [ ] Add a touch injector that obtains four `MotionEvent`s (down/up/down/up) at the display coordinates, sets touchscreen source and display 0, and injects each with `InputManager` from the shell process. Check each return value and recycle all events. Keep this interface replaceable in JVM tests.
- [ ] Integrate the recognizer into the absolute session button branch. If a pending second click becomes a drag, resume mouse press at its original location before forwarding movement. On injection failure, log the reason and keep the click suppressed.
- [ ] Run `tests/double-click/run.ps1`, `tests/absolute-pointer/run.ps1`, and `tests/session/run.ps1`.

### Task 3: Package and verify on device

**Files:** Modify `README.md` only if the new double-click behavior needs user documentation.

- [ ] Run `build/build-injector.ps1` and confirm `dist/pckvm.jar` is generated.
- [ ] Restart the PC-KVM connection so the new injector runs on the Android device; verify that Android still registers the virtual mouse and keyboard.
- [ ] In WeChat File Transfer Assistant, use disposable text to verify a double-click selects one word without sending it. Verify a single click and normal drag still operate.
- [ ] If the device test fails, inspect the new failure log and input-event trace before changing the algorithm.
- [ ] Review the focused diff, record test results, and commit only the intended files.
