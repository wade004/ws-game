using System;
using System.Collections.Generic;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>调度事件的种类（同一 tick 内按声明顺序先后触发：相位切换 → 窗口关闭 → 窗口打开 → 作者标记）。</summary>
    internal enum TimelineEventKind
    {
        PhaseEnter = 0,
        CancelClose = 1,
        ComboClose = 2,
        CancelOpen = 3,
        ComboOpen = 4,
        Marker = 5,
    }

    /// <summary>一个已换算到 tick 的调度事件。</summary>
    internal readonly struct TimelineEvent
    {
        public int Tick { get; }

        public TimelineEventKind Kind { get; }

        public ActionPhase Phase { get; }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Args { get; }

        public ActionClass Class { get; }

        public TimelineEvent(int tick, TimelineEventKind kind, ActionPhase phase, string name, IReadOnlyDictionary<string, string> args, ActionClass actionClass)
        {
            Tick = tick;
            Kind = kind;
            Phase = phase;
            Name = name;
            Args = args;
            Class = actionClass;
        }
    }

    /// <summary>一个已换算到 tick 的窗口（半开区间 [OpenTick, CloseTick)）。</summary>
    internal readonly struct TimelineWindow
    {
        public ActionClass Class { get; }

        public int OpenTick { get; }

        public int CloseTick { get; }

        public TimelineWindow(ActionClass actionClass, int openTick, int closeTick)
        {
            Class = actionClass;
            OpenTick = openTick;
            CloseTick = closeTick;
        }

        public bool Contains(int elapsedTicks) => elapsedTicks >= OpenTick && elapsedTicks < CloseTick;
    }

    /// <summary>档案侧参与分相换算的倍率（来自动作开始时快照的判定型手感视图，手感设计/01 第 4 节）。</summary>
    internal readonly struct TimelineScaling
    {
        public double PhaseScaleStartup { get; }

        public double PhaseScaleActive { get; }

        public double PhaseScaleRecovery { get; }

        public double CancelWindowScale { get; }

        public double ComboWindowScale { get; }

        /// <summary>速率重映射的动作时长下限（毫秒）；0 表示无下限。</summary>
        public double MinActionMs { get; }

        public TimelineScaling(double startup, double active, double recovery, double cancelWindowScale, double comboWindowScale, double minActionMs)
        {
            PhaseScaleStartup = startup;
            PhaseScaleActive = active;
            PhaseScaleRecovery = recovery;
            CancelWindowScale = cancelWindowScale;
            ComboWindowScale = comboWindowScale;
            MinActionMs = minActionMs;
        }

        /// <summary>全部倍率为 1、无下限（未接手感解析器时的缺省）。</summary>
        public static TimelineScaling Identity => new TimelineScaling(1, 1, 1, 1, 1, 0);
    }

    /// <summary>
    /// 一次时间线动作的 tick 调度表（手感设计/01 第 3.5 节"速率重映射"）：分相毫秒经"档案倍率 × 速率系数"换算、再按固定步长换算成
    /// tick（<see cref="FeelCalibration.MillisecondsToTicks"/>：四舍五入、非零至少 1、零保持零）；判定标记按分相分段线性映射；
    /// 窗口的起点跟随重映射，窗口的绝对长度以档案值为准（乘窗口倍率，不随速率缩放），超出动作末尾截到末尾。
    /// 纯数据计算，不读时钟与随机，同一输入恒得同一调度表。
    /// </summary>
    internal sealed class TimelineSchedule
    {
        public int StartupTicks { get; }

        public int ActiveTicks { get; }

        public int RecoveryTicks { get; }

        public int TotalTicks => StartupTicks + ActiveTicks + RecoveryTicks;

        /// <summary>按 (tick, 种类, 作者顺序) 排序的事件序列。</summary>
        public IReadOnlyList<TimelineEvent> Events { get; }

        public IReadOnlyList<TimelineWindow> CancelWindows { get; }

        /// <summary>连招接续窗口；无 <c>combo</c> 块为 null。</summary>
        public TimelineWindow? ComboWindow { get; }

        /// <summary>实际生效的时长系数（含下限夹取，1 = 未缩短）。</summary>
        public double AppliedFactor { get; }

        /// <summary>
        /// 各相的动画播放速率（ADR-0147）：作者毫秒 ÷ 重映射后实际毫秒（tick 数 × 步长）。表现层按它缩放该相的剪辑播放，
        /// 使被重映射（加速、体型/武器分相倍率、时长下限夹取）的动作与判定时间线不脱节；相位长度为 0 或作者值为 0 时取 1。
        /// </summary>
        public double StartupRate { get; }

        public double ActiveRate { get; }

        public double RecoveryRate { get; }

        private TimelineSchedule(
            int startup, int active, int recovery, IReadOnlyList<TimelineEvent> events,
            IReadOnlyList<TimelineWindow> cancelWindows, TimelineWindow? comboWindow, double appliedFactor,
            double startupRate, double activeRate, double recoveryRate)
        {
            StartupRate = startupRate;
            ActiveRate = activeRate;
            RecoveryRate = recoveryRate;
            StartupTicks = startup;
            ActiveTicks = active;
            RecoveryTicks = recovery;
            Events = events;
            CancelWindows = cancelWindows;
            ComboWindow = comboWindow;
            AppliedFactor = appliedFactor;
        }

        /// <summary>
        /// 构造调度表。<paramref name="durationFactor"/> 是速率系数（动作时长倍数，≤ 1 表示加快，1 表示不变）；
        /// <paramref name="scaling"/> 的下限只夹住速率造成的缩短（动作时长不会因下限被拉得比未加速更长）。
        /// </summary>
        public static TimelineSchedule Build(TimelineDef def, double stepSeconds, double durationFactor, TimelineScaling scaling)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!(durationFactor > 0)) durationFactor = 1.0;

            // 档案倍率（体型/武器层的分相倍率）先作用于三相，再叠速率系数。
            var baseStartup = def.StartupMs * scaling.PhaseScaleStartup;
            var baseActive = def.ActiveMs * scaling.PhaseScaleActive;
            var baseRecovery = def.RecoveryMs * scaling.PhaseScaleRecovery;
            var baseTotal = baseStartup + baseActive + baseRecovery;

            // 速率重映射的动作时长下限：只夹住"速率造成的缩短"，不会把时长拉到比未加速（系数 1）更长。
            var factor = durationFactor;
            if (factor < 1.0 && scaling.MinActionMs > 0 && baseTotal > 0)
            {
                var target = baseTotal * factor;
                if (target < scaling.MinActionMs)
                {
                    target = Math.Min(baseTotal, scaling.MinActionMs);
                    factor = target / baseTotal;
                }
            }

            var sMs = baseStartup * factor;
            var aMs = baseActive * factor;
            var rMs = baseRecovery * factor;
            var startupTicks = FeelCalibration.MillisecondsToTicks(sMs, stepSeconds);
            var activeTicks = FeelCalibration.MillisecondsToTicks(aMs, stepSeconds);
            var recoveryTicks = FeelCalibration.MillisecondsToTicks(rMs, stepSeconds);
            var total = startupTicks + activeTicks + recoveryTicks;

            var origS = def.StartupMs;
            var origA = def.ActiveMs;
            var origR = def.RecoveryMs;
            var origTotal = origS + origA + origR;

            // 作者毫秒 → 重映射后毫秒（分段线性：每一相各自一个斜率）。
            double MapMs(double atMs)
            {
                if (atMs <= 0) return 0;
                if (atMs >= origTotal) return sMs + aMs + rMs;
                if (atMs <= origS) return origS > 0 ? atMs / origS * sMs : 0;
                if (atMs <= origS + origA) return sMs + (origA > 0 ? (atMs - origS) / origA * aMs : 0);
                return sMs + aMs + (origR > 0 ? (atMs - origS - origA) / origR * rMs : 0);
            }

            // 作者毫秒 → tick，夹在其所属相位的 tick 范围内（保证标记不会因取整跨相）。
            int MapTick(double atMs)
            {
                var tick = FeelCalibration.MillisecondsToTicks(MapMs(atMs), stepSeconds);
                int lo, hi;
                if (atMs <= origS) { lo = 0; hi = startupTicks; }
                else if (atMs <= origS + origA) { lo = startupTicks; hi = startupTicks + activeTicks; }
                else { lo = startupTicks + activeTicks; hi = total; }
                if (tick < lo) tick = lo;
                if (tick > hi) tick = hi;
                return tick;
            }

            var events = new List<(TimelineEvent Ev, int Order)>();
            var order = 0;

            // 相位进入事件（零长度相位不发，但进入判定相的钩子由调用方按 ActiveEnterTick 处理）。
            if (startupTicks > 0) events.Add((new TimelineEvent(0, TimelineEventKind.PhaseEnter, ActionPhase.Startup, "startup", EmptyArgs, ActionClass.Move), order++));
            if (activeTicks > 0) events.Add((new TimelineEvent(startupTicks, TimelineEventKind.PhaseEnter, ActionPhase.Active, "active", EmptyArgs, ActionClass.Move), order++));
            if (recoveryTicks > 0) events.Add((new TimelineEvent(startupTicks + activeTicks, TimelineEventKind.PhaseEnter, ActionPhase.Recovery, "recovery", EmptyArgs, ActionClass.Move), order++));

            var cancelWindows = new List<TimelineWindow>();
            foreach (var w in def.CancelWindows)
            {
                var open = MapTick(w.OpenMs);
                int close;
                if (scaling.CancelWindowScale <= 0)
                {
                    close = open; // 窗口倍率 0（经典回合制预设）：窗口长度为零，永不打开。
                }
                else if (w.CloseMs.HasValue)
                {
                    var lenMs = Math.Max(0, w.CloseMs.Value - w.OpenMs) * scaling.CancelWindowScale;
                    close = Math.Min(total, open + FeelCalibration.MillisecondsToTicks(lenMs, stepSeconds));
                }
                else
                {
                    close = Math.Min(total, open + (int)Math.Round((total - open) * scaling.CancelWindowScale, MidpointRounding.AwayFromZero));
                }

                cancelWindows.Add(new TimelineWindow(w.Class, open, close));
                if (close > open)
                {
                    var name = TimelineDef.ActionClassName(w.Class);
                    events.Add((new TimelineEvent(open, TimelineEventKind.CancelOpen, ActionPhase.Startup, "cancel_open:" + name, EmptyArgs, w.Class), order++));
                    events.Add((new TimelineEvent(close, TimelineEventKind.CancelClose, ActionPhase.Startup, "cancel_close:" + name, EmptyArgs, w.Class), order++));
                }
            }

            TimelineWindow? comboWindow = null;
            if (def.Combo != null)
            {
                var open = MapTick(def.Combo.OpenMs);
                var close = scaling.ComboWindowScale <= 0
                    ? open
                    : Math.Min(total, open + FeelCalibration.MillisecondsToTicks(Math.Max(0, def.Combo.CloseMs - def.Combo.OpenMs) * scaling.ComboWindowScale, stepSeconds));
                comboWindow = new TimelineWindow(ActionClass.Attack, open, close);
                if (close > open)
                {
                    events.Add((new TimelineEvent(open, TimelineEventKind.ComboOpen, ActionPhase.Startup, "combo_open", EmptyArgs, ActionClass.Attack), order++));
                    events.Add((new TimelineEvent(close, TimelineEventKind.ComboClose, ActionPhase.Startup, "combo_close", EmptyArgs, ActionClass.Attack), order++));
                }
            }

            foreach (var m in def.Markers)
            {
                events.Add((new TimelineEvent(MapTick(m.AtMs), TimelineEventKind.Marker, ActionPhase.Startup, m.Name, m.Args, ActionClass.Move), order++));
            }

            events.Sort((x, y) =>
            {
                var c = x.Ev.Tick.CompareTo(y.Ev.Tick);
                if (c != 0) return c;
                c = x.Ev.Kind.CompareTo(y.Ev.Kind);
                if (c != 0) return c;
                return x.Order.CompareTo(y.Order);
            });

            var sorted = new List<TimelineEvent>(events.Count);
            foreach (var e in events) sorted.Add(e.Ev);

            double PhaseRate(double authoredMs, int ticks) =>
                authoredMs > 0 && ticks > 0 ? authoredMs / (ticks * stepSeconds * 1000.0) : 1.0;

            return new TimelineSchedule(
                startupTicks, activeTicks, recoveryTicks, sorted, cancelWindows, comboWindow, factor,
                PhaseRate(origS, startupTicks), PhaseRate(origA, activeTicks), PhaseRate(origR, recoveryTicks));
        }

        /// <summary>该类别的取消窗口在 <paramref name="elapsedTicks"/> 是否打开。</summary>
        public bool IsCancelOpen(ActionClass actionClass, int elapsedTicks)
        {
            for (var i = 0; i < CancelWindows.Count; i++)
            {
                var w = CancelWindows[i];
                if (w.Class == actionClass && w.Contains(elapsedTicks)) return true;
            }

            return false;
        }

        /// <summary>连招接续窗口在 <paramref name="elapsedTicks"/> 是否打开。</summary>
        public bool IsComboOpen(int elapsedTicks) => ComboWindow.HasValue && ComboWindow.Value.Contains(elapsedTicks);

        /// <summary>某标记名首次出现的 tick（不存在为 null）。</summary>
        public int? FirstTickOf(string markerName)
        {
            for (var i = 0; i < Events.Count; i++)
            {
                if (Events[i].Kind == TimelineEventKind.Marker && Events[i].Name == markerName) return Events[i].Tick;
            }

            return null;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyArgs = new Dictionary<string, string>();
    }
}
