# Task 2 报告：设备侧 UHID 注入器

日期：2026-09-21 ｜ 设备：Xiaomi 25053RP5CC (violin) / HyperOS 3.0 / Android 16 ｜ adb 序列号 04053891899C1540（同机，序列号与型号不同属正常）

## 实现内容

按 brief 逐字转写了四个文件（无任何改动）：

- `src/injector/HidDescriptor.java` — 组合 HID 报告描述符（Report ID 1 = 相对鼠标，Report ID 2 = 6 键位键盘）
- `src/injector/UhidDevice.java` — `/dev/uhid` 封装，`UHID_EVENT_SIZE = 4376`，手写字节偏移，全小端
- `src/injector/Injector.java` — stdin 命令行入口（`M dx dy buttons wheel` / `K mods k1..k6` / `Q`）
- `build/build-injector.ps1` — javac → d8 → zip 改名 jar → adb push；**已确认为 UTF-8 with BOM**（Write 工具写出的是无 BOM 的 UTF-8，首三字节原为 `24 45 72`，已重写为带 BOM，首三字节 `EF BB BF`）

Ruling 6 已执行：跳过了 Step 6 中 `echo 'M 20 20 0 0' >> /dev/null` 的占位噪声行，改用实际命令验证。

## 构建与部署（Step 5）

命令：`powershell -ExecutionPolicy Bypass -File build/build-injector.ps1`

输出（一次通过）：

```
javac（--release 8，会打印弃用警告，正常）...
d8 转 dex...
推送到设备...
C:\Users\mazhewen\AppData\Local\Temp\pckvm.jar: 1 file pushed, 0 skipped. 11.7 MB/s (3107 bytes in 0.000s)
完成: /data/local/tmp/pckvm.jar
```

- javac 无错误；`-nowarn` 下连弃用警告也未打印。
- **d8 没有要求 `--lib`**（代码无 lambda / 默认方法 / try-with-resources），未下载任何东西。
- 产物 jar 仅 3107 字节（单个 classes.dex 打成 zip）。

## 机械证据（Step 6，代替人眼观察的部分）

### 1. 虚拟设备被创建 —— `adb shell dumpsys input`

注入器运行期间（`INJECTOR ready` 状态）抓到两个输入设备，均挂在 `/sys/devices/virtual/misc/uhid/0003:1234:5679.0015` 下：

```
32: PC-KVM Virtual Input Mouse
    Classes: CURSOR | EXTERNAL
    Path: /dev/input/event15
    Location: pc-kvm
    Identifier: bus=0x0003, vendor=0x1234, product=0x5679, version=0x0001
    SysfsDevicePath: /sys/devices/virtual/misc/uhid/0003:1234:5679.0015

33: PC-KVM Virtual Input Keyboard
    Classes: KEYBOARD | ALPHAKEY | EXTERNAL
    Path: /dev/input/event16
    Location: pc-kvm
    Identifier: bus=0x0003, vendor=0x1234, product=0x5679, version=0x0001
```

Input Reader 侧 `Device 28: PC-KVM Virtual Input Mouse`，`Sources: KEYBOARD | MOUSE`，Motion Ranges 含 X/Y（source=MOUSE，min=0 max=3199/2135，即屏尺寸）与 VSCROLL（-1..1）。bus=0x0003(BUS_USB)、vendor=0x1234、product=0x5679、Location=pc-kvm —— 与 create2 写入的字段逐一对上，证明偏移表正确。

### 2. 报告在流动 —— `getevent -t /dev/input/event15`

注入器跑 `M` 指令序列时同步抓 event15（原始十六进制：type code value）：

```
# M -30 -30 0 0
0002 0000 ffffffe2    EV_REL REL_X  = -30
0002 0001 ffffffe2    EV_REL REL_Y  = -30
0000 0000 00000000    EV_SYN

# M 0 0 1 0（左键按下）
0004 0004 00090001    EV_MSC MSC_SCAN
0001 0110 00000001    EV_KEY BTN_LEFT(0x110) = 1
0000 0000 00000000    EV_SYN

# M 0 0 0 0（左键抬起）
0004 0004 00090001
0001 0110 00000000    EV_KEY BTN_LEFT = 0
0000 0000 00000000

# M 0 0 0 1（滚轮）
0002 0008 00000001    EV_REL REL_WHEEL = 1
0002 000b 00000078    EV_REL REL_WHEEL_HI_RES = 120
0000 0000 00000000
```

第二次补跑抓住正向位移（第一轮 getevent 附着太晚漏了前两条）：

```
# M 30 0 0 0
0002 0000 0000001e    EV_REL REL_X = +30
# M 0 30 0 0
0002 0001 0000001e    EV_REL REL_Y = +30
```

结论：**REL_X/REL_Y（正负两个方向）、BTN_LEFT 按下/抬起、REL_WHEEL 全部按指令值精确到达输入子系统**，数值与发送值完全一致。注入器生命周期日志每次均为 `INJECTOR start` → `INJECTOR ready` → `INJECTOR exit`（Q 正常退出）。

### 验证方法备注

FIFO（`mkfifo` + `cat fifo | app_process`）喂 stdin 的方案在 adb shell 下失败了（cat 提前拿到 EOF，注入器秒退），原因未深究；改用「单条 adb shell 内并联」：后台起注入器（管道喂带 sleep 的指令序列）→ 3 秒后用 `getevent -pl | grep -B1 "PC-KVM Virtual Input Mouse"` 动态找节点 → 后台 `getevent -t` 抓流 → `wait` + `kill`。该方式稳定可复现，后续任务调试可复用。

## 清理（Step 7）

- 已删除 `/data/local/tmp/pckvm.jar`、`pk.fifo`、`pk.inj.log`、`pk.gev.log`、`pk.inj2.log`、`pk.gev2.log`
- `ps -A | grep app_process` 无匹配 —— 无残留进程
- 验证过程中注入器每次都以 Q 正常退出，fd 关闭后内核自动销毁虚拟设备（dumpsys 中的 event15/16 条目随进程退出消失，第二次运行重新建为 event15）

## 文件变更

```
A  build/build-injector.ps1
A  src/injector/HidDescriptor.java
A  src/injector/Injector.java
A  src/injector/UhidDevice.java
```

提交：`7be0e02 feat(injector): 设备侧 UHID 注入器 —— 组合 HID 描述符 + 命令行驱动`（分支 `phase2-skeleton`）

## 自审结果

- 四个文件与 brief 逐字一致？是（仅转写，未改动）。
- UHID 偏移与 brief 背景表一致？是：name@4, phys@132, uniq@196, rd_size@260, bus@262, vendor@264, product@268, version@272, country@276, rd_data@280；input2 size@4 data@6。设备能枚举且 dumpsys 字段全部正确回读，侧面证明偏移无误。
- 描述符隐含报告长度 vs `sendMouse`/`sendKeyboard`？一致：鼠标 = 3 按钮位 + 5 填充位（1B）+ X + Y + wheel（各 1B）= 4B 负载 + report id = 5B = `MOUSE_REPORT_SIZE`；键盘 = 8 修饰位（1B）+ 保留（1B）+ 6 键槽（6B）= 8B + report id = 9B = `KEYBOARD_REPORT_SIZE`。getevent 数值精确到达，实证无错位。
- `android.*` 引用？无（仅 `java.io`）。lambda / 默认方法 / try-with-resources？无（`close()` 里是普通 try-catch）。d8 未要求 `--lib`。
- jar 已从设备删除？是。残留进程？无。
- 键盘路径未实测发键（brief Step 6 只要求 M 指令），但 Keyboard 设备已正确注册且走同一条 input2 通路，风险低。

## 顾虑

1. 键盘报告（`K` 指令）未做端到端验证 —— 如需可在人眼确认阶段顺手敲一条 `K 0 4 0 0 0 0 0` + `K 0 0 0 0 0 0 0`（应输入一个 'a'）。
2. FIFO 喂 stdin 在 adb shell 下不工作（见上文备注），如果后续任务的 socket 化之前还想长期交互调试，用「单 adb shell 并联」模式。
3. git 提示这几个文件下次 checkout 会 LF→CRLF（仓库无 .gitattributes 约束）。对 Java/ps1 均无害（ps1 的 BOM 不受影响），暂不处理。

## 人眼确认步骤（必须由人来做的部分）

1. PC 上跑：`powershell -ExecutionPolicy Bypass -File build/build-injector.ps1`（重新推送 jar）。
2. 保持手机屏幕亮着、停在桌面，PC 上跑：
   `adb shell "CLASSPATH=/data/local/tmp/pckvm.jar app_process / Injector"`
   看到 `INJECTOR ready` 后，**手机屏幕上应出现一个鼠标光标**。
3. 由于 stdin 直连 adb 会话，直接在同一窗口逐条键入并回车（每条的 sleep 不需要，人敲即可）：
   - `M 30 0 0 0` → 光标应向右移动
   - `M 0 30 0 0` → 向下
   - `M -30 -30 0 0` → 向左上回移
   - `M 0 0 0 1` → 滚轮一格（在可滚动页面更明显）
   - 可选：把光标移到某个图标上后 `M 0 0 1 0` / `M 0 0 0 0` → 按下/抬起（长按/点击效果）
   - 可选键盘：`K 0 4 0 0 0 0 0` 然后 `K 0 0 0 0 0 0 0` → 若焦点在输入框应打出 'a'
4. `Q` 退出，光标应消失（设备随 fd 关闭销毁）。
5. 判定：光标随 M 指令方向/幅度移动、滚轮生效 → 通过。

事后清理（人眼确认完成后）：`adb shell rm -f /data/local/tmp/pckvm.jar`
