namespace PcKvm
{
    /// <summary>
    /// PC 端持有的虚拟光标（坐标系 = 手机屏幕像素）。
    /// 职责：把任意目标增量钳制成"不会越界"的实际发送量，并同步更新自身位置。
    /// 之所以要钳制：设备侧的系统光标会停在屏幕边缘，若我们照发全额增量，两边就会越走越远。
    /// </summary>
    public class CursorModel
    {
        readonly int _w;
        readonly int _h;

        public int X { get; private set; }
        public int Y { get; private set; }

        public CursorModel(int phoneW, int phoneH)
        {
            _w = phoneW;
            _h = phoneH;
            X = 0;
            Y = 0;
        }

        /// <summary>归零。必须在每次进入接管、且设备侧已执行 HOME 之后调用。</summary>
        public void Reset()
        {
            X = 0;
            Y = 0;
        }

        /// <summary>直接定位（用于跨越入屏）。不发送任何东西，只改模型。</summary>
        public void SetPosition(int x, int y)
        {
            X = Clamp(x, 0, _w - 1);
            Y = Clamp(y, 0, _h - 1);
        }

        /// <summary>返回实际应发送的 dx（已钳制到不越界），并按它更新模型。</summary>
        public short NextDx(int want)
        {
            int nx = Clamp(X + want, 0, _w - 1);
            int actual = nx - X;
            X = nx;
            return (short)actual;
        }

        /// <summary>返回实际应发送的 dy（已钳制到不越界），并按它更新模型。</summary>
        public short NextDy(int want)
        {
            int ny = Clamp(Y + want, 0, _h - 1);
            int actual = ny - Y;
            Y = ny;
            return (short)actual;
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
