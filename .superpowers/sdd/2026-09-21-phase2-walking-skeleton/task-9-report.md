# Task 9 报告：键盘支持

**日期**：2026-09-23　**分支**：`phase2-skeleton`　**基线**：`a3e0205`（干净树）
**提交**：`6bd4e76` `feat: 键盘支持 —— scancode 映射、修饰键状态、退出时清空按键`
**状态**：DONE（运行时验证按计划另行安排，本任务只做离线验证）

## 实现内容

1. **`src/agent/KeyMap.cs`（新建，30 行）** — `public static class KeyMap`，含 brief Step 2 的 `ModifierBit(int scancode, bool isE0)` 原文。Ruling 26：纯映射不进 `Program.cs`。文件为 UTF-8 带 BOM（与 `Program.cs`/`RawInput.cs` 等同侪一致，亦符合项目硬约束）。
2. **`Program.cs` `KeyChanged` 处理器重写** — 修饰键状态存 `byte modifiers`（声明在 `int mc = 0, kc = 0;` 旁、`ri.KeyChanged +=` 订阅之前，闭包捕获无前向引用问题），逻辑为 brief 原文：修饰键先更新位图、TAKEOVER 下补发一条 KEY 报告（否则手机侧修饰态不更新）；普通键 `if (tracker.Current != KvmState.Takeover) return;` 门控。调用点 `KeyMap.ModifierBit(e.Scancode, e.IsE0)`。
3. **Ruling 27（既有缺陷）** — `MouseMoved` 里滚轮与三鼠标键的转发整体包进 `if (cursor != null && tracker.Current == KvmState.Takeover)`（Program.cs:157）。此前 IDLE 态每次点击/滚轮都会打到手机光标停留处。
4. **设备侧 `MSG_LEAVE` 分支（补既有缺口）** — `Injector.java` `handle()` 新增分支：`buttonsDown = 0` + `sendMouse(0,0,0,0)` + `KeyState.releaseAll(dev)`；`KeyState.java` 新增 `releaseAll`（清 `modifiers` 与 6 槽位，发一条全零键盘报告）。至此 Task 8 在逃逸键/心跳超时两处补发的 `EncodeLeave()` 不再是空操作，`buttonsDown`/修饰键不再永久残留。`Program.cs` 的 `ForegroundLost` 放弃路径也补发了 `EncodeLeave()`（Program.cs:110）。
5. **`ScancodeMap.java` 补全** — E0 前缀表（方向键、Home/End/PgUp/PgDn/Ins/Del、小键盘 Enter//、PrintScreen、右 Ctrl/Alt、左右 Win、Menu）+ 非 E0 新增 `-`/`=`/CapsLock/F1–F12/NumLock/ScrollLock。非 E0 修饰键（0x2A/0x36/0x1D/0x38）刻意落 default 返回 `-1`：它们由 PC 侧 `KeyMap.ModifierBit` 折算进 mods 字节，不走 6 键槽位。

## 验证

### 1. PC 侧构建（`build/build-agent.ps1`）

```
编译 10 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (26.5 KB)
已复制 pckvm.jar 到 dist\
exitcode=0
```

零警告（csc 未输出任何 warning）。`Program.cs` 终稿 **349 行** < 360 红线。

### 2. 设备侧构建（`build/build-injector.ps1`）

```
javac（--release 8，会打印弃用警告，正常）...
d8 转 dex...
推送到设备...
C:\Users\mazhewen\AppData\Local\Temp\pckvm.jar: 1 file pushed, 0 skipped. 18.4 MB/s (5647 bytes in 0.000s)
完成: /data/local/tmp/pckvm.jar
```

（首次构建报 5 个 javac 错：我的 `Injector.java` 编辑在 `else if` 链上多出一个 `}`；一次修复后通过。）

### 3a. `KeyMap.ModifierBit` 离线测试（`%TEMP%\keymaptest\`，csc 4.0.30319 独立编译，含 BOM 后的最终版源文件）

```
PASS  nonE0 0x1E (A) -> 0x00  got=0x00 want=0x00
PASS  nonE0 0x39 (Space) -> 0x00  got=0x00 want=0x00
PASS  nonE0 0x2A (LShift) -> 0x02  got=0x02 want=0x02
PASS  nonE0 0x36 (RShift) -> 0x20  got=0x20 want=0x20
PASS  nonE0 0x1D (LCtrl)  -> 0x01  got=0x01 want=0x01
PASS  nonE0 0x38 (LAlt)   -> 0x04  got=0x04 want=0x04
PASS  nonE0 0x5B (LWin)   -> 0x08  got=0x08 want=0x08
PASS  E0 0x1D (RCtrl) -> 0x10  got=0x10 want=0x10
PASS  E0 0x38 (RAlt)  -> 0x40  got=0x40 want=0x40
PASS  E0 0x5C (RWin)  -> 0x80  got=0x80 want=0x80
PASS  E0 0x48 (Up)    -> 0x00  got=0x00 want=0x00
PASS  E0 flag, mk only 0x1D -> 0x10  got=0x10 want=0x10
TOTAL: 12/12 passed, 0 failed
exitcode=0
```

任务书要求的最小集全部覆盖，另加 3 个边界用例（E0 前缀的 `0xE01D/0xE038/0xE05C` 形式与 `RawInput.e.Scancode` 一致；E0 非修饰键返回 0；只传 MakeCode+isE0 的形式）。

### 3b. `ScancodeMap.toHidUsage` 离线测试（`%TEMP%\scancodetest\`，JBR javac `--release 8`）

```
PASS  nonE0 0x01 (Esc)       -> 0x29 got=0x29 want=0x29
PASS  nonE0 0x0E (Backspace) -> 0x2A got=0x2A want=0x2A
PASS  nonE0 0x1C (Enter)     -> 0x28 got=0x28 want=0x28
PASS  nonE0 0x1E (A)         -> 0x04 got=0x04 want=0x04
PASS  nonE0 0x39 (Space)     -> 0x2C got=0x2C want=0x2C
PASS  nonE0 0x3B (F1)  -> 0x3A got=0x3A want=0x3A
PASS  nonE0 0x44 (F10) -> 0x43 got=0x43 want=0x43
PASS  nonE0 0x57 (F11) -> 0x44 got=0x44 want=0x44
PASS  nonE0 0x58 (F12) -> 0x45 got=0x45 want=0x45
PASS  nonE0 0x0C (-)  -> 0x2D got=0x2D want=0x2D
PASS  nonE0 0x0D (=)  -> 0x2E got=0x2E want=0x2E
PASS  nonE0 0x1A ([)  -> 0x2F got=0x2F want=0x2F
PASS  nonE0 0x27 (;)  -> 0x33 got=0x33 want=0x33
PASS  nonE0 0x3A (CapsLock) -> 0x39 got=0x39 want=0x39
PASS  E0 0x1D (RCtrl)   -> 0xE4 got=0xE4 want=0xE4
PASS  E0 0x48 (Up)      -> 0x52 got=0x52 want=0x52
PASS  E0 0x4B (Left)    -> 0x50 got=0x50 want=0x50
PASS  E0 0x53 (Delete)  -> 0x4C got=0x4C want=0x4C
PASS  E0 0x47 (Home)    -> 0x4A got=0x4A want=0x4A
PASS  E0 0x4F (End)     -> 0x4D got=0x4D want=0x4D
PASS  E0 0x1C (NumEnter)-> 0x58 got=0x58 want=0x58
PASS  E0 0x5B (LWin)    -> 0xE3 got=0xE3 want=0xE3
PASS  nonE0 0x2A (LShift) -> -1 got=-1 want=-1
PASS  nonE0 0x36 (RShift) -> -1 got=-1 want=-1
PASS  nonE0 0x1D (LCtrl)  -> -1 got=-1 want=-1
PASS  nonE0 0x38 (LAlt)   -> -1 got=-1 want=-1
TOTAL: 26/26 passed, 0 failed
exitcode=0
```

（javac 打印了 3 条 `--release 8` 过时警告，中文控制台下显示为乱码，非错误。）

### 4. IDLE 门控审计（Ruling 27）——逐条发送语句与门控

`Program.cs` 中全部 `transport.Send` / `EmitButton(transport, ...)`（`EmitButton` 内部调 `t.Send(EncodeButton)`，Program.cs:344-347）：

| 语句 | 门控 | IDLE 下会发吗 |
|---|---|---|
| :94 `EncodeHome`、:97 `EncodeEnter` | `tracker.EnterTakeover` 事件——仅在 Idle→Takeover 跃迁时触发；且前置 `transport.IsConnected` 与 `supp.Engage()` | 否 |
| :104 `EncodeLeave` | `supp.ForegroundLost`——仅在抑制已 Engage（=Takeover）时触发；放弃路径 | 否 |
| :110 `EncodeLeave` | `tracker.LeaveTakeover` 事件——仅 Takeover→Idle 跃迁时触发 | 否 |
| :133 `EncodeHome` | `cursor != null && _geometryChanged`（Task 5B 旋转重归位，非用户输入转发，历史已审查行为） | **可能**（仅旋转变化后首次鼠标事件；详见下） |
| :148 `EncodeMove` | `cursor != null` + `tracker.Current == KvmState.Idle` 的 else 分支（=Takeover） | 否 |
| :160 `EncodeScroll`、:164–166 `EmitButton`×3 | `cursor != null && tracker.Current == KvmState.Takeover`（:157，本次 Ruling 27 修复） | 否 |
| :182 `EncodeKey`（修饰键） | `tracker.Current == KvmState.Takeover`（:181） | 否 |
| :187 `EncodeKey`（普通键） | :186 `if (tracker.Current != KvmState.Takeover) return;` | 否 |
| :215 `EncodePing(1)` | `transport.Connected` 事件——连接建立时一次，非输入 | 不适用 |
| :302 `EncodeLeave` | 心跳安全网，外层 `if (tracker.Current == KvmState.Takeover)`（:293） | 否 |
| :311 `EncodePing` | 心跳（`!IsConnected` 时 return），PING 在手机侧无输入副作用 | 不适用 |
| :321 `EncodeLeave` | `host.Escape`（Ctrl+Alt+Esc 逃逸）——放弃路径；若已 Idle 则只是无害地再清一次设备状态 | 否（无输入副作用） |

**结论**：`MouseMoved`/`KeyChanged` 两个输入处理器中，**用户输入**（移动/滚轮/鼠标键/键盘）在 `KvmState.Idle` 下一条都不会发出。唯一不经状态门控的 MouseMoved 发送是 :133 的旋转重归位 HOME（Task 5B 引入、非本任务范围、非输入转发）；若控制方认为它也该收进 TAKEOVER 门控，留给后续裁决。

## 变更文件

- 新建 `src/agent/KeyMap.cs`（30 行，UTF-8 BOM）
- 修改 `src/agent/Program.cs`（321 → 349 行）
- 修改 `src/injector/ScancodeMap.java`（61 → 100 行）
- 修改 `src/injector/KeyState.java`（35 → 42 行）
- 修改 `src/injector/Injector.java`（166 → 172 行）

未触碰禁止清单中的任何文件。**未运行 `dist\pc-kvm.exe`**（按任务书要求，运行时验证另行安排）。

## 自审发现

1. **保留了 `kc++` 与 KEY 日志行**：brief Step 2 的替换代码不含这两行，但删掉会让 `kc` 变为未使用局部变量（CS0219 警告，破坏零警告要求），且与 `mc` 的日志风格不一致。日志行之下的逻辑为 brief 原文。
2. **`KeyState.apply` 的既有小瑕疵（未修，超本任务范围）**：`if (usage < 0) return;` 在 `modifiers = mods` 赋值之后、`sendKeyboard` 之前返回——纯非 E0 修饰键消息（如左 Shift 单独按下/抬起）更新了内部 `modifiers` 但**不发** HID 报告；mods 字节要等下一条普通键报告才落到手机。净效果：`Shift+A` 等组合输入完全正确；仅"修饰键抬起后、下一键之前"这一窗口里，手机侧最后一份报告的 mods 字节可能是旧值（输入核心层无影响，退出接管时 `releaseAll` 全清）。E0 修饰键（右 Ctrl/Alt/Win）不受影响（usage ≥ 0，正常发报告——它们会同时进 mods 字节和键槽位，Linux 输入核心会去重重复事件）。如需彻底修，改 `apply` 在 `usage < 0` 且 mods 变化时也发一条报告，建议留给后续 polish 任务。
3. **左 Win 键路径与 brief 表述的出入**：set 1 里左 Win 实际是 **E0 0x5B**（bare 0x5B 不存在），故 `KeyMap` 里非 E0 `0x5B→0x08` 这条在真实硬件上是死条目；真实的 E0 0x5B 走普通键路径 → `ScancodeMap` E0 表 `0x5B→0xE3`（键槽位），经 Linux hid-input 映射为 KEY_LEFTMETA，功能不受损。按 brief 原文实现（测试要求断言非 E0 0x5B→0x08）。
4. **brief 的 `git add` 清单漏了新建的 `src/agent/KeyMap.cs`**，已补上，否则提交不自洽（无法构建）。
5. `Injector.java` 编辑时 `else if` 链上多出一个 `}`（javac 5 错），一次修复通过；终稿已随构建验证。
6. `KeyMap.cs` 初稿无 BOM，已补 UTF-8 BOM 并重跑构建与 harness 确认无回归。

## 无法验证的内容

- 真机端到端（打字、`Shift+A`、`Ctrl+A/C/V`、方向键、修饰键不粘连、IDLE 键盘照常）——按任务书不可运行 `pc-kvm.exe`，与用户另行安排。
- `MSG_LEAVE` 分支的设备侧行为（`buttonsDown` 清零 + `releaseAll`）未在真机触发验证，仅静态审查 + 构建。

---

# 修轮 1 报告（审查 2 Important + 1 Minor）

**日期**：2026-09-23　**提交**：`cd86784` `fix: 修轮1 —— 左Win(E0 0x5B)修饰位、纯修饰键报文、E0修饰键不再占槽位`（3 文件，+33/−9）
本轮只动 `KeyMap.cs`、`ScancodeMap.java`、`KeyState.java`（`Program.cs`、`Injector.java` 未动）。计划正文已由控制方改好（提交 `3a750f1`），三处修改照抄计划。

## 改了什么

1. **Important 1（左 Win 无效）** — `KeyMap.cs` E0 分支补 `if (mk == 0x5B) return 0x08;`（真机左 Win 就是 E0 0x5B，原表非 E0 0x5B 是死条目、E0 分支漏 0x5B → LGUI 位永不置位）；非 E0 0x5B 行注释改为"历史形态；现代键盘走下面的 E0 分支"（条目保留，计划原文）。
2. **Important 2 + Minor 1（E0 修饰键槽位/超 Usage Max）** — `ScancodeMap.java` E0 表 `0x1D/0x38/0x5B/0x5C` 四条改 `return -1`（原 0xE4/0xE6/0xE3/0xE7 超过 HID 描述符按键数组 Usage Maximum 0x65，被解析器丢弃且白占槽位）；Menu 0x5D→0x65 保持（恰在 Max 上合法）。附计划新增的理由注释。
3. **Important 2（纯修饰键不发报告）** — `KeyState.apply` 换成计划新版：先算 `newMods`/`modsChanged` 再更新状态；`usage < 0` 时**若修饰态变了就发一条键盘报告再 return**；槽位打包抽成 `static byte[] slotsToBytes()` 复用。`releaseAll` 原样保留。

## KeyState.apply 逐行确认（代码审查替代离线单测——参数是具体 UhidDevice，构造需 /dev/uhid）

| 行 | 代码 | 确认 |
|---|---|---|
| :9 | `int newMods = mods & 0xFF;` | 取 KEY 报文携带的 mods 字节 |
| :10 | `boolean modsChanged = (newMods != modifiers);` | 在更新前判定"修饰态是否变化" |
| :11 | `modifiers = newMods;` | 更新内部状态 |
| :13-14 | `usage = ScancodeMap.toHidUsage(...); if (usage < 0) {` | 修饰键/未映射键进入该分支（四个 E0 修饰键现在也走这里） |
| **:19** | `if (modsChanged) dev.sendKeyboard((byte) modifiers, slotsToBytes());` | **核心行：修饰态一变确实走到 sendKeyboard**——按下/单独抬起左 Shift、右 Shift、左 Ctrl、左 Alt、E0 修饰键都发一条带新 mods 字节 + 当前 6 槽位的报告；未映射且 mods 未变才静默 return（:20） |
| :23-30 | 槽位增删 | 普通键路径不变 |
| :31 | `dev.sendKeyboard((byte) modifiers, slotsToBytes());` | 普通键照发报告（捎带最新 mods） |
| :34-39 | `slotsToBytes()` | 两条路径共用，行为一致 |
| :46-51 | `releaseAll` | 退出接管清零并发全零报告，不受本轮影响 |

配合 PC 侧（KeyChanged 里修饰键按下/抬起都发一条 `EncodeKey`，即使 `ScancodeMap` 返回 -1）：**鼠标报文不带 mods 字节**的问题就此闭合——"按住 Ctrl 再点击"在手机上能看到 Ctrl。

## 验证

### KeyMap harness（13/13，新增 E0 0x5B→0x08）

```
PASS  nonE0 0x1E (A) -> 0x00  got=0x00 want=0x00
PASS  nonE0 0x39 (Space) -> 0x00  got=0x00 want=0x00
PASS  nonE0 0x2A (LShift) -> 0x02  got=0x02 want=0x02
PASS  nonE0 0x36 (RShift) -> 0x20  got=0x20 want=0x20
PASS  nonE0 0x1D (LCtrl)  -> 0x01  got=0x01 want=0x01
PASS  nonE0 0x38 (LAlt)   -> 0x04  got=0x04 want=0x04
PASS  nonE0 0x5B (LWin)   -> 0x08  got=0x08 want=0x08
PASS  E0 0x5B (LWin)  -> 0x08  got=0x08 want=0x08
PASS  E0 0x1D (RCtrl) -> 0x10  got=0x10 want=0x10
PASS  E0 0x38 (RAlt)  -> 0x40  got=0x40 want=0x40
PASS  E0 0x5C (RWin)  -> 0x80  got=0x80 want=0x80
PASS  E0 0x48 (Up)    -> 0x00  got=0x00 want=0x00
PASS  E0 flag, mk only 0x1D -> 0x10  got=0x10 want=0x10
TOTAL: 13/13 passed, 0 failed
exitcode=0
```

### ScancodeMap harness（29/29；E0 0x1D/0x38/0x5B/0x5C 断言 -1，新增 Menu 0x65）

```
PASS  nonE0 0x01 (Esc)       -> 0x29 got=0x29 want=0x29
PASS  nonE0 0x0E (Backspace) -> 0x2A got=0x2A want=0x2A
PASS  nonE0 0x1C (Enter)     -> 0x28 got=0x28 want=0x28
PASS  nonE0 0x1E (A)         -> 0x04 got=0x04 want=0x04
PASS  nonE0 0x39 (Space)     -> 0x2C got=0x2C want=0x2C
PASS  nonE0 0x3B (F1)  -> 0x3A got=0x3A want=0x3A
PASS  nonE0 0x44 (F10) -> 0x43 got=0x43 want=0x43
PASS  nonE0 0x57 (F11) -> 0x44 got=0x44 want=0x44
PASS  nonE0 0x58 (F12) -> 0x45 got=0x45 want=0x45
PASS  nonE0 0x0C (-)  -> 0x2D got=0x2D want=0x2D
PASS  nonE0 0x0D (=)  -> 0x2E got=0x2E want=0x2E
PASS  nonE0 0x1A ([)  -> 0x2F got=0x2F want=0x2F
PASS  nonE0 0x27 (;)  -> 0x33 got=0x33 want=0x33
PASS  nonE0 0x3A (CapsLock) -> 0x39 got=0x39 want=0x39
PASS  E0 0x1D (RCtrl)   -> -1 got=-1 want=-1
PASS  E0 0x38 (RAlt)    -> -1 got=-1 want=-1
PASS  E0 0x5B (LWin)    -> -1 got=-1 want=-1
PASS  E0 0x5C (RWin)    -> -1 got=-1 want=-1
PASS  E0 0x48 (Up)      -> 0x52 got=0x52 want=0x52
PASS  E0 0x4B (Left)    -> 0x50 got=0x50 want=0x50
PASS  E0 0x53 (Delete)  -> 0x4C got=0x4C want=0x4C
PASS  E0 0x47 (Home)    -> 0x4A got=0x4A want=0x4A
PASS  E0 0x4F (End)     -> 0x4D got=0x4D want=0x4D
PASS  E0 0x1C (NumEnter)-> 0x58 got=0x58 want=0x58
PASS  E0 0x5D (Menu)    -> 0x65 got=0x65 want=0x65
PASS  nonE0 0x2A (LShift) -> -1 got=-1 want=-1
PASS  nonE0 0x36 (RShift) -> -1 got=-1 want=-1
PASS  nonE0 0x1D (LCtrl)  -> -1 got=-1 want=-1
PASS  nonE0 0x38 (LAlt)   -> -1 got=-1 want=-1
TOTAL: 29/29 passed, 0 failed
exitcode=0
```

### 两端构建

命令：`powershell -ExecutionPolicy Bypass -File build/build-agent.ps1`、`powershell -ExecutionPolicy Bypass -File build/build-injector.ps1`

```
编译 10 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (26.5 KB)
已复制 pckvm.jar 到 dist\
exitcode=0
```

```
javac（--release 8，会打印弃用警告，正常）...
d8 转 dex...
推送到设备...
C:\Users\mazhewen\AppData\Local\Temp\pckvm.jar: 1 file pushed, 0 skipped. 24.4 MB/s (5683 bytes in 0.000s)
完成: /data/local/tmp/pckvm.jar
exitcode=0
```

PC 侧零警告；设备侧 javac/d8 零错误、push 成功（一次通过，本轮无需 adb 重启）。`Program.cs` 本轮未动，仍 349 行。

## 无法验证

`KeyState.apply` 的真机行为（/dev/uhid 依赖）与端到端左 Win/修饰键手感——留给设备验收。仍未运行 `dist\pc-kvm.exe`。
