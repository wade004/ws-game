#nullable enable
// LabLiveModel：人手试玩面板的视图模型（手感设计/06 第 4 节"面板"，ADR-0141）。纯 C#，不依赖引擎 API——
// 面板（LabPlayground 的屏上叠层）只从它读、只经 LabPlayground 的命令方法写；引擎侧不含逻辑，
// 所以编辑模式/PlayMode 测试可以不画任何界面就断言它的内容。
//
// 判断记录（指标口径，06 第 3.3 节"响应三项实时滚动；性能三项"）：
//   响应 1「输入→动作接受」：按键事件盖的固定步序号到本人 cast_success 事件所在固定步序号的差（逻辑，tick），同时记两者之间的真实时间（ms）。
//   响应 2「输入→首次可见响应」：按键的真实时刻到"接受该动作的那一帧、渲染提交之前"的真实时刻（ms）与帧数；
//     引擎测不了真实屏幕延迟（06 第 3.3 节），这里的"可见"指提交到渲染的时刻，真实屏幕延迟要靠角落闪块与外接相机测。
//   响应 3「命中确认→首个反馈提交」：本人 damage 事件所在固定步到打击反馈流水线首个指令提交所在固定步的差（tick）与同一帧推进内的真实耗时（ms）。
//   性能：帧间隔（含渲染与 GPU 等待，Update 到 Update）p50/p95、每帧推进耗时（逻辑 + 引擎侧驱动）p50/p95。
using System;
using System.Collections.Generic;

namespace Adapter.Unity.LabHost
{
    /// <summary>滚动统计：保留最近 <see cref="Capacity"/> 个样本，给最近值、均值、分位数。</summary>
    public sealed class RollingStat
    {
        private readonly Queue<double> _samples = new Queue<double>();

        public int Capacity { get; }

        public RollingStat(int capacity = 20)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Count { get; private set; }

        /// <summary>自开局累计的样本总数（滚动窗口之外的也计）。</summary>
        public int Total => Count;

        public double Last { get; private set; } = double.NaN;

        public void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            _samples.Enqueue(value);
            while (_samples.Count > Capacity)
            {
                _samples.Dequeue();
            }

            Last = value;
            Count++;
        }

        public bool HasData => _samples.Count > 0;

        public double Mean
        {
            get
            {
                if (_samples.Count == 0)
                {
                    return double.NaN;
                }

                var sum = 0.0;
                foreach (var v in _samples)
                {
                    sum += v;
                }

                return sum / _samples.Count;
            }
        }

        /// <summary>窗口内的分位数（最近邻法，<paramref name="q"/> 取 0..1）；没有样本返回 NaN。</summary>
        public double Percentile(double q)
        {
            if (_samples.Count == 0)
            {
                return double.NaN;
            }

            var sorted = new List<double>(_samples);
            sorted.Sort();
            var index = (int)Math.Ceiling(q * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }

        public void Clear()
        {
            _samples.Clear();
            Count = 0;
            Last = double.NaN;
        }
    }

    /// <summary>面板上的一个下拉/按钮选项（显示名 + 数据行 id）。</summary>
    public sealed class LabChoice
    {
        public string Id { get; }

        public string Label { get; }

        public LabChoice(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }

    public sealed class LabLiveModel
    {
        // ───────── 总览 ─────────
        public string Cell { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public int Tick { get; set; }

        public int DummyCount { get; set; }

        public bool PanelVisible { get; set; } = true;

        public bool HelpVisible { get; set; } = true;

        // ───────── 场景控制 ─────────
        public IReadOnlyList<LabChoice> DummyKinds { get; set; } = Array.Empty<LabChoice>();

        public int GroupCount { get; set; } = 5;

        public IReadOnlyList<LabChoice> Weapons { get; set; } = Array.Empty<LabChoice>();

        /// <summary>当前武器行 id；空 = 无武器覆盖。</summary>
        public string Weapon { get; set; } = string.Empty;

        public IReadOnlyList<LabChoice> Archetypes { get; set; } = Array.Empty<LabChoice>();

        /// <summary>当前体型行 id；空 = 无体型覆盖。</summary>
        public string Archetype { get; set; } = string.Empty;

        public double TimeScale { get; set; } = 1.0;

        public bool Paused { get; set; }

        public bool HitStopOn { get; set; } = true;

        public bool CornerFlash { get; set; }

        /// <summary>角落闪块是否正亮（输入瞬间的几帧）。</summary>
        public int CornerFlashFrames { get; set; }

        // ───────── 预设与 A/B ─────────
        public IReadOnlyList<LabChoice> Presets { get; set; } = Array.Empty<LabChoice>();

        /// <summary>当前生效的基础预设 id。</summary>
        public string Preset { get; set; } = string.Empty;

        public string PresetA { get; set; } = string.Empty;

        public string PresetB { get; set; } = string.Empty;

        /// <summary>当前生效的槽位：'A' 或 'B'。</summary>
        public char ActiveSlot { get; set; } = 'A';

        public int SlotSwitches { get; set; }

        // ───────── 指标 ─────────
        public RollingStat InputToAcceptTicks { get; } = new RollingStat();

        public RollingStat InputToAcceptMs { get; } = new RollingStat();

        public RollingStat InputToVisibleMs { get; } = new RollingStat();

        public RollingStat InputToVisibleFrames { get; } = new RollingStat();

        public RollingStat HitToFeedbackTicks { get; } = new RollingStat();

        public RollingStat HitToFeedbackMs { get; } = new RollingStat();

        public RollingStat FrameIntervalMs { get; } = new RollingStat(300);

        public RollingStat AdvanceMs { get; } = new RollingStat(300);

        // ───────── 呈现通道 ─────────
        public IReadOnlyList<string> EffectChannels { get; set; } = Array.Empty<string>();

        public Func<string, bool> EffectOn { get; set; } = _ => true;

        public Func<string, int> EffectSubmitted { get; set; } = _ => 0;

        public Func<string, int> EffectSuppressed { get; set; } = _ => 0;

        // ───────── 录制 ─────────
        public int RecordedEvents { get; set; }

        public string LastSavedPath { get; set; } = string.Empty;

        // ───────── 日志 ─────────
        private readonly List<string> _log = new List<string>();

        public IReadOnlyList<string> Log => _log;

        public void Note(string line)
        {
            _log.Add(line);
            while (_log.Count > 8)
            {
                _log.RemoveAt(0);
            }

            Status = line;
        }

        /// <summary>武器/体型/预设等行 id 的人读名（面板显示用；未知的取 id 末段）。</summary>
        public static string FriendlyName(string id)
        {
            if (id.Length == 0)
            {
                return "无";
            }

            var tail = id.Substring(id.LastIndexOf('.') + 1);
            switch (tail)
            {
                case "sword_1h": return "单手剑";
                case "greatsword": return "巨剑";
                case "light": return "轻型";
                case "medium": return "中型";
                case "heavy": return "重型";
                case "arpg_responsive": return "arpg_responsive（动作式）";
                case "rpg_classic": return "rpg_classic（目标选择式）";
                case "stake": return "木桩";
                case "stake_tough": return "韧性桩";
                case "stake_resilient": return "高韧桩";
                case "mob": return "小怪";
                case "frail": return "脆皮怪";
                case "elite": return "精英";
                case "patrol": return "巡逻靶";
                case "breakable": return "可破坏障碍";
                default: return tail;
            }
        }
    }
}
