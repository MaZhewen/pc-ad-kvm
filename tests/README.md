# 离线回归测试

## 共享剪贴板

`tests/clipboard/run.ps1` 汇总编码、认证会话、平台适配、缓存和接入测试。Windows 实际剪贴板用例需解锁电脑桌面；Android 逻辑用例不需要连接设备。

真实两端测试明确指定已确认设备，例如：

```powershell
./tests/clipboard/run-device.ps1 -TransportId <adb中的transport_id> -Serial <已确认的设备序列> -AdbPath <adb.exe完整路径>
```

运行前解锁两端、暂停其他剪贴板同步软件。测试使用标记文本、PNG 和文本文件，并恢复原剪贴板；不发送微信消息。它验证生产剪贴板协议和平台适配，不能替代微信与文件管理器各自的 Ctrl+V 应用验收。

应用验收工具位于 `probes/05-clipboard`：`build-paste-probes.ps1` 编译正式 UHID 键盘和剪贴板代码；`KeyboardPasteProbe` 只注入 Ctrl+V，不注入 Enter。`ClipboardMetadataProbe` 只输出格式与 URI 元数据，传入自建文件的 SHA-256 时额外校验正式适配器读取的文件字节。仅对已明确创建的测试内容运行，不用它读取用户原有文件。`ClipboardDeviceProbe` 的 `QUIT` 恢复原始剪贴板；测试时必须保持双方解锁，并手动确认预览后取消，不能把传输 ACK 当成应用验收。

## 无线连接回归

无线方案新增以下离线测试，均从当前源码编译：

```powershell
pwsh -File tests\connection\run.ps1
pwsh -File tests\coordinator\run.ps1
pwsh -File tests\connection-form\run.ps1
pwsh -File tests\transport\run.ps1
pwsh -File tests\session\run.ps1
pwsh -File tests\direct-network\run.ps1
pwsh -File tests\direct-bootstrap\run.ps1
pwsh -File tests\direct-daemon\run.ps1
```

这些用例覆盖设备身份、部署事务、owner 锁、DKVM v2 互通、私钥回收、剪贴板会话隔离、心跳和几何就绪。`coordinator` 使用假 ADB 与真实本机 TLS 验证 ADB 离线时重连。`direct-network`、`coordinator`、`transport` 和 `session` 需要本机套接字权限，`connection-form` 需要 Windows 桌面会话。真机步骤见[验证记录](../docs/validation/2026-11-direct-network.md)。

三个纯逻辑 harness，**不需要手机、不需要 adb、不需要用户在场**（对比：本项目大多数验收是真机 + 人眼）。

它们存在的理由是具体的：下面这些用例里有 5 条是**真机上实测过的缺陷**的直接回归，
丢掉它们意味着同样的缺陷下次会静默复发——尤其 `EdgeTracker` 那组，失效形态是
"进接管 0–6 秒就被踢回 PC"，真机上极难定位（第一次排查靠的是翻 570 KB 日志）。

## 跑法

在仓库根目录（或任意位置）用 PowerShell：

```powershell
pwsh -File tests\edge-tracker\run.ps1
pwsh -File tests\scancode-map\run.ps1
pwsh -File tests\agent-logic\run.ps1
```

三个脚本都会**从仓库当前源码**重新编译（绝不复用 `%TEMP%` 里的预编译产物——
本项目栽过一次"验的不是用户实际会跑的那一份"），并在用例失败时以非零码退出。

## 依赖

| 用途 | 需要 |
|---|---|
| `edge-tracker` | `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（C# 5，无需 SDK） |
| `agent-logic` | 同 `edge-tracker`（`csc.exe`）；覆盖 `Config` / `MouseScaler` / `KeyMap` |
| `scancode-map` | PyCharm 自带 JDK：`C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\`（路径硬编码，与 `build\build-injector.ps1` 同一套，换机器要改两处） |

## `edge-tracker/` — `EdgeTracker` 边缘状态机

编译 `Main.cs` + `src\agent\EdgeTracker.cs` + `src\agent\CursorModel.cs`，覆盖原有边缘行为及双次贴边切换。

喂事件的方式是刻意还原 `Program.cs` 的接线顺序（先 `CursorModel.NextDx` 更新虚拟光标，
再把**原始**增量与更新后的坐标交给 tracker），而不是让用例凭空造虚拟光标坐标。
**这条纪律有来历**：Task 6 的离线用例曾喂 `cursorX=3840`——一个真实光标物理上达不到的
坐标（Windows 把虚拟桌面钳在 3839），于是"进入接管"的入口条件在真机上永远不成立，
而用例全绿。**用例的输入必须来自外部约束（API 物理边界），不能来自实现者的心智模型。**

| 用例 | 覆盖 |
|---|---|
| T1–T3 | 进入接管 + 比例映射（`540 × 2136 / 1080 = 1068`）；边界带的上下两端 |
| T4–T5 | `3838` = 距最右可达列差一列 → 不触发；在边界但朝屏内推 → 不触发 |
| T6 | 竖直安全带之外（上下两侧） |
| T7 | 回程冷却：离开安全带才重新武装 |
| T8 | 不得提前回程（屏中、反向推） |
| T9–T10 | 修轮 1（Ruling 21）：入口处**持续**推回必须能离开；`rawDx=0` 不得回程 |
| **T11** | **真机缺陷回归**：入口处微小左向抖动（`-1`）不得回程——旧实现下接管寿命 0–6 秒 |
| **T12** | 从屏内滑到左边界的那一下是"**走到**边界"，不是"**越过**边界"，不计入外推 |
| **T13** | 在边界上持续外推可离开（阈值 40 mickeys），且"走到"与"越过"可区分 |
| **T14** | 中途反向推回原边界必须把累积量清零 |
| **T15** | 审查 I1 回归：`dx=0` 的纯纵向/静止事件**既不累积也不清零**（斜推时清零会导致"回不去"） |
| **L1** | 左挂镜像（T1）：从桌面左缘入屏，落点为手机右缘 `x=3199`，y 仍比例映射 |
| **L2** | 左挂（T4 镜像）：在左缘但朝屏内推 → 不触发 |
| **L3** | 左挂（T4 镜像）：离开左缘一列（`x=1`）→ 不触发 |
| **L4** | 左挂镜像 T11：入口处微小【右】向抖动（`+1`）不得回程——阈值 40 在左挂下同样生效 |
| **L5a–L5c** | 左挂镜像 T12/T13：「走到边界」不算外推；边界上外推 ≥40 才回程；纯纵向不清零 |
| **L6a–L6b** | `SetEdge` 换边后立刻解除武装（`armed=false`），防止换边瞬间光标恰在新边缘被弹过去 |
| **D1–D14** | 双向双次贴边、12 像素离边、1.2 秒窗口、超时清除残余外推、快捷键切换，以及放弃/换边时清除等待状态 |
| **D15–D20** | 关闭贴边切换后双向均不触发，快捷键仍可进出；重新开启后旧的首次贴边不生效 |

> **为什么没有 `Suppressor`（`ClipCursor`）的 harness**：这是刻意的。一个真去调
> `ClipCursor` 的测试若中途卡住或崩掉，会让用户面对"鼠标被锁死且找不到原因"——
> 为验证安全性反而制造不安全。该文件的验收靠逐条安全不变量的人工审计 + 真机演练
> （含 `taskkill` 解锁那条）。

## `scancode-map/` — 设备侧纯逻辑（`ScancodeMap` 映射 + `PointerPlan` 位移拆分）

编译 `TestMain.java` + `src\injector\ScancodeMap.java` + `src\injector\PointerPlan.java`
（`--release 8`，与构建脚本一致），**69 条用例**。

`ScancodeMap` 覆盖：非 E0 基本键 / 功能键 / 标点 / **数字键盘 14 条** / E0 前缀键 /
四个 E0 修饰键必须返回 `-1`。

数字键盘里那 11 个数字/小数点键**必须映到数字行 usage（`0x1E`–`0x27`、`0x37`），
不能映到 Keypad usage**。这是两层缺陷叠出来的结论：第一层是整段缺失（返回 `-1` → 注入器
静默丢弃，真机现象"按了没反应"）；补上之后**仍然不产字符**，因为 Android 的
`/system/usr/keychars/Virtual.kcm` 里 `NUMPAD_0..9`/`NUMPAD_DOT` 是
`base: fallback <导航动作>` + `numlock: '<字符>'`——**只有手机自己的 NumLock 亮着才产字符**，
而本虚拟键盘的描述符里没有 LED Output 报告，那个灯永远点不亮 → `base` 永远生效 →
小键盘永远不产字符，与 PC 的 NumLock 状态无关。详见 `ScancodeMap.java` 的注释。

`PointerPlan` 覆盖：位移拆分的**各条之和必须恰好等于目标**（少一条丢位移、多一条多发空报告）、
每条 `|v| <= 127`、整倍数不多发一条、两轴取较长边、`-5080` 恰好 40 条（= 原先 HOME 硬编码的条数）。

最后一条 `ScancodeMap` 用例是不变量检查：**全表任何映射都不得超过 `0x65`**（HID 描述符按键数组的
Usage Maximum）。这条不是形式主义——左 Win 与四个 E0 修饰键都曾因为映射到 `0xE3+`
而被设备端解析器静默丢弃，症状是"能打字但某些键完全没反应"，没有任何报错。

## 剪贴板修复回归（2026-10-16）

clipboard/run.ps1 包括双语协议交叉样本、Android 适配器/会话、Windows 服务/原生适配器，以及 ACK 与原生通知锁顺序测试。PublicationMain 使用注入式平台接口验证图片准备期间发生本地复制时保留新内容，不修改系统剪贴板；也覆盖声明有文本但内容暂不可读时拒绝捕获。ACK 测试验证失败后相同内容可重试，以及迟到确认不能覆盖较新状态。

ImagesMain 验证隐藏临时目录、字节完整性、取消回滚、大小限制和无关文件保护；CacheMain 覆盖旧收据兼容、五分钟图片保留、数量/容量回收、当前引用和正在发布的数据保护。run-device.ps1 使用正式会话和平台适配器验证双向内容，并检查图片是隐藏目录中的普通媒体文件记录、未进入相册图片集合；需要两端解锁和可用的系统剪贴板，结束后恢复双方原剪贴板，不发送消息。
