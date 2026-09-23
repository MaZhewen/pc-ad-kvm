/**
 * 把一次相对位移拆成"每轴不超过 ±127"的报告序列。
 *
 * 为什么需要它：HID 鼠标报告的 dx/dy 是 int8（描述符里 Logical Minimum/Maximum = -127/127），
 * 一条报告最多走 127 像素。而三处都需要"送一个任意大小的相对位移"——
 *   MSG_MOVE（用户推鼠标的增量）、MSG_ENTER（把光标从 HOME 后的 (0,0) 挪到入屏点，
 *   左侧入屏时 x=3199 需要 26 条）、MSG_HOME（往左上角硬顶）。
 * 拆分算法只有一份，就在这里。
 *
 * 纯函数、不碰设备，故由 tests/scancode-map 离线钉住：
 * 各条之和必须恰好等于目标（少一条会丢位移、多一条会多发空报告），且每条 |v| <= 127。
 */
public class PointerPlan {

    /** 该位移需要几条报告（两轴取较长的那一边）。 */
    public static int stepCount(int dx, int dy) {
        int n = steps1(dx);
        int m = steps1(dy);
        return n > m ? n : m;
    }

    /** 第 i 条（0 基）报告应有的 dx。越界返回 0。 */
    public static int stepX(int dx, int i) { return step1(dx, i); }

    /** 第 i 条（0 基）报告应有的 dy。越界返回 0。 */
    public static int stepY(int dy, int i) { return step1(dy, i); }

    /** 单轴需要几条：ceil(|v| / 127)。 */
    static int steps1(int v) {
        int a = v < 0 ? -v : v;
        return (a + 126) / 127;      // 整数除法下这等于向上取整；v=0 时为 0
    }

    /** 单轴第 i 条的量：先每步走满 127，最后一条走余数。 */
    static int step1(int v, int i) {
        int a = v < 0 ? -v : v;
        int taken = 127 * i;
        if (taken >= a) return 0;
        int rest = a - taken;
        int s = rest > 127 ? 127 : rest;
        return v < 0 ? -s : s;
    }
}
