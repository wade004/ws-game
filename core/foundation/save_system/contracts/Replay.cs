using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 单条录制的输入意图（10_存档与持久化.md 第 8 节"输入录像（玩家每个 tick 提交的意图
    /// 请求序列）"）。<see cref="IntentKind"/> 与 <see cref="Args"/> 是中立表示，具体意图
    /// 词汇表由更上层（规则层/玩法层）定义，本模块不解释其含义。与
    /// <see cref="Core.Foundation.SimLoop.Intent"/> 字段一一对应，可互转（见该类型注释）；
    /// 本类型不反向依赖 <c>sim_loop</c> 之外的具体业务类型。
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

        /// <summary>序列化为 <see cref="JsonObject"/>，供 <see cref="ReplayData.ToJson"/> 复用
        /// （见 10 第 8 节"输入录像"，往返用途：录像存盘/读盘）。</summary>
        public JsonObject ToJson()
        {
            return new JsonObjectBuilder()
                .Add("tick", new JsonNumber(Tick))
                .Add("actorId", new JsonString(ActorId.Value))
                .Add("intentKind", new JsonString(IntentKind))
                .Add("args", Args)
                .Build();
        }

        /// <summary>按 <see cref="ToJson"/> 的形状反序列化。</summary>
        public static ReplayInputRecord FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (!((JsonNumber)json["tick"]).TryGetInt64(out var tick))
            {
                throw new FormatException("ReplayInputRecord.tick 不是合法整数");
            }

            var actorId = new Id(((JsonString)json["actorId"]).Value);
            var intentKind = ((JsonString)json["intentKind"]).Value;
            json.TryGetValue("args", out var rawArgs);
            var args = rawArgs is JsonObject argsObject ? argsObject : new JsonObjectBuilder().Build();

            return new ReplayInputRecord(tick, actorId, intentKind, args);
        }
    }

    /// <summary>
    /// 单条录制的"本 tick 用的是哪种 <see cref="SimStep"/>"（ADR-0013 离散时间模型落地：本任务
    /// 前的录像格式恒假定每个 tick 都是 <see cref="SimStepKind.Continuous"/>——<see cref="ReplayPlayer"/>
    /// 硬编码 <c>world.Tick(SimStep.Continuous(...))</c>，见该类型 T1-9 阶段实现；本任务把"这个
    /// tick 到底是连续步还是离散步（含离散步的 actorId/phase）"也记下来，使
    /// <see cref="ReplayPlayer.StepTo"/> 能在模式切换的录像上正确重放，见该类型 §StepTo 判断记录）。
    /// <para>
    /// 判断记录（旧格式兼容）：<see cref="ReplayData.Steps"/> 是按 tick 号排列的稀疏/完整列表，
    /// 旧格式（<see cref="ReplayData.FormatVersion"/> == 1，无 <c>steps</c> 字段）解析后为空列表；
    /// <see cref="ReplayPlayer.StepTo"/> 对"没有对应记录的 tick"一律按
    /// <see cref="SimStepKind.Continuous"/> 处理（与本任务之前的唯一行为完全一致），即"缺字段视为
    /// 连续步"。
    /// </para>
    /// </summary>
    public readonly struct ReplayStepRecord
    {
        /// <summary>本记录对应的 tick 号（从 1 起，与 <see cref="ReplayInputRecord.Tick"/> 同一
        /// 编号体系）。</summary>
        public long Tick { get; }

        public SimStepKind Kind { get; }

        /// <summary><see cref="Kind"/> 为 <see cref="SimStepKind.Discrete"/> 时有值。</summary>
        public Id? ActorId { get; }

        /// <summary><see cref="Kind"/> 为 <see cref="SimStepKind.Discrete"/> 时有值。</summary>
        public StepPhase? Phase { get; }

        private ReplayStepRecord(long tick, SimStepKind kind, Id? actorId, StepPhase? phase)
        {
            Tick = tick;
            Kind = kind;
            ActorId = actorId;
            Phase = phase;
        }

        /// <summary>从驱动录制的调用方实际使用的 <see cref="SimStep"/> 构造一条记录（见
        /// <see cref="IReplayRecorder.RecordStep"/>）。</summary>
        public static ReplayStepRecord FromStep(long tick, SimStep step)
        {
            return step.Kind == SimStepKind.Continuous
                ? new ReplayStepRecord(tick, SimStepKind.Continuous, null, null)
                : new ReplayStepRecord(tick, SimStepKind.Discrete, step.ActorId, step.Phase);
        }

        /// <summary>还原为 <see cref="ReplayPlayer.StepTo"/> 传给 <c>world.Tick</c> 的
        /// <see cref="SimStep"/>；连续步需要外部传入 <paramref name="stepSeconds"/>（本记录不重复
        /// 存一份，与 <see cref="ReplayData.StepSeconds"/> 保持单一来源）。</summary>
        public SimStep ToSimStep(double stepSeconds)
        {
            return Kind == SimStepKind.Continuous
                ? SimStep.Continuous(stepSeconds)
                : SimStep.Discrete(ActorId!.Value, Phase!.Value);
        }

        public JsonObject ToJson()
        {
            var builder = new JsonObjectBuilder()
                .Add("tick", new JsonNumber(Tick))
                .Add("kind", new JsonString(Kind == SimStepKind.Continuous ? "continuous" : "discrete"));

            builder.Add("actorId", ActorId.HasValue ? (JsonValue)new JsonString(ActorId.Value.Value) : JsonNull.Instance);
            builder.Add("phase", Phase.HasValue ? (JsonValue)new JsonString(Phase.Value.ToString()) : JsonNull.Instance);

            return builder.Build();
        }

        public static ReplayStepRecord FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (!((JsonNumber)json["tick"]).TryGetInt64(out var tick))
            {
                throw new FormatException("ReplayStepRecord.tick 不是合法整数");
            }

            var kindText = ((JsonString)json["kind"]).Value;
            if (kindText == "continuous")
            {
                return new ReplayStepRecord(tick, SimStepKind.Continuous, null, null);
            }

            if (kindText != "discrete")
            {
                throw new FormatException($"ReplayStepRecord.kind 取值非法：\"{kindText}\"");
            }

            var actorId = new Id(((JsonString)json["actorId"]).Value);
            var phase = Enum.Parse<StepPhase>(((JsonString)json["phase"]).Value);
            return new ReplayStepRecord(tick, SimStepKind.Discrete, actorId, phase);
        }
    }

    /// <summary>
    /// 单条录制的"某行动者在某 tick 提交了结束回合"（离散模式回放完整性任务新增，见
    /// <see cref="ReplayData.EndTurns"/>、<see cref="IReplayRecorder.RecordEndTurn"/>）。与
    /// <see cref="ReplayInputRecord"/>（提交意图）是两种独立的"玩家决策时刻"，不合并成同一个字段
    /// 里靠 <see cref="ReplayInputRecord.IntentKind"/> 的某个哨兵值区分——本模块契约本来就不解释
    /// <see cref="ReplayInputRecord.IntentKind"/> 的具体取值含义（见该类型注释"具体意图词汇表由
    /// 更上层定义，本模块不解释其含义"），若借用它的某个取值表示"这其实不是一个意图，是结束回合"，
    /// 就打破了这条边界；单独建一个结构化类型不依赖任何字符串约定。</summary>
    public sealed class ReplayEndTurnRecord
    {
        /// <summary>提交"结束回合"时所处的固定步长 tick 序号，与 <see cref="ReplayInputRecord.Tick"/>
        /// 同一编号体系（见 <see cref="ReplayPlayer"/> "tick 序号从 1 起"约定）。</summary>
        public long Tick { get; }

        /// <summary>结束回合的行动者。</summary>
        public Id ActorId { get; }

        public ReplayEndTurnRecord(long tick, Id actorId)
        {
            Tick = tick;
            ActorId = actorId;
        }

        public JsonObject ToJson()
        {
            return new JsonObjectBuilder()
                .Add("tick", new JsonNumber(Tick))
                .Add("actorId", new JsonString(ActorId.Value))
                .Build();
        }

        public static ReplayEndTurnRecord FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (!((JsonNumber)json["tick"]).TryGetInt64(out var tick))
            {
                throw new FormatException("ReplayEndTurnRecord.tick 不是合法整数");
            }

            var actorId = new Id(((JsonString)json["actorId"]).Value);
            return new ReplayEndTurnRecord(tick, actorId);
        }
    }

    /// <summary>
    /// 一份完整回放数据（10 第 8 节"回放 = 固定步长 + 分流随机源初始状态 + 输入录像"）。
    /// <see cref="StepSeconds"/>（固定步长本身）一并记录：虽然属于 <c>sim_loop</c> 的运行时
    /// 配置，但只记随机源与输入、不记步长会导致回放脱离原始运行的时间基准，
    /// 因此本类型把它一并纳入（T1-9 判断记录：契约原注释"固定步长本身...不属于本类型"
    /// 改为"必须一并记录，否则 <see cref="IReplayPlayer.StepTo"/> 无法独立于外部配置推进"）。
    /// <see cref="TickCount"/> 记录本次录像覆盖的总 tick 数（含没有任何输入的 tick）。
    /// <see cref="Steps"/>/<see cref="FormatVersion"/> 是 ADR-0013 离散时间模型任务新增（见
    /// <see cref="ReplayStepRecord"/> 类型注释）。
    /// </summary>
    public sealed class ReplayData
    {
        /// <summary>当前录像格式版本，见 <see cref="ReplayStepRecord"/> 类型注释"判断记录（旧格式
        /// 兼容）"。1 = 本任务之前的格式（无 <c>steps</c> 字段，恒连续步）；2 = ADR-0013 任务新增
        /// <see cref="Steps"/>；3 = 离散模式回放完整性任务新增 <see cref="EndTurns"/>（"结束回合"
        /// 发生时刻，见该属性注释、<see cref="IReplayPlayer.LoadDiscrete"/>）。</summary>
        /// <summary>P1-04 收口新增 <see cref="MasterSeed"/> 字段。</summary>
        public const int CurrentFormatVersion = 4;

        /// <summary>本份数据的格式版本，见 <see cref="CurrentFormatVersion"/>。</summary>
        public int FormatVersion { get; }

        /// <summary>
        /// 录制开始时使用的 <see cref="IRngHost.MasterSeed"/>（P1-04 收口新增，
        /// <see cref="FormatVersion"/> &lt; 4 的旧格式解析后为 <c>0</c>——旧格式从未记录过主种子，
        /// 这是已知的旧录像兼容边界，见 <see cref="ReplayPlayer"/> 判断记录）。<see cref="ReplayPlayer.Load"/>/
        /// <see cref="ReplayPlayer.LoadDiscrete"/> 把它原样传给 <see cref="WorldFactory"/>/
        /// <see cref="DiscreteWorldFactory"/>，使"录制起点之后才第一次被访问的流"（<see cref="RngSeeds"/>
        /// 没有覆盖到的流）也能派生自与录制时相同的主种子，而不是工厂另行给出的任意值。
        /// </summary>
        public ulong MasterSeed { get; }

        /// <summary>录制开始时各分流随机源的初始状态（流 <see cref="Id"/> 的文本形式 →
        /// <see cref="RngStreamState"/>），与 <see cref="IRngHost.GetStreamState"/> 同构。</summary>
        public IReadOnlyDictionary<string, RngStreamState> RngSeeds { get; }

        /// <summary>按 tick 顺序的输入意图序列。</summary>
        public IReadOnlyList<ReplayInputRecord> Inputs { get; }

        /// <summary>录制时使用的固定步长（秒），供 <see cref="IReplayPlayer"/> 用同样的步长
        /// 重放（见 10 第 8 节"回放 = 固定步长 + ..."）。</summary>
        public double StepSeconds { get; }

        /// <summary>录像覆盖的总 tick 数。</summary>
        public long TickCount { get; }

        /// <summary>按 tick 号记录"这个 tick 用的是哪种 <see cref="SimStep"/>"（见
        /// <see cref="ReplayStepRecord"/>）。旧格式（<see cref="FormatVersion"/> == 1）解析后为空；
        /// 空列表或某个 tick 没有对应记录，<see cref="IReplayPlayer.StepTo"/> 都按
        /// <see cref="SimStepKind.Continuous"/> 处理。</summary>
        public IReadOnlyList<ReplayStepRecord> Steps { get; }

        /// <summary>按 tick 顺序记录的"结束回合"发生时刻（离散模式回放完整性任务新增，见
        /// <see cref="ReplayEndTurnRecord"/>、<see cref="IReplayPlayer.LoadDiscrete"/>"离散重放
        /// 只记录玩家意图/结束回合的发生时刻，不记录调度器内部决策"判断记录）。<see cref="FormatVersion"/>
        /// &lt; 3 的旧格式解析后为空列表（该格式不存在这个概念，见 <see cref="FromJson"/>）。</summary>
        public IReadOnlyList<ReplayEndTurnRecord> EndTurns { get; }

        public ReplayData(
            IReadOnlyDictionary<string, RngStreamState> rngSeeds,
            IReadOnlyList<ReplayInputRecord> inputs,
            double stepSeconds,
            long tickCount,
            IReadOnlyList<ReplayStepRecord>? steps = null,
            int formatVersion = CurrentFormatVersion,
            IReadOnlyList<ReplayEndTurnRecord>? endTurns = null,
            ulong masterSeed = 0UL)
        {
            if (stepSeconds <= 0)
            {
                throw new ArgumentException("stepSeconds 必须为正数", nameof(stepSeconds));
            }

            if (tickCount < 0)
            {
                throw new ArgumentException("tickCount 不能为负数", nameof(tickCount));
            }

            RngSeeds = rngSeeds ?? throw new ArgumentNullException(nameof(rngSeeds));
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            StepSeconds = stepSeconds;
            TickCount = tickCount;
            Steps = steps ?? Array.Empty<ReplayStepRecord>();
            EndTurns = endTurns ?? Array.Empty<ReplayEndTurnRecord>();
            FormatVersion = formatVersion;
            MasterSeed = masterSeed;
        }

        /// <summary>序列化为 <see cref="JsonObject"/>（供存盘，见 10 第 8 节"问题复现：玩家反馈
        /// 的问题若能提供录像，可离线重放定位"——离线即意味着需要落盘）。键顺序固定，
        /// <see cref="RngSeeds"/> 按 key 的原始插入顺序写出（<see cref="JsonObjectBuilder"/>
        /// 保序），不额外排序。<c>format_version</c>/<c>steps</c> 恒写出（即便 <see cref="Steps"/>
        /// 为空——"没有任何离散步"本身也是一条有意义的信息，与"这份录像根本没有 steps 字段的旧
        /// 格式"是两回事，见 <see cref="FromJson"/> 判断记录）。</summary>
        public JsonObject ToJson()
        {
            var seedsBuilder = new JsonObjectBuilder();
            foreach (var pair in RngSeeds)
            {
                seedsBuilder.Add(pair.Key, new JsonString(pair.Value.ToString()));
            }

            var inputsArray = new List<JsonValue>(Inputs.Count);
            foreach (var input in Inputs)
            {
                inputsArray.Add(input.ToJson());
            }

            var stepsArray = new List<JsonValue>(Steps.Count);
            foreach (var step in Steps)
            {
                stepsArray.Add(step.ToJson());
            }

            var endTurnsArray = new List<JsonValue>(EndTurns.Count);
            foreach (var endTurn in EndTurns)
            {
                endTurnsArray.Add(endTurn.ToJson());
            }

            return new JsonObjectBuilder()
                .Add("format_version", new JsonNumber(CurrentFormatVersion))
                .Add("stepSeconds", new JsonNumber(StepSeconds))
                .Add("tickCount", new JsonNumber(TickCount))
                // 十六进制文本而非 JsonNumber：同 RngStreamsPersistable.Save 判断记录，避免 ulong
                // 全值域经 JsonNumber 内部 double 存储丢精度。
                .Add("master_seed", new JsonString(MasterSeed.ToString("x16", CultureInfo.InvariantCulture)))
                .Add("rngSeeds", seedsBuilder.Build())
                .Add("inputs", new JsonArray(inputsArray))
                .Add("steps", new JsonArray(stepsArray))
                .Add("end_turns", new JsonArray(endTurnsArray))
                .Build();
        }

        /// <summary>按 <see cref="ToJson"/> 的形状反序列化；<c>format_version</c>/<c>steps</c> 两个
        /// 字段缺失时视为旧格式（<see cref="FormatVersion"/> = 1，<see cref="Steps"/> 为空，即
        /// "每个 tick 都是连续步"——本任务之前唯一存在过的格式），保证旧录像文件仍可读。</summary>
        public static ReplayData FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var formatVersion = json.TryGetValue("format_version", out var fv) && fv is JsonNumber fvn && fvn.TryGetInt64(out var fvi)
                ? (int)fvi
                : 1;

            var stepSeconds = ((JsonNumber)json["stepSeconds"]).Value;

            if (!((JsonNumber)json["tickCount"]).TryGetInt64(out var tickCount))
            {
                throw new FormatException("ReplayData.tickCount 不是合法整数");
            }

            // master_seed：FormatVersion < 4 的旧格式没有该字段，退化为 0（见 MasterSeed 属性注释
            // "已知的旧录像兼容边界"）。
            var masterSeed = 0UL;
            if (json.TryGetValue("master_seed", out var masterSeedRaw) && masterSeedRaw is JsonString masterSeedText)
            {
                if (!ulong.TryParse(masterSeedText.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out masterSeed))
                {
                    throw new FormatException("ReplayData.master_seed 文本非法");
                }
            }

            var seeds = new Dictionary<string, RngStreamState>(StringComparer.Ordinal);
            foreach (var pair in (JsonObject)json["rngSeeds"])
            {
                seeds.Add(pair.Key, RngStreamState.Parse(((JsonString)pair.Value).Value));
            }

            var inputsJson = (JsonArray)json["inputs"];
            var inputs = new List<ReplayInputRecord>(inputsJson.Count);
            foreach (var item in inputsJson)
            {
                inputs.Add(ReplayInputRecord.FromJson((JsonObject)item));
            }

            var steps = new List<ReplayStepRecord>();
            if (json.TryGetValue("steps", out var stepsRaw) && stepsRaw is JsonArray stepsJson)
            {
                foreach (var item in stepsJson)
                {
                    steps.Add(ReplayStepRecord.FromJson((JsonObject)item));
                }
            }

            var endTurns = new List<ReplayEndTurnRecord>();
            if (json.TryGetValue("end_turns", out var endTurnsRaw) && endTurnsRaw is JsonArray endTurnsJson)
            {
                foreach (var item in endTurnsJson)
                {
                    endTurns.Add(ReplayEndTurnRecord.FromJson((JsonObject)item));
                }
            }

            return new ReplayData(seeds, inputs, stepSeconds, tickCount, steps, formatVersion, endTurns, masterSeed);
        }
    }

    /// <summary>
    /// 回放录制器契约（10 第 8 节伪代码 <c>ReplayRecorder</c>）。实现见
    /// <see cref="ReplayRecorder"/>（T1-9）。
    /// </summary>
    public interface IReplayRecorder
    {
        /// <summary>开始录制，记下当前主种子（<paramref name="masterSeed"/>，P1-04 收口新增参数，
        /// 见 <see cref="ReplayData.MasterSeed"/>）与各分流随机源当时的初始状态。</summary>
        void BeginRecording(ulong masterSeed, IReadOnlyDictionary<string, RngStreamState> rngSeeds);

        /// <summary>记录某个 tick 提交的一条输入意图。</summary>
        void RecordInput(long tick, ReplayInputRecord intent);

        /// <summary>记录某个 tick 实际使用的 <see cref="SimStep"/>（ADR-0013：区分连续/离散步，见
        /// <see cref="ReplayStepRecord"/> 类型注释）。调用方应在每次 <c>world.Tick(step)</c> 前后
        /// 都调用一次，与 <see cref="RecordInput"/> 同一 tick 编号体系；不调用等价于该 tick 按
        /// 连续步处理（旧格式录像的天然语义，见 <see cref="ReplayStepRecord"/>）。
        /// <para>
        /// 判断记录（离散模式回放完整性任务，<see cref="IReplayPlayer.LoadDiscrete"/> 路径下本方法
        /// 的定位降级为"诊断信息"）：经 <see cref="IReplayPlayer.LoadDiscrete"/> 播放的录像不依赖
        /// 本方法记下的 <see cref="ReplayStepRecord.ActorId"/>/<see cref="ReplayStepRecord.Phase"/>
        /// 来决定重放时"这一步到底是谁在行动"——那是 <c>TurnScheduler</c> 的内部决策，重放时应由
        /// 重新驱动的 <c>TurnScheduler</c>（在同样的确定性输入下）自己算出来，不应该被录像"喂"进去
        /// （见 <see cref="IReplayPlayer.LoadDiscrete"/> 判断记录）。调用方仍可以继续调用本方法——
        /// 录下的内容作为人工核对"这次录制实际发生了什么"的诊断信息依然有价值，只是
        /// <see cref="IReplayPlayer.LoadDiscrete"/> 播放路径不读取它。
        /// </para>
        /// </summary>
        void RecordStep(long tick, SimStep step);

        /// <summary>记录某个 tick 某行动者提交了"结束回合"（见 <see cref="ReplayEndTurnRecord"/>、
        /// 10 第 8 节"输入录像"——结束回合与提交意图（<see cref="RecordInput"/>）同属"玩家决策"，
        /// 只是不携带具体的 <see cref="Intent"/> 内容）。</summary>
        void RecordEndTurn(long tick, Id actorId);

        /// <summary>导出迄今为止录制的完整回放数据。</summary>
        ReplayData Export();
    }

    /// <summary>
    /// 回放播放器契约（10 第 8 节伪代码 <c>ReplayPlayer</c>）。<see cref="StepTo"/> 的返回类型
    /// 在 T1-9 落地为本文件定义的 <see cref="WorldSnapshot"/>（原契约用 <c>object</c> 占位，
    /// 见本类型 T1-6 阶段的历史注释——占位已按任务书授权在本任务替换为具体类型）。
    /// 实现见 <see cref="ReplayPlayer"/>。
    /// </summary>
    public interface IReplayPlayer
    {
        /// <summary>加载一份回放数据，准备从头播放。</summary>
        void Load(ReplayData data);

        /// <summary>
        /// 离散模式回放完整性任务新增：加载一份"整段都由 <c>TurnScheduler</c> 驱动"的离散回放
        /// （<paramref name="factory"/> 额外产出一个已经 <c>BeginCombat</c> 过的
        /// <see cref="Core.Foundation.SimLoop.TurnScheduler"/>），随后 <see cref="StepTo"/>
        /// 改走"经 <c>TurnScheduler</c> 驱动"的路径（见判断记录）。与 <see cref="Load"/>
        /// 二选一——同一个 <see cref="IReplayPlayer"/> 实例上先调用哪个，<see cref="StepTo"/> 就走
        /// 对应的路径；本方法不改变 <see cref="Load"/> 既有行为。
        /// <para>
        /// 判断记录（为什么不是 <c>GameplayAssembly.Advance</c> 本身驱动、而是等价的
        /// <c>TurnScheduler</c> 循环）：<c>GameplayAssembly</c> 属于 L4 玩法装配层
        /// （<c>core/gameplay/assembly</c>），本接口所在的 <c>core/foundation/save_system</c> 是
        /// L0 基础层（见 01_分层与依赖.md）——L0 不能反向依赖 L4。<c>GameplayAssembly.Advance</c>
        /// 的驱动算法本身（<c>while(true) { step = scheduler.NextStep(); if (step==null) 停下等
        /// 外部输入; world.Tick(step); scheduler.NotifyStepConsumed(...); }</c>）只用到 L0 的
        /// <c>TurnScheduler</c>/<c>IWorldSim</c>，不涉及 <c>GameplayAssembly</c> 独有的
        /// <c>Pacing</c>/<c>AppState</c> 子状态（那些是"给表现层看的节奏门"，离线重放不需要、也
        /// 不应该需要——重放不用等动画播完）；本方法在 L0 内原样复刻这段驱动算法本身（见
        /// <see cref="ReplayPlayer"/> 实现），效果与"由 <c>GameplayAssembly.Advance</c> 驱动"完全
        /// 一致（同一份确定性输入喂给同一个 <c>TurnScheduler</c> 算法，必然产出同一个行动者序列），
        /// 只是不通过那个类本身——这不是偷懒简化，是分层边界决定的唯一合法实现位置。
        /// </para>
        /// <para>
        /// 判断记录（只记录玩家意图/结束回合的发生时刻，不记录调度器内部决策）：<see cref="ReplayData.Inputs"/>/
        /// <see cref="ReplayData.EndTurns"/> 只记录"在第几个 tick，哪个行动者提交了什么意图/结束了
        /// 回合"——这是外部世界（玩家/AI 决策脚本）喂给 <c>TurnScheduler</c> 的输入。重放时，
        /// <see cref="StepTo"/> 反复调用 <c>scheduler.NextStep()</c>：返回非空即代表"轮到的这一步
        /// 不需要外部输入"（AI 行动者，或正在继续上一次跨轮动作的行动者，见 <c>TurnScheduler.
        /// NextStep</c>），直接 <c>world.Tick</c>；返回空代表"轮到的行动者需要外部输入"，此时按
        /// 当前 tick 号在 <see cref="ReplayData.Inputs"/>/<see cref="ReplayData.EndTurns"/> 里查找
        /// 录制时留下的决策并原样提交（<c>scheduler.SubmitIntent</c>/<c>scheduler.EndTurn</c>），
        /// 找不到则判定录像与调度器决策不一致，抛异常（而不是静默按某种默认值继续，见 11 第 4 节
        /// "不做静默降级"）——<b>录像里完全没有"tick N 是谁的回合、第几阶段"这类信息</b>，那正是
        /// <c>TurnScheduler</c> 每次都重新算出来的（先攻属性、Id 字典序、分流随机源，三者都已经
        /// 由 <see cref="ReplayData.RngSeeds"/> 与固定的世界初始状态锁定，因此重放时
        /// <c>TurnScheduler</c> 必然重算出与录制时完全相同的序列——这正是任务书"TurnScheduler
        /// 的顺序完全由确定性输入决定"这句话在实现层面的落地）。
        /// </para>
        /// </summary>
        void LoadDiscrete(ReplayData data, DiscreteWorldFactory factory);

        /// <summary>推进播放到指定 tick（只能向前推进，不支持回退），返回该 tick 的世界快照。
        /// <paramref name="entityStateProvider"/>（F6 收口新增，可选，见
        /// <see cref="WorldSnapshot.Capture"/> 判断记录）原样转给 <see cref="WorldSnapshot.Capture"/>，
        /// 默认 <c>null</c> 时行为与此前完全一致。</summary>
        WorldSnapshot StepTo(long tick, Func<Id, IReadOnlyList<string>>? entityStateProvider = null);
    }

    /// <summary>
    /// 世界工厂：由持有具体游戏内容（阶段处理器、意图词汇表等）的调用方（测试/更上层模块）
    /// 提供"如何组装一个带处理器的世界"，<see cref="ReplayPlayer"/> 自身不知道任何具体业务
    /// 内容，只按 10 第 8 节"回放"流程驱动世界推进与随机源状态恢复。
    /// <paramref name="masterSeed"/> 用于工厂内部构造 <c>IRngHost</c> 的初始主种子——
    /// <see cref="ReplayPlayer.Load"/> 传入的是 <see cref="ReplayData.MasterSeed"/>（录制时的真实
    /// 主种子）。P1-04 收口：此前本注释声称"具体传入值对回放结果不产生影响，工厂可忽略"、
    /// <see cref="ReplayPlayer.Load"/> 也恒传 <c>0UL</c>——这个说法只对"录制开始时已经创建过的流"
    /// 成立（那些流会在 <see cref="ReplayPlayer.Load"/> 内按 <see cref="ReplayData.RngSeeds"/>
    /// 逐条 <c>SetStreamState</c> 覆盖）；但"录制起点之后才第一次被访问的流"不在
    /// <see cref="ReplayData.RngSeeds"/> 里，它们的懒创建初始状态由工厂构造 <c>IRngHost</c> 时给的
    /// 主种子派生——传 <c>0UL</c> 会让这类流在重放里产生与录制时不同的随机序列。工厂不应再忽略本
    /// 参数，必须原样用它构造 <c>IRngHost</c>；
    /// <paramref name="bus"/> 由 <see cref="ReplayPlayer"/> 持有生命周期并传入，工厂只负责在其上
    /// 注册阶段处理器与构造世界，不自行另建事件总线（否则 <see cref="ReplayPlayer"/> 读不到
    /// 事件审计日志）。
    /// </summary>
    public delegate (IWorldSim World, IRngHost Rng) WorldFactory(ulong masterSeed, IEventBus bus);

    /// <summary>
    /// <see cref="IReplayPlayer.LoadDiscrete"/> 用的世界工厂：与 <see cref="WorldFactory"/> 同样的
    /// "不自建事件总线"约定（<paramref name="bus"/> 由 <see cref="ReplayPlayer"/> 持有生命周期），
    /// 额外产出一个已经 <c>BeginCombat</c>（登记好参战者、算好初始行动顺序）的
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler"/>——离散重放整段回放期间都由它驱动
    /// （见 <see cref="IReplayPlayer.LoadDiscrete"/> 判断记录），本委托因此不像 <see cref="WorldFactory"/>
    /// 那样需要支持"连续/离散混合的一段录像"：一次 <see cref="IReplayPlayer.LoadDiscrete"/> 调用
    /// 覆盖的是"整段都是离散" 的回放（典型场景：战斗全程录像；探索段落用普通 <see cref="WorldFactory"/>
    /// + <see cref="IReplayPlayer.Load"/> 单独覆盖，见 10 第 8 节"回放"对时间模型切换的既有处理）。
    /// </summary>
    public delegate (IWorldSim World, IRngHost Rng, TurnScheduler Scheduler) DiscreteWorldFactory(ulong masterSeed, IEventBus bus);

    /// <summary>
    /// 一次 <see cref="IReplayPlayer.StepTo"/> 调用返回的世界快照（10 第 8 节伪代码
    /// <c>WorldSnapshot</c>）：<see cref="Tick"/> 是推进到的 tick 序号，<see cref="EventLog"/>
    /// 是自播放开始以来审计到的全部事件 key 序列（保序、不去重，逐条对应一次派发），
    /// <see cref="Digest"/> 是对 <see cref="EventLog"/> 与当前全部存活实体
    /// （<c>(EntityId, Position, LayerDepth)</c>，按 <c>EntityId</c> 序数排序）的确定性摘要
    /// （自写 FNV-1a 64 位，见 <see cref="Capture"/>；不使用 <see cref="object.GetHashCode"/>
    /// ——.NET 不保证 GetHashCode 跨进程/跨版本稳定，摘要必须可在不同机器/不同时间比对）。
    /// </summary>
    public sealed class WorldSnapshot
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public long Tick { get; }

        public IReadOnlyList<string> EventLog { get; }

        public string Digest { get; }

        public WorldSnapshot(long tick, IReadOnlyList<string> eventLog, string digest)
        {
            Tick = tick;
            EventLog = eventLog ?? throw new ArgumentNullException(nameof(eventLog));
            Digest = digest ?? throw new ArgumentNullException(nameof(digest));
        }

        /// <summary>
        /// 按当前 <paramref name="world"/> 的存活实体集合与给定 <paramref name="eventLog"/>
        /// 计算一份快照（供 <see cref="ReplayPlayer"/> 与直接驱动 <c>WorldSim</c> 的调用方
        /// ——例如确定性回归测试——复用同一套摘要算法，保证两条路径的 <see cref="Digest"/>
        /// 可比）。只读取 <paramref name="world"/>，不产生任何副作用、不消耗随机数、
        /// 不引入系统时间/线程。
        /// <para>
        /// F6 收口新增 <paramref name="entityStateProvider"/>（可选，默认 <c>null</c> 时行为与此前
        /// 完全一致，既有基线文件的摘要口径不受影响）：本类型所在的 <c>core/foundation</c> 是 L0
        /// 基础层，天然不知道 HP/伤害/资源池这类由 L2/L3（<c>core/numbers/power_set</c> 等）持有的
        /// 战斗状态语义（见 01_分层与依赖.md 依赖方向）——此前的摘要只覆盖事件 key 序列与
        /// <c>(EntityId, Position, LayerDepth)</c>，两份"同位置、同实体 id/layer、同事件 key，但
        /// 伤害/HP payload 不同"的状态会得到相同 Digest（见本类型判断记录）。调用方（持有 L2/L3
        /// 具体战斗状态查询能力的上层，例如测试夹具经 <c>IPowerHost</c> 查询）可选择性地传入一个
        /// "取某实体额外状态摘要字段"的委托，其返回的每个字符串都会按实体顺序折入哈希——不引入
        /// 新的跨层依赖，只是把"要不要、要哪些额外字段"的决定权交给持有那些字段语义的调用方。
        /// </para>
        /// </summary>
        public static WorldSnapshot Capture(
            long tick,
            IReadOnlyList<string> eventLog,
            IWorldSim world,
            Func<Id, IReadOnlyList<string>>? entityStateProvider = null)
        {
            if (eventLog == null)
            {
                throw new ArgumentNullException(nameof(eventLog));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var hash = FnvOffsetBasis;

            for (var i = 0; i < eventLog.Count; i++)
            {
                hash = FnvCombine(hash, eventLog[i]);
            }

            // QueryEntities 结果已按 EntityId 序数排序（见 WorldSim.QueryEntities 注释），
            // 遍历顺序天然确定，无需在这里再次排序。
            var entities = world.QueryEntities(new EntityFilter());
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                hash = FnvCombine(hash, entity.EntityId.Value);
                hash = FnvCombine(hash, entity.Position.X.ToString("R", CultureInfo.InvariantCulture));
                hash = FnvCombine(hash, entity.Position.Y.ToString("R", CultureInfo.InvariantCulture));
                hash = FnvCombine(hash, entity.LayerDepth.ToString("R", CultureInfo.InvariantCulture));

                if (entityStateProvider != null)
                {
                    var extraState = entityStateProvider(entity.EntityId);
                    if (extraState != null)
                    {
                        for (var j = 0; j < extraState.Count; j++)
                        {
                            hash = FnvCombine(hash, extraState[j]);
                        }
                    }
                }
            }

            return new WorldSnapshot(tick, eventLog, hash.ToString("x16", CultureInfo.InvariantCulture));
        }

        /// <summary>FNV-1a 64 位：<c>hash = (hash XOR byte) * FNV_prime</c>，逐字节处理
        /// <paramref name="text"/> 的 UTF-8 编码（自写实现，不借助任何哈希/加密库）。</summary>
        private static ulong FnvCombine(ulong hash, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }

            // 段分隔符（0 字节不会出现在 UTF-8 文本中间）：避免 "ab"+"c" 与 "a"+"bc" 意外撞出
            // 同一摘要（相邻字段拼接歧义），用一个固定的分隔字节把每次 Combine 的输入彼此隔开。
            hash ^= 0;
            hash *= FnvPrime;

            return hash;
        }
    }
}
