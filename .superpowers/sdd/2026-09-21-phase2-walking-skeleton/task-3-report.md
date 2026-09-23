# Task 3 报告：协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端

日期：2026-09-21
分支：phase2-skeleton
提交：`9488386 feat: 协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端 + adb reverse 隧道`

## 实现内容

按 brief 原文逐字转录，含 Ruling 1 补齐的三个 `Files:` 块遗漏文件，共九件：

**新建**
- `src/agent/Protocol.cs` — 消息常量 0x01–0x09、Encode* 系列、定长 PayloadLength 查表、LE 读写助手
- `src/agent/Transport.cs` — TCP 服务端（`IPAddress.Loopback:27183`），单连接、断线回 Accept 重等、NoDelay、线程安全 Send
- `src/agent/DeviceLauncher.cs` — `adb reverse` + `adb push`（Prepare）、`app_process` 拉起（Start）、Kill + rm jar + reverse --remove（Cleanup）
- `src/injector/StdinMode.java` — Task 2 的 stdin 循环原样搬入，类改名 `StdinMode`，入口 `public static void run()`
- `src/injector/KeyState.java` — 修饰键 + 6 槽位按键状态，变化时发键盘报告
- `src/injector/ScancodeMap.java` — Windows scancode → HID Usage（字母/数字/常用符号/Esc/Tab/Enter/Space 等）

**修改**
- `src/injector/Injector.java` — 主路径改为 socket 客户端（连 `127.0.0.1:27183`，100 次 ×100ms 重试）；`--stdin` 走 StdinMode；handle() 处理 MOVE/BUTTON/SCROLL/KEY/PING（回 PONG）
- `src/agent/Program.cs` — 托盘装配前插入 Transport 启动 → DeviceLauncher.Prepare → Start，Connected 时发 `EncodePing(1)`，收到 Pong 写日志；ApplicationExit 增加 `transport.Stop(); DeviceLauncher.Cleanup(devProc);`
- `build/build-agent.ps1` — 末尾追加 `Copy-Item "$env:TEMP\pckvm.jar" "$dist\pckvm.jar" -Force`

## 构建命令与输出

```
> powershell -ExecutionPolicy Bypass -File build/build-injector.ps1
javac（--release 8，会打印弃用警告，正常）...
d8 转 dex...
推送到设备...
...\Temp\pckvm.jar: 1 file pushed, 0 skipped. 12.7 MB/s (5266 bytes in 0.000s)
完成: /data/local/tmp/pckvm.jar

> powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
编译 5 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (15.0 KB)
已复制 pckvm.jar 到 dist\
```

d8 未要求 `--lib`，未触碰 android.jar。

## 启动证据（dist\pc-kvm.log）

```
# 启动 2026-09-21 18:08:50
# 设备已连接
# PONG seq=1
MOUSE dx=54 dy=-2 btn=0x0000 wheel=0
```

`# 设备已连接` 与 `# PONG seq=1` 均出现，隧道双向打通。

## 关闭与清理

程序窗口隐藏，`CloseMainWindow()` 返回 False，改用 `Stop-Process` 结束，因此 ApplicationExit 里的清理未执行。已手工执行等价清理并验证：
- `adb shell rm -f /data/local/tmp/pckvm.jar`（ls 确认不存在）
- `adb reverse --remove tcp:27183`（`adb reverse --list` 为空）
- 设备侧无残留 app_process（`ps -A | grep app_process` 无输出——adb shell 被杀后远端进程随之退出）

## 自查结果

- **九件文件齐全**，内容与 brief 一致。
- **PayloadLength vs Encode 帧长**逐条核对（payload 字节数）：Enter 4 / Move 4 / Button 2 / Scroll 4 / Key 4 / Leave 0 / Config 7 / Ping 4 / Pong 4 —— 两侧完全一致（含类型字节的总帧长为 5/5/3/5/5/1/8/5/5）。
- **PC `MsgXxx` 与手机 `MSG_*` 常量**同为 0x01–0x09 同序；PING/PONG 实际往返成功，是对线格式一致性最有力的实证。
- **C# 6+ 语法扫描**：rg 查 `$"`、`?.`、`=>`、`nameof`、`using static`、内联 out 变量，仅命中 RawInput.cs 一行注释，无违规。
- **ps1 BOM**：`build-agent.ps1` 与 `build-injector.ps1` 前三字节均为 `EF BB BF`（Edit 原地修改保住了 BOM）。
- **d8 无需 --lib**：构建通过。
- **无 brief 之外的多余产物**。`Injector.java` 顶部的 `BufferedReader/InputStreamReader` import 为 brief 原文保留（javac 不报 unused import）。

## 关注点 / 偏差

1. **设备序列号与 brief 不符**：`adb devices` 显示 `04053891899C1540`，brief 写的是 `25053RP5CC`。功能不受影响（adb 单设备直接命中），但如果现场有两台手机需要注意。
2. **优雅退出只能走托盘菜单**：隐藏窗体拿不到 CloseMainWindow，外部结束进程会跳过 ApplicationExit 的隧道清理（本次已手工补清）。人类验证时请用托盘"退出"菜单关闭，以顺带验证 Cleanup 路径。
3. 本任务未接采集转发，鼠标/键盘事件仍只落日志（符合 brief：可见效果在 Task 4）。

## 人类确认隧道的操作步骤

1. 确认手机已连且 `adb devices` 有设备（若为空：`adb kill-server && adb start-server`）。
2. 依次执行：
   ```powershell
   powershell -ExecutionPolicy Bypass -File build/build-injector.ps1
   powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
   dist\pc-kvm.exe
   ```
3. 打开 `dist\pc-kvm.log`，确认出现 `# 设备已连接` 与 `# PONG seq=1`。
4. 观察手机：无新图标、无安装提示（零安装）。
5. 从系统托盘 PC-KVM 图标右键选"退出"，确认日志出现 `# 退出`，并可另行验证 `adb reverse --list` 为空、`/data/local/tmp/pckvm.jar` 已删除（Cleanup 生效）。
