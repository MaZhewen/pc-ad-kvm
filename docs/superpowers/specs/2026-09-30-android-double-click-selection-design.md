# Android mouse double-click selection design

## Problem and evidence

On the connected Android device, double-clicking a word in the WeChat compose field sends that word. A finger double-tap selects it. A captured PC-KVM double-click produced two left-button down/up pairs at the same pointer coordinates, roughly 69 ms apart, with no keyboard event. Android classifies the PC-KVM pointer as a mouse. The observed difference therefore lies after mouse event delivery; WeChat's internal handling is not observable here.

## Intended behavior

A PC-KVM mouse double-click at one position should select the word as a finger double-tap does and must not send it. Ordinary single clicks, right/middle clicks, scrolling, and pointer movement should keep their current behavior. This change addresses double-clicks only.

## Design

Keep the first completed left click as a mouse click. If a second left-button press follows within Android's double-tap interval and close to the first position, suppress that second mouse press and release. On its release, inject two touchscreen taps at the Android display coordinates through InputManager from the shell-owned injector process. The touch events use one consistent display ID, source, device ID, and monotonically ordered event times. If injection fails, swallow the second mouse click and log the failure rather than forwarding the click that may send text.

The recognition state lives in a small pure Java class. AbsoluteSession owns the Android input injector and integrates the recognition into button forwarding. A left-button drag cancels double-click candidacy. State resets on leave, disconnect, geometry change, and close.

## Verification

Test the recognizer offline for timing, distance, interrupted sequences, drag, and reset. Test the touch-event construction and failure behavior without an Android device where possible. Build the Android injector and run existing pointer/session tests. Then verify on the connected device in WeChat File Transfer Assistant using disposable text: double-click selects one word without sending it; single click and right click still work.

The device test is necessary because Android input delivery and WeChat's response cannot be proven by an offline unit test.
