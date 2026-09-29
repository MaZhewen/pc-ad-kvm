# Settings Window Visual Refresh Implementation Plan

> **For agentic workers:** Execute this plan inline, one task at a time. Keep the user approved single-column layout and existing settings behavior.

**Goal:** Restyle the settings window as a modern light card layout while keeping every control, validation path, keyboard action, and saved value intact.

**Architecture:** Keep `SettingsForm` and its current WinForms controls. Replace etched `GroupBox` sections with named white card panels, apply a restrained light palette and typography, and style the primary and secondary buttons. Put cards in a scrollable viewport, pin the action footer outside it, and cap the dialog height to the current screen's working area.

**Tech Stack:** C# 5, .NET Framework WinForms, PowerShell test harness.

---

### Task 1: Add failing appearance checks

**Files:**
- Modify: `tests/message-host/Main.cs`

- [x] **Step 1: Extend the control finder to match names and add style assertions**

The recursive finder must match `control.Name` as well as `control.Text`, so tests can identify card panels. Add a recursive `FindFirstControl<T>` helper, then assert these values:

```csharp
Panel speedCard = FindControl<Panel>(form, "鼠标速度");
TrackBar slider = FindFirstControl<TrackBar>(form);
Check(form.BackColor == Color.FromArgb(244, 247, 251), "Settings uses a light blue-gray canvas");
Check(speedCard != null && speedCard.BackColor == Color.White, "Settings uses white section cards");
Check(okay.FlatStyle == FlatStyle.Flat && okay.BackColor == Color.FromArgb(37, 99, 235),
 "Settings confirm button uses the blue primary style");
Check(slider != null && slider.TickStyle == TickStyle.None, "Settings slider hides redundant ticks");
```

- [x] **Step 2: Run the harness and confirm the new appearance assertions fail**

Run in PowerShell from the repository root, using a unique temp directory so the harness cannot overwrite existing temp data:

```powershell
$scratch = Join-Path $env:TEMP ('pckvm-visual-red-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$env:TEMP = $scratch
$env:TMP = $scratch
& 'tests\message-host\run.ps1'
```

Expected: compilation succeeds; the light-canvas/card/button/slider assertion fails against the current classic styling.

### Task 2: Apply the approved light card theme

**Files:**
- Modify: `src/agent/SettingsForm.cs`
- Test: `tests/message-host/Main.cs`

- [x] **Step 1: Replace section frames with named card panels**

Use a `SettingsCard : Panel` with `Name` set to the section title, white background, 1 px `#DFE5ED` border, consistent padding, and a bold Microsoft YaHei UI heading. Keep one column and the existing section order. Set the form canvas to `#F4F7FB`; use `#233044` for primary text and `#68778B` for help text.

- [x] **Step 2: Apply consistent control styles without changing behavior**

Use sans-serif Microsoft YaHei UI 9 pt throughout the form; style `确定` as flat blue `#2563EB` with white text and `取消` as a white neutral button with a subtle border. Set the speed slider `TickStyle` to `None`. Preserve control names/text, event handlers, validation, keyboard focus, and Enter/Esc behavior. Keep the footer fixed outside the content viewport; make the viewport scroll when the screen or future settings require it.

- [x] **Step 3: Run the message-host harness**

Run: `tests\message-host\run.ps1` with the unique `$env:TEMP`/`$env:TMP` pattern from Task 1.

Expected: all appearance assertions, section/button visibility assertions, settings readback and INI save/reload assertions pass; existing message-host checks pass or report their documented no-foreground-window skip.

### Task 3: Build a fresh isolated preview package

**Files:**
- Read: `assets/pc-kvm.ico`, all `src/agent/*.cs`, all `src/injector/*.java`
- Create: `G:\pc-kvm\dist\layout-preview-modern-<unique>\pc-kvm.exe`
- Create: `G:\pc-kvm\dist\layout-preview-modern-<unique>\pckvm.jar`

- [x] **Step 1: Compile the WinForms application into a new package directory**

Use the framework `csc.exe` with `-target:winexe -platform:x64 -optimize+`, the repository icon, and the `System.Windows.Forms`/`System.Drawing` references. Do not replace or stop a running `pc-kvm.exe`.

- [x] **Step 2: Build the companion injector jar from current Java sources**

Compile the injector sources with JDK `javac --release 8`, run R8 D8 with `--release --min-api 28`, zip `classes.dex` as `pckvm.jar`, and place it beside the new executable. Do not run `adb push`.

- [x] **Step 3: Verify the package and working tree**

Confirm both files exist, the C# compile and D8 commands exit 0, `git diff --check` is clean, and only intended source/test/spec files are modified. Leave the user’s running older preview untouched.
