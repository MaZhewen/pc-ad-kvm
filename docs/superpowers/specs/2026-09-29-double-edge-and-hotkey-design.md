# Double edge and shortcut switching

## Behavior

Both directions require two deliberate edge pushes. The first valid outward push arms a 1,200 ms window. The pointer must move at least 12 pixels into the current screen and then return to the edge for a second valid outward push. Only the second push switches control. The existing 40 mickey outward threshold applies to each phone-side push. A timeout makes the next push a new first push. Aborts, disconnections, geometry changes, and side changes clear the pending attempt.

Settings includes an "Enable double edge switching" checkbox, on by default and persisted in the INI file. Turning it off disables edge switching in both directions and clears any pending first push. The global shortcut continues to work. Turning it back on requires the pointer to leave the PC edge before an edge attempt can start.

The global shortcut defaults to Ctrl+Alt+Space and toggles control in either direction. Users can record a replacement combination in Settings. A combination needs Ctrl or Alt plus one nonmodifier key. Ctrl+Alt+Esc remains the emergency exit and cannot be assigned. The shortcut uses Windows hotkey registration so it works while a PC application has focus; key repeat does not cause a second switch. If registration fails, the previous shortcut and saved setting remain in place and Settings explains the conflict. Entry still checks device connection and pointer readiness before capturing PC input.

## Structure

`EdgeTracker` owns the two edge attempt states and exposes a shortcut toggle method that uses the same takeover events as pointer switching. It reads a monotonic tick in production and accepts explicit event times for deterministic tests. `MessageHost` owns Windows hotkey registration and delivers a single toggle event. `Config`, `SettingsForm`, and `TrayUi` parse, record, validate, and save the shortcut. `Program` wires the toggle and applies settings.

## Verification

Pure logic tests cover right and left edge attempts, both directions, timeout, aborted attempts, and shortcut transitions. Configuration tests cover default, round trip, invalid combinations, and reserved emergency key. Build the Windows agent and run existing regression harnesses. Device-specific input capture requires a later interactive PC and Android check.
