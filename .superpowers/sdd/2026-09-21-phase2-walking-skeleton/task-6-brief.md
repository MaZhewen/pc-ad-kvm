### Task 6: 边缘状态机与坐标映射

**产出**：光标推到配置的外侧边缘时进入 `TAKEOVER`，虚拟光标映射到手机屏；在手机屏上把光标推回邻接边缘时退出 `TAKEOVER`。**此任务仍不做抑制**（PC 键盘鼠标照常工作），先把状态机与几何跑通。

**Files:**
- Create: `src/agent/EdgeTracker.cs`
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 5 的 `CursorModel`
- Produces:
  - `EdgeTracker`：`public enum State { Idle, Takeover }`
    - `public EdgeTracker(int edgeX, int edgeTop, int edgeBottom, int phoneW, int phoneH, bool phoneIsRightOfPc)`
    - `public State Current { get; }`
    - `public event Action<short, short> EnterTakeover`（入屏点，手机屏坐标）
    - `public event Action LeaveTakeover`
    - `public void OnMouseMoved(int dx, int dy, int cursorX, int cursorY)` → 返回 `bool handled`（true 表示该事件已被接管、不应再转发给设备）
    - `public void OnIdleMove(int cursorX, int cursorY)`

- [ ] **Step 1: 编写 EdgeTracker**

创建 `src/agent/EdgeTracker.cs`：

```csharp
using System;

namespace PcKvm
{
    public enum KvmState { Idle, Takeover }

    /// <summary>
    /// 边缘跨越状态机与坐标映射。
    /// 只在 IDLE 态读真实光标位置（此时未被 ClipCursor 冻结）；进入 TAKEOVER 后 PC 光标不再可用，
    /// 一切位置都由 CursorModel 承担。
    /// </summary>
    public class EdgeTracker
    {
        readonly int _edgeX;            // 触发边界的 x（屏幕像素）
        readonly int _edgeTop;
        readonly int _edgeBottom;
        readonly int _phoneW;
        readonly int _phoneH;
        readonly bool _phoneRight;      // true = 手机在 PC 右侧（此时从手机左边缘入屏）

        public KvmState Current { get; private set; }
        public bool Armed { get; private set; }   // 回程冷却：离开边缘安全带后才重新武装

        public event Action<short, short> EnterTakeover;
        public event Action LeaveTakeover;

        public EdgeTracker(int edgeX, int edgeTop, int edgeBottom,
                           int phoneW, int phoneH, bool phoneRight)
        {
            _edgeX = edgeX;
            _edgeTop = edgeTop;
            _edgeBottom = edgeBottom;
            _phoneW = phoneW;
            _phoneH = phoneH;
            _phoneRight = phoneRight;
            Current = KvmState.Idle;
            Armed = true;
        }

        /// <summary>IDLE 态下、每次鼠标事件调用。cursorX/Y 为真实光标位置。</summary>
        public void OnIdleMove(int dx, int dy, int cursorX, int cursorY)
        {
            const int SAFE = 12;   // 安全带宽度，防止回来瞬间被弹回去

            if (!Armed)
            {
                bool away = _phoneRight
                    ? cursorX < _edgeX - SAFE
                    : cursorX > _edgeX + SAFE;
                if (away) Armed = true;
                return;
            }

            if (cursorY < _edgeTop || cursorY >= _edgeBottom) return;

            bool atEdge;
            bool pushingOut;
            if (_phoneRight)
            {
                atEdge = cursorX >= _edgeX;
                pushingOut = dx > 0;
            }
            else
            {
                atEdge = cursorX <= _edgeX;
                pushingOut = dx < 0;
            }
            if (!atEdge || !pushingOut) return;

            // 入屏点用比例映射，保证 PC 边缘顶端 → 手机顶端
            int span = _edgeBottom - _edgeTop;
            int phoneY = (int)((long)(cursorY - _edgeTop) * _phoneH / span);
            if (phoneY < 0) phoneY = 0;
            if (phoneY > _phoneH - 1) phoneY = _phoneH - 1;
            int phoneX = _phoneRight ? 0 : _phoneW - 1;

            Current = KvmState.Takeover;
            Armed = false;
            Action<short, short> h = EnterTakeover;
            if (h != null) h((short)phoneX, (short)phoneY);
        }

        /// <summary>TAKEOVER 态下、每次鼠标事件调用，传入游标模型算出的实际增量。</summary>
        public void OnTakeoverMove(int actualDx, int actualDy, int vx, int vy)
        {
            if (Current != KvmState.Takeover) return;

            // 回程判定：虚拟光标撞到与 PC 相邻的那条边
            bool backAtEdge = _phoneRight ? vx <= 0 : vx >= _phoneW - 1;
            if (!backAtEdge) return;

            // 必须仍在继续往外推，否则光标停在边上就会立刻回程
            bool pushingBack = _phoneRight ? actualDx < 0 : actualDx > 0;
            if (!pushingBack) return;

            Current = KvmState.Idle;
            Armed = false;          // 回到 IDLE 后先解除武装，等光标离开安全带
            Action h = LeaveTakeover;
            if (h != null) h();
        }
    }
}
```

**Task 5B 带来的修正（必须实现）**：

(a) 手机尺寸必须能跟随旋转变化。把 `readonly int _phoneW;` / `readonly int _phoneH;` **去掉 `readonly`**，并追加：

```csharp
        /// <summary>屏幕旋转/尺寸变化时更新手机逻辑尺寸。</summary>
        public void SetPhoneSize(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            _phoneW = w;
            _phoneH = h;
        }
```

(b) 构造时 `phoneW: 2136, phoneH: 3200` 只是**占位初值**（Task 5B 之后真值来自运行时读取）。真值会在连接后首次鼠标移动、以及之后每次旋转变化时通过 `SetPhoneSize` 灌进来。**必须在构造处写注释说明这是占位值**，以免后人以为它是权威常量。

- [ ] **Step 2: 在 Program.cs 里接入状态机**

**Task 5B 带来的修正（必须实现，否则会静默丢掉旋转恢复与连接守卫）**：

下面这个版本已经把 Task 5B 引入的几何变化消费、以及连接守卫一并整合好了。**照抄，不要退回上面那种写法**：

```csharp
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                mc++;
                if (mc % 50 == 0)
                    log.WriteLine("MOUSE dx=" + e.Dx + " dy=" + e.Dy
                        + " btn=0x" + e.ButtonFlags.ToString("X4") + " wheel=" + e.WheelDelta);

                // 未连接时 cursor 为 null：此时绝不能让状态机跑起来，
                // 否则推到边缘会触发 EnterTakeover → cursor.Reset() 空引用（异常被
                // RawInput 的 catch{} 吞掉，表现为状态机卡死在 TAKEOVER 且无任何报错）
                if (cursor != null)
                {
                    if (_geometryChanged)
                    {
                        _geometryChanged = false;
                        cursor.SetBounds(_phoneW, _phoneH);
                        tracker.SetPhoneSize(_phoneW, _phoneH);
                        log.WriteLine("# 几何已应用 " + _phoneW + "x" + _phoneH);
                        transport.Send(Protocol.EncodeHome());
                        cursor.Reset();
                    }

                    if (tracker.Current == KvmState.Idle)
                    {
                        POINT p;
                        GetCursorPos(out p);
                        tracker.OnIdleMove(e.Dx, e.Dy, p.X, p.Y);
                    }
                    else
                    {
                        short sdx = cursor.NextDx((int)(e.Dx * sensitivity));
                        short sdy = cursor.NextDy((int)(e.Dy * sensitivity));
                        if (sdx != 0 || sdy != 0)
                            transport.Send(Protocol.EncodeMove(sdx, sdy));
                        tracker.OnTakeoverMove(sdx, sdy, cursor.X, cursor.Y);
                    }
                }

                short wheel = (short)(e.WheelDelta / 120);
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                if (e.ButtonFlags != 0)
                {
                    EmitButton(transport, e.ButtonFlags, 0x0001, 1);
                    EmitButton(transport, e.ButtonFlags, 0x0004, 2);
                    EmitButton(transport, e.ButtonFlags, 0x0010, 3);
                }
            };
```

**声明顺序**：`EdgeTracker tracker = ...` 必须**在** `transport.Connected` 订阅之前声明（C# 局部变量不能前向引用——Task 4 已踩过这个坑）。本任务的处理器在 `if (cursor != null)` 之后才用 `tracker`，所以只要 tracker 的声明早于 `ri.MouseMoved +=` 这一行即可。

在 `Main` 里加字段与装配（**放在 transport 装配之后**）：

```csharp
            double sensitivity = 1.0;
            // 目标机几何：手机挂在 DISPLAY1（1920,0,1920x1080）的右侧，故边界 x = 3840
            EdgeTracker tracker = new EdgeTracker(
                edgeX: 3840, edgeTop: 0, edgeBottom: 1080,
                phoneW: 2136, phoneH: 3200, phoneRight: true);

            tracker.EnterTakeover += delegate(short px, short py)
            {
                log.WriteLine("# ENTER takeover at phone(" + px + "," + py + ")");
                transport.Send(Protocol.EncodeHome());
                cursor.Reset();
                cursor.SetPosition(px, py);
                transport.Send(Protocol.EncodeEnter(px, py));
            };
            tracker.LeaveTakeover += delegate
            {
                log.WriteLine("# LEAVE takeover");
                transport.Send(Protocol.EncodeLeave());
            };
```

需要 `using System.Runtime.InteropServices;` 与在本类里声明：

```csharp
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }
```

**注意**：`EncodeHome` 之后 `cursor.SetPosition(px, py)` —— 设备侧执行 HOME 时把光标顶到 `(0,0)`，随后 `EncodeEnter` 只是把设备侧的光标位置"告知"用于后续对齐；**Task 5 之后设备侧尚未处理 ENTER**，所以本任务的实际效果是：进入接管时光标从手机左上角开始。这是可接受的中间状态，Task 7 会补齐。

- [ ] **Step 3: 验证**

Run: `dist\pc-kvm.exe`

判定：
1. 把鼠标推到**最右边显示器（DISPLAY1）的右边缘并继续往右推** → `pc-kvm.log` 出现 `# ENTER takeover at phone(0,<y>)`，且 `y` 随你在边缘的高度变化 ✅
2. 继续移动鼠标 → 手机上光标移动；**PC 光标不动**（此时 PC 光标被 Windows 钳在边缘） ✅
3. 把手机光标**推回左边缘并继续往左推** → 日志出现 `# LEAVE takeover` ✅
4. **回到 IDLE 后立刻再往右推** → **不应立刻再次 ENTER**（回程冷却生效），需先把光标往左移开一段再推 ✅
5. 在**左边那块显示器（DISPLAY2）的最左边缘**推 → **不应触发**（手机挂在右侧） ✅

- [ ] **Step 4: 提交**

```bash
cd /g/pc-kvm
git add src/agent/EdgeTracker.cs src/agent/Program.cs
git commit -m "feat: 边缘状态机与坐标映射 —— 比例入屏、回程冷却、虚拟光标钳制"
```

---

