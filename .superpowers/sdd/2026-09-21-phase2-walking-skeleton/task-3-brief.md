### Task 3: 协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端

**产出**：PC 端启动后监听 `127.0.0.1:27183` 的 TCP；`adb reverse` 打通后，手机侧进程连上来，双方能互发消息。**此任务不接采集也不接注入**，只把管道打通并用 PING/PONG 证明。

**Files:**
- Create: `src/agent/Protocol.cs`
- Create: `src/agent/Transport.cs`
- Create: `src/agent/DeviceLauncher.cs`
- Create: `src/injector/Injector.java`（修改：stdin 循环改为 socket 循环，保留 stdin 调试模式）
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 2 的 `Injector`
- Produces:
  - `Protocol`：`public const byte MsgEnter=0x01, MsgMove=0x02, MsgButton=0x03, MsgScroll=0x04, MsgKey=0x05, MsgLeave=0x06, MsgConfig=0x07, MsgPing=0x08, MsgPong=0x09;`
    - `public static byte[] EncodeEnter(short x, short y)`
    - `public static byte[] EncodeMove(short dx, short dy)`
    - `public static byte[] EncodeButton(byte btn, byte down)`
    - `public static byte[] EncodeScroll(short dx, short dy)`
    - `public static byte[] EncodeKey(ushort scancode, byte down, byte mods)`
    - `public static byte[] EncodeLeave()`
    - `public static byte[] EncodePing(uint seq)` / `EncodePong(uint seq)`
    - `public static bool TryDecode(byte[] buf, int len, out byte type, out byte[] payload)`
  - `Transport`：`public Transport(int port)`；`public bool Start()`；`public bool IsConnected { get; }`；`public event Action Connected` / `Disconnected`；`public void Send(byte[] frame)`；`public void Stop()`
  - `DeviceLauncher`：`public static bool Prepare(string jarLocalPath)`（`adb reverse` + `push`）；`public static Process Start()`（`app_process` 拉起）；`public static void Cleanup()`

**背景**：`adb reverse tcp:27183 tcp:27183` 让**手机的** `127.0.0.1:27183` 转发到 **PC 的** `127.0.0.1:27183`。所以 **PC 是服务端、手机是客户端**。PC 必须先开始监听，再拉起手机进程。

- [ ] **Step 1: 编写协议编解码**

创建 `src/agent/Protocol.cs`：

```csharp
using System;

namespace PcKvm
{
    /// <summary>协议编解码。纯函数，不做任何 I/O，便于单独推理与测试。</summary>
    public static class Protocol
    {
        public const byte MsgEnter  = 0x01;   // int16 x, int16 y
        public const byte MsgMove   = 0x02;   // int16 dx, int16 dy
        public const byte MsgButton = 0x03;   // uint8 btn, uint8 down
        public const byte MsgScroll = 0x04;   // int16 dx, int16 dy
        public const byte MsgKey    = 0x05;   // uint16 scancode, uint8 down, uint8 mods
        public const byte MsgLeave  = 0x06;   // 无负载
        public const byte MsgConfig = 0x07;   // uint8 edge, uint16 pcEdgeLen, uint16 phoneW, uint16 phoneH
        public const byte MsgPing   = 0x08;   // uint32 seq
        public const byte MsgPong   = 0x09;   // uint32 seq

        public static byte[] EncodeEnter(short x, short y)
        {
            byte[] b = new byte[5];
            b[0] = MsgEnter;
            PutI16(b, 1, x); PutI16(b, 3, y);
            return b;
        }

        public static byte[] EncodeMove(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgMove;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeButton(byte btn, byte down)
        {
            byte[] b = new byte[3];
            b[0] = MsgButton; b[1] = btn; b[2] = down;
            return b;
        }

        public static byte[] EncodeScroll(short dx, short dy)
        {
            byte[] b = new byte[5];
            b[0] = MsgScroll;
            PutI16(b, 1, dx); PutI16(b, 3, dy);
            return b;
        }

        public static byte[] EncodeKey(ushort scancode, byte down, byte mods)
        {
            byte[] b = new byte[5];
            b[0] = MsgKey;
            PutU16(b, 1, scancode); b[3] = down; b[4] = mods;
            return b;
        }

        public static byte[] EncodeLeave()
        {
            return new byte[] { MsgLeave };
        }

        public static byte[] EncodeConfig(byte edge, ushort pcEdgeLen, ushort phoneW, ushort phoneH)
        {
            byte[] b = new byte[8];
            b[0] = MsgConfig; b[1] = edge;
            PutU16(b, 2, pcEdgeLen); PutU16(b, 4, phoneW); PutU16(b, 6, phoneH);
            return b;
        }

        public static byte[] EncodePing(uint seq) { return EncodeU32(MsgPing, seq); }
        public static byte[] EncodePong(uint seq) { return EncodeU32(MsgPong, seq); }

        static byte[] EncodeU32(byte type, uint v)
        {
            byte[] b = new byte[5];
            b[0] = type;
            PutU32(b, 1, v);
            return b;
        }

        /// <summary>所有消息都是定长的，按 type 查表得长度；返回 0 表示类型未知。</summary>
        public static int PayloadLength(byte type)
        {
            switch (type)
            {
                case MsgEnter: return 4;
                case MsgMove: return 4;
                case MsgButton: return 2;
                case MsgScroll: return 4;
                case MsgKey: return 4;
                case MsgLeave: return 0;
                case MsgConfig: return 7;
                case MsgPing: return 4;
                case MsgPong: return 4;
                default: return -1;
            }
        }

        public static short GetI16(byte[] b, int off)
        {
            return (short)(b[off] | (b[off + 1] << 8));
        }

        public static ushort GetU16(byte[] b, int off)
        {
            return (ushort)(b[off] | (b[off + 1] << 8));
        }

        public static uint GetU32(byte[] b, int off)
        {
            return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));
        }

        static void PutI16(byte[] b, int off, short v) { PutU16(b, off, (ushort)v); }
        static void PutU16(byte[] b, int off, ushort v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
        }
        static void PutU32(byte[] b, int off, uint v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
```

- [ ] **Step 2: 编写传输层**

创建 `src/agent/Transport.cs`：

```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PcKvm
{
    /// <summary>TCP 服务端。单一连接，断线后自动等待重连。</summary>
    public class Transport
    {
        readonly int _port;
        TcpListener _listener;
        TcpClient _client;
        NetworkStream _stream;
        Thread _acceptThread;
        volatile bool _running;
        readonly object _sendLock = new object();

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte, byte[]> MessageReceived;   // (type, payload)

        public bool IsConnected { get { return _client != null && _client.Connected; } }

        public Transport(int port) { _port = port; }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
            }
            catch (Exception)
            {
                return false;
            }
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            return true;
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient c = _listener.AcceptTcpClient();
                    c.NoDelay = true;          // 输入转发对延迟敏感，禁用 Nagle
                    _client = c;
                    _stream = c.GetStream();
                    Action conn = Connected;
                    if (conn != null) conn();
                    ReadLoop();
                    Action disc = Disconnected;
                    if (disc != null) disc();
                }
                catch (Exception)
                {
                    // 对端断开或监听器被 Stop，回到等待下一次连接
                }
                finally
                {
                    try { if (_stream != null) _stream.Close(); } catch (Exception) { }
                    try { if (_client != null) _client.Close(); } catch (Exception) { }
                    _stream = null;
                    _client = null;
                }
            }
        }

        void ReadLoop()
        {
            byte[] head = new byte[1];
            while (_running)
            {
                if (!ReadExact(head, 1)) return;
                int plen = Protocol.PayloadLength(head[0]);
                if (plen < 0) return;                      // 未知类型，认为流已错位
                byte[] payload = new byte[plen];
                if (plen > 0 && !ReadExact(payload, plen)) return;
                Action<byte, byte[]> h = MessageReceived;
                if (h != null) h(head[0], payload);
            }
        }

        bool ReadExact(byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r;
                try { r = _stream.Read(buf, got, n - got); }
                catch (Exception) { return false; }
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        /// <summary>线程安全发送。连接不存在时静默丢弃——调用方不应因对端断开而崩溃。</summary>
        public void Send(byte[] frame)
        {
            NetworkStream s = _stream;
            if (s == null) return;
            lock (_sendLock)
            {
                try { s.Write(frame, 0, frame.Length); }
                catch (Exception) { /* 对端已断开，下一次 Connected 会重建 */ }
            }
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch (Exception) { }
            try { if (_client != null) _client.Close(); } catch (Exception) { }
        }
    }
}
```

- [ ] **Step 3: 编写设备拉起器**

创建 `src/agent/DeviceLauncher.cs`：

```csharp
using System.Diagnostics;
using System.IO;

namespace PcKvm
{
    /// <summary>通过 adb 建立反向隧道并拉起设备侧进程。零安装：不推送 APK，只 push 一个 jar。</summary>
    public static class DeviceLauncher
    {
        public const int Port = 27183;
        const string RemoteJar = "/data/local/tmp/pckvm.jar";

        public static bool Prepare(string localJarPath)
        {
            if (!File.Exists(localJarPath)) return false;
            if (RunAdb("reverse tcp:" + Port + " tcp:" + Port) != 0) return false;
            if (RunAdb("push \"" + localJarPath + "\" " + RemoteJar) != 0) return false;
            return true;
        }

        /// <summary>拉起设备侧进程。返回的 Process 由调用方负责 Kill——它是 PC 端唯一能关掉它的手段。</summary>
        public static Process Start()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = "shell \"CLASSPATH=" + RemoteJar + " app_process / Injector\"";
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            Process p = Process.Start(psi);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        public static void Cleanup(Process p)
        {
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch (System.Exception) { }
            }
            RunAdb("shell rm -f " + RemoteJar);
            RunAdb("reverse --remove tcp:" + Port);
        }

        static int RunAdb(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "adb";
            psi.Arguments = args;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }
    }
}
```

- [ ] **Step 4: 把设备侧改成 socket 循环**

修改 `src/injector/Injector.java`：把 stdin 循环换成 **socket 客户端**，连 `127.0.0.1:27183`；**保留 stdin 调试模式**（命令行传 `--stdin` 时走原路径）。

```java
import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;

public class Injector {

    static final int PORT = 27183;
    static final byte MSG_ENTER = 0x01, MSG_MOVE = 0x02, MSG_BUTTON = 0x03,
                      MSG_SCROLL = 0x04, MSG_KEY = 0x05, MSG_LEAVE = 0x06,
                      MSG_CONFIG = 0x07, MSG_PING = 0x08, MSG_PONG = 0x09;

    /** 当前按下的鼠标按钮位图（bit0 左, bit1 右, bit2 中）。 */
    static int buttonsDown = 0;

    public static void main(String[] args) throws Exception {
        if (args.length > 0 && args[0].equals("--stdin")) {
            StdinMode.run();
            return;
        }

        System.out.println("INJECTOR start");
        UhidDevice dev;
        try {
            dev = new UhidDevice("PC-KVM Virtual Input", HidDescriptor.MOUSE_KEYBOARD);
        } catch (Exception e) {
            System.out.println("INJECTOR FAIL open /dev/uhid: " + e);
            return;
        }
        System.out.println("INJECTOR ready");

        // 连不上就重试：PC 端可能比我们先起好，也可能刚重连
        Socket sock = null;
        for (int i = 0; i < 100 && sock == null; i++) {
            try {
                sock = new Socket();
                sock.connect(new InetSocketAddress("127.0.0.1", PORT), 1000);
                sock.setTcpNoDelay(true);
            } catch (Exception e) {
                sock = null;
                Thread.sleep(100);
            }
        }
        if (sock == null) {
            System.out.println("INJECTOR FAIL cannot connect to PC");
            dev.close();
            return;
        }
        System.out.println("INJECTOR connected");

        InputStream in = sock.getInputStream();
        OutputStream out = sock.getOutputStream();

        try {
            while (true) {
                int type = in.read();
                if (type < 0) break;
                int plen = payloadLength(type);
                if (plen < 0) break;
                byte[] p = new byte[plen];
                if (plen > 0 && !readExact(in, p, plen)) break;
                handle(dev, out, (byte) type, p);
            }
        } catch (Exception e) {
            System.out.println("INJECTOR loop end: " + e);
        }

        dev.close();
        sock.close();
        System.out.println("INJECTOR exit");
    }

    static void handle(UhidDevice dev, OutputStream out, byte type, byte[] p) throws Exception {
        if (type == MSG_MOVE) {
            int dx = i16(p, 0), dy = i16(p, 1 + 1);
            // int8 轴：超过 ±127 要拆成多条报告，否则会丢位移
            while (dx != 0 || dy != 0) {
                int sx = clamp127(dx), sy = clamp127(dy);
                dev.sendMouse((byte) buttonsDown, (byte) sx, (byte) sy, (byte) 0);
                dx -= sx; dy -= sy;
            }
        } else if (type == MSG_BUTTON) {
            int btn = p[0] & 0xFF, down = p[1] & 0xFF;
            int bit = btnToBit(btn);
            if (down != 0) buttonsDown |= bit; else buttonsDown &= ~bit;
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) 0);
        } else if (type == MSG_SCROLL) {
            int dy = i16(p, 2);
            dev.sendMouse((byte) buttonsDown, (byte) 0, (byte) 0, (byte) clamp127(dy));
        } else if (type == MSG_KEY) {
            KeyState.apply(dev, u16(p, 0), p[2] != 0, p[3]);
        } else if (type == MSG_PING) {
            int seq = i32(p, 0);
            byte[] r = new byte[5];
            r[0] = MSG_PONG;
            r[1] = (byte) (seq & 0xFF);
            r[2] = (byte) ((seq >>> 8) & 0xFF);
            r[3] = (byte) ((seq >>> 16) & 0xFF);
            r[4] = (byte) ((seq >>> 24) & 0xFF);
            out.write(r);
            out.flush();
        }
        // MSG_ENTER / MSG_LEAVE / MSG_CONFIG 由后续任务接管
    }

    static int btnToBit(int btn) {
        if (btn == 1) return 1;   // 左
        if (btn == 2) return 2;   // 右
        if (btn == 3) return 4;   // 中
        return 0;
    }

    static int payloadLength(int type) {
        switch (type) {
            case MSG_ENTER:  return 4;
            case MSG_MOVE:   return 4;
            case MSG_BUTTON: return 2;
            case MSG_SCROLL: return 4;
            case MSG_KEY:    return 4;
            case MSG_LEAVE:  return 0;
            case MSG_CONFIG: return 7;
            case MSG_PING:   return 4;
            case MSG_PONG:   return 4;
            default:         return -1;
        }
    }

    static boolean readExact(InputStream in, byte[] buf, int n) throws Exception {
        int got = 0;
        while (got < n) {
            int r = in.read(buf, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    static int clamp127(int v) {
        if (v > 127) return 127;
        if (v < -127) return -127;
        return v;
    }

    static int i16(byte[] b, int off) {
        return (short) ((b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8));
    }

    static int u16(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8);
    }

    static int i32(byte[] b, int off) {
        return (b[off] & 0xFF) | ((b[off + 1] & 0xFF) << 8)
             | ((b[off + 2] & 0xFF) << 16) | ((b[off + 3] & 0xFF) << 24);
    }
}
```

同时创建 `src/injector/StdinMode.java`（把 Task 2 的 stdin 循环原样搬进来，类名改 `StdinMode`，方法改 `public static void run()`），以及 `src/injector/KeyState.java`：

```java
/** 维护按下的修饰键与 6 个按键槽位，并在变化时发出键盘报告。 */
public class KeyState {

    static int modifiers = 0;
    static int[] slots = new int[6];

    /** scancode 是 Windows 形式（低字节 MakeCode，E0 置 0xE000），此处先只处理非 E0 的普通键。 */
    public static void apply(UhidDevice dev, int scancode, boolean down, int mods) throws Exception {
        modifiers = mods & 0xFF;
        int usage = ScancodeMap.toHidUsage(scancode);
        if (usage < 0) return;   // 未映射的键直接忽略，不破坏报告

        if (down) {
            if (!contains(usage)) {
                int free = freeSlot();
                if (free >= 0) slots[free] = usage;
            }
        } else {
            for (int i = 0; i < 6; i++) if (slots[i] == usage) slots[i] = 0;
        }
        byte[] keys = new byte[6];
        for (int i = 0; i < 6; i++) keys[i] = (byte) slots[i];
        dev.sendKeyboard((byte) modifiers, keys);
    }

    static boolean contains(int usage) {
        for (int i = 0; i < 6; i++) if (slots[i] == usage) return true;
        return false;
    }

    static int freeSlot() {
        for (int i = 0; i < 6; i++) if (slots[i] == 0) return i;
        return -1;
    }
}
```

以及 `src/injector/ScancodeMap.java`：Windows scancode → HID Usage ID（Keyboard/Keypad page `0x07`）映射表。**本任务只需覆盖字母、数字、常用符号与修饰键**（完整表在 Task 9 补齐）：

```java
/** Windows scancode → HID Usage ID（Keyboard/Keypad page 0x07）。 */
public class ScancodeMap {

    /** 返回 -1 表示未映射。scancode 低字节为 MakeCode。 */
    public static int toHidUsage(int scancode) {
        int mk = scancode & 0xFF;
        switch (mk) {
            case 0x01: return 0x29;   // Esc
            case 0x02: return 0x1E;   // 1
            case 0x03: return 0x1F;   // 2
            case 0x04: return 0x20;   // 3
            case 0x05: return 0x21;   // 4
            case 0x06: return 0x22;   // 5
            case 0x07: return 0x23;   // 6
            case 0x08: return 0x24;   // 7
            case 0x09: return 0x25;   // 8
            case 0x0A: return 0x26;   // 9
            case 0x0B: return 0x27;   // 0
            case 0x0E: return 0x2A;   // Backspace
            case 0x0F: return 0x2B;   // Tab
            case 0x10: return 0x14;   // Q
            case 0x11: return 0x1A;   // W
            case 0x12: return 0x08;   // E
            case 0x13: return 0x15;   // R
            case 0x14: return 0x17;   // T
            case 0x15: return 0x1C;   // Y
            case 0x16: return 0x18;   // U
            case 0x17: return 0x0C;   // I
            case 0x18: return 0x12;   // O
            case 0x19: return 0x13;   // P
            case 0x1A: return 0x2F;   // [
            case 0x1B: return 0x30;   // ]
            case 0x1C: return 0x28;   // Enter
            case 0x1E: return 0x04;   // A
            case 0x1F: return 0x16;   // S
            case 0x20: return 0x07;   // D
            case 0x21: return 0x09;   // F
            case 0x22: return 0x0A;   // G
            case 0x23: return 0x0B;   // H
            case 0x24: return 0x0D;   // J
            case 0x25: return 0x0E;   // K
            case 0x26: return 0x0F;   // L
            case 0x27: return 0x33;   // ;
            case 0x28: return 0x34;   // '
            case 0x29: return 0x35;   // `
            case 0x2B: return 0x31;   // backslash
            case 0x2C: return 0x1D;   // Z
            case 0x2D: return 0x1B;   // X
            case 0x2E: return 0x06;   // C
            case 0x2F: return 0x19;   // V
            case 0x30: return 0x05;   // B
            case 0x31: return 0x11;   // N
            case 0x32: return 0x10;   // M
            case 0x33: return 0x36;   // ,
            case 0x34: return 0x37;   // .
            case 0x35: return 0x38;   // /
            case 0x39: return 0x2C;   // Space
            default:   return -1;
        }
    }
}
```

- [ ] **Step 5: 在 Program.cs 里接上传输并验证 PING/PONG**

修改 `src/agent/Program.cs`，在托盘装配之前插入：

```csharp
            Transport transport = new Transport(DeviceLauncher.Port);
            if (!transport.Start())
            {
                MessageBox.Show("TCP 端口 " + DeviceLauncher.Port + " 监听失败，程序退出。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string jar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pckvm.jar");
            if (!DeviceLauncher.Prepare(jar))
            {
                MessageBox.Show("adb 隧道/推送失败。确认手机已连接且 USB 调试已开。",
                    "PC-KVM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            System.Diagnostics.Process devProc = DeviceLauncher.Start();

            transport.Connected += delegate
            {
                log.WriteLine("# 设备已连接");
                transport.Send(Protocol.EncodePing(1));
            };
            transport.Disconnected += delegate { log.WriteLine("# 设备已断开"); };
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    log.WriteLine("# PONG seq=" + Protocol.GetU32(payload, 0));
            };
```

并在 `ApplicationExit` 里加上 `transport.Stop(); DeviceLauncher.Cleanup(devProc);`。

同时把 `build/build-agent.ps1` 的最后加一行，把 `%TEMP%\pckvm.jar` 复制到 `dist\pckvm.jar`（PC 端运行时从 exe 同目录找它）：

```powershell
Copy-Item "$env:TEMP\pckvm.jar" "$dist\pckvm.jar" -Force
Write-Host "已复制 pckvm.jar 到 dist\" -ForegroundColor Green
```

- [ ] **Step 6: 端到端验证**

Run（先确保手机已连接、`adb devices` 有设备）:
```powershell
powershell -ExecutionPolicy Bypass -File build/build-injector.ps1
powershell -ExecutionPolicy Bypass -File build/build-agent.ps1
dist\pc-kvm.exe
```

Expected：`dist\pc-kvm.log` 出现 `# 设备已连接` 与 `# PONG seq=1`；手机无任何图标出现（零安装）。

- [ ] **Step 7: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Protocol.cs src/agent/Transport.cs src/agent/DeviceLauncher.cs \
        src/agent/Program.cs src/injector/ build/build-agent.ps1
git commit -m "feat: 协议与传输 —— PC 侧 TCP 服务端 + 手机侧 socket 客户端 + adb reverse 隧道"
```

---

