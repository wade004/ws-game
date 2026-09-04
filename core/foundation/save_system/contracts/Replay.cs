using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 单条录制的输入意图（10_存档与持久化.md 第 8 节"输入录像（玩家每个 tick 提交的意图
    /// 请求序列）"）。<see cref="IntentKind"/> 与 <see cref="Args"/> 是中立表示，具体意图
    /// 词汇表由更上层（规则层/玩法层）定义，本模块不解释其含义。
    /// </summary>
    public sealed class ReplayInputRecord
    {
        /// <summary>提交该意图时所处的固定步长 tick 序号。</summary>
        public long Tick { get; }

        /// <summary>提交该意图的行动者。</summary>
        public Id ActorId { get; }

        /// <summary>意图种类（如技能施放、移动指令……），由上层约定的字符串词汇表。</summary>
        public string IntentKind { get; }

        /// <summary>意图携带的参数。</summary>
        public JsonObject Args { get; }

        public ReplayInputRecord(long tick, Id actorId, string intentKind, JsonObject args)
        {
            Tick = tick;
            ActorId = actorId;
            IntentKind = intentKind ?? string.Empty;
            Args = args;
        }
    }

    /// <summary>
    /// 一份完整回放数据（10 第 8 节"回放 = 固定步长 + 分流随机源初始状态 + 输入录像"，
    /// 固定步长本身是 <c>sim_loop</c> 的运行时配置，不属于本类型）。
    /// </summary>
    public sealed class ReplayData
    {
        /// <summary>录制开始时各分流随机源的初始状态（流 <see cref="Id"/> 的文本形式 →
        /// <see cref="RngStreamState"/>），与 <see cref="IRngHost.GetStreamState"/> 同构。</summary>
        public IReadOnlyDictionary<string, RngStreamState> RngSeeds { get; }

        /// <summary>按 tick 顺序的输入意图序列。</summary>
        public IReadOnlyList<ReplayInputRecord> Inputs { get; }

        public ReplayData(IReadOnlyDictionary<string, RngStreamState> rngSeeds, IReadOnlyList<ReplayInputRecord> inputs)
        {
            RngSeeds = rngSeeds;
            Inputs = inputs;
        }
    }

    /// <summary>
    /// 回放录制器契约（10 第 8 节伪代码 <c>ReplayRecorder</c>）。本任务（T1-6）只建立契约与
    /// 数据类型，不提供实现——实现留给 T1-9（见本模块 README"回放"一节）。
    /// </summary>
    public interface IReplayRecorder
    {
        /// <summary>开始录制，记下各分流随机源当时的初始状态。</summary>
        void BeginRecording(IReadOnlyDictionary<string, RngStreamState> rngSeeds);

        /// <summary>记录某个 tick 提交的一条输入意图。</summary>
        void RecordInput(long tick, ReplayInputRecord intent);

        /// <summary>导出迄今为止录制的完整回放数据。</summary>
        ReplayData Export();
    }

    /// <summary>
    /// 回放播放器契约（10 第 8 节伪代码 <c>ReplayPlayer</c>）。本任务（T1-6）只建立契约，
    /// 不提供实现——实现与 <c>WorldSnapshot</c> 的具体形状留给 T1-9 及依赖它的更上层模块
    /// （<c>WorldSnapshot</c> 属于 <c>sim_loop</c>/世界模型范畴，本模块不定义该类型，
    /// 用 <see cref="object"/> 占位，由实现方在其自己的程序集里用更具体的类型重新声明
    /// 该接口，或本接口在 T1-9 阶段按需调整签名）。
    /// </summary>
    public interface IReplayPlayer
    {
        /// <summary>加载一份回放数据，准备从头播放。</summary>
        void Load(ReplayData data);

        /// <summary>推进播放到指定 tick，返回该 tick 的世界快照（具体类型见本接口 XML 注释）。</summary>
        object StepTo(long tick);
    }
}
