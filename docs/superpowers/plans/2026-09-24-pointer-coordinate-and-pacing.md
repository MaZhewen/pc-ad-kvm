# Pointer coordinate and pacing implementation plan

**Goal:** Eliminate coordinate drift and uneven mouse delivery while preserving zero-install Android operation.
**Architecture:** Validate absolute mouse support on the actual device before modifying production. Keep UHID keyboard behavior. Only integrate a mouse backend proven to position the visible pointer and preserve hover/button semantics.
**Tech Stack:** C# .NET Framework, Java 8 bytecode / D8, Android 16 shell app_process, UHID.

- [ ] Probe: build standalone absolute UHID mouse (unsigned 16-bit X/Y, relative wheel, three buttons). Run bounded lifetime, inspect Android classification and screen position. Reject backend if touchscreen/joystick or absent pointer.
- [ ] If rejected, inspect shell-accessible absolute MotionEvent or no-acceleration mouse support. Validate actual visible pointer, not only event acceptance. Document any blocker before changing production.
- [ ] Once backend passes: add failing coordinate/report tests; implement absolute-position protocol with sequence/geometry confirmation, resetting on reconnect/rotation.
- [ ] Add failing pacing/order tests; implement bounded coalescing with buttons/keys/scroll as barriers and independent send pacing, no stale backlog.
- [ ] Validate real screen coordinates and mouse semantics, then left/right edges and rotation. Build deployable artifacts only after checks pass.

Current work continues in the existing feature checkout to retain the already-tested uncommitted TCP repair and user changes. No unrelated edits or commits.
