### Task 2: 设备侧注入器 —— UHID 设备与组合 HID 报告描述符

**产出**：一个 dex jar，push 到手机后用 `app_process` 跑起来，**手机上出现真实鼠标光标**。此阶段它从标准输入读简单文本指令，便于手工测试。

**Files:**
- Create: `src/injector/HidDescriptor.java`
- Create: `src/injector/UhidDevice.java`
- Create: `src/injector/Injector.java`
- Create: `build/build-injector.ps1`

**Interfaces:**
- Consumes: 无
- Produces:
  - `HidDescriptor.MOUSE_KEYBOARD`：`static final int[]`，组合 HID 报告描述符
  - `UhidDevice`：构造 `UhidDevice(String name, int[] descriptor)`，抛 `IOException`；`public void sendMouse(byte buttons, byte dx, byte dy, byte wheel)`；`public void sendKeyboard(byte modifiers, byte[] keys6)`；`public void close()`
  - `Injector.main(String[])`：从 stdin 逐行读指令，格式 `M dx dy buttons wheel` / `K mods k1 k2 k3 k4 k5 k6` / `Q`
  - `build/build-injector.ps1`：javac → d8 → 打包 → `adb push` 到 `/data/local/tmp/pckvm.jar`

**背景（实现者必读）**：

1. **UHID 事件必须写满 4376 字节。** 不写满内核会拒（阶段一实测确认 4376 一次通过）。
2. **偏移表（`__attribute__((packed))`，必须手写字节偏移，不要用 `ByteBuffer` 默认对齐）**：
   - `uhid_event`: `type`(u32 @0) + union(@4)
   - `UHID_CREATE2 = 11`，`UHID_INPUT2 = 12`
   - `create2`: `name[128]`@4, `phys[64]`@132, `uniq[64]`@196, `rd_size`(u16)@260, `bus`(u16)@262, `vendor`(u32)@264, `product`(u32)@268, `version`(u32)@272, `country`(u32)@276, `rd_data[4096]`@280
   - `input2`: `size`(u16)@4, `data[4096]`@6
   - 全部小端。
3. **`report id` 必须在报告的第一个字节。** 描述符里声明了两个 Report ID（鼠标 = 1，键盘 = 2），因此 `input2` 的 data 首字节是 report id，`size` 字段要含它（鼠标 5 字节，键盘 9 字节）。

- [ ] **Step 1: 编写 HID 报告描述符**

创建 `src/injector/HidDescriptor.java`：

```java
/** 组合 HID 报告描述符：Report ID 1 = 相对鼠标，Report ID 2 = 6 键位键盘。 */
public class HidDescriptor {

    // 鼠标报告负载（不含 report id）：[buttons(1B)][dx(1B)][dy(1B)][wheel(1B)]
    public static final int MOUSE_REPORT_ID = 1;
    public static final int MOUSE_REPORT_SIZE = 5;   // report id + 4

    // 键盘报告负载（不含 report id）：[modifiers(1B)][reserved(1B)][key1..key6(6B)]
    public static final int KEYBOARD_REPORT_ID = 2;
    public static final int KEYBOARD_REPORT_SIZE = 9; // report id + 8

    public static final int[] MOUSE_KEYBOARD = {
        // ---- 鼠标 ----
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x02,        // Usage (Mouse)
        0xA1, 0x01,        // Collection (Application)
        0x85, 0x01,        //   Report ID (1)
        0x09, 0x01,        //   Usage (Pointer)
        0xA1, 0x00,        //   Collection (Physical)
        0x05, 0x09,        //     Usage Page (Button)
        0x19, 0x01,        //     Usage Minimum (1)
        0x29, 0x03,        //     Usage Maximum (3)
        0x15, 0x00,        //     Logical Minimum (0)
        0x25, 0x01,        //     Logical Maximum (1)
        0x95, 0x03,        //     Report Count (3)
        0x75, 0x01,        //     Report Size (1)
        0x81, 0x02,        //     Input (Data,Var,Abs)   -- 3 个按钮位
        0x95, 0x01,        //     Report Count (1)
        0x75, 0x05,        //     Report Size (5)
        0x81, 0x01,        //     Input (Const)          -- 5 位填充
        0x05, 0x01,        //     Usage Page (Generic Desktop)
        0x09, 0x30,        //     Usage (X)
        0x09, 0x31,        //     Usage (Y)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x02,        //     Report Count (2)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- dx, dy
        0x09, 0x38,        //     Usage (Wheel)
        0x15, 0x81,        //     Logical Minimum (-127)
        0x25, 0x7F,        //     Logical Maximum (127)
        0x75, 0x08,        //     Report Size (8)
        0x95, 0x01,        //     Report Count (1)
        0x81, 0x06,        //     Input (Data,Var,Rel)   -- wheel
        0xC0,              //   End Collection
        0xC0,              // End Collection

        // ---- 键盘 ----
        0x05, 0x01,        // Usage Page (Generic Desktop)
        0x09, 0x06,        // Usage (Keyboard)
        0xA1, 0x01,        // Collection (Application)
        0x85, 0x02,        //   Report ID (2)
        0x05, 0x07,        //   Usage Page (Keyboard/Keypad)
        0x19, 0xE0,        //   Usage Minimum (224 = LeftCtrl)
        0x29, 0xE7,        //   Usage Maximum (231 = RightGUI)
        0x15, 0x00,        //   Logical Minimum (0)
        0x25, 0x01,        //   Logical Maximum (1)
        0x95, 0x08,        //   Report Count (8)
        0x75, 0x01,        //   Report Size (1)
        0x81, 0x02,        //   Input (Data,Var,Abs)   -- 8 个修饰键位
        0x95, 0x01,        //   Report Count (1)
        0x75, 0x08,        //   Report Size (8)
        0x81, 0x01,        //   Input (Const)          -- 保留字节
        0x95, 0x06,        //   Report Count (6)
        0x75, 0x08,        //   Report Size (8)
        0x15, 0x00,        //   Logical Minimum (0)
        0x25, 0x65,        //   Logical Maximum (101)
        0x05, 0x07,        //   Usage Page (Keyboard/Keypad)
        0x19, 0x00,        //   Usage Minimum (0)
        0x29, 0x65,        //   Usage Maximum (101)
        0x81, 0x00,        //   Input (Data,Ary,Abs)   -- 6 个按键槽位
        0xC0               // End Collection
    };
}
```

- [ ] **Step 2: 编写 UHID 设备封装**

创建 `src/injector/UhidDevice.java`：

```java
import java.io.IOException;
import java.io.RandomAccessFile;

/** 封装 /dev/uhid：创建一个虚拟 HID 设备并持续写入输入报告。 */
public class UhidDevice {

    static final int UHID_CREATE2 = 11;
    static final int UHID_INPUT2  = 12;
    static final int UHID_EVENT_SIZE = 4376;   // sizeof(struct uhid_event)，内核一次接受（实测）

    static final int OFF_NAME    = 4;
    static final int OFF_PHYS    = 132;
    static final int OFF_UNIQ    = 196;
    static final int OFF_RD_SIZE = 260;
    static final int OFF_BUS     = 262;
    static final int OFF_VENDOR  = 264;
    static final int OFF_PRODUCT = 268;
    static final int OFF_VERSION = 272;
    static final int OFF_COUNTRY = 276;
    static final int OFF_RD_DATA = 280;

    static final int OFF_INPUT_SIZE = 4;
    static final int OFF_INPUT_DATA = 6;

    static final int BUS_USB = 0x03;

    private final RandomAccessFile dev;

    public UhidDevice(String name, int[] descriptor) throws IOException {
        dev = new RandomAccessFile("/dev/uhid", "rw");
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_CREATE2);
        putStr(ev, OFF_NAME, name, 128);
        putStr(ev, OFF_PHYS, "pc-kvm", 64);
        putStr(ev, OFF_UNIQ, "", 64);
        putU16(ev, OFF_RD_SIZE, descriptor.length);   // 真实描述符长度，不是缓冲容量
        putU16(ev, OFF_BUS, BUS_USB);
        putU32(ev, OFF_VENDOR, 0x1234);
        putU32(ev, OFF_PRODUCT, 0x5679);
        putU32(ev, OFF_VERSION, 1);
        putU32(ev, OFF_COUNTRY, 0);
        for (int i = 0; i < descriptor.length; i++) {
            ev[OFF_RD_DATA + i] = (byte) descriptor[i];
        }
        dev.write(ev);
    }

    /** 鼠标报告：[reportId][buttons][dx][dy][wheel] */
    public void sendMouse(byte buttons, byte dx, byte dy, byte wheel) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.MOUSE_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.MOUSE_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = buttons;
        ev[OFF_INPUT_DATA + 2] = dx;
        ev[OFF_INPUT_DATA + 3] = dy;
        ev[OFF_INPUT_DATA + 4] = wheel;
        dev.write(ev);
    }

    /** 键盘报告：[reportId][modifiers][reserved][6 个 key slot] */
    public void sendKeyboard(byte modifiers, byte[] keys6) throws IOException {
        byte[] ev = new byte[UHID_EVENT_SIZE];
        putU32(ev, 0, UHID_INPUT2);
        putU16(ev, OFF_INPUT_SIZE, HidDescriptor.KEYBOARD_REPORT_SIZE);
        ev[OFF_INPUT_DATA + 0] = (byte) HidDescriptor.KEYBOARD_REPORT_ID;
        ev[OFF_INPUT_DATA + 1] = modifiers;
        ev[OFF_INPUT_DATA + 2] = 0;   // reserved，必须为 0
        for (int i = 0; i < 6; i++) {
            ev[OFF_INPUT_DATA + 3 + i] = i < keys6.length ? keys6[i] : 0;
        }
        dev.write(ev);
    }

    public void close() {
        try { dev.close(); } catch (IOException e) { /* 关 fd 时内核自动销毁设备 */ }
    }

    static void putU32(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
        b[off + 2] = (byte) ((v >>> 16) & 0xFF);
        b[off + 3] = (byte) ((v >>> 24) & 0xFF);
    }

    static void putU16(byte[] b, int off, int v) {
        b[off]     = (byte) (v & 0xFF);
        b[off + 1] = (byte) ((v >>> 8) & 0xFF);
    }

    static void putStr(byte[] b, int off, String s, int cap) {
        byte[] raw;
        try { raw = s.getBytes("UTF-8"); } catch (Exception e) { raw = s.getBytes(); }
        int len = Math.min(raw.length, cap - 1);   // 保留结尾 NUL
        System.arraycopy(raw, 0, b, off, len);
    }
}
```

- [ ] **Step 3: 编写命令行入口**

创建 `src/injector/Injector.java`。此阶段从 **stdin 逐行读文本指令**，便于 `adb shell` 手工喂：

```java
import java.io.BufferedReader;
import java.io.InputStreamReader;

/**
 * 阶段二 Task 2 的命令行版本：从 stdin 读文本指令驱动虚拟 HID 设备。
 *   M <dx> <dy> <buttons> <wheel>   发送鼠标报告（dx/dy 会被夹到 -127..127）
 *   K <mods> <k1> <k2> <k3> <k4> <k5> <k6>   发送键盘报告
 *   Q                                退出
 * 后续任务会把 stdin 换成 socket，本类保留为手工调试入口。
 */
public class Injector {

    public static void main(String[] args) throws Exception {
        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        BufferedReader in = new BufferedReader(new InputStreamReader(System.in));
        String line;
        while ((line = in.readLine()) != null) {
            line = line.trim();
            if (line.length() == 0) continue;
            if (line.equals("Q")) break;

            String[] t = line.split("\\s+");
            try {
                if (t[0].equals("M") && t.length >= 5) {
                    dev.sendMouse((byte) clampToByte(parse(t[3])),
                                  (byte) clamp127(parse(t[1])),
                                  (byte) clamp127(parse(t[2])),
                                  (byte) clamp127(parse(t[4])));
                } else if (t[0].equals("K") && t.length >= 8) {
                    byte[] keys = new byte[6];
                    for (int i = 0; i < 6; i++) keys[i] = (byte) parse(t[i + 2]);
                    dev.sendKeyboard((byte) parse(t[1]), keys);
                } else {
                    System.out.println("INJECTOR bad line: " + line);
                }
            } catch (Exception ex) {
                System.out.println("INJECTOR err on [" + line + "]: " + ex);
            }
        }

        dev.close();
        System.out.println("INJECTOR exit");
    }

    /** 把任意整数夹到有符号 8 位范围，HID 报告的轴是 int8。 */
    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int clampToByte(int v) {
        if (v > 127) return 127;
        if (v < -128) return -128;
        return v;
    }

    static int parse(String s) { return Integer.parseInt(s); }
}
```

- [ ] **Step 4: 编写构建脚本**

创建 `build/build-injector.ps1`。**必须存为 UTF-8 with BOM**（PowerShell 5.1 会把无 BOM 的 UTF-8 中文注释按 GBK 误读，静默破坏解析——实测踩过）。

```powershell
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src   = Join-Path $root 'src\injector'
$r8    = Join-Path $root 'tools\r8.jar'
$javac = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\javac.exe'
$java  = 'C:\Program Files\JetBrains\PyCharm 2026.2.0.1\jbr\bin\java.exe'

foreach ($p in @($javac, $java, $r8)) {
    if (-not (Test-Path $p)) { throw "缺少: $p" }
}

$out = Join-Path $env:TEMP 'pckvm-injector-classes'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = Get-ChildItem -Path $src -Filter '*.java' | ForEach-Object { $_.FullName }

Write-Host "javac（--release 8，会打印弃用警告，正常）..." -ForegroundColor Cyan
& $javac --release 8 -nowarn -d $out $sources
if ($LASTEXITCODE -ne 0) { throw "javac 失败" }

Write-Host "d8 转 dex..." -ForegroundColor Cyan
$classes = Get-ChildItem -Path $out -Filter '*.class' | ForEach-Object { $_.FullName }
& $java -cp $r8 com.android.tools.r8.D8 --release --min-api 28 --output $out $classes
if ($LASTEXITCODE -ne 0) { throw "d8 失败" }

$dex = Join-Path $out 'classes.dex'
if (-not (Test-Path $dex)) { throw "未生成 classes.dex" }

$jar = Join-Path $env:TEMP 'pckvm.jar'
if (Test-Path $jar) { Remove-Item -Force $jar }
$zip = Join-Path $out 'payload.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $dex -DestinationPath $zip -Force
Move-Item $zip $jar

Write-Host "推送到设备..." -ForegroundColor Cyan
& adb push $jar /data/local/tmp/pckvm.jar
if ($LASTEXITCODE -ne 0) { throw "adb push 失败" }

Write-Host "完成: /data/local/tmp/pckvm.jar" -ForegroundColor Green
```

- [ ] **Step 5: 构建**

Run: `powershell -ExecutionPolicy Bypass -File build/build-injector.ps1`

Expected: javac（含 `--release 8` 弃用警告，正常）→ d8 → `完成: /data/local/tmp/pckvm.jar`。**若 d8 报缺库，停下报告——不要自行下载 `android.jar`**（那会把下载量从 1 个 jar 变成 3 个，属范围变更）。本任务的 Java 无 lambda、无默认方法、无 try-with-resources，不需要脱糖。

- [ ] **Step 6: 手工验证 —— 手机出现光标并能被指令驱动**

终端 A（保持运行）：

```powershell
adb shell "CLASSPATH=/data/local/tmp/pckvm.jar app_process / Injector"
```

Expected: 打印 `INJECTOR start` / `INJECTOR ready`，**手机屏幕上出现鼠标光标**。

终端 B 逐条喂指令，每条之后看手机：

```powershell
adb shell "echo 'M 20 20 0 0' >> /dev/null"   # 占位，实际用下面的交互方式
```

**更好用的方式**：在终端 A 直接敲（stdin 是通的）：

```
M 30 0 0 0      → 光标向右跳 30 像素
M 0 30 0 0      → 向下 30
M -30 -30 0 0   → 左上
M 0 0 1 0       → 左键按下（图标/按钮应显示按下态）
M 0 0 0 0       → 左键抬起
M 0 0 0 1       → 滚轮下滚一格
```

判定：光标按指令移动、按钮生效、滚轮生效 → **通过**。

- [ ] **Step 7: 清理并提交**

```bash
cd /g/pc-kvm
adb shell rm -f /data/local/tmp/pckvm.jar
adb shell "ps -A | grep app_process"   # 确认无残留（若有，kill 掉）
git add build/build-injector.ps1 src/injector/
git commit -m "feat(injector): 设备侧 UHID 注入器 —— 组合 HID 描述符 + 命令行驱动"
```

---

