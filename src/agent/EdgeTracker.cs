using System;

namespace PcKvm
{
    public enum KvmState { Idle, Takeover }

    /// <summary>
    /// 边缘跨越状态机与坐标映射。
    /// 只在 IDLE 态读真实光标位置（此时未被 ClipCursor 冻结）；进入 TAKEOVER 后 PC 光标不再可用，
    /// 一切位置都由 CursorModel 承担。
    /// </summary>
    public class EdgeTracker
    {
        int _edgeX;                     // 触发侧最外侧有效像素列的 x（右挂 = 桌面宽-1，左挂 = 0）。
                                        // 约定：不是"边界外那条虚线"，而是光标真实可达的最后一列——
                                        // Windows 把光标钳在最后一列内，GetCursorPos 永远到不了桌面宽。
                                        // 左右两侧在此约定下天然对称（atEdge/安全带判定无需分侧特判）。
        int _edgeTop;
        int _edgeBottom;
        int _phoneW;                    // 可变：旋转/尺寸变化时由 SetPhoneSize 更新
        int _phoneH;
        bool _phoneRight;               // true = 手机在 PC 右侧（此时从手机左边缘入屏）

        // 回程外推的累积量（mickeys）。**必须累积越过阈值**才算"用户想回 PC"，
        // 不能看单次增量符号：真机实测入屏瞬间虚拟光标就在 x=0（= 与 PC 相邻的那条边），
        // 此时任何一次 rawDx=-1 的抖动都会满足"在边缘且向外推"，接管寿命 0–6 秒。
        int _backPush;
        // 上一次事件后的虚拟光标 x。用来区分「从屏内滑到边界」与「在边界上继续外推」：
        // 前者是到达边界，不应计入外推；后者才是"越过边界"的回程意图。
        int _lastVx;

        /// <summary>回程外推阈值（mickeys）。一格的物理位移约 1 像素，
        /// 40 相当于鼠标移动约 1 厘米——远大于抖动（实测抖动｜增量｜≤ 8），
        /// 又小到用户在边界上"再推一下"即可触发。</summary>
        const int BackPushThreshold = 40;

        public KvmState Current { get; private set; }
        public bool Armed { get; private set; }   // 回程冷却：离开边缘安全带后才重新武装

        public event Action<short, short> EnterTakeover;
        public event Action LeaveTakeover;

        /// <summary>phoneW/phoneH 为占位初值（竖屏 2136x3200），非权威常量；
        /// 真值在连接后首次鼠标移动、及每次旋转变化时经 SetPhoneSize 灌入（Task 5B 几何轮询）。
        /// edgeX 约定：触发侧最外侧有效像素列的 x（右挂传桌面宽-1，左挂传 0），见字段注释。</summary>
        public EdgeTracker(int edgeX, int edgeTop, int edgeBottom,
                           int phoneW, int phoneH, bool phoneRight)
        {
            _edgeX = edgeX;
            _edgeTop = edgeTop;
            _edgeBottom = edgeBottom;
            _phoneW = phoneW;
            _phoneH = phoneH;
            _phoneRight = phoneRight;
            Current = KvmState.Idle;
            Armed = true;
        }

        /// <summary>屏幕旋转/尺寸变化时更新手机逻辑尺寸。</summary>
        public void SetPhoneSize(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            _phoneW = w;
            _phoneH = h;
        }

        /// <summary>Screen rotation changes the takeover coordinate system without ending it.</summary>
        public void RebaseTakeover(int x)
        {
            if (Current != KvmState.Takeover) return;
            _lastVx = x;
            _backPush = 0;
        }

        /// <summary>运行时改变跨越边（阶段三 #2：手机在左/右可配置）。
        /// **刻意不重建 tracker**：EnterTakeover/LeaveTakeover 的订阅挂在对象上，
        /// 重建会丢订阅（那是"接不到事件"的静默失效）。故只改字段。
        /// 同时解除武装：换边后光标很可能正好落在新边缘上，等它离开安全带再重新武装，
        /// 免得用户刚点完"确定"就被弹进接管。</summary>
        public void SetEdge(int edgeX, int edgeTop, int edgeBottom, bool phoneRight)
        {
            _edgeX = edgeX;
            _edgeTop = edgeTop;
            _edgeBottom = edgeBottom;
            _phoneRight = phoneRight;
            Armed = false;
            _backPush = 0;
        }

        /// <summary>放弃跨越：回到 IDLE 并解除武装，等待用户把光标移离边缘。</summary>
        public void AbortTakeover()
        {
            Current = KvmState.Idle;
            Armed = false;
            _backPush = 0;
        }

        /// <summary>上一次回程判定所累积的外推量（mickeys），供日志记录。</summary>
        public int BackPush { get { return _backPush; } }

        /// <summary>IDLE 态下、每次鼠标事件调用。cursorX/Y 为真实光标位置。</summary>
        public void OnIdleMove(int dx, int dy, int cursorX, int cursorY)
        {
            const int SAFE = 12;   // 安全带宽度，防止回来瞬间被弹回去

            if (!Armed)
            {
                bool away = _phoneRight
                    ? cursorX < _edgeX - SAFE
                    : cursorX > _edgeX + SAFE;
                if (away) Armed = true;
                return;
            }

            if (cursorY < _edgeTop || cursorY >= _edgeBottom) return;

            bool atEdge;
            bool pushingOut;
            if (_phoneRight)
            {
                atEdge = cursorX >= _edgeX;
                pushingOut = dx > 0;
            }
            else
            {
                atEdge = cursorX <= _edgeX;
                pushingOut = dx < 0;
            }
            if (!atEdge || !pushingOut) return;

            // 入屏点用比例映射，保证 PC 边缘顶端 → 手机顶端
            int span = _edgeBottom - _edgeTop;
            int phoneY = (int)((long)(cursorY - _edgeTop) * _phoneH / span);
            if (phoneY < 0) phoneY = 0;
            if (phoneY > _phoneH - 1) phoneY = _phoneH - 1;
            int phoneX = _phoneRight ? 0 : _phoneW - 1;

            Current = KvmState.Takeover;
            Armed = false;
            _backPush = 0;
            _lastVx = phoneX;   // 入屏瞬间虚拟光标就落在这条"与 PC 相邻"的边上
            Action<short, short> h = EnterTakeover;
            if (h != null) h((short)phoneX, (short)phoneY);
        }

        /// <summary>TAKEOVER 态下、每次鼠标事件调用。rawDx/rawDy 为原始（未钳制）增量，
        /// 表达用户意图；CursorModel 钳制后的增量表达实际位移，回程判定要的是前者——
        /// 入口处虚拟光标在 x=0，NextDx 对负增量恒钳成 0，用钳后值判定会让"刚进去就推回"
        /// 永远无法离开。vx/vy 为虚拟光标当前坐标（已由调用方按本次增量更新）。
        ///
        /// 回程需要**累积越过阈值**，见 BackPush 与 BackPushThreshold 的注释：
        /// 单次增量的符号不足以判定意图，否则入屏瞬间的 -1 抖动就会把用户踢回 PC。</summary>
        public void OnTakeoverMove(int rawDx, int rawDy, int vx, int vy)
        {
            if (Current != KvmState.Takeover) return;

            bool atEdge = _phoneRight ? vx <= 0 : vx >= _phoneW - 1;
            bool wasAtEdge = _phoneRight ? _lastVx <= 0 : _lastVx >= _phoneW - 1;
            _lastVx = vx;

            // 两种情况都不算外推：光标还在屏内（含"本次事件刚刚扫到边界"——
            // 那是走到边界，不是越过边界）；或方向朝屏内（抖动/反向）。
            if (!atEdge || !wasAtEdge) { _backPush = 0; return; }

            int outward = _phoneRight ? -rawDx : rawDx;   // >0 = 朝 PC 方向推
            // 纯纵向/静止事件（outward == 0）**既不累积也不清零**：这类事件很常见
            //（日志里 `dx=0 dy=N` 的采样行大量存在——斜推、高回报率鼠标、手腕横移时分包），
            // 若拿它清零，用户斜着往 PC 方向推时累积量会被反复打散，表现为"回不去"，
            // 那比"退得太容易"更糟。反向增量才清零（正常接线下反向会先把光标带离边界，
            // 这里是防御性兜底）。
            if (outward < 0) { _backPush = 0; return; }
            if (outward == 0) return;

            _backPush += outward;
            if (_backPush < BackPushThreshold) return;

            Current = KvmState.Idle;
            Armed = false;          // 回到 IDLE 后先解除武装，等光标离开安全带
            Action h = LeaveTakeover;
            if (h != null) h();
        }
    }
}
