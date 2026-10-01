using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>逻辑时间线的一步：该宿主固定步执行完（<c>Advance</c> 返回后）玩家的状态快照。</summary>
    public sealed class TickSample
    {
        public int Tick { get; }

        public Vec2 Position { get; }

        public double Facing { get; }

        public string MovementState { get; }

        public bool Alive { get; }

        /// <summary>本步宿主提交了移动请求的轴值（未提交为零向量）。</summary>
        public Vec2 MoveAxis { get; }

        public bool MoveRequested { get; }

        public TickSample(int tick, Vec2 position, double facing, string movementState, bool alive, Vec2 moveAxis, bool moveRequested)
        {
            Tick = tick;
            Position = position;
            Facing = facing;
            MovementState = movementState;
            Alive = alive;
            MoveAxis = moveAxis;
            MoveRequested = moveRequested;
        }
    }

    /// <summary>逻辑时间线上宿主提交的一条施放意图。</summary>
    public sealed class CastIntentRecord
    {
        public int Tick { get; }

        public string Action { get; }

        public string SkillId { get; }

        public CastIntentRecord(int tick, string action, string skillId)
        {
            Tick = tick;
            Action = action;
            SkillId = skillId;
        }
    }

    /// <summary>逻辑时间线上的一条领域事件（只保留实验室度量关心的几类，靶子用出场标签而不是实体 id）。</summary>
    public sealed class LogicEventRecord
    {
        /// <summary>事件落在哪个宿主固定步里（该步 <c>Advance</c> 期间被总线派发）。</summary>
        public int Tick { get; }

        /// <summary><c>cast_success</c>、<c>cast_failed</c>、<c>damage</c>、<c>avoided</c>、<c>died</c>。</summary>
        public string Kind { get; }

        public string Source { get; }

        public string Target { get; }

        public string SkillId { get; }

        /// <summary>攻击实例序号（按首次出现顺序从 1 编号；无实例为 0）。</summary>
        public int Instance { get; }

        public double Amount { get; }

        /// <summary>命中结果或失败原因文本。</summary>
        public string Detail { get; }

        public LogicEventRecord(int tick, string kind, string source, string target, string skillId, int instance, double amount, string detail)
        {
            Tick = tick;
            Kind = kind;
            Source = source;
            Target = target;
            SkillId = skillId;
            Instance = instance;
            Amount = amount;
            Detail = detail;
        }
    }

    /// <summary>表现时间线的一帧：假适配器 View 收到的最后一次 <c>SyncPose</c>。</summary>
    public sealed class FrameSample
    {
        public int Frame { get; }

        /// <summary>该帧的模拟时间（秒）：<c>Frame / frameRateCap</c>。</summary>
        public double Time { get; }

        public int TicksDone { get; }

        public double Alpha { get; }

        public Vec2 ViewPosition { get; }

        public double ViewFacingRadians { get; }

        /// <summary>方向量化档位；连续朝向为 0。</summary>
        public int ViewDirectionIndex { get; }

        public int ViewDirectionCount { get; }

        public bool HasPose { get; }

        public FrameSample(
            int frame, double time, int ticksDone, double alpha, bool hasPose, Vec2 viewPosition, double viewFacingRadians,
            int viewDirectionIndex, int viewDirectionCount)
        {
            Frame = frame;
            Time = time;
            TicksDone = ticksDone;
            Alpha = alpha;
            HasPose = hasPose;
            ViewPosition = viewPosition;
            ViewFacingRadians = viewFacingRadians;
            ViewDirectionIndex = viewDirectionIndex;
            ViewDirectionCount = viewDirectionCount;
        }
    }

    /// <summary>真实时间采样（只进性能组的"实时"度量，不进逻辑组，也不进字节比较）。</summary>
    public sealed class RealTimeSamples
    {
        /// <summary>每个帧回调（含其中触发的全部固定步）的耗时，毫秒。</summary>
        public List<double> FrameMilliseconds { get; } = new List<double>();

        /// <summary>每个帧回调内分配的字节数。</summary>
        public List<long> FrameAllocatedBytes { get; } = new List<long>();
    }

    /// <summary>
    /// 一次运行的全部记录：逻辑时间线（<see cref="Ticks"/>、<see cref="Intents"/>、<see cref="Events"/>）、表现时间线
    /// （<see cref="Frames"/>，假 View 收到的位姿）与真实时间采样（<see cref="Real"/>）。06 第 2 节"记录器"的三条线。
    /// 记录文件只落本地（<c>.gitignore</c>），入库的只有脚本夹具与指纹基线。
    /// </summary>
    public sealed class LabRecording
    {
        public InputScript Script { get; }

        public LabScenario Cell { get; }

        public double StepSeconds { get; }

        public List<TickSample> Ticks { get; } = new List<TickSample>();

        /// <summary>脚本事件注入记录（实际按 tick 注入的事件，含 tick 之外的原样值）。</summary>
        public List<ScriptEvent> InjectedInputs { get; } = new List<ScriptEvent>();

        public List<CastIntentRecord> Intents { get; } = new List<CastIntentRecord>();

        public List<LogicEventRecord> Events { get; } = new List<LogicEventRecord>();

        public List<FrameSample> Frames { get; } = new List<FrameSample>();

        public RealTimeSamples Real { get; } = new RealTimeSamples();

        /// <summary>整个运行里总线派发的事件总数（含实验室不记录的种类，只数个数，供性能组）。</summary>
        public int TotalEventCount { get; set; }

        /// <summary>玩家出生点（<c>Ticks</c> 的第 0 步之前的位置）。</summary>
        public Vec2 StartPosition { get; set; }

        /// <summary>出场靶子清单（出场标签 → 初始位置），按出场顺序。</summary>
        public List<KeyValuePair<string, Vec2>> Dummies { get; } = new List<KeyValuePair<string, Vec2>>();

        public LabRecording(InputScript script, LabScenario cell, double stepSeconds)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            Cell = cell ?? throw new ArgumentNullException(nameof(cell));
            StepSeconds = stepSeconds;
        }
    }
}
