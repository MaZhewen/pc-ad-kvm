# Settings Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the Settings dialog into the approved single-column sections while preserving all current settings and their behavior.

**Architecture:** Keep the existing `SettingsForm` class, fields, value capture, validation, and `ReadBack` flow. Replace the form-wide fixed-coordinate composition with nested WinForms layout containers for speed, device position, switching, recovery, and actions. Do not change `Config`, `TrayUi`, or runtime switching logic.

**Tech Stack:** C# WinForms targeting .NET Framework; `TableLayoutPanel`, `FlowLayoutPanel`, `GroupBox`, and existing controls.

---

## Files and responsibilities

- Modify `src/agent/SettingsForm.cs`: reorganize the current controls and assign them to semantic sections. Keep all existing event handlers, public result properties, shortcut capture and validation behavior.
- Refer to `docs/superpowers/specs/2026-09-29-settings-layout-design.md`: approved section order and future-setting conventions.
- Refer to `docs/superpowers/specs/2026-09-29-double-edge-and-hotkey-design.md`: current switch and shortcut semantics that must remain unchanged.

## Task 1: Build the single-column Settings dialog

**Files:**
- Modify: `src/agent/SettingsForm.cs`

- [ ] **Step 1: Replace form-wide coordinate placement with a vertical layout root.**

Create one root `TableLayoutPanel`, dock it to the form, give it one 100% width column, five auto-sized rows, and consistent padding and row spacing. Keep the form centered, fixed-dialog, and non-maximizable/non-minimizable. Set the client size to about 440 × 560 pixels, then adjust it only if the built layout shows clipping at the normal Windows font size. Future sections must increase the form size instead of compressing existing rows.

Use this root configuration as the starting point:

```csharp
TableLayoutPanel layout = new TableLayoutPanel();
layout.Dock = DockStyle.Fill;
layout.AutoSize = false;
layout.ColumnCount = 1;
layout.RowCount = 5;
layout.Padding = new Padding(14);
layout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
for (int i = 0; i < layout.RowCount - 1; i++)
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // footer stays at the bottom
```

Set the form size before adding the root panel:

```csharp
ClientSize = new Size(440, 560);
Controls.Add(layout);
```

- [ ] **Step 2: Put speed controls in the first section.**

Use a `GroupBox` with an auto-sized inner table. Its first row places “手机上的鼠标速度” at the left and `_speedValue` at the right; the trackbar fills the next row; the “慢” and “快” labels align below it. Keep `_speed`, its range and tick settings, and the `ValueChanged` handler unchanged. Remove `_speed.Location`, `_speed.Width`, and all other absolute `Location` assignments for controls added to the form.

```csharp
GroupBox speedSection = new GroupBox();
speedSection.Text = "鼠标速度";
speedSection.Dock = DockStyle.Fill;
TableLayoutPanel speedLayout = new TableLayoutPanel();
speedLayout.Dock = DockStyle.Fill;
speedLayout.ColumnCount = 1;
speedLayout.RowCount = 3;
speedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
speedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
speedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
TableLayoutPanel speedHeader = new TableLayoutPanel();
speedHeader.Dock = DockStyle.Fill;
speedHeader.ColumnCount = 2;
speedHeader.RowCount = 1;
speedHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
speedHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
speedHeader.Controls.Add(l1, 0, 0);
speedHeader.Controls.Add(_speedValue, 1, 0);
_speed.Dock = DockStyle.Fill;
TableLayoutPanel speedEndpoints = new TableLayoutPanel();
speedEndpoints.Dock = DockStyle.Fill;
speedEndpoints.ColumnCount = 2;
speedEndpoints.RowCount = 1;
speedEndpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
speedEndpoints.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
speedEndpoints.Controls.Add(new Label { Text = "慢", AutoSize = true }, 0, 0);
Label fastLabel = new Label();
fastLabel.Text = "快";
fastLabel.AutoSize = true;
fastLabel.Anchor = AnchorStyles.Right;
speedEndpoints.Controls.Add(fastLabel, 1, 0);
speedLayout.Controls.Add(speedHeader, 0, 0);
speedLayout.Controls.Add(_speed, 0, 1);
speedLayout.Controls.Add(speedEndpoints, 0, 2); // two-column row: “慢” left, “快” right
speedSection.Controls.Add(speedLayout);
layout.Controls.Add(speedSection, 0, 0);
```

- [ ] **Step 3: Put device position in the second section.**

Use a “手机位置” `GroupBox` with a single horizontal `FlowLayoutPanel` for `_left` and `_right`. Keep the current checked state derived from `Config.PhoneOnLeft` and keep each radio button’s existing text.

```csharp
GroupBox positionSection = new GroupBox();
positionSection.Text = "手机位置";
positionSection.Dock = DockStyle.Fill;
positionSection.AutoSize = true;
positionSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
FlowLayoutPanel positionRow = new FlowLayoutPanel();
positionRow.Dock = DockStyle.Fill;
positionRow.FlowDirection = FlowDirection.LeftToRight;
positionRow.WrapContents = false;
positionRow.Controls.Add(_left);
positionRow.Controls.Add(_right);
positionSection.Controls.Add(positionRow);
layout.Controls.Add(positionSection, 0, 1);
```

- [ ] **Step 4: Put edge-switch and shortcut controls in the third section.**

Use a “切换方式” `GroupBox` with a one-column auto-sized table containing, in order: `_enableEdgeSwitch`, a short note that the shortcut remains available when edge switching is off, the shortcut label, `_hotkeyInput`, the existing key-combination hint, and `_hotkeyError`. Let the error label wrap and take its height from its text; reserve a row so a conflict message cannot cover the next control. Keep `_hotkeyInput` read-only and preserve its `KeyDown`/`PreviewKeyDown` handlers.

```csharp
GroupBox switchSection = new GroupBox();
switchSection.Text = "切换方式";
switchSection.Dock = DockStyle.Fill;
switchSection.AutoSize = true;
switchSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
TableLayoutPanel switchLayout = new TableLayoutPanel();
switchLayout.Dock = DockStyle.Fill;
switchLayout.AutoSize = true;
switchLayout.ColumnCount = 1;
switchLayout.RowCount = 6;
switchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
for (int i = 0; i < switchLayout.RowCount; i++)
    switchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
_hotkeyInput.Dock = DockStyle.Fill;
_hotkeyError.AutoSize = true;
_hotkeyError.MaximumSize = new Size(380, 0);
switchLayout.Controls.Add(_enableEdgeSwitch, 0, 0);
switchLayout.Controls.Add(edgeSwitchHint, 0, 1);
switchLayout.Controls.Add(hotkeyLabel, 0, 2);
switchLayout.Controls.Add(_hotkeyInput, 0, 3);
switchLayout.Controls.Add(hotkeyHint, 0, 4);
switchLayout.Controls.Add(_hotkeyError, 0, 5);
switchSection.Controls.Add(switchLayout);
layout.Controls.Add(switchSection, 0, 2);
```

- [ ] **Step 5: Put adb recovery and actions in the final sections.**

Place `_killAdb` and its existing warning together in a “断联恢复” `GroupBox`; allow the warning to wrap inside the section. Put `取消` and `确定` in a right-aligned `FlowLayoutPanel`. Preserve the current result flow: OK calls `ReadBack()` and closes only on success; Cancel returns `DialogResult.Cancel`; `AcceptButton` and `CancelButton` remain assigned.

```csharp
GroupBox recoverySection = new GroupBox();
recoverySection.Text = "断联恢复";
recoverySection.Dock = DockStyle.Fill;
recoverySection.AutoSize = true;
recoverySection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
TableLayoutPanel recoveryLayout = new TableLayoutPanel();
recoveryLayout.Dock = DockStyle.Fill;
recoveryLayout.AutoSize = true;
recoveryLayout.ColumnCount = 1;
recoveryLayout.RowCount = 2;
recoveryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
recoveryLayout.Controls.Add(_killAdb, 0, 0);
recoveryLayout.Controls.Add(l3, 0, 1); // wrap inside the recovery section
recoverySection.Controls.Add(recoveryLayout);
layout.Controls.Add(recoverySection, 0, 3);

FlowLayoutPanel buttonRow = new FlowLayoutPanel();
buttonRow.FlowDirection = FlowDirection.RightToLeft;
buttonRow.WrapContents = false;
buttonRow.AutoSize = true;
buttonRow.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
buttonRow.Controls.Add(ok);
buttonRow.Controls.Add(cancel);
layout.Controls.Add(buttonRow, 0, 4);
```

- [ ] **Step 6: Keep value and validation methods unchanged.**

Do not alter `OnSpeedChanged`, `CaptureShortcut`, `ProcessCmdKey`, or the validation and output assignments in `ReadBack`. Confirm that `EnableEdgeSwitch`, `SwitchHotkey`, `AllowKillAdb`, `PhoneOnLeft`, and `MouseSensitivity` still come from the same controls and only return after successful OK.

## Task 2: Build and review the result

**Files:**
- Review: `src/agent/SettingsForm.cs`
- Reference: `docs/superpowers/specs/2026-09-29-settings-layout-design.md`

- [ ] **Step 1: Compile the Windows agent without touching the running app.**

Do not run `build/build-agent.ps1` for this check: it stops any running `pc-kvm.exe` and requires a current device injector JAR. Invoke the same C# compiler directly, writing only to `%TEMP%`:

```powershell
$sources = Get-ChildItem .\src\agent -Filter '*.cs' | ForEach-Object { $_.FullName }
$previewExe = Join-Path $env:TEMP 'pc-kvm-settings-layout-compile-check.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
    -nologo -target:winexe -platform:x64 -optimize+ `
    -win32icon:"$((Resolve-Path .\assets\pc-kvm.ico).Path)" `
    -out:"$previewExe" -r:System.Windows.Forms.dll -r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'C# compile failed' }
```

Expected result: exit code 0. This confirms C# compilation; it does not rebuild the Android injector JAR or launch the application.

- [ ] **Step 2: Review the final source diff against the design.**

Confirm the five sections remain in the specified order; controls use layout containers rather than fixed `Location` coordinates; labels and validation messages are within their sections; the dialog has enough room for all current content; and no configuration or runtime files changed. Do not add or run test suites for this layout-only task unless the user requests testing.
