# PC-AD-KVM

在 Windows PC 和 Android 手机之间共享鼠标与键盘。鼠标移动到屏幕边缘后进入 Android，向边缘外继续推动即可返回 PC。
在 Windows 11 和 小米pad 7s pro 上实测可用

## 特性

- Android 端通过 ADB reverse 和 `/dev/uhid` 接收键盘、鼠标输入
- 绝对坐标指针，支持横屏、竖屏以及使用过程中旋转屏幕
- 旋转时保持接管状态并重新校准坐标；设备短暂断开后自动重连
- 鼠标移动使用有界队列和相邻移动合并，减少延迟和抖动
- 支持左侧或右侧挂载手机、鼠标速度调节和可选的 ADB 激进恢复
- PC 端不显示会拦截点击的悬浮窗口

## 运行要求

- Windows x64
- .NET Framework 4.x（系统通常已安装）
- Android 手机开启 USB 调试，并能被 `adb devices` 识别
- 手机支持 `/dev/uhid`；设备侧需要允许通过 `app_process` 创建虚拟 HID 设备
- 构建时需要 JDK 8+、Android R8 `tools/r8.jar` 和 Windows `csc.exe`

## 使用

1. 安装 Android platform-tools，并确认：

   ```powershell
   adb devices
   ```

   输出设备状态为 `device`。

2. 运行 `dist/pc-kvm.exe`。程序会自动创建 ADB reverse 隧道、推送 `dist/pckvm.jar` 并启动 Android 注入器。

3. 把鼠标推到连接手机一侧的屏幕边缘进入 Android。回到 PC 时，在 Android 屏幕边缘继续向外推动。

配置文件为 `dist/pc-kvm.ini`，支持：

```ini
MouseSensitivity=0.50
PhoneSide=Right
AllowKillAdb=false
ReconnectSeconds=5
```

设置窗口修改后立即生效；直接编辑 ini 后需要重启程序。

## 构建

先构建 Android 注入器，再构建 Windows 程序：

```powershell
./build/build-injector.ps1
./build/build-agent.ps1
```

生成文件：

- `dist/pc-kvm.exe`
- `dist/pckvm.jar`

`build-injector.ps1` 默认使用 `C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin` 下的 JDK；如果 JDK 安装位置不同，请调整脚本中的 `$javac` 和 `$java`。

## 测试

核心回归测试：

```powershell
./tests/absolute-pointer/run.ps1
./tests/edge-tracker/run.ps1
./tests/transport/run.ps1
```

真机测试需要保持手机亮屏，并覆盖竖屏、横屏、接管中旋转、鼠标点击和断开重连场景。运行日志写入 `dist/pc-kvm.log`。

## 故障排查

- `adb devices` 没有 `device`：重新插拔数据线并在手机上确认 USB 调试授权。
- 端口 27183 被占用：程序会自动选择备用本地端口，并更新 ADB reverse 目标。
- 旋转后暂时没有指针：等待 Android 输入设备重新注册；超过安全超时后程序会释放接管，避免鼠标被锁在 PC 端。
- 详细连接、几何变更和 READY 状态可查看 `dist/pc-kvm.log`。

## 许可

当前仓库未声明开源许可证。
