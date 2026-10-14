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

2. 确保 `dist/pc-kvm.exe` 与 `dist/pckvm.jar` 来自同一次构建。运行 exe 会先打开「设备与连接」主界面；重复打开程序或双击托盘也会回到此界面。选择 USB 或无线，设置中的鼠标速度、手机位置等选项可从底部「设置…」进入。程序会对选定目标创建 ADB reverse 隧道、推送 jar 并启动注入器。

### 无线调试首次连接

1. 在手机开发者选项中打开「无线调试」，点「使用配对码配对设备」。在 PC 的「设备与连接」窗口选择「无线调试」，点「添加新设备 / 首次配对」，在展开的表单中分别输入手机弹窗的 **IP**、**配对端口**和六位码，再点「配对」。三个字段均需手动输入。配对码只通过 adb 标准输入传递，不保存到 ini。
2. 配对成功后，程序会把 IP 填入「连接地址」。在手机无线调试**主页面**查看 **IP:连接端口**，把连接端口补齐后点「连接」；也可选择已经在线的无线设备。连接端口通常与配对弹窗端口不同。
3. 点「连接」，等待顶部显示「已连接 · 当前控制电脑」和设备标识。配对成功、ADB 在线与 KVM 就绪是三个独立阶段；只有就绪后才能接管。下次启动会优先按已验证的设备身份恢复，网络中断恢复后仍需重新贴边或按快捷键接管。

「断开」会停止自动恢复；关闭连接窗口只隐藏管理界面。「更多… → 忘记设备」清除本程序的记录，手机中的 ADB 授权需在手机无线调试页面撤销。无线模式不会自动执行全局 `adb kill-server`，设置中的「强杀 adb」仅用于 USB 恢复。

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

设置窗口点击「确定」或「保存并返回连接」后生效；取消不保存。设置窗口打开期间切换快捷键暂停，关闭后恢复。直接编辑 ini 后需要重启程序。
主界面顶部与托盘同步显示连接和控制对象；连接成功后收起配置并展示使用说明。连接过程中可停止连接，失败原因在自动重试期间保留。USB 模式隐藏无线配置，未授权或离线设备显示具体提示。关闭主界面仍在托盘运行，完全退出请使用托盘菜单。

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
./tests/double-click/run.ps1
./tests/wifi-latency/run.ps1
./tests/drag-cursor/run.ps1
./tests/native-cursor/run.ps1
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

### 无线输入随机变慢

在同一台设备上分别用 USB 和无线连接，连续滑动鼠标、停手后点击，并记录哪次操作感觉卡顿。日志每隔约 5 秒汇总一行带时间的 `# 输入时延`，断线时也会汇总最后一段；新增的汇总行不记录鼠标坐标或按键内容。比较卡顿时段与流畅时段的 `max`，并结合以下指标判断：

- `PC排队`：输入帧进入 PC 发送队列到开始写入隧道的时间；`队列峰值` 是同时积压的帧数。排队升高可能由 PC 发送节拍造成，也可能是前一帧写入隧道时被阻塞，需结合下两项判断。
- `心跳排队`：心跳帧等待开始写入的时间。它与输入同时积压时，说明发送队列受阻。
- `写入耗时`：一次隧道写入在 PC 上阻塞的时间。它升高时，后续帧的 `PC排队` 也可能随之升高。
- `链路往返`：PING/PONG 从 PC 开始写入到收到回复的时间，包含 ADB 无线通路与两端调度；它与卡顿同步升高时，再检查 Wi-Fi 信号、干扰和网络负载。
- `指针提交确认`：鼠标位置开始写入到 Android 处理位置并回复 ACK 的时间；普通移动提交 UHID，触控拖动更新手势及光标图层。只统计实际回复的序号，被合并的旧位置没有独立 ACK。它不代表屏幕已经显示新位置，也不测量随后鼠标按键或点击的处理时间。若它升高而链路往返稳定，再检查 Android 输入队列与输入处理。

`n` 是本次汇总的样本数；没有样本的窗口不能用来判断该环节。少量心跳样本的 `p95` 也不能单独证明网络稳定，需结合多段日志与 USB 对照。当前统计仅用于定位瓶颈，不会自动改变输入节拍。

### 无线输入体验优化

Android 接收队列会合并相邻且属于同一几何版本的鼠标位置，减少无线卡顿恢复后追赶旧位置的过程。按钮、键盘、滚轮和几何变更保持顺序；点击前的最新位置会保留。连续移动仍按四毫秒节拍发送，位置后已有离散事件时会及时发送位置和该事件。

按住左键时，界面接收连续触控手势，通过原始输入窗口 token 隐藏原生鼠标图标，成功后由独立的 SurfaceControl 图层显示和移动光标；图层没有输入窗口或输入事件。优先复用系统默认光标图像、像素大小和点击热点，无法读取时使用备用箭头。拖动期间不混入原生鼠标悬停，以免 Android 取消触控滚动；松开后恢复原生鼠标。退出、断线、取消和旋转会隐藏或释放图层。隐藏接口或图层不可用时记录 `DRAG_CURSOR` 状态，保留触控手势且不增加第二个光标。当前目标平板已通过图标隐藏/恢复、系统图像绘制和图层移除的真机探测；用户已确认重启后的拖动只显示一个光标。

部分厂商的 PointerIcon 返回透明占位图：程序检查可见像素，透明图改用尺寸相同的系统 `pointer_arrow` 资源并保留热点；资源不可用则使用可见备用箭头。目标平板已通过真实画面比对，用户确认光标显示、样式、拖动界面和双击选字正常。当前原生图像加载使用默认大小及缩放 1，不承诺跟随其他设备的自定义指针主题或无障碍放大设置。

`build/android-stubs` 仅提供原始窗口监听器的编译期接口声明，D8 只接收业务类，不打包这些 Android 类；真机使用系统自己的实现。窗口快照回调可在注册过程中先到达，初始缓存不能覆盖更新后的快照，空快照同样有效。

电脑日志中的 `# Android ... INJECTOR input timing` 每五秒记录 Android 接收排队的平均值/峰值，以及处理帧的平均值/峰值；处理时间包含输入提交和回复写入。队列计时从完整收到帧开始，不包含无线传输时间。几何初始化可能使启动窗口的处理时间较高，比较时应选取进入接管后的窗口。

无线连接会传入 `--wifi-low-latency`，在该会话中申请 Android `WIFI_MODE_FULL_LOW_LATENCY` Wi-Fi 锁，并在断线时释放；USB 不申请。`WIFI_LATENCY requested held=true activation=unverified` 仅说明锁已持有，不证明低延迟已激活。Android 还要求申请方处于前台、屏幕亮起以及设备支持；ADB shell 注入器可能不符合前台条件。失败会记录 `WIFI_LATENCY unavailable`，不影响 KVM 输入，不自动提权或更改系统全局 Wi-Fi 设置。

2026-10-14 在目标 Android 16 平板上，锁申请成功，但系统最新 Wi-Fi 统计仍为 `is_low_latency_activated=false`。这台设备当前不能把 Wi-Fi 锁申请视为实际的无线加速。试用产物位于 `G:\pc-kvm\dist\wireless-experience`；源码位于已附加的 `codex/wireless-experience` 工作区。

## 许可

当前仓库未声明开源许可证。
