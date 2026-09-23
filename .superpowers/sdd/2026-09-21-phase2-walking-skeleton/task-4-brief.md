### Task 4: 最小闭环 —— PC 鼠标直接驱动手机光标

**产出**：移动 PC 鼠标，**手机屏幕上的光标同步移动**。此任务**不做边缘判定、不做抑制**——PC 鼠标照常工作，同时手机光标跟着动。这是第一个可见成果。

**Files:**
- Modify: `src/agent/Program.cs`

**Interfaces:**
- Consumes: Task 1 的 `RawInput`、Task 3 的 `Transport` / `Protocol`
- Produces: 无新接口（纯接线）

- [ ] **Step 1: 接线**

在 `src/agent/Program.cs` 的 `ri.MouseMoved += ...` 处理器里，把已有日志之外追加发送：

```csharp
            ri.MouseMoved += delegate(RawMouseEvent e)
            {
                transport.Send(Protocol.EncodeMove((short)e.Dx, (short)e.Dy));

                short wheel = (short)(e.WheelDelta / 120);   // Windows 一格 = 120，HID 一格 = 1
                if (wheel != 0) transport.Send(Protocol.EncodeScroll((short)0, wheel));

                if (e.ButtonFlags != 0)
                {
                    EmitButton(transport, e.ButtonFlags, 0x0001, 1);   // 左
                    EmitButton(transport, e.ButtonFlags, 0x0004, 2);   // 右
                    EmitButton(transport, e.ButtonFlags, 0x0010, 3);   // 中
                }
            };
```

并在 `Program` 里加这个辅助方法（放在 `Main` 之后）：

```csharp
        /// <summary>把 Windows 的 down/up 位对翻译成协议的单次按钮事件。</summary>
        static void EmitButton(Transport t, ushort flags, ushort downBit, byte btn)
        {
            ushort upBit = (ushort)(downBit << 1);
            if ((flags & downBit) != 0) t.Send(Protocol.EncodeButton(btn, 1));
            else if ((flags & upBit) != 0) t.Send(Protocol.EncodeButton(btn, 0));
        }
```

- [ ] **Step 2: 验证**

Run: `dist\pc-kvm.exe`（前置：Task 3 的构建已跑过）

判定：
1. 移动鼠标 → **手机光标跟着动**，方向一致 ✅
2. 快速甩动鼠标 → 手机光标不应"少走"（验证 int8 拆分逻辑生效） ✅
3. 滚轮上下一格 → 手机内容滚动 ✅
4. 左键点击 → 手机上对应位置被点击 ✅
5. 右键/中键 → 同样生效 ✅
6. **PC 自己一切照常**（此任务无抑制） ✅

- [ ] **Step 3: 提交**

```bash
cd /g/pc-kvm
git add src/agent/Program.cs
git commit -m "feat: 最小闭环 —— 移动 PC 鼠标即驱动手机光标"
```

---

