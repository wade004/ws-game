using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="IReplayPlayer"/> 的默认实现（T1-9，见 10_存档与持久化.md 第 8 节）。
    /// 本类不知道任何具体游戏内容（阶段处理器、意图词汇表等）——世界如何组装由调用方经
    /// <see cref="WorldFactory"/> 提供；本类只负责：
    /// 1) <see cref="Load"/> 时用工厂构造世界，并把 <see cref="ReplayData.RngSeeds"/>
    ///    逐条 <c>SetStreamState</c> 回随机源（10 第 8 节"分流随机源初始状态"）；
    /// 2) <see cref="StepTo"/> 时按 tick 顺序把该 tick 录入的意图 <c>SubmitIntent</c> 后
    ///    调用一次 <c>world.Tick(Continuous)</c>（10 第 8 节"输入录像"+"固定步长"）；
    /// 3) 从调用方提供的 <see cref="InMemoryEventAudit"/> 读出自开始以来的事件 key 序列，
    ///    连同当前世界状态一起交给 <see cref="WorldSnapshot.Capture"/> 算出确定性摘要。
    /// 不读取任何系统时间、不使用线程/反射，只使用调用方传入的 <see cref="ReplayData"/>
    /// 与 <see cref="WorldFactory"/> 构造出的对象。
    /// </summary>
    public sealed class ReplayPlayer : IReplayPlayer
    {
        private readonly WorldFactory _factory;
        private readonly IEventBus _bus;
        private readonly InMemoryEventAudit _audit;

        private ReplayData? _data;
        private IWorldSim? _world;
        private long _ticksAdvanced;
        private Dictionary<long, ReplayStepRecord>? _stepsByTick;

        /// <summary>非空表示本次是经 <see cref="LoadDiscrete"/> 加载的离散回放：<see cref="StepTo"/>
        /// 改走"经 TurnScheduler 驱动"的路径（见 <see cref="IReplayPlayer.LoadDiscrete"/> 判断
        /// 记录），不再使用 <see cref="_stepsByTick"/>。</summary>
        private TurnScheduler? _scheduler;

        /// <summary>
        /// <paramref name="bus"/>/<paramref name="audit"/> 由调用方构造并持有：<paramref name="bus"/>
        /// 必须以 <c>EventBusOptions.AuditLog = true</c>、且以 <paramref name="audit"/> 作为其
        /// <c>IEventAudit</c> 构造（否则 <see cref="StepTo"/> 读不到事件流，<c>EventLog</c> 恒为空）；
        /// <paramref name="factory"/> 只负责在 <paramref name="bus"/> 上注册处理器、构造世界与随机源，
        /// 不应另建事件总线（见 <see cref="WorldFactory"/> 注释）。
        /// </summary>
        public ReplayPlayer(WorldFactory factory, IEventBus bus, InMemoryEventAudit audit)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Load(ReplayData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));

            // P1-04 收口：传 data.MasterSeed（录制时的真实主种子）而不是恒定 0UL，见
            // Core.Foundation.SaveSystem.WorldFactory 判断记录——"录制起点之后才第一次被访问的流"
            // 不在 RngSeeds 里，它们的懒创建初始状态需要与录制时相同的主种子才能派生出一致的序列。
            var (world, rng) = _factory(data.MasterSeed, _bus);
            _world = world ?? throw new InvalidOperationException("WorldFactory 返回的 World 不能为 null");

            if (rng == null)
            {
                throw new InvalidOperationException("WorldFactory 返回的 Rng 不能为 null");
            }

            RestoreRngSeeds(data, rng);

            // ADR-0013：按 tick 号建一份 Steps 的查找表（见 ReplayStepRecord 类型注释"判断记录
            // （旧格式兼容）"——旧格式/未记录的 tick 在字典里找不到，StepTo 按 Continuous 兜底）。
            _stepsByTick = new Dictionary<long, ReplayStepRecord>();
            for (var i = 0; i < data.Steps.Count; i++)
            {
                var record = data.Steps[i];
                _stepsByTick[record.Tick] = record;
            }

            _scheduler = null;
            _ticksAdvanced = 0;
        }

        public void LoadDiscrete(ReplayData data, DiscreteWorldFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            _data = data ?? throw new ArgumentNullException(nameof(data));

            // P1-04 收口：同 Load 的判断记录，传 data.MasterSeed 而不是恒定 0UL。
            var (world, rng, scheduler) = factory(data.MasterSeed, _bus);
            _world = world ?? throw new InvalidOperationException("DiscreteWorldFactory 返回的 World 不能为 null");
            _scheduler = scheduler ?? throw new InvalidOperationException("DiscreteWorldFactory 返回的 Scheduler 不能为 null");

            if (rng == null)
            {
                throw new InvalidOperationException("DiscreteWorldFactory 返回的 Rng 不能为 null");
            }

            RestoreRngSeeds(data, rng);

            _stepsByTick = null;
            _ticksAdvanced = 0;
        }

        /// <summary>10 第 8 节"分流随机源初始状态"：把录制时记下的每条流状态原样恢复（P1-04 收口：
        /// 工厂内部用哪个主种子构造 IRngHost 现在很重要——见 WorldFactory 判断记录，
        /// <see cref="Load"/>/<see cref="LoadDiscrete"/> 已改为传入 <see cref="ReplayData.MasterSeed"/>；
        /// 本方法只负责覆盖"录制开始时已经创建过的流"，未覆盖到的流依赖工厂拿到的主种子派生）。
        /// <see cref="Load"/>/<see cref="LoadDiscrete"/> 共用。</summary>
        private static void RestoreRngSeeds(ReplayData data, IRngHost rng)
        {
            foreach (var pair in data.RngSeeds)
            {
                rng.SetStreamState(new Id(pair.Key), pair.Value);
            }
        }

        public WorldSnapshot StepTo(long tick, Func<Id, IReadOnlyList<string>>? entityStateProvider = null)
        {
            if (_data == null || _world == null)
            {
                throw new InvalidOperationException("StepTo 之前必须先调用 Load/LoadDiscrete");
            }

            if (tick < _ticksAdvanced)
            {
                throw new ArgumentException(
                    $"StepTo 只能向前推进：当前已推进到 tick {_ticksAdvanced}，不能回退到 {tick}", nameof(tick));
            }

            return _scheduler != null ? StepToDiscrete(tick, entityStateProvider) : StepToContinuousTape(tick, entityStateProvider);
        }

        /// <summary>F6 收口新增：暴露本次 <see cref="Load"/>/<see cref="LoadDiscrete"/> 内部构造的
        /// 世界，供调用方在 <see cref="StepTo"/> 之后（或期间）按需查询该世界之外的战斗状态
        /// （例如经与 <see cref="WorldFactory"/> 同一构造链路取到的 <c>IPowerHost</c>）以构造
        /// <see cref="WorldSnapshot.Capture"/> 的 <c>entityStateProvider</c>——本类型只读暴露，不
        /// 因此产生新的跨层依赖（见该方法参数判断记录）。<see cref="Load"/>/<see cref="LoadDiscrete"/>
        /// 之前为 <c>null</c>。</summary>
        public IWorldSim? World => _world;

        /// <summary>经 <see cref="LoadDiscrete"/> 加载后的推进路径：见 <see cref="IReplayPlayer.LoadDiscrete"/>
        /// 判断记录——反复调用 <see cref="TurnScheduler.NextStep"/>，非空即直接 <c>world.Tick</c>；
        /// 空即代表"轮到的行动者需要外部输入"，按当前 tick 号从录像的 <see cref="ReplayData.Inputs"/>/
        /// <see cref="ReplayData.EndTurns"/> 里取出录制时的决策原样提交，找不到则判定录像与调度器
        /// 决策不一致（不静默降级，见 11 第 4 节），抛出说明性异常。</summary>
        private WorldSnapshot StepToDiscrete(long tick, Func<Id, IReadOnlyList<string>>? entityStateProvider)
        {
            var inputs = _data!.Inputs;
            var endTurns = _data.EndTurns;

            while (_ticksAdvanced < tick)
            {
                var step = _scheduler!.NextStep();
                if (step == null)
                {
                    var tickNumber = _ticksAdvanced + 1;
                    var appliedAny = false;

                    for (var i = 0; i < inputs.Count; i++)
                    {
                        var input = inputs[i];
                        if (input.Tick != tickNumber)
                        {
                            continue;
                        }

                        _scheduler.SubmitIntent(input.ActorId, new Intent(input.ActorId, input.IntentKind, input.Args));
                        appliedAny = true;
                    }

                    for (var i = 0; i < endTurns.Count; i++)
                    {
                        var endTurn = endTurns[i];
                        if (endTurn.Tick != tickNumber)
                        {
                            continue;
                        }

                        _scheduler.EndTurn(endTurn.ActorId);
                        appliedAny = true;
                    }

                    if (!appliedAny)
                    {
                        var waitingActor = _scheduler.GetCurrentActor();
                        throw new InvalidOperationException(
                            $"离散重放在 tick {tickNumber} 需要外部输入（TurnScheduler 等待行动者 " +
                            $"\"{waitingActor}\" 提交意图或结束回合），但录像的 Inputs/EndTurns 里没有任何" +
                            $"对应这个 tick 的记录——说明重放时的调度器决策与录制时不一致（可能是录像不" +
                            "完整，也可能是 TurnScheduler 存在非确定性来源），拒绝静默继续。");
                    }

                    continue; // 已提交决策，回到循环开头重新问一次 NextStep（不消耗一个 tick）。
                }

                _world!.Tick(step.Value);
                _scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                _ticksAdvanced++;
            }

            return WorldSnapshot.Capture(_ticksAdvanced, BuildEventLog(), _world!, entityStateProvider);
        }

        /// <summary>经 <see cref="Load"/> 加载后的推进路径（改动前既有行为，逐字节不变）：按录像里
        /// 记下的 <see cref="ReplayStepRecord"/>（没有记录的 tick 按 Continuous 兜底）原样重放给
        /// <c>world.Tick</c>，不涉及任何 <see cref="TurnScheduler"/>。</summary>
        private WorldSnapshot StepToContinuousTape(long tick, Func<Id, IReadOnlyList<string>>? entityStateProvider)
        {
            var inputs = _data!.Inputs;

            while (_ticksAdvanced < tick)
            {
                // 录像里的 tick 序号从 1 起（见 10_存档与持久化.md 第 8 节、本任务
                // DeterminismTests 的意图脚本约定）：第 _ticksAdvanced+1 个 Tick() 调用对应
                // 录像里 Tick == _ticksAdvanced+1 的输入。
                var tickNumber = _ticksAdvanced + 1;

                for (var i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    if (input.Tick == tickNumber)
                    {
                        _world!.SubmitIntent(new Intent(input.ActorId, input.IntentKind, input.Args));
                    }
                }

                // ADR-0013：按录制的 ReplayStepRecord 还原本 tick 实际使用的 SimStep（离散步含
                // actorId/phase）；没有对应记录（旧格式录像，或本任务之前录的档）时按 Continuous
                // 处理，与本任务之前唯一存在过的行为完全一致（见 ReplayStepRecord 类型注释）。
                var step = _stepsByTick!.TryGetValue(tickNumber, out var stepRecord)
                    ? stepRecord.ToSimStep(_data.StepSeconds)
                    : SimStep.Continuous(_data.StepSeconds);

                _world!.Tick(step);
                _ticksAdvanced++;
            }

            return WorldSnapshot.Capture(_ticksAdvanced, BuildEventLog(), _world!, entityStateProvider);
        }

        private IReadOnlyList<string> BuildEventLog()
        {
            var records = _audit.Records;
            var log = new List<string>(records.Count);

            for (var i = 0; i < records.Count; i++)
            {
                log.Add(records[i].Key.Value);
            }

            return log;
        }
    }
}
