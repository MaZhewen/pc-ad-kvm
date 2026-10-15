# Wireless input experience

User approved all three directions on 2026-10-14: receive-side position coalescing, prompt button dispatch, and Android Wi-Fi low-latency support.

## Receive queue

Replace Android's uncoalesced 128-frame FIFO with a bounded synchronized queue. Adjacent absolute pointer frames from the same geometry epoch may replace one another. Every other frame is an ordering barrier. Preserve the last position before a button, both button transitions, keyboard releases, wheel frames, and geometry/leave ordering. Do not sleep to collect a batch. Saturation of nonreplaceable events still closes the session. Acknowledge only positions actually submitted; the existing cumulative sequence handling accepts later acknowledgements.

Measure Android queue wait and frame processing duration in bounded five-second summaries. Summaries must not contain coordinates, keys, or payloads. Session shutdown flushes the last summary. Initial implementation retains drag down/up boundaries; burst coalescing can reduce intermediate drag samples, which must be checked with real dragging and double-click selection.

## Sender

Movement pacing stays at four milliseconds for ordinary consecutive pointer frames. A pending position immediately followed by a discrete event must flush without waiting, and the discrete event itself must not inherit movement pacing. Preserve all wire order. Use a pure scheduling predicate tested with synthetic time and actual writer ordering tested with a recording stream.

## Wi-Fi latency request

Only WirelessTls launches pass `--wifi-low-latency` to Injector. USB remains compatible with the two-argument session invocation. A small reflection-based Android adapter requests `WIFI_MODE_FULL_LOW_LATENCY` through a context for `com.android.shell`; no APK, global settings, rooting, or WorkSource reassignment. The lock lasts only for the authenticated wireless session and is released independently during cleanup; Binder death also releases the framework lock.

Acquisition is best effort and cannot fail KVM startup. Report requested/held versus unavailable accurately; held does not mean activated because Android additionally checks foreground UID, screen, and hardware conditions. On this Android 16 tablet, the force-low-latency shell command is denied to UID 2000. Validate held lock and activation separately through `dumpsys wifi`; if foreground requirements block activation, report that limitation rather than claiming acceleration.

## Validation

RED→GREEN tests cover same-epoch coalescing, cross-epoch barriers, button/key/wheel ordering, reliable overflow, concurrent producer/consumer, prompt sender barriers, and Wi-Fi acquire/release/failure behavior. Run the core regressions plus existing Android double-click tests. Build matching exe/jar in a separate trial directory with copied configuration, then use the same tablet for wireless sliding/clicking/dragging. No latency improvement claim without comparable live data and user feedback.

## Drag feedback amendment from real validation

The touch-based left drag originally suppressed native mouse positions and froze the visible cursor. Sending native positions while a touch was held then prevented real interface scrolling; Android may cancel pointer actions when mouse hover interrupts an active touch. Preserve the touch gesture and avoid all native mouse moves during it. Hide the native cursor before DOWN, display a small independent compositor cursor at logical display coordinates, move only that visual layer alongside touch MOVE, and hide it after UP/CANCEL before restoring the native mouse. No window, input channel, or input event is owned by this layer. Graphics failure must not break touch delivery, and native resources are released independently at session close. Cover reflection/lifecycle via platform API boundary fixtures and verify the actual layer on the tablet before asking for another drag trial.

User feedback subsequently showed that UHID InRange=false does not hide the native sprite. Use the device-scoped pointer icon API with TYPE_NULL and a raw InputWindowHandle input-channel token obtained from WindowInfosListener, before injecting touch DOWN. Never substitute the IWindow token, change global pointer settings, or inject hover during a held touch. Show the compositor cursor only after hiding succeeds. Restore TYPE_DEFAULT after UP/CANCEL and retry temporarily rejected restoration on subsequent ordinary positions; device removal ends ownership. Prepare the optional listener before input takeover, clean it up independently, and exclude all compile-only Android declarations from the packaged dex. Unsupported APIs retain touch input without adding a duplicate cursor.

Reuse the default native bitmap and hotspot when available. Reject transparent vendor placeholders; try the visible framework pointer_arrow bitmap only at identical dimensions to preserve the supplied hotspot. Missing or unsupported resources retain a visible vector fallback. Native image reads currently use default size and scale 1, so arbitrary platform pointer themes and accessibility scaling are outside this validation. Actual tablet images and final user feedback confirmed visible matching cursor, interface drag, and double-click selection.
