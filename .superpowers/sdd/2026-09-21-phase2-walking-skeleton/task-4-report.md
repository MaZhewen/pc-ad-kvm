# Task 4 报告：最小闭环 —— PC 鼠标直接驱动手机光标

状态：DONE（代码/构建/链路健康验证完成；六项人工观察待人类执行）

## 已实现

修改 `G:\pc-kvm\src\agent\Program.cs`，内容与 task-4-brief.md Step 1 完全一致：

1. `ri.MouseMoved` 处理器：在保留原有降频日志之外，追加
   - `transport.Send(Protocol.EncodeMove((short)e.Dx, (short)e.Dy))`
   - `short wheel = (short)(e.WheelDelta / 120);`（Windows 一格=120 → HID 一格=1），非 0 时 `EncodeScroll((short)0, wheel)`
   - `ButtonFlags != 0` 时调用三次 `EmitButton`：左 `0x0001, 1`、右 `0x0004, 2`、中 `0x0010, 3`
2. 新增辅助方法 `static void EmitButton(Transport t, ushort flags, ushort downBit, byte btn)`（置于 `Main` 之后）：`upBit = downBit << 1`，先判 down 再判 up，各发一次 `EncodeButton(btn, 1/0)`。

### 一处必要的非代码性重排

原文件中 `Transport transport = new Transport(...)` 声明在 `MouseMoved` 处理器**之后**。C# 局部变量只在声明点之后可见，lambda 前向引用会编译失败。因此把 transport 创建 + `Start()` 失败检查块整体**上移到两个事件处理器注册之前**（含注释说明）。行为语义不变：transport 创建失败仍弹窗退出，只是发生在注册 Raw Input 处理器之前（此窗口期内本来就无连接可发送）。

## 构建命令与输出

```
powershell -ExecutionPolicy Bypass -File build/build-injector.ps1
powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
```

输出（GBK 控制台显示为乱码，实际内容）：
- build-injector: javac --release 8 → d8 dex → adb push 到 `%TEMP%\pckvm.jar`（5266 bytes），并推送 `/data/local/tmp/pckvm.jar`
- build-agent: `编译 5 个源文件... 编译成功: G:\pc-kvm\dist\pc-kvm.exe (15.5 KB)`，`已复制 pckvm.jar 到 dist\`
- 单独重跑 build-agent 并过滤：**零 warning、零 error**，退出码 0。

## 启动证据（dist\pc-kvm.log）

```
# 启动 2026-09-21 18:19:44
# 设备已连接
# PONG seq=1
```

`# 设备已连接` + `# PONG seq=1` 证明改动后 TCP 链路/adb reverse 隧道仍然健康。之后已 `taskkill /IM pc-kvm.exe /F` 关闭（托盘程序，无法以编程方式点托盘菜单，属强制关闭）。

## 变更文件

- `G:\pc-kvm\src\agent\Program.cs`（唯一改动，+29/-7）
- 提交：`a57081b feat: 最小闭环 —— 移动 PC 鼠标即驱动手机光标`（分支 `phase2-skeleton`）

## 自审

- 代码与 brief 逐字一致（除上述必要重排）；无任何 brief 之外的新增功能。
- C# 6+ 语法扫描（`$"`, `?.`, `=>`, `nameof`, inline out）：`src/` 内唯一命中是 `RawInput.cs:141` 的一行**注释**（"C# 5 没有 ?."），非代码。本次改动无违禁语法。
- EmitButton 位对验证：调用点 `0x0001/0x0004/0x0010`，`downBit << 1` 推得 up 位 `0x0002/0x0008/0x0020`，与 Windows RAWINPUT 的 RI_BUTTON 左/右/中 down-up 位对完全一致。
- 滚轮 `/120` 存在。
- `.ps1` 未改动，无 BOM 风险；未引入全局键盘钩子。

## 关注点

- 强杀进程会跳过 `ApplicationExit` 清理（tray/transport/adb reverse）。`DeviceLauncher.Prepare` 每次启动会重建隧道，故下次启动自愈；但若人工观察时发现连不上，先跑一次 `adb kill-server && adb start-server`。
- 零位移事件：PC 静止不动时 Windows 不会发 Raw Input 事件，故光标不会漂移；与计划中"精确化验证 2"的说明一致。

## 人工观察步骤（必须由人执行）

前置：手机通过 USB 连接并已开 USB 调试（当前 `adb devices` 显示 `04053891899C1540 device`），Task 3/4 构建已跑过。

1. 双击运行 `G:\pc-kvm\dist\pc-kvm.exe`（托盘出现图标，无窗口，这是正常的）。
2. **移动 PC 鼠标** → 看手机屏幕：**手机光标应同步移动，方向一致**。
3. **快速甩动鼠标** → 手机光标不应"少走"（大位移经 int8 拆分，验证拆分逻辑）。
4. **滚轮上、下一格** → 手机页面内容应滚动。
5. **左键点击** → 手机上光标所在位置被点击。
6. **右键、中键** → 同样生效（右键菜单/中键行为）。
7. **PC 本身一切照常**：本任务无输入抑制，PC 鼠标行为应与平时完全一样，同时手机光标镜像移动。
8. 观察完，右键托盘图标 → 退出（不要任务管理器强杀，以便走清理路径）。

任何一项不符，把现象（方向反/不动/多走少走/按键无效）记下并回报。
