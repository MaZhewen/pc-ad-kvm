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
        readonly int _edgeX;            // 触发边界的 x（屏幕像素）
        readonly int _edgeTop;
        readonly int _edgeBottom;
        int _phoneW;                    // 可变：旋转/尺寸变化时由 SetPhoneSize 更新
        int _phoneH;
        readonly bool _phoneRight;      // true = 手机在 PC 右侧（此时从手机左边缘入屏）

        public KvmState Current { get; private set; }
        public bool Armed { get; private set; }   // 回程冷却：离开边缘安全带后才重新武装

        public event Action<short, short> EnterTakeover;
        public event Action LeaveTakeover;

        /// <summary>phoneW/phoneH 为占位初值（竖屏 2136x3200），非权威常量；
        /// 真值在连接后首次鼠标移动、及每次旋转变化时经 SetPhoneSize 灌入（Task 5B 几何轮询）。</summary>
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
            Action<short, short> h = EnterTakeover;
            if (h != null) h((short)phoneX, (short)phoneY);
        }

        /// <summary>TAKEOVER 态下、每次鼠标事件调用，传入游标模型算出的实际增量。</summary>
        public void OnTakeoverMove(int actualDx, int actualDy, int vx, int vy)
        {
            if (Current != KvmState.Takeover) return;

            // 回程判定：虚拟光标撞到与 PC 相邻的那条边
            bool backAtEdge = _phoneRight ? vx <= 0 : vx >= _phoneW - 1;
            if (!backAtEdge) return;

            // 必须仍在继续往外推，否则光标停在边上就会立刻回程
            bool pushingBack = _phoneRight ? actualDx < 0 : actualDx > 0;
            if (!pushingBack) return;

            Current = KvmState.Idle;
            Armed = false;          // 回到 IDLE 后先解除武装，等光标离开安全带
            Action h = LeaveTakeover;
            if (h != null) h();
        }
    }
}
