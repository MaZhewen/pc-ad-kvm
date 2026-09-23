### Task 5B: 手机显示几何 —— 运行时读取 + 轮询检测旋转（插队，必须先于 Task 6）

**为什么插队**：Task 5 把手机分辨率硬编码成 `2136×3200`（竖屏），本计划自审记录里也把「`CONFIG` 不接线、分辨率硬编码」记为刻意 YAGNI。**2026-09-21 实测推翻了这条 YAGNI**：

- `dumpsys input` 里我们设备的 `Cursor Input Mapper` 实测 `Motion Ranges: X 0–3199 / Y 0–2135`，即手机当时是**横屏**（`mRotation=ROTATION_90`，逻辑尺寸 3200×2136），而模型是竖屏。
- 同一份 dump 里 `XScale: 1.000 / YScale: 1.000`，且**没有** `PointerVelocityControlParameters`（而滚轮参数 `WheelYVelocityControlParameters` 打印了）→ 指针加速不作用于本设备，**增量与像素是 1:1**。所以漂移不是加速造成的，就是几何量错。
- 后果：模型 X 上限 2135 < 真实 3199 → 向右推时模型先撞上限**停发包**，真光标冻在屏幕 2/3 处的**"虚拟边界"**；模型 Y 上限 3199 > 真实 2135 → 向下推时真光标顶住下边缘、模型继续空走，记账错位，之后左/上的停点就随错位量漂移。
- 用户实测症状「向左/上推到某个位置就不再移动，像有虚拟边界，且边界不固定」与上述机制吻合。

**产出**：几何不再硬编码。连接时从手机读**真实逻辑尺寸**；接管期间每 2 秒轮询一次，一旦发现尺寸/旋转变化就把模型与真光标**重新 HOME 归零对齐**。

**Files:**
- Modify: `src/agent/DeviceLauncher.cs`（加 `RunAdbCapture` + `QueryDisplay` + 纯解析 `ParseDisplaySize`）
- Modify: `src/agent/CursorModel.cs`（加 `SetBounds`）
- Modify: `src/agent/Program.cs`（接线 + 轮询线程 + 在 MouseMoved 里消费几何变化）

**Interfaces:**
- `DeviceLauncher.QueryDisplay(out int w, out int h)` → `bool`：跑一次 adb 取回"物理尺寸 + 旋转"，算出**逻辑尺寸**（旋转 90/270 时 W/H 互换）。失败返回 false，且**不改动** out。
- `DeviceLauncher.ParseDisplaySize(string adbOutput, out int w, out int h)` → `bool`：**纯函数，无任何 I/O**。用真实 adb 输出直接离线测（见验证步骤）。
- `CursorModel.SetBounds(int w, int h)` → `void`：更新边界并把 X/Y 钳进新边界。

**采样到的真实 adb 输出（直接当测试向量）**：

```
$ adb shell "wm size; dumpsys window displays 2>/dev/null | grep -o mRotation=[A-Z0-9_]* | head -1"
Physical size: 2136x3200
mRotation=ROTATION_90
```
横屏 → 期望 `3200x2136`。

- [ ] **Step 1: DeviceLauncher 加取几何**

在 `src/agent/DeviceLauncher.cs` 里**追加**（不要改动已有的 `RunAdb` 与 `Prepare`）：

```csharp
        /// <summary>跑 adb 并把 stdout 取回。返回退出码；失败返回 -1。</summary>
        static int RunAdbCapture(string args, out string stdout)
        {
            stdout = "";
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                Process p = Process.Start(psi);
                stdout = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        /// <summary>读取手机当前逻辑屏幕尺寸（已按旋转换算）。失败返回 false，且不改动 out。</summary>
        public static bool QueryDisplay(out int w, out int h)
        {
            w = 0; h = 0;
            string outp;
            if (RunAdbCapture("shell \"wm size; dumpsys window displays 2>/dev/null"
                              + " | grep -o mRotation=[A-Z0-9_]* | head -1\"", out outp) != 0)
                return false;
            return ParseDisplaySize(outp, out w, out h);
        }

        /// <summary>纯函数：把 adb 输出解析成逻辑尺寸。旋转 90/270 时宽高互换。</summary>
        public static bool ParseDisplaySize(string adbOutput, out int w, out int h)
        {
            w = 0; h = 0;
            if (adbOutput == null) return false;

            int ow = 0, oh = 0;      // Override size 优先
            int pw = 0, ph = 0;      // Physical size 兜底
            int rot = -1;

            string[] lines = adbOutput.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.StartsWith("Override size:"))
                    ParseWxH(s.Substring(14), ref ow, ref oh);
                else if (s.StartsWith("Physical size:"))
                    ParseWxH(s.Substring(14), ref pw, ref ph);
                else if (s.StartsWith("mRotation="))
                    rot = ParseRotation(s.Substring(10));
            }

            int baseW = ow > 0 ? ow : pw;
            int baseH = ow > 0 ? oh : ph;
            if (baseW <= 0 || baseH <= 0 || rot < 0) return false;
            if (rot == 90 || rot == 270) { w = baseH; h = baseW; }
            else { w = baseW; h = baseH; }
            return true;
        }

        static void ParseWxH(string s, ref int w, ref int h)
        {
            int i = s.IndexOf('x');
            if (i <= 0) return;
            int a, b;
            if (!int.TryParse(s.Substring(0, i).Trim(), out a)) return;
            if (!int.TryParse(s.Substring(i + 1).Trim(), out b)) return;
            if (a <= 0 || b <= 0) return;
            w = a; h = b;
        }

        static int ParseRotation(string s)
        {
            if (s == "ROTATION_0") return 0;
            if (s == "ROTATION_90") return 90;
            if (s == "ROTATION_180") return 180;
            if (s == "ROTATION_270") return 270;
            return -1;
        }
```

- [ ] **Step 2: CursorModel 支持改边界**

`src/agent/CursorModel.cs`：把 `readonly int _w;` / `readonly int _h;` **去掉 `readonly`**（改成 `int _w; int _h;`），并追加：

```csharp
        /// <summary>屏幕旋转/尺寸变化时更新边界，并把当前位置钳进新边界。</summary>
        public void SetBounds(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            _w = w;
            _h = h;
            if (X > _w - 1) X = _w - 1;
            if (Y > _h - 1) Y = _h - 1;
        }
```

- [ ] **Step 3: Program.cs 接线 + 轮询线程**

**(a)** 在 `Program` 类里加三个**静态字段**（必须是字段：`volatile` 不能修饰局部变量，而这三个量要被轮询线程写、UI 线程读）：

```csharp
        static volatile int _phoneW = 0;
        static volatile int _phoneH = 0;
        static volatile bool _geometryChanged = false;
```

**(b)** 把 `transport.Connected` 处理器里的 `cursor = new CursorModel(2136, 3200);` 换成：

```csharp
            transport.Connected += delegate
            {
                log.WriteLine("# 设备已连接");
                int dw, dh;
                if (DeviceLauncher.QueryDisplay(out dw, out dh))
                {
                    _phoneW = dw; _phoneH = dh;
                    log.WriteLine("# 屏幕几何 " + dw + "x" + dh);
                }
                else
                {
                    _phoneW = 2136; _phoneH = 3200;   // 查询失败时的保守回退
                    log.WriteLine("# 屏幕几何查询失败，回退 " + _phoneW + "x" + _phoneH);
                }
                cursor = new CursorModel(_phoneW, _phoneH);
                _geometryChanged = true;   // 首次鼠标移动时消费：SetBounds + HOME 归零
                transport.Send(Protocol.EncodePing(1));
            };
```

**同时删除 `cursorResetPending`**：把 `CursorModel cursor = null;` 旁边的 `bool cursorResetPending = false;` 字段与它在 `MouseMoved` 里的分支一并删掉。理由：它要表达的语义（"连接后第一次移动时归零对齐"）已被 `_geometryChanged` 完全覆盖，而且原字段是**跨线程未同步**的普通 bool（Task 5 的审查已把它挂为 deferred minor），现在换成 `volatile` 的 `_geometryChanged` 顺带修掉。这是 Ruling 4（"T6 替换处理器时删掉 cursorResetPending"）的**提前执行**——机制更好，时机提前一个任务。

**(c)** 在 `MessageHost`/托盘装配**之前**启动轮询线程：

```csharp
            System.Threading.Thread rotPoll = new System.Threading.Thread(delegate()
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (!transport.IsConnected) continue;
                    int w, h;
                    if (!DeviceLauncher.QueryDisplay(out w, out h)) continue;   // 掉线时静默跳过，不刷日志
                    if (w != _phoneW || h != _phoneH)
                    {
                        _phoneW = w; _phoneH = h;
                        _geometryChanged = true;
                    }
                }
            });
            rotPoll.IsBackground = true;
            rotPoll.Start();
```

**(d)** 在 `MouseMoved` 里消费几何变化——把现有的 `if (cursorResetPending) {...}` 那一小段**整体替换**为（注意：`cursorResetPending` 已在 (b) 里删除，这里不再出现）：

```csharp
                    if (_geometryChanged)
                    {
                        _geometryChanged = false;
                        cursor.SetBounds(_phoneW, _phoneH);
                        log.WriteLine("# 几何已应用 " + _phoneW + "x" + _phoneH);
                        transport.Send(Protocol.EncodeHome());
                        cursor.Reset();
                    }
```

**实现者必读（本任务特有的坑）**：

1. **C# 5**：`out` 变量必须先声明再传（`int a; Foo(out a);`），**不要**写 `out int a`（C# 7 内联声明，本机 csc 编不过——本计划 Task 7 就踩过）。禁用 `?.`、`$""`、表达式体成员。
2. **`volatile` 只能修饰字段**，所以 `_phoneW/_phoneH/_geometryChanged` 必须是 `Program` 的**静态字段**，不能是 `Main` 的局部变量。
3. **轮询必须无条件运行**（只判 `IsConnected`）：Task 6 才有 IDLE/TAKEOVER 状态机，本任务**不要**自己造状态机。
4. **轮询在后台线程做 I/O，UI 线程只消费标志位**。绝不要在 `Timer.Tick`（UI 线程）里跑 adb——那会阻塞 Raw Input 消息泵。
5. 掉线时 `QueryDisplay` 会失败：**静默 `continue`，不要打日志**，否则每 2 秒一条会把日志刷爆。
6. `Program.cs` 体量红线 **250 行**（Ruling 3）：改完若超线，报 `DONE_WITH_CONCERNS`，不要自行拆分。
7. **不要动 `RunAdb`**：它已被 3 处调用且有已评审的行为，只新增 `RunAdbCapture`。
8. 本任务**不涉及输入路径**，所以 GameViewer.exe 开不开都不影响本任务的验证。

- [ ] **Step 4: 编译**

Run: `powershell -ExecutionPolicy Bypass -File build/build-agent.ps1`
Expected: `编译成功`，`$LASTEXITCODE` 为 0，**零警告**。

- [ ] **Step 5: 离线测试纯解析（本任务可在无手机时完成的验证）**

`ParseDisplaySize` 是无 I/O 纯函数，**不需要手机**即可测。在 `%TEMP%` 下建一个一次性测试程序（**不要放进 `src/agent/`**，那个目录的 `*.cs` 会被构建脚本全部编进 exe）：

测试向量（第 1 条是我本次实测抓到的真实输出）：

| 输入 | 期望 |
|---|---|
| `"Physical size: 2136x3200\nmRotation=ROTATION_90\n"` | `true, 3200x2136` |
| `"Physical size: 2136x3200\nmRotation=ROTATION_0\n"` | `true, 2136x3200` |
| `"Physical size: 2136x3200\nmRotation=ROTATION_270\n"` | `true, 3200x2136` |
| `"Physical size: 2136x3200\nOverride size: 1080x1920\nmRotation=ROTATION_0\n"` | `true, 1080x1920`（Override 优先） |
| `"Override size: 1080x1920\nPhysical size: 2136x3200\nmRotation=ROTATION_90\n"` | `true, 1920x1080`（顺序颠倒也成立） |
| `"Physical size: 2136x3200\r\nmRotation=ROTATION_90\r\n"` | `true, 3200x2136`（CRLF） |
| `""` | `false` |
| `"Physical size: 2136x3200\n"`（无旋转行） | `false` |
| `"error: no devices/emulators found\n"` | `false` |
| `"Physical size: 2136x3200\nmRotation=ROTATION_LEFT\n"` | `false`（未知旋转值） |

编译方式（把 `DeviceLauncher.cs` 与测试的 Main 一起编，`DeviceLauncher` 只依赖 `System.Diagnostics`/`System.IO`，可独立编译）：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:exe `
    -out:"$env:TEMP\geotest\geotest.exe" `
    "$env:TEMP\geotest\Main.cs" "$root\src\agent\DeviceLauncher.cs"
```

判定：10 条向量全部符合期望 → 通过。

- [ ] **Step 6: 提交**

```bash
cd /g/pc-kvm
git add src/agent/DeviceLauncher.cs src/agent/CursorModel.cs src/agent/Program.cs
git commit -m "fix(agent): 显示几何改为运行时读取 + 轮询检测旋转，修复硬编码竖屏导致的虚拟边界"
```

**本任务**不做**的事（等手机回线后另行验收，不属于本任务范围）**：

- 真机上横屏/竖屏下的实际光标行为（Task 5 的复验）
- 旋转时 `# 旋转变化 → 重新归零对齐` 的实机验证

---

