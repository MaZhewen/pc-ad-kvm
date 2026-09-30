# Android left-click selection design

## Problem and evidence

In WeChat's compose field, a PC-KVM mouse double-click can send the selected word, while a finger double-tap selects it. PC-KVM forwards left-button presses as a UHID mouse. Android's input trace showed no keyboard event during the accidental send. The first attempt preserved the first mouse click and converted the second to two touch taps; selection appeared and vanished. Converting only the second click to one touch tap kept selection more often, but the first mouse click still reached WeChat and roughly one in five user trials sent text. The precise WeChat handler is not observable, so the reliable boundary is to stop forwarding left-button mouse clicks altogether.

## Intended behavior

Two PC-KVM left clicks should reach Android as two touchscreen taps, matching the user's working finger gesture and selecting a word without sending it. A single left click should behave as a touchscreen tap. Holding and moving the left button should form one continuous touch gesture. Pointer hover, right/middle buttons, and wheel input remain on the existing UHID mouse path.

## Design

`AbsoluteSession` routes left-button down, pointer movement while held, and up to `TouchPointer`, and never sets the UHID primary-button bit. `TouchPointer` creates touchscreen `MotionEvent` down/move/up events on display 0 through the shell process's `InputManagerGlobal`. Each gesture shares one `downTime`, event times are monotonic, and each injection waits for a dispatch result. Leave, disconnect, geometry changes, wheel input, and another button cancel an active touch. If injection fails, the left click is swallowed instead of falling back to the mouse path that may send text.

This change necessarily makes all Android-side PC-KVM left clicks touch based, so it needs device verification beyond Java tests.

## Verification

Tests cover two taps with separate down times, drag movement, cancellation, injection failure, session event routing, and no UHID primary-button state. Build the injector and run existing pointer and session suites. On the device, test WeChat File Transfer Assistant with disposable text: repeated double-clicks should keep the word selected and never send it; a single click and other mouse buttons should remain usable.