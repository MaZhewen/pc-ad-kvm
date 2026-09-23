### Task 5: 推角落归零

**产出**：每次进入接管状态前，把手机光标**硬顶到 `(0,0)`**，使 PC 端的虚拟光标模型与系统真光标强制对齐。

**为什么必须做**：阶段一实测确认，UHID 虚拟设备销毁/重建后**光标位置持续存在**。不归零会导致两边永久错位，且症状隐蔽（表现为"鼠标飘了"，而不是明显故障）。

**Files:**
- Create: `src/agent/CursorModel.cs`
- Modify: `src/injector/Injector.java`
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 3 的 `Protocol` / `Transport`
- Produces:
  - `CursorModel`：`public CursorModel(short phoneW, short phoneH)`；`public int X { get; }` / `public int Y { get; }`；`public void Reset()`（把模型置 0,0）；`public short NextDx(int target)` / `NextDy(int target)`（返回**钳制后**应发的增量，并同步更新模型）
  - 协议新增：`Protocol.EncodeHome()` → 类型 `0x0A`，无负载。设备侧收到后把光标推回 `(0,0)`。

- [ ] **Step 1: 协议加 HOME 消息**

在 `Protocol.cs` 加常量与方法：

```csharp
        public const byte MsgHome = 0x0A;   // 无负载：让设备把光标硬顶到 (0,0)
```

```csharp
        public static byte[] EncodeHome()
        {
            return new byte[] { MsgHome };
        }
```

并在 `PayloadLength` 的 `switch` 里加 `case MsgHome: return 0;`。

- [ ] **Step 2: 设备侧实现 HOME**

在 `Injector.java` 加常量 `static final byte MSG_HOME = 0x0A;`（放进已有的常量声明行组），在 `payloadLength` 加 `case MSG_HOME: return 0;`，并在 `handle` 里加分支：

```java
        } else if (type == MSG_HOME) {
            // 8 位相对轴每报告最多走 127，按屏幕尺寸算够用的次数硬顶到左上角。
            // 3200 像素需要 ceil(3200/127)=26 次；取 40 次留余量，代价是几十毫秒。
            for (int i = 0; i < 40; i++) {
                dev.sendMouse((byte) buttonsDown, (byte) -127, (byte) -127, (byte) 0);
            }
        }
```

- [ ] **Step 3: 编写光标模型**

创建 `src/agent/CursorModel.cs`：

```csharp
namespace PcKvm
{
    /// <summary>
    /// PC 端持有的虚拟光标（坐标系 = 手机屏幕像素）。
    /// 职责：把任意目标增量钳制成"不会越界"的实际发送量，并同步更新自身位置。
    /// 之所以要钳制：设备侧的系统光标会停在屏幕边缘，若我们照发全额增量，两边就会越走越远。
    /// </summary>
    public class CursorModel
    {
        readonly int _w;
        readonly int _h;

        public int X { get; private set; }
        public int Y { get; private set; }

        public CursorModel(int phoneW, int phoneH)
        {
            _w = phoneW;
            _h = phoneH;
            X = 0;
            Y = 0;
        }

        /// <summary>归零。必须在每次进入接管、且设备侧已执行 HOME 之后调用。</summary>
        public void Reset()
        {
            X = 0;
            Y = 0;
        }

        /// <summary>直接定位（用于跨越入屏）。不发送任何东西，只改模型。</summary>
        public void SetPosition(int x, int y)
        {
            X = Clamp(x, 0, _w - 1);
            Y = Clamp(y, 0, _h - 1);
        }

        /// <summary>返回实际应发送的 dx（已钳制到不越界），并按它更新模型。</summary>
        public short NextDx(int want)
        {
            int nx = Clamp(X + want, 0, _w - 1);
            int actual = nx - X;
            X = nx;
            return (short)actual;
        }

        /// <summary>返回实际应发送的 dy（已钳制到不越界），并按它更新模型。</summary>
        public short NextDy(int want)
        {
            int ny = Clamp(Y + want, 0, _h - 1);
            int actual = ny - Y;
            Y = ny;
            return (short)actual;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
```

- [ ] **Step 4: 在 Program.cs 接入归零流程**

**本任务先做一个可验证的最小接法**：程序启动、设备连上后，自动执行一次 HOME 并归零模型，然后进入"鼠标事件走模型"的模式。

在 `transport.Connected` 处理器里追加：

```csharp
            cursor = new CursorModel(2136, 3200);   // 目标机手机分辨率，后续任务改成从 CONFIG 协商
            cursorResetPending = true;
```

新增两个字段在 `Main` 开头：

```csharp
            CursorModel cursor = null;
            bool cursorResetPending = false;
```

并把 `MouseMoved` 处理器里的 `EncodeMove` 那行改成：

```csharp
                if (cursorResetPending)
                {
                    transport.Send(Protocol.EncodeHome());
                    cursor.Reset();
                    cursorResetPending = false;
                }
                short sdx = cursor.NextDx(e.Dx);
                short sdy = cursor.NextDy(e.Dy);
                if (sdx != 0 || sdy != 0)
                    transport.Send(Protocol.EncodeMove(sdx, sdy));
```

- [ ] **Step 5: 验证 —— 反复归零不漂移**

Run: `dist\pc-kvm.exe`

判定（**这一条是本任务的全部意义**）：
1. 移动鼠标，手机光标动 ✅
2. **把手机光标推到屏幕任意位置，然后重启 `pc-kvm.exe`** → 手机光标应**回到左上角**并跟随鼠标 ✅
3. **反复重启 5 次**，每次都归零、每次都从左上角开始，位置不累积漂移 ✅
4. 把手机光标**推到屏幕右下角**后再重启 → 仍能归零到左上角（证明 40 次 −127 的位移量足够覆盖整屏） ✅

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add src/agent/CursorModel.cs src/agent/Protocol.cs src/agent/Program.cs src/injector/Injector.java
git commit -m "feat: 推角落归零 —— 修复 UHID 光标位置跨设备重建持续存在导致的模型错位"
```

---

