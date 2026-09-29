# Review Findings Repair Plan

**Goal:** Repair the six confirmed review findings without changing the input protocol.

**Architecture:** Keep geometry in the existing `EdgeTracker`, preserve a pending geometry barrier when control frames clear the queue, and retain the chosen keypad mapping until its key-up. Route short ADB commands through one bounded process runner. Remove unconditional key logging and make the injector build create its output directory.

**Tech Stack:** C# 5 on .NET Framework, Java 8, PowerShell.

- [x] Add a failing queue test for `LEAVE` while `GEOMETRY` is pending; preserve the latest geometry after leave and rerun pointer tests.
- [x] Add a failing keypad press/release test with NumLock changed between events; implement per-key mapping state and rerun agent logic tests.
- [x] Add a failing ADB timeout test using a local fake `adb.exe`; bound the process lifetime and drain both streams without blocking; rerun transport tests.
- [x] Synchronize `EdgeTracker` dimensions on initial connection and verify the left-edge entry and return with a focused harness.
- [x] Remove default scancode logging and create `dist` before copying the injector artifact.
- [x] Run all noninteractive offline tests, build the injector in a clean staging directory, check the diff, query the connected device, and stage a new Windows binary without interfering with the running instance.
