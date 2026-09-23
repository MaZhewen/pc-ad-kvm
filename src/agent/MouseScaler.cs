using System;

namespace PcKvm
{
    /// <summary>
    /// 原始鼠标增量 → 实际发送增量的缩放器，**带小数余量累积**。
    ///
    /// 为什么不能直接 (int)(dx * sensitivity)：系数小于 1 时小位移会被截断成 0
    /// （dx=1、系数 0.5 → 0 像素），表现为**慢速移动发涩、细调走不动**——
    /// 而那正是低灵敏度下最常用的场景，等于把滑块往低调就直接废掉这个功能。
    /// 故每轴保留一个 double 残差，把不足 1 像素的部分带到下一次。
    ///
    /// 纯逻辑、无 I/O、无 UI，故独立成文件（Program.cs 只剩 1 行预算，Ruling 26 禁令）
    /// 且可离线测（tests/agent-logic）。
    /// </summary>
    public class MouseScaler
    {
        double _sensitivity;
        double _resX;
        double _resY;

        public MouseScaler(double sensitivity)
        {
            _sensitivity = sensitivity;
        }

        public void SetSensitivity(double sensitivity)
        {
            _sensitivity = sensitivity;
        }

        /// <summary>进入接管时清零残差，避免带着跨越前的零头。几何变化路径**刻意不调它**：
        /// 残差对应的是用户的手真实移动出的 mickeys，跨几何变化保留是故意的。</summary>
        public void Reset()
        {
            _resX = 0;
            _resY = 0;
        }

        public int ApplyX(int rawDx)
        {
            _resX += rawDx * _sensitivity;
            // 向零截断：与"发送整数增量"的语义一致（不能用 Floor，那会让负方向多走一格）
            int send = (int)Math.Truncate(_resX);
            _resX -= send;
            return send;
        }

        public int ApplyY(int rawDy)
        {
            _resY += rawDy * _sensitivity;
            int send = (int)Math.Truncate(_resY);
            _resY -= send;
            return send;
        }
    }
}
