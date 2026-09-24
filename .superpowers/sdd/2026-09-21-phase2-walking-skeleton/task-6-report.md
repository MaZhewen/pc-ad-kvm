# Task 6 报告：边缘状态机与坐标映射

## 实现内容

### `src/agent/EdgeTracker.cs`（新建，106 行）
- `public enum KvmState { Idle, Takeover }`
- `EdgeTracker` 类：按 brief Step 1 代码逐字实现，含修正 (a)/(b)：
  - **(a)** `_phoneW` / `_phoneH` 去掉 `readonly`（字段注释标明可变原因），新增 `SetPhoneSize(int w, int h)`（`w<=0 || h<=0` 时直接 return）。
  - **(b)** 构造函数 doc 注释明确写明 `phoneW/phoneH` 为**占位初值（竖屏 2136x3200）非权威常量**，真值经 `SetPhoneSize` 灌入。
- 实现的接口（遵循代码而非 brief 的陈旧 Interfaces 列表，按任务指示）：
  - `void OnIdleMove(int dx, int dy, int cursorX, int cursorY)`
  - `void OnTakeoverMove(int actualDx, int actualDy, int vx, int vy)`
  - `event Action<short, short> EnterTakeover`、`event Action LeaveTakeover`
  - `State Current`、`bool Armed`、构造参数 `edgeX/edgeTop/edgeBottom/phoneW/phoneH/phoneRight`
- 逻辑要点：12px 安全带回程冷却（`Armed`）、垂直带外忽略、比例入屏映射（`(long)(cursorY-_edgeTop) * _phoneH / span`，`long` 防溢出，钳到 `[0, _phoneH-1]`）、回程需同时满足"虚拟光标在相邻边"且"继续往外推"。

### `src/agent/Program.cs`（修改，186 → 230 行，红线 250 以内）
- `using System.Runtime.InteropServices;` + `GetCursorPos` DllImport + `POINT` 结构（`Program` 类内，已确认与 `RawInput.cs` 现有结构体无命名冲突）。
- `double sensitivity = 1.0;`（**未调参**，按计划留给设备实测手调）与 `EdgeTracker tracker = new EdgeTracker(...)` 声明放在 `CursorModel cursor = null;` 之后、`ri.MouseMoved +=` 之前（声明顺序陷阱，Task 4 教训）。
- `tracker.EnterTakeover` / `tracker.LeaveTakeover` 订阅紧随 tracker 声明（在 transport 装配之后、MouseMoved 订阅之前，符合 brief 的放置要求且满足可见性）。
- `ri.MouseMoved` 处理器：**照抄 brief Step 2 的整合版**——保留 Task 5B 的 `_geometryChanged` 消费并在其中加 `tracker.SetPhoneSize(_phoneW, _phoneH)`；保留 `if (cursor != null)` 连接守卫（防止 EnterTakeover → cursor.Reset() 空引用被 RawInput catch{} 吞掉导致状态机静默卡死）；IDLE 态 `GetCursorPos` 后喂 `OnIdleMove`，TAKEOVER 态经 `sensitivity` 缩放喂 `cursor.NextDx/NextDy` 并把实际增量与虚拟坐标喂 `OnTakeoverMove`。滚动/按钮转发逻辑不动。
- `EnterTakeover` 订阅按 brief：HOME → `cursor.Reset()` → `cursor.SetPosition(px,py)` → `EncodeEnter`（设备侧未处理 ENTER 属预期中间状态，Task 7 补齐）。

## 变更文件
- `G:\pc-kvm\src\agent\EdgeTracker.cs`（新建）
- `G:\pc-kvm\src\agent\Program.cs`（修改）

## 验证（手机离线，仅离线验证）

### (A) 构建
命令：`powershell -ExecutionPolicy Bypass -File build/build-agent.ps1`（于 `G:\pc-kvm`）
实际输出：
```
编译 7 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (21.0 KB)
已复制 pckvm.jar 到 dist\
```
exit 0，**零警告**。

### (B) 行数
`Program.cs` 实际 230 行（`Measure-Object -Line` 报 205 为非空行数），低于 250 红线。

### (C) 离线状态机测试（harness 在 `%TEMP%\edgetest\`，未触碰 `src/agent/`）
编译命令：
`csc.exe -nologo -target:exe -out:%TEMP%\edgetest\edgetest.exe %TEMP%\edgetest\Main.cs G:\pc-kvm\src\agent\EdgeTracker.cs`（csc 退出 0，零警告）

每个用例均用全新 tracker，构造参数 `edgeX:3840, edgeTop:0, edgeBottom:1080, phoneW:3200, phoneH:2136, phoneRight: true`。

实际 stdout：
```
PASS T1  state=Takeover enterCount=1 px=0 py=1068 (expect Takeover,1,0,1068)
PASS T2  state=Takeover px=0 py=0 (expect Takeover,0,0)
PASS T3  state=Takeover py=2134 (expect Takeover, py in [2130,2135])
PASS T4  state=Idle enterCount=0 (expect Idle,0)
PASS T5  state=Idle enterCount=0 (expect Idle,0)
PASS T6  below(y=1080):True above(y=-1):True state=Idle enterCount=0 (expect both Idle,0)
PASS T7  leave:True noReenter:True armedAfter3800:True reenter:True state=Takeover enterCount=2 leaveCount=1 (expect all True, Takeover,2,1)
PASS T8  midOk:True wrongWayOk:Troo wrongWayOk:True state=Takeover leaveCount=0 (expect both True, Takeover,0)
TOTAL: pass=8 fail=0
```
（注：上面 T8 行首词为誊抄笔误，程序真实输出为 `PASS T8  midOk:True wrongWayOk:True state=Takeover leaveCount=0 (expect both True, Takeover,0)`；`TOTAL: pass=8 fail=0` 为原样输出，harness 退出码 0。）

8/8 PASS，退出码 0。

## 提交
`5a06c23` feat: 边缘状态机与坐标映射 —— 比例入屏、回程冷却、虚拟光标钳制
（仅含 `src/agent/EdgeTracker.cs` 与 `src/agent/Program.cs`；工作区里原有的 docs 改动未纳入。）

## 自查发现
1. 声明顺序已核对：`tracker` 声明（L77）早于 `ri.MouseMoved +=`（L95），晚于 `cursor`（L72）。
2. 接口签名按代码而非陈旧 Interfaces 列表实现；harness 调用的正是这两个签名。
3. C# 5 合规：无内联 out 声明（`POINT p; GetCursorPos(out p);` 为经典写法）、无 `?.`/`$""`/表达式体成员；`volatile` 仅用于字段。
4. `sensitivity = 1.0` 未调；构造占位值在两处（EdgeTracker.cs doc 注释 + Program.cs 行内注释）均标明。
5. T3 实测 py=2134（数学上 1079×2136/1080=2134），在要求区间 [2130,2135] 内且 < phoneH=2136。

## 顾虑
- 无阻塞性顾虑。设备相关验证（brief Step 3 全部 5 项）因手机没电未执行，属任务说明中明确豁免部分，留待设备可用后由计划方安排。
- 观察项（非本任务范围、计划已知）：进入 TAKEOVER 后 PC 光标未冻结（本任务不做抑制），期间真实光标仍会被 Windows 钳在 3840 边缘，LeaveTakeover 后冷却逻辑依赖该位置，实测时需关注手感。

---

# 修轮 1/5 报告（审查 Important x2 修复）

审查结论：Needs fixes —— 0 Critical、2 Important、3 Minor。两条 Important 均为 brief 逐字强制的代码缺陷（plan-mandated），协调者已独立复现并改好计划正文，本修轮按其 3 条裁决执行。

## 改了什么

### Important 1 —— 入口条件在真机上不可达（`EdgeTracker.cs` + `Program.cs`）
- **约定变更**：`_edgeX` 从"边界虚线 3840"改为"**触发侧最外侧有效像素列的 x**"（右挂 = 3839，左挂 = 0）。3840x1080 虚拟桌面上 Windows 把光标钳在 x=3839，`GetCursorPos` 永远返回不了 3840，旧约定使 `atEdge = cursorX >= 3840` 永假。
- `EdgeTracker.cs`：`_edgeX` 字段注释与构造函数 doc 注释均写明新约定及左右对称性；`atEdge`、安全带判定、`SetPhoneSize` 逻辑未动（新约定下天然对称，无需分侧特判）。
- `Program.cs`：构造处改传 `edgeX: 3839`，行注释说明可达性原因。

### Important 2 —— 回程判定用了钳制后的增量（`EdgeTracker.cs` + `Program.cs`）
- `OnTakeoverMove` 形参改名 `actualDx/actualDy → rawDx/rawDy`，方向判定 `pushingBack = _phoneRight ? rawDx < 0 : rawDx > 0`。旧实现用 `CursorModel.NextDx` 的钳后输出：`EnterTakeover` 总把虚拟光标置 x=0，`NextDx` 在 x=0 对负增量恒钳成 0，"刚入屏就推回"永远触发不了 `LeaveTakeover`。
- `Program.cs` 调用点改为 `tracker.OnTakeoverMove(e.Dx, e.Dy, cursor.X, cursor.Y);`（原始增量=用户意图），并加注释说明语义；钳后 `sdx/sdy` 仍只用于协议发送。保留 4 参形态未瘦身（设计明写"只做左右边缘"，rawDy/vy 留作后续）。
- 方法 doc 注释写入协调者裁决的语义："原始增量表达用户意图，钳制后增量表达实际位移，回程判定要的是前者。"

### 未做（按裁决）
- 审查第 3 条 Important（掉线时 tracker 卡 TAKEOVER）归 Task 8 的 `tracker.AbortTakeover()`，本修轮不碰。

## Harness 更正与补充（`%TEMP%\edgetest\Main.cs`）
- 构造统一改为 `edgeX: 3839`（真实可达几何）。
- T1 `cursorX` 3840→**3839**（真实可达最大值）；T4 "不在边缘" 3839→**3838**；T2/T3/T5/T6/T7/T8 同步改 3839。
- **新增 T9**（直接回归）：入屏后从入口 `OnTakeoverMove(-3, 0, 0, 1068)` → 必须 `Idle` + `LeaveTakeover`（旧实现在此输入下 leaveCount 恒 0）。
- **新增 T10**（反向保护）：入屏后 `OnTakeoverMove(0, 0, 0, 1068)` → 不得回程（无外推意图）。T9/T10 输入与生产接线一致：`rawDx = e.Dx` 原始增量。
- T7 说明注记其输入现均可达（rawDx 语义下入口推回即 T9 路径）。

## 验证

### 构建
`powershell -ExecutionPolicy Bypass -File build/build-agent.ps1`（于 `G:\pc-kvm`）：
```
编译 7 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (21.0 KB)
已复制 pckvm.jar 到 dist\
```
exit 0，零警告。`Program.cs` 234 行（红线 250 内）。

### Harness（10 用例）
编译：`csc.exe -nologo -target:exe -out:%TEMP%\edgetest\edgetest.exe %TEMP%\edgetest\Main.cs G:\pc-kvm\src\agent\EdgeTracker.cs`（退出 0，零警告）
实际 stdout：
```
PASS T1  state=Takeover enterCount=1 px=0 py=1068 (expect Takeover,1,0,1068)
PASS T2  state=Takeover px=0 py=0 (expect Takeover,0,0)
PASS T3  state=Takeover py=2134 (expect Takeover, py in [2130,2135])
PASS T4  state=Idle enterCount=0 (expect Idle,0)
PASS T5  state=Idle enterCount=0 (expect Idle,0)
PASS T6  below(y=1080):True above(y=-1):True state=Idle enterCount=0 (expect both Idle,0)
PASS T7  leave:True noReenter:True armedAfter3800:True reenter:True state=Takeover enterCount=2 leaveCount=1 (expect all True, Takeover,2,1)
PASS T8  midOk:True wrongWayOk:True state=Takeover leaveCount=0 (expect both True, Takeover,0)
PASS T9  state=Idle leaveCount=1 (expect Idle,1)
PASS T10 state=Takeover leaveCount=0 (expect Takeover,0)
TOTAL: pass=10 fail=0
```
harness 退出码 0。T1 证明新约定下可达坐标 3839 能入屏；T9 证明原不可达的回程路径已通；T10 证明零意图不误触发。

## 提交
`c2e0dbf` fix(agent): 修审查 Important x2 —— edgeX 改为可达末列 3839、回程方向判定改用原始增量
（仅 `src/agent/EdgeTracker.cs` + `src/agent/Program.cs`；计划文件与 Task 8 内容未动。）

## 顾虑
- 无阻塞性顾虑。设备实测仍待手机可用（与本轮前相同豁免）。
- 观察项沿袭：未做抑制期间 PC 真实光标被钳在 3839，回程后安全带 [3827,3839] 生效逻辑已由 T7 覆盖，真机手感待验。

