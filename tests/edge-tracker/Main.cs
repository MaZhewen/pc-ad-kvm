using System;
using PcKvm;

static class EdgeTest
{
    static int _pass = 0;
    static int _fail = 0;

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("PASS " + name + "  " + detail); }
        else { _fail++; Console.WriteLine("FAIL " + name + "  " + detail); }
    }

    // 真实可达几何：3840x1080 虚拟桌面，光标最右只能到 3839（Windows 钳制）
    static EdgeTracker NewTracker()
    {
        return new EdgeTracker(edgeX: 3839, edgeTop: 0, edgeBottom: 1080,
            phoneW: 3200, phoneH: 2136, phoneRight: true);
    }

    // 左挂镜像：手机在 PC 左侧 → 从手机【右】边缘入屏（phoneX = phoneW-1 = 3199），
    // 与 PC 相邻的那条边是手机的逻辑右缘。PC 侧 _edgeX = 0（桌面左缘可达列）。
    static EdgeTracker NewTrackerLeft()
    {
        return new EdgeTracker(edgeX: 0, edgeTop: 0, edgeBottom: 1080,
            phoneW: 3200, phoneH: 2136, phoneRight: false);
    }

    /// <summary>严格按 Program.cs 的接线喂事件：先 CursorModel.NextDx 更新虚拟光标，
    /// 再把**原始**增量与更新后的光标位置交给 tracker。
    /// 这样 vx 恒与真实增量自洽（不能用实现者的心智模型凭空造 vx）。</summary>
    static void Feed(EdgeTracker t, CursorModel c, int dx, int dy)
    {
        c.NextDx(dx);
        c.NextDy(dy);
        t.OnTakeoverMove(dx, dy, c.X, c.Y);
    }

    static void Main()
    {
        // ---- T1: enter + proportional mapping (540 * 2136 / 1080 = 1068); cursorX=3839 为真实可达最大值 ----
        {
            EdgeTracker t = NewTracker();
            int enterCount = 0; short px = -1, py = -1;
            t.EnterTakeover += delegate(short x, short y) { enterCount++; px = x; py = y; };
            t.OnIdleMove(5, 0, 3839, 540);
            Check("T1", t.Current == KvmState.Takeover && enterCount == 1
                && px == 0 && py == 1068,
                "state=" + t.Current + " enterCount=" + enterCount
                + " px=" + px + " py=" + py + " (expect Takeover,1,0,1068)");
        }

        // ---- T2: top of the edge band ----
        {
            EdgeTracker t = NewTracker();
            short px = -1, py = -1;
            t.EnterTakeover += delegate(short x, short y) { px = x; py = y; };
            t.OnIdleMove(5, 0, 3839, 0);
            Check("T2", t.Current == KvmState.Takeover && px == 0 && py == 0,
                "state=" + t.Current + " px=" + px + " py=" + py + " (expect Takeover,0,0)");
        }

        // ---- T3: bottom of the edge band ----
        {
            EdgeTracker t = NewTracker();
            short py = -1;
            t.EnterTakeover += delegate(short x, short y) { py = y; };
            t.OnIdleMove(5, 0, 3839, 1079);
            Check("T3", t.Current == KvmState.Takeover && py >= 2130 && py <= 2135,
                "state=" + t.Current + " py=" + py + " (expect Takeover, py in [2130,2135])");
        }

        // ---- T4: not at the edge (3838 = one column short of the last reachable column) ----
        {
            EdgeTracker t = NewTracker();
            int enterCount = 0;
            t.EnterTakeover += delegate(short x, short y) { enterCount++; };
            t.OnIdleMove(5, 0, 3838, 540);
            Check("T4", t.Current == KvmState.Idle && enterCount == 0,
                "state=" + t.Current + " enterCount=" + enterCount + " (expect Idle,0)");
        }

        // ---- T5: at the edge but pushed inward ----
        {
            EdgeTracker t = NewTracker();
            int enterCount = 0;
            t.EnterTakeover += delegate(short x, short y) { enterCount++; };
            t.OnIdleMove(-5, 0, 3839, 540);
            Check("T5", t.Current == KvmState.Idle && enterCount == 0,
                "state=" + t.Current + " enterCount=" + enterCount + " (expect Idle,0)");
        }

        // ---- T6: outside the vertical band (both sides) ----
        {
            EdgeTracker t = NewTracker();
            int enterCount = 0;
            t.EnterTakeover += delegate(short x, short y) { enterCount++; };
            t.OnIdleMove(5, 0, 3839, 1080);
            bool belowOk = (t.Current == KvmState.Idle) && enterCount == 0;
            t.OnIdleMove(5, 0, 3839, -1);
            bool aboveOk = (t.Current == KvmState.Idle) && enterCount == 0;
            Check("T6", belowOk && aboveOk,
                "below(y=1080):" + belowOk + " above(y=-1):" + aboveOk
                + " state=" + t.Current + " enterCount=" + enterCount + " (expect both Idle,0)");
        }

        // ---- T7: return cooldown。回程改用"决定性外推"（见 T9/T11 的新语义） ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int enterCount = 0, leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { enterCount++; };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter at (0,1068)
            Feed(t, c, -50, 0);                               // 在边界上决定性地往外推
            bool leftOk = (t.Current == KvmState.Idle) && leaveCount == 1;
            t.OnIdleMove(5, 0, 3839, 540);                    // still inside 12px safe band [3827,3839]
            bool noReenterOk = (t.Current == KvmState.Idle) && enterCount == 1;
            t.OnIdleMove(5, 0, 3800, 540);                    // leave the band -> re-armed
            bool armedOk = (t.Current == KvmState.Idle) && t.Armed;
            t.OnIdleMove(5, 0, 3839, 540);                    // re-enter
            bool reenterOk = (t.Current == KvmState.Takeover) && enterCount == 2;
            Check("T7", leftOk && noReenterOk && armedOk && reenterOk,
                "leave:" + leftOk + " noReenter:" + noReenterOk
                + " armedAfter3800:" + armedOk + " reenter:" + reenterOk
                + " state=" + t.Current + " enterCount=" + enterCount
                + " leaveCount=" + leaveCount + " (expect all True, Takeover,2,1)");
        }

        // ---- T8: no premature return ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter
            c.SetPosition(100, 1068);
            Feed(t, c, -3, 0);                                // not at adjacent edge
            bool midOk = (t.Current == KvmState.Takeover) && leaveCount == 0;
            c.SetPosition(0, 1068);
            Feed(t, c, 3, 0);                                 // at edge but pushing wrong way
            bool wrongWayOk = (t.Current == KvmState.Takeover) && leaveCount == 0;
            Check("T8", midOk && wrongWayOk,
                "midOk:" + midOk + " wrongWayOk:" + wrongWayOk
                + " state=" + t.Current + " leaveCount=" + leaveCount + " (expect both True, Takeover,0)");
        }

        // ---- T9: 入口处**持续**推回必须能离开。
        //      Ruling 21 的原意（"跨进去再推回能出来"）保留，但改为累积语义：
        //      单个微小左向增量不再算"推回"，必须累积越过阈值。 ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter, virtual cursor at (0,1068)
            Feed(t, c, -20, 0);                               // 累积 20，未达阈值
            bool notYet = (t.Current == KvmState.Takeover) && leaveCount == 0;
            Feed(t, c, -25, 0);                               // 累积 45 >= 阈值 40
            Check("T9", notYet && t.Current == KvmState.Idle && leaveCount == 1,
                "notYet:" + notYet + " state=" + t.Current + " leaveCount=" + leaveCount
                + " (expect True, Idle, 1)");
        }

        // ---- T10: 反向保护 —— 无外推意图（rawDx=0）不得回程 ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter, virtual cursor at (0,1068)
            Feed(t, c, 0, 0);                                 // no pushing intent
            Check("T10", t.Current == KvmState.Takeover && leaveCount == 0,
                "state=" + t.Current + " leaveCount=" + leaveCount + " (expect Takeover,0)");
        }

        // ---- T11（新，本次真机缺陷的直接回归）: 入口处的微小左向抖动不得回程 ----
        //      真机日志现场：`# ENTER takeover at phone(0,61)` 下一行就是 `# LEAVE takeover`。
        //      旧实现只要 vx<=0 且 rawDx<0（哪怕 -1）就立刻回程。
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter at (0,1068)
            Feed(t, c, -1, 0);                                // 抖动
            Feed(t, c, -2, 0);
            Feed(t, c, 1, 0);                                 // 反向 → 光标离开边界
            Feed(t, c, -3, 0);
            Feed(t, c, -2, 0);
            Feed(t, c, -1, 0);
            Feed(t, c, 0, 2);                                 // 纯按键/静止事件
            Check("T11", t.Current == KvmState.Takeover && leaveCount == 0,
                "state=" + t.Current + " leaveCount=" + leaveCount + " (expect Takeover,0)");
        }

        // ---- T12（新）: 从屏内滑到左边界的那一下不算"越过边界"，
        //      一个大增量把光标直接扫到 0 也不得立刻回程（防"滑动就回 PC"）。
        //      必须在边界上**继续**推才累积。 ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);
            Feed(t, c, 200, 0);                               // 推到 vx=200
            bool movedOk = (t.Current == KvmState.Takeover) && c.X == 200;
            Feed(t, c, -260, 0);                              // 一记大扫把光标直接扫到 0（并越过）
            bool sweepOk = (t.Current == KvmState.Takeover) && leaveCount == 0 && c.X == 0;
            Check("T12", movedOk && sweepOk,
                "moved:" + movedOk + " sweep:" + sweepOk
                + " state=" + t.Current + " vx=" + c.X + " leaveCount=" + leaveCount
                + " (expect True,True,Takeover,0,0)");
        }

        // ---- T13（新）: 在边界上持续外推可离开，且"走到边界"与"越过边界"可区分 ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);
            Feed(t, c, 200, 0);                               // vx=200
            Feed(t, c, -260, 0);                              // 扫到 0（不算外推）
            Feed(t, c, -20, 0);                               // 在边界上外推 20
            bool notYet = (t.Current == KvmState.Takeover) && leaveCount == 0;
            Feed(t, c, -25, 0);                               // 累积 45 >= 40
            Check("T13", notYet && t.Current == KvmState.Idle && leaveCount == 1,
                "notYet:" + notYet + " state=" + t.Current + " leaveCount=" + leaveCount
                + " (expect True, Idle, 1)");
        }

        // ---- T14（新）: 移动端反向 —— 中途反向推回原边界不得累积（累积量必须清零） ----
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);
            Feed(t, c, -30, 0);                               // 边界上累积 30
            Feed(t, c, 5, 0);                                 // 反向：光标离开边界
            Feed(t, c, -5, 0);                                // 走回边界（本事件不算外推）
            Feed(t, c, -30, 0);                               // 重新从 0 开始累积 30
            Check("T14", t.Current == KvmState.Takeover && leaveCount == 0,
                "state=" + t.Current + " leaveCount=" + leaveCount + " (expect Takeover,0)");
        }

        // ---- T15（新，审查 I1 的回归）: dx=0 的纯纵向/静止事件既不累积也不清零 ----
        //      日志里 dx=0 dy=N 的采样行大量存在（斜推/高回报率鼠标），若用它清零，
        //      用户斜着往 PC 方向推时累积量会被反复打散 → 表现为"回不去"。
        {
            EdgeTracker t = NewTracker();
            CursorModel c = new CursorModel(3200, 2136);
            int leaveCount = 0;
            t.EnterTakeover += delegate(short x, short y) { };
            t.LeaveTakeover += delegate { leaveCount++; };
            t.OnIdleMove(5, 0, 3839, 540);                    // enter at (0,1068)
            Feed(t, c, -20, 0);                               // 边界上累积 20
            Feed(t, c, 0, 3);                                 // 纯纵向事件：不得清零
            Feed(t, c, 0, 2);
            Feed(t, c, -25, 0);                               // 20 + 25 = 45 >= 40
            Check("T15", t.Current == KvmState.Idle && leaveCount == 1,
                "state=" + t.Current + " leaveCount=" + leaveCount + " (expect Idle,1)");
        }

        // ---- L1（左挂镜像）: 从桌面左缘入屏，落点为手机右边缘 x=3199，y 仍比例映射 ----
        // 与 T1 严格镜像：cursorY=540 → phoneY = 540*2136/1080 = 1068
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0; short px = -1, py = -1;
            t.EnterTakeover += delegate(short x, short y) { en++; px = x; py = y; };
            t.OnIdleMove(-5, 0, 0, 540);          // 在左缘、继续向左推
            Check("L1", t.Current == KvmState.Takeover && en == 1 && px == 3199 && py == 1068,
                "state=" + t.Current + " en=" + en + " px=" + px + " py=" + py
                + " (expect Takeover,1,3199,1068)");
        }

        // ---- L2（左挂）: 左缘但朝屏内推 → 不触发 ----
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(5, 0, 0, 540);           // 向右推 = 朝屏内
            Check("L2", t.Current == KvmState.Idle && en == 0,
                "state=" + t.Current + " en=" + en + " (expect Idle,0)");
        }

        // ---- L3（左挂）: 离开左缘一列（x=1）→ 不触发 ----
        {
            EdgeTracker t = NewTrackerLeft();
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(-5, 0, 1, 540);
            Check("L3", t.Current == KvmState.Idle && en == 0,
                "state=" + t.Current + " en=" + en + " (expect Idle,0)");
        }

        // ---- L4（左挂）: 入口处微小【右】向抖动不得回程（根因 B 的左挂镜像） ----
        // T11 是右挂版的 -1 抖动；左挂的"向外推"是 +dx，故抖动是 +1。
        // 入屏瞬间 _lastVx 被置为 phoneX = 3199（手机右缘），所以 +1 会被计入外推，
        // 但 1 < 40 阈值，必须仍留在接管态。这条钉住"阈值在左挂下同样生效"。
        {
            EdgeTracker t = NewTrackerLeft();
            CursorModel c = new CursorModel(3200, 2136);
            int lv = 0;
            t.LeaveTakeover += delegate { lv++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 进接管，_lastVx = 3199
            c.SetPosition(3199, 1068);              // 对齐真实接线：Program.cs 入屏即
                                                    // cursor.Reset()+SetPosition(入屏点)。
                                                    // 右挂用例没显式调它是因为入屏点 x=0
                                                    // 恰与 CursorModel 初值重合（巧合，不可依赖）
            Feed(t, c, 1, 0);                       // 入口处向右抖 1 mickey
            Check("L4", t.Current == KvmState.Takeover && lv == 0 && t.BackPush == 1,
                "state=" + t.Current + " leave=" + lv + " backPush=" + t.BackPush
                + " (expect Takeover,0,1)");
        }

        // ---- L5（左挂）: "走到边界"不算外推，在边界上继续外推 ≥40 才回程 ----
        // 分三步，与 T12/T13 的右挂语义严格镜像。
        {
            EdgeTracker t = NewTrackerLeft();
            CursorModel c = new CursorModel(3200, 2136);
            int lv = 0;
            t.LeaveTakeover += delegate { lv++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 进接管，vx=3199
            c.SetPosition(3199, 1068);              // 同 L4：对齐真实接线（入屏即定位到入屏点）
            Feed(t, c, -120, 0);                    // 往屏内走
            Check("L5a", t.Current == KvmState.Takeover && c.X == 3079,
                "state=" + t.Current + " vx=" + c.X + " (expect Takeover,3079)");
            Feed(t, c, 120, 0);                     // 又滑回右缘：这是"走到边界"，不得算外推
            Check("L5b", t.Current == KvmState.Takeover && c.X == 3199 && t.BackPush == 0,
                "state=" + t.Current + " vx=" + c.X + " backPush=" + t.BackPush
                + " (expect Takeover,3199,0)");
            Feed(t, c, 20, 0);                      // 在边界上外推 20
            Feed(t, c, 0, 3);                       // 纯纵向事件：既不累积也不清零
            Feed(t, c, 25, 0);                      // 20 + 25 = 45 >= 40
            Check("L5c", t.Current == KvmState.Idle && lv == 1,
                "state=" + t.Current + " leave=" + lv + " (expect Idle,1)");
        }

        // ---- L6（左挂）: SetEdge 切换后立刻解除武装（防止换边瞬间被弹过去） ----
        {
            EdgeTracker t = NewTracker();
            t.OnIdleMove(5, 0, 3839, 540);         // 右挂下先进入接管
            Check("L6a", t.Current == KvmState.Takeover, "state=" + t.Current);
            t.AbortTakeover();
            t.SetEdge(0, 0, 1080, false);           // 切成左挂
            bool armed = t.Armed;
            int en = 0;
            t.EnterTakeover += delegate(short x, short y) { en++; };
            t.OnIdleMove(-5, 0, 0, 540);            // 此刻光标恰在新边缘
            Check("L6b", !armed && t.Current == KvmState.Idle && en == 0,
                "armedAfterSetEdge=" + armed + " state=" + t.Current + " en=" + en
                + " (expect False, Idle, 0)");
        }

        Console.WriteLine("TOTAL: pass=" + _pass + " fail=" + _fail);
        if (_fail > 0) Environment.Exit(1);
    }
}
