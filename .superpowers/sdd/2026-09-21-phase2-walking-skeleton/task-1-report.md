# Task 1 Report — PC 侧骨架（构建脚本、托盘、Raw Input 采集）

日期：2026-09-21　分支：`phase2-skeleton`　提交：`37fac82`

## 实现内容

按 brief 逐字转写了三个文件，并全部存为 **UTF-8 with BOM**（.ps1 是硬性要求；两个 .cs 因含中文注释也加了 BOM，防 csc 误判码页）：

- `G:\pc-kvm\build\build-agent.ps1` — 从 `src\agent\*.cs` 编译 `dist\pc-kvm.exe`；csc 开关全部用 `-` 前缀（未做任何"修正"）。
- `G:\pc-kvm\src\agent\RawInput.cs` — RawInput 采集层（`NativeWindow` + `RegisterRawInputDevices`，鼠标+键盘均带 `RIDEV_INPUTSINK`）；`RAWMOUSE` 含 `_pad` 字段；事件用局部变量 + null 判断（C# 5 安全）。
- `G:\pc-kvm\src\agent\Program.cs` — 隐藏 MessageHost 窗体、NotifyIcon 托盘（右键"退出"）、日志 `dist\pc-kvm.log`（`# 启动`/MOUSE 每 50 次采样一行/KEY 每键一行/`# 退出`）。

无任何 brief 之外的文件或代码改动。

## 编译

命令：`powershell -ExecutionPolicy Bypass -File G:\pc-kvm\build\build-agent.ps1`

完整输出：

```
编译 2 个源文件...
编译成功: G:\pc-kvm\dist\pc-kvm.exe  (9.0 KB)
EXITCODE=0
```

零警告，退出码 0。exe 为 9.0 KB（brief 说"约 20 KB"，实为近似估计；单文件 winexe 9 KB 合理，无异常）。

## 启动证据

- `Start-Process G:\pc-kvm\dist\pc-kvm.exe` → 进程运行：`pid=7408 path=G:\pc-kvm\dist\pc-kvm.exe`
- `dist\pc-kvm.log` 创建，首行：`# 启动 2026-09-21 17:35:01`
- 已按指令用 `Stop-Process -Name pc-kvm` 关闭，进程确认消失。
- 日志中无 `# 退出` —— 因为是被 Stop-Process 强杀而非托盘退出，属预期；`# 退出` 的验证留给人工 Step 5 第 5 项。

## 文件变更（commit 37fac82，4 files changed, 268 insertions）

- `build/build-agent.ps1`（新增）
- `src/agent/RawInput.cs`（新增）
- `src/agent/Program.cs`（新增）
- `.gitignore`（**仅追加** 2 行：`dist/`、`*.log`；`git diff` 确认只有 `2 ++`，原 4 行未动）

## 自检结论

- 禁用 C# 6+ 语法扫描（`$"`, `?.`, `=>`, `nameof`, inline `out`）：代码中零命中；唯一 `?.` 匹配在注释文字里，无害。
- `RAWMOUSE._pad` 字段存在（RawInput.cs 第 62 行），`usButtonFlags` 落在偏移 4。
- 三个文件 BOM 均确认（`ef bb bf`）。
- 无全局键盘钩子（纯 Raw Input）；无注册表/服务/自启。

## 疑虑

- exe 实际 9.0 KB 与 brief 的"约 20 KB"不符，判定为 brief 的粗略估计，不影响功能。
- 编译时 git 提示 LF→CRLF 转换警告（4 个文件），仓库无 .gitattributes，属既有行为，未处理。

## 人工完成 Step 5 的操作步骤

双击（或命令行）运行 `G:\pc-kvm\dist\pc-kvm.exe`，然后依次验证：

1. **托盘图标**：系统托盘出现图标，悬停提示 `PC-KVM（阶段二骨架）`。
2. **鼠标**：移动物理鼠标几秒，打开 `dist\pc-kvm.log`，应出现 `MOUSE dx=.. dy=.. btn=0x0000 wheel=0` 行（每移动约 50 次才记一行，属故意的降频）。
3. **失焦采集**（关键，验证 `RIDEV_INPUTSINK`）：把焦点切到记事本等其他窗口，继续移动鼠标 —— 日志应仍在增长。
4. **键盘**：敲几个键（如 `a`、`Shift`），日志应出现 `KEY scancode=0x1E DOWN` / `KEY scancode=0x1E UP` 这样的行（按下的键不同 scancode 不同；`0xE0xx` 前缀表示扩展键如方向键）。
5. **退出**：托盘图标右键 → `退出`，图标消失，`tasklist` 中无 pc-kvm 进程，且 `pc-kvm.log` 末尾新增一行 `# 退出`。
