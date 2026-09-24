### Task 9: 键盘支持

**产出**：`TAKEOVER` 期间按 PC 键盘，字符出现在**手机上**（而不是 PC 上）。

**Files:**
- Create: `src/agent/KeyMap.cs`（Ruling 26：`ModifierBit` 放这里，不塞进已 315 行的 `Program.cs`）
- Modify: `src/agent/Program.cs`
- Modify: `src/injector/ScancodeMap.java`
- Modify: `src/injector/KeyState.java`
- Modify: `src/injector/Injector.java`（Step 3 要补设备侧的 `MSG_LEAVE` 分支——**目前它还是空的**，见下方注意）

**Interfaces:**
- Consumes: Task 3 的 `Protocol.EncodeKey`、Task 2 的 `UhidDevice.sendKeyboard`
- Produces: 无新接口

- [ ] **Step 1: 补齐 scancode 映射表**

修改 `src/injector/ScancodeMap.java`，把 Task 3 的骨架表补全：方向键、Home/End/PgUp/PgDn/Insert/Delete、F1–F12、左右 Ctrl/Shift/Alt/Win（E0 前缀）、小键盘（E0 前缀的数字区）、`-=`、`[]`、`;'`、`` ` ``、`\,./`。

E0 前缀键的写法（示例，按同样方式补齐其余）：

```java
    public static int toHidUsage(int scancode) {
        boolean e0 = (scancode & 0xE000) == 0xE000;
        int mk = scancode & 0xFF;

        if (e0) {
            switch (mk) {
                case 0x1C: return 0x58;   // 小键盘 Enter
                case 0x1D: return 0xE4;   // 右 Ctrl
                case 0x35: return 0x54;   // 小键盘 /
                case 0x37: return 0x46;   // PrintScreen
                case 0x38: return 0xE6;   // 右 Alt
                case 0x47: return 0x4A;   // Home
                case 0x48: return 0x52;   // Up
                case 0x49: return 0x4B;   // PgUp
                case 0x4B: return 0x50;   // Left
                case 0x4D: return 0x4F;   // Right
                case 0x4F: return 0x4D;   // End
                case 0x50: return 0x51;   // Down
                case 0x51: return 0x4E;   // PgDn
                case 0x52: return 0x49;   // Insert
                case 0x53: return 0x4C;   // Delete
                case 0x5B: return 0xE3;   // 左 Win
                case 0x5C: return 0xE7;   // 右 Win
                case 0x5D: return 0x65;   // Menu
                default:   return -1;
            }
        }
        // ... 原有的非 E0 表，再加上：
        switch (mk) {
            case 0x0C: return 0x2D;   // -
            case 0x0D: return 0x2E;   // =
            case 0x1A: return 0x2F;   // [
            case 0x1B: return 0x30;   // ]
            case 0x27: return 0x33;   // ;
            case 0x28: return 0x34;   // '
            case 0x29: return 0x35;   // `
            case 0x2B: return 0x31;   // backslash
            case 0x33: return 0x36;   // ,
            case 0x34: return 0x37;   // .
            case 0x35: return 0x38;   // /
            case 0x3A: return 0x39;   // CapsLock
            case 0x3B: return 0x3A; case 0x3C: return 0x3B; case 0x3D: return 0x3C;
            case 0x3E: return 0x3D; case 0x3F: return 0x3E; case 0x40: return 0x3F;
            case 0x41: return 0x40; case 0x42: return 0x41; case 0x43: return 0x42;
            case 0x44: return 0x43;                       // F1-F10
            case 0x57: return 0x44; case 0x58: return 0x45;   // F11, F12
            case 0x45: return 0x53;   // NumLock
            case 0x46: return 0x48;   // ScrollLock
            default:   return -1;
        }
    }
```

**修饰键注意**：左右 Shift/Ctrl/Alt 在 HID 里是报告中的独立位（`0x02/0x20` = 左/右 Shift，`0x01/0x10` = 左/右 Ctrl，`0x04/0x40` = 左/右 Alt，`0x08/0x80` = 左/右 GUI），**不是** 6 个按键槽位里的普通键。因此 `ScancodeMap.toHidUsage` 对修饰键应返回 `-1`，由 **PC 侧**把它们折算进 `mods` 字节随每条 KEY 消息下发。

- [ ] **Step 2: PC 侧维护修饰键状态**

**新建 `src/agent/KeyMap.cs`，把下面的 `ModifierBit` 放进这个新文件的 `public static class KeyMap` 里**（Ruling 26：`Program.cs` 已 315 行、红线 360，纯映射不该再往里堆；放独立文件也便于离线测试）。调用点相应写成 `KeyMap.ModifierBit(e.Scancode, e.IsE0)`。新文件需自带 `using`（本方法只用内建类型，`namespace PcKvm` 即可）。

```csharp
        /// <summary>把 Windows scancode 映射为 HID 修饰位（不是普通键）。返回 0 表示不是修饰键。</summary>
        public static byte ModifierBit(int scancode, bool isE0)
        {
            int mk = scancode & 0xFF;
            if (!isE0)
            {
                if (mk == 0x2A) return 0x02;   // 左 Shift
                if (mk == 0x36) return 0x20;   // 右 Shift
                if (mk == 0x1D) return 0x01;   // 左 Ctrl
                if (mk == 0x38) return 0x04;   // 左 Alt
                if (mk == 0x5B) return 0x08;   // 左 Win
            }
            else
            {
                if (mk == 0x1D) return 0x10;   // 右 Ctrl
                if (mk == 0x38) return 0x40;   // 右 Alt
                if (mk == 0x5C) return 0x80;   // 右 Win
            }
            return 0;
        }
```

把 `KeyChanged` 处理器替换为：

```csharp
            ri.KeyChanged += delegate(RawKeyEvent e)
            {
                byte bit = ModifierBit(e.Scancode, e.IsE0);
                if (bit != 0)
                {
                    if (e.IsUp) modifiers &= (byte)~bit; else modifiers |= bit;
                    // 修饰键变化也要发一条报告，否则手机侧修饰态不更新
                    if (tracker.Current == KvmState.Takeover)
                        transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
                    return;
                }

                if (tracker.Current != KvmState.Takeover) return;   // IDLE 态不转发，PC 正常用
                transport.Send(Protocol.EncodeKey((ushort)e.Scancode, (byte)(e.IsUp ? 0 : 1), modifiers));
            };
```

并在 `Main` 里加字段：

```csharp
            byte modifiers = 0;
```

**声明位置**：必须放在 `int mc = 0, kc = 0;` 那一组旁边（即 `ri.KeyChanged += ...` 订阅**之前**）。`KeyChanged` 处理器会捕获并修改它，而 C# 局部变量不支持前向引用（Task 4 与 Task 7 的 `supp` 都栽在这上面）。

- [ ] **Step 2b: 把滚轮与鼠标键的转发也门控在 TAKEOVER 上（控制方 Ruling 27，必做）**

**这是一个既有缺陷，不是本任务新增的。** Task 4 写 `MouseMoved` 时把滚轮与三个鼠标键的转发放在**状态判定之外**，Task 6 引入状态机时也没有把它们收进去。结果：**在 IDLE 态（也就是用户正常使用 PC 的全部时间）里，每一次点击与每一格滚轮都会被转发到手机**，作用在手机光标停留的位置——用户可能因此误开手机 App、误触按钮，而且完全无法"正常用 PC 而不影响手机"。

键盘那侧 Task 9 已经写对了（`if (tracker.Current != KvmState.Takeover) return;`），鼠标这侧要补齐，让"IDLE 态一个字节都不发给设备"这条不变量对**所有**输入类型成立。

把 `MouseMoved` 处理器末尾那段改为整体包在状态门控里：

```csharp
                // 滚轮与鼠标键只在接管期转发：IDLE 态用户是在操作 PC，
                // 此时转发会在手机光标停留处产生误点击/误滚动（既有缺陷，Ruling 27）
                if (cursor != null && tracker.Current == KvmState.Takeover)
                {
                    short wheel = (short)(e.WheelDelta / 120);   // Windows 一格 = 120，HID 一格 = 1
                    if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                    if (e.ButtonFlags != 0)
                    {
                        EmitButton(transport, e.ButtonFlags, 0x0001, 1);   // 左
                        EmitButton(transport, e.ButtonFlags, 0x0004, 2);   // 右
                        EmitButton(transport, e.ButtonFlags, 0x0010, 3);   // 中
                    }
                }
```

注意：`cursor != null && tracker.Current == KvmState.Takeover` 里的 `cursor != null` 是必需的（未连接时 `cursor` 为 null，`tracker` 的状态机本就不该跑）。改完后 `MouseMoved` 里应当**没有任何**在 IDLE 态发包的路径——请在报告里逐条确认。

**注意**：`modifiers` 被 lambda 捕获并修改，C# 5 下需要它是**局部变量而非字段**（lambda 捕获局部变量是 C# 3 特性，可以）。若编译器报错，改为用一个 `byte[] modifiersBox = new byte[1];` 包装。

- [ ] **Step 3: 设备侧在退出接管时清空按键**

**注意（Task 8 实现者发现，控制方确认）**：这个 `MSG_LEAVE` 分支**目前还不存在**——`Injector.java` 的 `handle()` 结尾仍是注释"`MSG_ENTER` / `MSG_LEAVE` / `MSG_CONFIG` 由后续任务接管"。因此：

- Task 8 在两处**放弃**路径（逃逸键、心跳超时）里补发的 `Protocol.EncodeLeave()` 目前是**空操作**，Ruling 25 的"防手机侧按键残留"意图要**等你这一步落地才真正生效**；
- 更重要的是：**在那之前，手机侧的 `buttonsDown` 从来没有被清零过**——用户按着鼠标键时无论走哪条路径退出，手机上都会留下一个"按住的键"。所以本步骤不是可选的锦上添花，而是补上一个既有的功能缺口。
- 协议侧无风险：`payloadLength(MSG_LEAVE)` 早已是 0，PC 发送的 LEAVE 帧能被正确解析、只是被忽略。

在 `Injector.java` 的 `handle` 里给 `MSG_LEAVE` 加分支——**防止修饰键卡在按下态**（这是遥控类软件的经典 bug：接管期间按下 Ctrl，退出时没抬起，之后手机上一直是 Ctrl 生效）：

```java
        } else if (type == MSG_LEAVE) {
            buttonsDown = 0;
            dev.sendMouse((byte) 0, (byte) 0, (byte) 0, (byte) 0);
            KeyState.releaseAll(dev);
        }
```

在 `KeyState.java` 加：

```java
    /** 全部抬起：清修饰键与所有按键槽位，各发一条报告。 */
    public static void releaseAll(UhidDevice dev) throws Exception {
        modifiers = 0;
        for (int i = 0; i < 6; i++) slots[i] = 0;
        dev.sendKeyboard((byte) 0, new byte[6]);
    }
```

- [ ] **Step 4: 验证**

Run: `dist\pc-kvm.exe`，打开记事本点进去确认能打字。

1. 进入 `TAKEOVER`，在手机上打开一个可输入的应用（如备忘录），**用 PC 键盘打字** → 字符出现在**手机**上 ✅
2. `Shift+A` → 出大写 `A` ✅
3. `Ctrl+A` 全选、`Ctrl+C/V` 复制粘贴 ✅
4. 方向键、`Home`/`End`、`Backspace`/`Delete` ✅
5. **修饰键不粘连**：按住 `Ctrl` 进入再退出接管 → 退出后手机上不应有 Ctrl 残留（在手机上再打字验证） ✅
6. **IDLE 态键盘照常**：退出接管后，在 PC 记事本打字 → 正常输入 ✅

- [ ] **Step 5: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs src/injector/ScancodeMap.java src/injector/KeyState.java src/injector/Injector.java
git commit -m "feat: 键盘支持 —— scancode 映射、修饰键状态、退出时清空按键"
```

---

## 自审记录

**Spec 覆盖检查**（对照 `2026-09-21-pc-android-kvm-design.md`）：

| Spec 要求 | 覆盖任务 |
|---|---|
| 第 4 节 架构：PC 侧 7 部件 + 手机侧零安装 | Task 1–3（文件结构即按此拆分） |
| 第 6 节 状态机 IDLE ⇄ TAKEOVER | Task 6 |
| 第 6 节 边缘判定「贴边且继续外推」 | Task 6 Step 1 |
| 第 6 节 回程冷却 | Task 6 Step 1（`Armed` 字段） |
| 第 7 节 PC 端持有虚拟光标 + 钳制 | Task 5（`CursorModel`） |
| 第 7 节 推角落归零 | Task 5 |
| 第 7 节 两套比例（去程比例映射 / 接管期 1:1） | Task 6 Step 1（比例入屏）+ Task 6 Step 2（`sensitivity` 默认 1.0） |
| 第 7 节 协议 9 种消息 | Task 3（+ Task 5 加 `MsgHome` = 第 10 种） |
| 第 7 节 `LEAVE` 由 PC 端发起 | Task 6 Step 1（`OnTakeoverMove` 在 PC 侧判定）+ Task 9 Step 3 |
| 第 7 节 键盘 scancode → HID Usage ID | Task 9 |
| 第 8 节 抑制机制（焦点小窗 + ClipCursor） | Task 7 |
| 第 8 节 不用全局钩子 | Task 7（全程无 `SetWindowsHookEx`） |
| 第 9 节 ① 断线必须立刻解除抑制 | Task 8 Step 1 |
| 第 9 节 ② 前台被抢走就漏键 | Task 7 的 `CheckForeground` + Task 8 Step 1 |
| 第 9 节 ③ 紧急逃逸键 | Task 8 Step 2 |
| 第 10 节 验证 5（推角落归零） | Task 5 Step 5 |
| 第 10 节 验证 6（端到端闭环） | Task 4（最小闭环）+ Task 6/7/8/9 逐步补全 |
| 第 1 节 不传画面、PC 上不开镜像窗口 | 全程无视频代码，Task 7 的小窗是状态指示器而非镜像 |

**未覆盖项（明确留给后续，非遗漏）**：

- **`CONFIG` 消息仍未接线（但手机几何不再硬编码）**：Task 3 定义了 `CONFIG`，`DeviceLauncher`/`Program` 里仍不发送它。原本此处写的是"手机分辨率硬编码（2136/3200），属刻意 YAGNI"——**该理由已于 2026-09-21 被实测推翻并由用户裁决撤销**：手机逻辑尺寸随旋转变（实测横屏 3200×2136 与硬编码竖屏 2136×3200 冲突，直接造成"虚拟边界"症状）。现由 **Task 5B** 在运行时从手机读取真实逻辑尺寸并每 2 秒轮询旋转变化。`CONFIG` 消息本身仍不接线（PC 侧边缘几何 `3840/0/1080` 仍是配置常量，PC 显示器不会变），这与"换手机/手机挂左侧"仍属 YAGNI 并不矛盾。
- **上/下边缘未实现**：Spec 第 3 节的实测几何决定了手机只能挂在双屏的最外侧左右，`EdgeTracker` 只实现 left/right。上下边缘留待有实际需求时再加。
- **无线（Wi-Fi）传输**：Spec 第 7 节说"先用 USB 跑通，网络问题留到最后"。本计划全程走 adb，无线不在范围内。
- **`UHID_START` 事件未读取**：阶段一发现不读也能工作。读取它能让设备侧感知就绪/销毁，但非必需。

**占位符扫描**：已通过。无 TBD / TODO / "类似 Task N" / 空泛的"适当处理错误"。每个代码步骤都给出了完整可编译的代码。

**类型一致性检查**：

- `Protocol.PayloadLength` 的返回长度与 `Encode*` 产生的帧长逐项核对：Enter 4+1=5 ✅ Move 5 ✅ Button 3 ✅ Scroll 5 ✅ Key 5 ✅ Leave 1 ✅ Config 8 ✅ Ping/Pong 5 ✅ Home 1 ✅
- PC 侧 `Protocol.MsgXxx`（byte 常量）与手机侧 `Injector.MSG_XXX`（byte 常量）取值一一对应 ✅
- `CursorModel.NextDx/NextDy` 返回 `short`，`Protocol.EncodeMove(short,short)` 消费 `short` ✅
- `EdgeTracker.EnterTakeover` 是 `Action<short,short>`，`Program` 里的处理器签名 `delegate(short px, short py)` 匹配 ✅
- `Suppressor.Engage()` 返回 `bool`，`Program` 里 `if (!supp.Engage())` 匹配 ✅
- `KeyState.apply(UhidDevice, int, boolean, int)` 与 `Injector.handle` 里的 `KeyState.apply(dev, u16(p,0), p[2]!=0, p[3])` 匹配 ✅
- `HidDescriptor.MOUSE_REPORT_SIZE`(5) 与 `UhidDevice.sendMouse` 写入的字节数（report id + buttons + dx + dy + wheel = 5）一致 ✅
- `HidDescriptor.KEYBOARD_REPORT_SIZE`(9) 与 `sendKeyboard` 写入的字节数（report id + mods + reserved + 6 = 9）一致 ✅
- `EdgeTracker.AbortTakeover()` 在 Task 7 引入并被 `Program` 调用；Task 6 的类定义中未包含，Task 7 Step 2 明确要求补上 ✅

**已知的实现风险（实现者需留意）**：

1. **Task 7 是唯一有系统级副作用的任务**，`ClipCursor` 失控会锁死用户鼠标。三条安全不变量必须原样实现。
2. **Task 6 的中间状态**：Task 5 之后设备侧尚未处理 `ENTER`，所以进入接管时光标从手机左上角开始而非入屏点。这是刻意的中间状态，不要为此提前实现 `ENTER`。
3. **`sensitivity` 为 1.0 只是起点**。Raw Input 的 `lLastX/lLastY` 是 mickeys 而非像素，Windows 指针加速不作用于它，真实手感需在 Task 6 验证时手调后写回常量。
