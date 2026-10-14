# Wireless Input Latency Diagnostics Implementation Plan

**Goal:** Distinguish PC queuing, wireless ADB transport, and Android input processing stalls without changing input behavior.

**Architecture:** Record monotonic timestamps for sampled PING/PONG and pointer acknowledgements. Record the PC writer queue age and depth at dequeue. Publish bounded, periodic summaries in the existing log; preserve frame ordering and avoid per-event logging. Use the current protocol and no device-side changes.

**Tech Stack:** .NET Framework 4.x C#, PowerShell regression scripts, existing ADB reverse protocol.

---

### Task 1: Queue timing

**Files:** `src/agent/FrameQueue.cs`, `src/agent/FrameWriter.cs`, `tests/transport/Main.cs`

- [x] Add a failing test that a coalesced pointer retains the newest enqueue timestamp and that a button remains an ordering barrier.
- [x] Store enqueue ticks with queue entries; report age and depth when a frame is dequeued, without altering send order.
- [x] Run `./tests/transport/run.ps1` and verify the test passes.

### Task 2: Round-trip timing

**Files:** `src/agent/InputLatencyStats.cs`, `src/agent/Program.cs`, `src/agent/Transport.cs`, `tests/transport/Main.cs`, `tests/transport/run.ps1`

- [x] Add failing deterministic tests for sampled PING/PONG and pointer ACK latency, stale acknowledgement rejection, and summary reset.
- [x] Instrument the existing messages with a monotonic clock; log a summary every few seconds, including sample count and tail latency. Do not record coordinates, keys, or per-frame payloads.
- [x] Run the focused test and relevant existing connection and transport regressions.

### Task 3: Verify and use measurements

**Files:** `README.md`

- [x] Document how to capture a wireless and USB comparison and read queue wait, write duration, heartbeat, and pointer confirmation.
- [x] Build the diagnostic executable and run the seven core offline regressions.
- [x] Measure real wireless pointer input before changing its pacing: on 2026-10-14, PC queue p95 stayed at 1–3 ms in active windows and socket writes below 1 ms, while pointer confirmation reached 367 ms and heartbeat reached 144 ms. The tablet-to-gateway probe also reached 101 ms. Keep the existing frame pacing; test a less variable wireless path before tuning code.
