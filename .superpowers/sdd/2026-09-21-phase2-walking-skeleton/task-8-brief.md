### Task 8: 边界情况 —— 心跳、断线解锁、逃逸键

**产出**：手机掉线、adb 断开、进程被杀等异常下，**用户永远不会被困在锁死状态**。这三条是"能日常用"与"偶尔抽风"的分界。

**Files:**
- Modify: `src/agent/Program.cs`
- Modify: `src/injector/Injector.java`

**Interfaces:**
- Consumes: Task 7 的 `Suppressor`、Task 3 的 `Transport`
- Produces: 无新接口

- [ ] **Step 1: 加心跳**

PC 侧每 1 秒发一次 PING，记录最后收到 PONG 的时间；超过 2 秒未收到则视为断线。

在 `Main` 里加：

```csharp
            uint pingSeq = 0;
            long lastPongTicks = DateTime.UtcNow.Ticks;
            transport.MessageReceived += delegate(byte type, byte[] payload)
            {
                if (type == Protocol.MsgPong)
                    lastPongTicks = DateTime.UtcNow.Ticks;
            };

            Timer heartbeat = new Timer();
            heartbeat.Interval = 1000;
            heartbeat.Tick += delegate
            {
                // 先做安全网：处于 TAKEOVER 时，「没连接」与「PONG 超时」都要立刻解除抑制。
                // 原写法把 `if (!transport.IsConnected) return;` 放在最前，会在掉线后让
                // 安全网整体短路——用户此时再推到边缘会重新进入 TAKEOVER 并夺取前台，
                // 而没有任何东西能把他救出来（见 Task 7 安全不变量 4，第二轮跨任务扫描发现）。
                if (tracker.Current == KvmState.Takeover)
                {
                    double age = (DateTime.UtcNow - new DateTime(lastPongTicks)).TotalSeconds;
                    if (!transport.IsConnected || age > 2.0)
                    {
                        log.WriteLine("# 心跳失联（" + age.ToString("F1") + "s, connected="
                                      + transport.IsConnected + "），强制解除抑制");
                        // 与逃逸键同理：放弃路径要通知设备侧清 buttonsDown/按键槽位。
                        // 若连接已断，Send 是安全 no-op（Transport.Send 在 _stream 为 null 时直接返回）。
                        transport.Send(Protocol.EncodeLeave());
                        tracker.AbortTakeover();
                        supp.Release();
                        host.SetStatus("IDLE");
                    }
                }

                if (!transport.IsConnected) return;
                pingSeq++;
                transport.Send(Protocol.EncodePing(pingSeq));
            };
            heartbeat.Start();
```

同时在 `transport.Disconnected` 处理器里加：

```csharp
            transport.Disconnected += delegate
            {
                log.WriteLine("# 设备已断开");
                if (tracker.Current == KvmState.Takeover)
                {
                    tracker.AbortTakeover();
                    supp.Release();
                    host.SetStatus("IDLE");
                }
            };
```

**这一段不是可选的**：Task 6 的审查把它列为 Important —— 掉线时 `tracker` 会**卡在 TAKEOVER**（`Disconnected` 只打日志、`cursor` 保持非 null、`Armed` 也不复位），而重连时 `Connected` 会装一个**全新的 `CursorModel`**，两者状态不再配套；此后所有 PC 鼠标移动都会走接管分支并镜像给手机。控制方裁决此条**在 Task 8 关闭**（ledger Ruling 22）：Task 8 本就是"边界情况"，且它的 `AbortTakeover()` 正好把 `Current` 与 `Armed` 一起复位。**Task 8 实现者不得省略这一段。**

- [ ] **Step 2: 加紧急逃逸键**

在 `MessageHost` 上挂键盘处理。**注意**：抑制生效时焦点在本窗口，所以这个键一定能收到。

```csharp
        public event Action Escape;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Alt | Keys.Escape))
            {
                Action h = Escape;
                if (h != null) h();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
```

（两个都重写是因为 WinForms 对含修饰键的组合在不同路径下分发不一致。）

在 `Main` 里接上：

```csharp
            host.Escape += delegate
            {
                log.WriteLine("# 逃逸键触发");
                // 放弃路径也必须通知设备侧清状态：接管期间若按着鼠标键/修饰键再逃逸，
                // 不补发 LEAVE 会让手机侧 buttonsDown 与按键槽位永久残留（leave 才会清）。
                // 设备侧处理 MSG_LEAVE 时会 buttonsDown=0 并 KeyState.releaseAll。
                transport.Send(Protocol.EncodeLeave());
                tracker.AbortTakeover();
                supp.Release();
                host.SetStatus("IDLE");
            };
```

- [ ] **Step 3: 设备侧在 stdin/socket 关闭时正确退出**

`Injector.java` 已经在内层循环退出后调 `dev.close()` 并打印 `INJECTOR exit`。补一条：**socket 读到 EOF 应立即退出**，不要留在后台。已有代码满足（`in.read()` 返回 -1 → `break`）。**额外补一个防御**：捕获 `IOException` 时也退出循环而非无限重试。已有 `try/catch` 覆盖。**本步骤只需确认这两点，不需改代码**——若发现不符，按上述要求修正。

- [ ] **Step 4: 验证（三条异常路径，逐条实测）**

Run: `dist\pc-kvm.exe`

1. **拔线解锁**：进入 `TAKEOVER` 后，**直接拔掉手机数据线** → 2 秒内小窗回到 `IDLE`，鼠标解锁 ✅
2. **杀设备进程**：进入 `TAKEOVER` 后，在另一个终端执行 `adb shell "pkill -f Injector"` → 心跳超时后回 `IDLE` ✅
3. **逃逸键**：进入 `TAKEOVER` 后按 `Ctrl+Alt+Esc` → 立即回 `IDLE` ✅
4. **UAC 抢占**：进入 `TAKEOVER` 后，触发一个 UAC 弹窗（例如运行 `powershell Start-Process cmd -Verb RunAs`）→ 250ms 内检测到前台丢失并解除 ✅
5. **正常流程不受影响**：完整走一遍跨越→收回，仍正常 ✅

- [ ] **Step 5: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs src/injector/Injector.java
git commit -m "feat: 边界情况 —— 心跳超时、断线解锁、紧急逃逸键、前台丢失保护"
```

---

