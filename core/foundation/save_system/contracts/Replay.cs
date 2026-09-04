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
    /// 一份完整回放数据（10 第 8 节"回放 = 固定步长 + 分流随机源初始状态 + 输入录像"）。
    /// <see cref="StepSeconds"/>（固定步长本身）一并记录：虽然属于 <c>sim_loop</c> 的运行时
    /// 配置，但只记随机源与输入、不记步长会导致回放脱离原始运行的时间基准，
    /// 因此本类型把它一并纳入（T1-9 判断记录：契约原注释"固定步长本身...不属于本类型"
    /// 改为"必须一并记录，否则 <see cref="IReplayPlayer.StepTo"/> 无法独立于外部配置推进"）。
    /// <see cref="TickCount"/> 记录本次录像覆盖的总 tick 数（含没有任何输入的 tick）。
    /// </summary>
    public sealed class ReplayData
    {
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

        public ReplayData(
            IReadOnlyDictionary<string, RngStreamState> rngSeeds,
            IReadOnlyList<ReplayInputRecord> inputs,
            double stepSeconds,
            long tickCount)
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
        }

        /// <summary>序列化为 <see cref="JsonObject"/>（供存盘，见 10 第 8 节"问题复现：玩家反馈
        /// 的问题若能提供录像，可离线重放定位"——离线即意味着需要落盘）。键顺序固定，
        /// <see cref="RngSeeds"/> 按 key 的原始插入顺序写出（<see cref="JsonObjectBuilder"/>
        /// 保序），不额外排序。</summary>
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

            return new JsonObjectBuilder()
                .Add("stepSeconds", new JsonNumber(StepSeconds))
                .Add("tickCount", new JsonNumber(TickCount))
                .Add("rngSeeds", seedsBuilder.Build())
                .Add("inputs", new JsonArray(inputsArray))
                .Build();
        }

        /// <summary>按 <see cref="ToJson"/> 的形状反序列化。</summary>
        public static ReplayData FromJson(JsonObject json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var stepSeconds = ((JsonNumber)json["stepSeconds"]).Value;

            if (!((JsonNumber)json["tickCount"]).TryGetInt64(out var tickCount))
            {
                throw new FormatException("ReplayData.tickCount 不是合法整数");
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

            return new ReplayData(seeds, inputs, stepSeconds, tickCount);
        }
    }

    /// <summary>
    /// 回放录制器契约（10 第 8 节伪代码 <c>ReplayRecorder</c>）。实现见
    /// <see cref="ReplayRecorder"/>（T1-9）。
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
    /// 回放播放器契约（10 第 8 节伪代码 <c>ReplayPlayer</c>）。<see cref="StepTo"/> 的返回类型
    /// 在 T1-9 落地为本文件定义的 <see cref="WorldSnapshot"/>（原契约用 <c>object</c> 占位，
    /// 见本类型 T1-6 阶段的历史注释——占位已按任务书授权在本任务替换为具体类型）。
    /// 实现见 <see cref="ReplayPlayer"/>。
    /// </summary>
    public interface IReplayPlayer
    {
        /// <summary>加载一份回放数据，准备从头播放。</summary>
        void Load(ReplayData data);

        /// <summary>推进播放到指定 tick（只能向前推进，不支持回退），返回该 tick 的世界快照。</summary>
        WorldSnapshot StepTo(long tick);
    }

    /// <summary>
    /// 世界工厂：由持有具体游戏内容（阶段处理器、意图词汇表等）的调用方（测试/更上层模块）
    /// 提供"如何组装一个带处理器的世界"，<see cref="ReplayPlayer"/> 自身不知道任何具体业务
    /// 内容，只按 10 第 8 节"回放"流程驱动世界推进与随机源状态恢复。
    /// <paramref name="masterSeed"/> 只用于工厂内部构造 <c>IRngHost</c> 的初始主种子（实际每条
    /// 分流随机源的状态会在 <see cref="ReplayPlayer.Load"/> 内按 <see cref="ReplayData.RngSeeds"/>
    /// 逐条 <c>SetStreamState</c> 覆盖，因此具体传入值对回放结果不产生影响，工厂可忽略）；
    /// <paramref name="bus"/> 由 <see cref="ReplayPlayer"/> 持有生命周期并传入，工厂只负责在其上
    /// 注册阶段处理器与构造世界，不自行另建事件总线（否则 <see cref="ReplayPlayer"/> 读不到
    /// 事件审计日志）。
    /// </summary>
    public delegate (IWorldSim World, IRngHost Rng) WorldFactory(ulong masterSeed, IEventBus bus);

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
        /// </summary>
        public static WorldSnapshot Capture(long tick, IReadOnlyList<string> eventLog, IWorldSim world)
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
