# PC-AD-KVM

在 Windows PC 和 Android 手机之间共享鼠标与键盘。向连接侧边缘推动两次可在 PC 与 Android 之间切换，也可以使用全局快捷键。
原有 USB 方案曾在 Windows 11 和小米 Pad 7s Pro 上实测。无线调试实现仍需在目标网络和设备上验收。

## 特性

- Android 端通过 ADB reverse 和 `/dev/uhid` 接收键盘、鼠标输入
- 绝对坐标指针，支持横屏、竖屏以及使用过程中旋转屏幕
- 旋转时保持接管状态并重新校准坐标；断线后释放 PC 输入，恢复连接后等待用户重新接管
- Android 11+ 无线调试配对、设备身份绑定、mDNS 发现和手动连接地址
- 鼠标移动使用有界队列和相邻移动合并，减少延迟和抖动
- 支持左侧或右侧挂载手机、鼠标速度调节和可选的 ADB 激进恢复
- 双次贴边防误触；可在设置中自定义全局切换快捷键
- PC 端不显示会拦截点击的悬浮窗口

## 运行要求

- Windows x64
- .NET Framework 4.x（系统通常已安装）
- Android 手机开启 USB 调试，或 Android 11+ 开启无线调试；目标必须在 `adb devices -l` 中显示为 `device`
- 无线模式要求 PC 与手机所在局域网能互通 TCP；PC 可以使用有线网络
- 手机支持 `/dev/uhid`；设备侧需要允许通过 `app_process` 创建虚拟 HID 设备
- 构建时需要 JDK 8+、Android R8 `tools/r8.jar` 和 Windows `csc.exe`

## 使用

1. 安装支持 `adb pair` 的 Android platform-tools。USB 使用时确认：

   ```powershell
   adb devices
   ```

   输出设备状态为 `device`。

2. 确保 `dist/pc-kvm.exe` 与 `dist/pckvm.jar` 来自同一次构建。运行 exe 会先打开设置窗口，可调整鼠标速度、手机位置等选项；点击「连接设备…」或从托盘菜单进入连接窗口，选择 USB 或无线。程序会对选定目标创建 ADB reverse 隧道、推送 jar 并启动注入器。

### 无线调试首次连接

1. 在手机开发者选项中打开「无线调试」，点「使用配对码配对设备」。在 PC 的「连接设备…」窗口选择「无线调试」，在「首次配对」中分别输入手机弹窗的 **IP**、**配对端口**和六位码，再点「配对」。三个字段均需手动输入。配对码只通过 adb 标准输入传递，不保存到 ini。
2. 配对成功后，程序会把 IP 填入「连接地址」。在手机无线调试**主页面**查看 **IP:连接端口**，把连接端口补齐后点「连接」；也可选择已经在线的无线设备。连接端口通常与配对弹窗端口不同。
3. 点「连接」，等待窗口显示「无线：已就绪」。配对成功、ADB 在线与 KVM 就绪是三个独立阶段；只有就绪后才能接管。下次启动会优先按已验证的设备身份恢复，网络中断恢复后仍需重新贴边或按快捷键接管。

「断开」会停止自动恢复；关闭连接窗口只隐藏管理界面。「忘记设备」清除本程序的记录，手机中的 ADB 授权需在手机无线调试页面撤销。无线模式不会自动执行全局 `adb kill-server`，设置中的「强杀 adb」仅用于 USB 恢复。

3. 把鼠标推到连接手机一侧的屏幕边缘，向外推动一次，再向屏内移动至少 12 像素，并在 1.2 秒内再次贴边外推，即可进入 Android。回到 PC 时，在 Android 与 PC 相邻的边缘重复这个动作；手机侧每次外推需持续推动约 40 单位。

也可以按 `Ctrl+Alt+Space` 直接切换；托盘图标右键「设置…」可更改快捷键。`Ctrl+Alt+Esc` 是接管时的紧急退出键。
设置中可关闭「启用双次贴边切换」；关闭后双向贴边都不会触发切换，快捷键仍可使用。默认开启。

配置文件为 `dist/pc-kvm.ini`，支持：

```ini
MouseSensitivity=0.50
PhoneSide=Right
AllowKillAdb=false
ReconnectSeconds=5
SwitchHotkey=Ctrl+Alt+Space
EnableEdgeSwitch=true
ConnectionMode=Usb
AdbPath=
UsbSerial=
WirelessDeviceSerial=
WirelessDeviceGuid=
WirelessServiceName=
WirelessLastEndpoint=
```

设置窗口修改后立即生效；直接编辑 ini 后需要重启程序。
连接窗口可选择 platform-tools 中的 `adb.exe`，路径会保存到 ini；无线配对需要支持 `adb pair` 的版本。
自定义快捷键时，点击设置窗口里的快捷键输入框，再按包含 Ctrl 或 Alt 的组合键（可加 Shift）；Esc 保留作紧急退出。若组合键已被占用，设置窗口会提示并保留原快捷键。

## 构建

先构建 Android 注入器，再构建 Windows 程序：

```powershell
./build/build-injector.ps1
./build/build-agent.ps1
```

生成文件：

- `dist/pc-kvm.exe`
- `dist/pckvm.jar`

`build-injector.ps1` 默认使用 `C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin` 下的 JDK；如 R8 位于别处，可设置 `PCKVM_R8_JAR`。构建脚本只生成 jar，目标推送由连接流程执行。`build-agent.ps1` 不会强制结束正在运行的 PC-KVM；若正在运行当前目录中的 exe，请先从托盘正常退出。

## 测试

核心回归测试：

```powershell
./tests/absolute-pointer/run.ps1
./tests/edge-tracker/run.ps1
./tests/transport/run.ps1
./tests/connection/run.ps1
./tests/coordinator/run.ps1
./tests/connection-form/run.ps1
./tests/session/run.ps1
```

真机测试需要保持手机亮屏，并覆盖竖屏、横屏、接管中旋转、鼠标点击和断开重连场景。运行日志写入 `dist/pc-kvm.log`。

## 故障排查

- `adb devices -l` 没有 `device`：USB 检查数据线与调试授权；无线检查无线调试、局域网互通，必要时重新配对。
- 配对后没有在线无线设备：点刷新，并手动输入手机无线调试主页面的完整 `IP:连接端口`。
- 手动输入连接地址后无法连接：确认已在本机配对，手机与 PC 可以互通，且填写的是无线调试主页面显示的连接端口。
- 显示设备身份不符：重新选择正确设备。程序不会按相同 IP、型号或列表首项自动改绑。
- 端口 27183 被占用：程序会自动选择备用本地端口，并更新 ADB reverse 目标。
- 旋转后暂时没有指针：等待 Android 输入设备重新注册；超过安全超时后程序会释放接管，避免鼠标被锁在 PC 端。
- 详细连接、几何变更和 READY 状态可查看 `dist/pc-kvm.log`。

## 许可

当前仓库未声明开源许可证。
