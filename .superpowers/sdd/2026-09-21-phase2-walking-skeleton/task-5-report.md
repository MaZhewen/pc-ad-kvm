# Task 5 报告：推角落归零

## 实现内容

按 brief 完成了全部 4 个文件改动（Step 1–4），Step 5 需要人工观察，按指示停止。

1. **`src/agent/Protocol.cs`**
   - 新增常量 `MsgHome = 0x0A`
   - 新增 `EncodeHome()`（无负载，单字节）
   - `PayloadLength` 的 switch 加 `case MsgHome: return 0;`

2. **`src/injector/Injector.java`**
   - 常量组加 `MSG_HOME = 0x0A`
   - `payloadLength` 加 `case MSG_HOME: return 0;`
   - `handle` 加 `MSG_HOME` 分支：循环 40 次发送 `(dx=-127, dy=-127)` 的鼠标报告，把光标硬顶到左上角。40×127=5080 像素，覆盖 3200 高的屏幕（需 26 次）并留余量。

3. **`src/agent/CursorModel.cs`（新建）**
   - 按 brief 逐字创建。`NextDx`/`NextDy` 钳制到 `[0, w-1]` / `[0, h-1]`，返回**钳制后的实际增量**（非请求值），并用实际值更新模型——这是防止模型与真光标分道扬镳的关键。`SetPosition`/`Reset`/`Clamp` 均按 brief。

4. **`src/agent/Program.cs`**
   - `Main` 开头（事件处理器声明之前）加局部变量 `CursorModel cursor = null;` 和 `bool cursorResetPending = false;`
   - `transport.Connected` 处理器追加：`cursor = new CursorModel(2136, 3200); cursorResetPending = true;`
   - `MouseMoved` 处理器按 Ruling 5 整体改写：未连接设备时（cursor == null）不再发送 move；连接后首个鼠标事件先 `Send(EncodeHome())` + `cursor.Reset()` + 清 pending 标志，然后走 `NextDx`/`NextDy` 钳制，`sdx==0 && sdy==0` 时不发包。保留了原有的降频日志（`mc % 50`）、滚轮分发、按钮分发逻辑，均未动。

## 构建命令与输出

```
powershell -ExecutionPolicy Bypass -File G:\pc-kvm\build\build-injector.ps1
powershell -ExecutionPolicy Bypass -File G:\pc-kvm\build\build-agent.ps1
```

输出（零警告零错误）：
```
javac（--release 8，会打印弃用警告，正常）...
d8 转 dex...
推送到设备...
...pckvm.jar: 1 file pushed ... 完成: /data/local/tmp/pckvm.jar
编译 6 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (16.5 KB)
已复制 pckvm.jar 到 dist\
```
注：源文件数从 5 → 6，确认 `CursorModel.cs` 被通配符收进编译。

## 启动证据

`adb devices` 确认设备 `04053891899C1540 device` 在线（无需 kill-server）。
启动 `dist\pc-kvm.exe`，等待 6 秒后读取日志：

```
# 启动 2026-09-21 18:40:15
# 设备已连接
# PONG seq=1
MOUSE dx=0 dy=1 btn=0x0000 wheel=0
...
```

`# 设备已连接` 和 `# PONG seq=1` 均出现，随后正常关闭进程（日志有 `# 退出` / `# 设备已断开`，说明 Exit 清理路径正常）。

## 人工验证步骤（我无法执行，需人来跑）

1. 运行 `dist\pc-kvm.exe`，移动 PC 鼠标 → 手机光标应跟随移动。
2. 把手机光标推到屏幕任意位置，然后退出并重启 `pc-kvm.exe` → 重启后第一次移动鼠标时，手机光标应**回到左上角 (0,0)** 再跟随鼠标。
3. 反复重启 5 次，每次都应归零、从左上角开始，位置不累积漂移（这是本任务的核心判据）。
4. 把手机光标推到**屏幕右下角**后重启 → 仍应归零到左上角（证明 40 次 × -127 = 5080 像素的硬顶量足够覆盖 2136×3200 整屏）。

观察要点：如果第 2 步光标没有回到左上角，说明 HOME 消息没到达设备（查 jar 是否已推送新版：`build-injector.ps1` 会自动 push）；如果回了一半，说明循环次数不够（但 40 次理论上有近一倍余量）。

## 文件变更

- 修改：`G:\pc-kvm\src\agent\Protocol.cs`
- 修改：`G:\pc-kvm\src\agent\Program.cs`
- 修改：`G:\pc-kvm\src\injector\Injector.java`
- 新建：`G:\pc-kvm\src\agent\CursorModel.cs`

## 自查结果

- [x] C# `PayloadLength` 与 Java `payloadLength` 都加了 `MsgHome`/`MSG_HOME → 0`，两处一致（都返回 0，都是 0x0A）。
- [x] Java HOME 循环 40 次：40×127=5080 > 3200（需 26 次），余量充足。
- [x] `CursorModel.NextDx/NextDy` 钳制在 `[0, w-1]`/`[0, h-1]` 内，返回**钳制后实际增量**，模型用实际值更新。
- [x] 全文扫描 `.cs` 禁用语法（`$"`、`?.`、`=>`、`nameof`、`using static`、`out var`）：仅 RawInput.cs 第 141 行一条**注释**提到 `?.`，无实际违规代码。
- [x] 无 brief 之外的多余改动；日志/按钮/滚轮逻辑保持原样；未触碰任何 `.ps1`。

## 顾虑

- `Program.cs` 中 `cursor == null` 时（设备未连接）不再发送 move 包——这是正确行为（之前发往无连接 transport 也是空转），但行为上有变化，记录在此。
- 手机分辨率 `2136×3200` 为硬编码（brief 明确说后续任务改成 CONFIG 协商），若换机型需同步改。
- 本机有三套 adb server 竞争的老毛病，本次未触发；若他人复现验证时 `adb devices` 为空，先 `adb kill-server && adb start-server`。
