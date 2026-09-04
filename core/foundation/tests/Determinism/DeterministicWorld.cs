using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.Determinism
{
    /// <summary>
    /// `DeterminismTests` 共用的"小而有戏"的世界组装：全部用桩与 L0 模块搭建（
    /// <see cref="EventBus"/>、<see cref="RngHost"/>、<see cref="WorldSim"/>），不依赖任何
    /// 上层模块（呼应 11_工程规范与测试.md 第 6 节"集成测试...用桩适配层驱动 WorldSim"，
    /// 本用例连桩适配层都不需要——不涉及渲染/输入/文件，直接用 L0 契约即可）。
    /// </summary>
    internal static class DeterministicWorld
    {
        /// <summary>固定步长（秒）：任意取值，只要录制/回放/直接跑三条路径统一使用同一个值。</summary>
        public const double StepSeconds = 1.0 / 30.0;

        /// <summary>意图脚本覆盖的总 tick 数。</summary>
        public const int TotalTicks = 30;

        public static readonly Id MapId = new Id("map.determinism_test");
        public static readonly Id PlayerId = new Id("unit.player");
        public static readonly Id NpcAId = new Id("unit.npc_a");
        public static readonly Id NpcBId = new Id("unit.npc_b");
        public static readonly Id WanderStream = new Id("ai.wander");

        /// <summary>
        /// 测试自定义事件 key：`unit.moved`（03_运行时骨架.md 第 5 节举例提及，但当前
        /// `data/_sample/found/found.event_catalog.json` 尚未登记该行——见本文件
        /// <see cref="BuildCatalog"/> 判断记录）。
        /// </summary>
        public static readonly Id UnitMovedKey = new Id("unit.moved");

        /// <summary>玩家意图脚本：tick 序号 → (dx, dy)，四个 tick 各不相同的位移参数。</summary>
        public static readonly IReadOnlyList<(long Tick, double Dx, double Dy)> MoveScript = new[]
        {
            (3L, 1.0, 0.0),
            (7L, 0.0, 1.0),
            (12L, -1.0, 2.0),
            (25L, 3.0, -1.0),
        };

        /// <summary>
        /// 判断记录：`unit.moved` 未在生产用 `found.event_catalog.json` 登记（该数据表目前
        /// 只覆盖 06 规则层已定义的事件，`unit.moved` 是 03 第 5 节"View 只读订阅事件"一节
        /// 举例提到、但尚未落到登记表的一个例子）。本测试不改动生产数据文件（超出 T1-9
        /// 授权范围），改为给测试自建一份登记表：在 <c>EventKeys.All</c>（生产登记表的全部
        /// 正式事件）基础上，额外登记本测试自用的 `unit.moved`，保持
        /// `EventBusOptions.StrictCatalog` 默认值 `true` 不变（不用"关闭严格校验"这条捷径掩盖
        /// 未登记事件的问题），本模块用到的其余事件 key（entity.created/destroyed、
        /// sim.tick_started/finished、ai.state_changed）都已在生产登记表中。
        /// </summary>
        public static IEventCatalog BuildCatalog()
        {
            var definitions = new List<EventDefinition>(EventKeys.All.Length + 1);

            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }

            definitions.Add(new EventDefinition(
                UnitMovedKey,
                "unit",
                new[] { "unitId", "dx", "dy", "newX", "newY" },
                "确定性回放集成测试自定义事件（DeterminismTests，非正式登记表）：单位按 move 意图位移。"));

            return EventCatalog.FromDefinitions(definitions);
        }

        /// <summary>构造一个全新的 (bus, audit, rng, world) 四元组，注册好三个测试阶段处理器
        /// 与三个初始实体（玩家 + 两个 NPC）。每次调用都是完全独立的新实例——用于"两次独立
        /// 运行比较事件流"的确定性用例。</summary>
        public static (IWorldSim World, IRngHost Rng, IEventBus Bus, InMemoryEventAudit Audit) Build(ulong masterSeed)
        {
            var audit = new InMemoryEventAudit();
            var bus = new EventBus(BuildCatalog(), new EventBusOptions { AuditLog = true }, audit: audit);
            var (world, rng) = BuildOnBus(masterSeed, bus);
            return (world, rng, bus, audit);
        }

        /// <summary>
        /// 在调用方已经构造好的 <paramref name="bus"/> 上组装世界：供 <see cref="ReplayPlayer"/>
        /// 经 <see cref="WorldFactory"/> 复用（回放必须与录制共用同一条事件流管线，见
        /// <c>ReplayPlayer</c> 构造函数注释），方法签名与 <see cref="WorldFactory"/> 结构相同，
        /// 可直接作为方法组转换为该委托。
        /// </summary>
        public static (IWorldSim World, IRngHost Rng) BuildOnBus(ulong masterSeed, IEventBus bus)
        {
            var rng = new RngHost(masterSeed);
            var world = new WorldSim(bus);

            world.AddEntity(new DeterministicUnit(PlayerId, MapId, "player"));
            world.AddEntity(new DeterministicUnit(NpcAId, MapId, "npc"));
            world.AddEntity(new DeterministicUnit(NpcBId, MapId, "npc"));

            world.RegisterPhaseHandler(TickPhase.AiDecision, new WanderHandler(rng, bus, WanderStream));
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new IntentMoveHandler(bus, UnitMovedKey));
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new SpawnDespawnHandler(MapId));

            return (world, rng);
        }

        public static JsonObject MoveArgs(double dx, double dy)
        {
            return new JsonObjectBuilder()
                .Add("dx", new JsonNumber(dx))
                .Add("dy", new JsonNumber(dy))
                .Build();
        }

        /// <summary>
        /// 按 <see cref="MoveScript"/>（或调用方传入的 <paramref name="script"/>）跑满
        /// <see cref="TotalTicks"/> 个 tick：每个 tick 先提交该 tick 到期的 move 意图
        /// （<see cref="IWorldSim.SubmitIntent"/>，阶段 1 之前），再调用一次
        /// <see cref="IWorldSim.Tick"/>。<paramref name="onSubmit"/> 可选：每提交一条意图时回调
        /// 一次（供录制场景同步写入 <see cref="ReplayRecorder"/>）。
        /// </summary>
        public static void RunScript(
            IWorldSim world,
            Id playerId,
            IReadOnlyList<(long Tick, double Dx, double Dy)>? script = null,
            Action<long, Intent>? onSubmit = null)
        {
            var effectiveScript = script ?? MoveScript;

            for (long tickNumber = 1; tickNumber <= TotalTicks; tickNumber++)
            {
                for (var i = 0; i < effectiveScript.Count; i++)
                {
                    var entry = effectiveScript[i];
                    if (entry.Tick != tickNumber)
                    {
                        continue;
                    }

                    var intent = new Intent(playerId, "move", MoveArgs(entry.Dx, entry.Dy));
                    world.SubmitIntent(intent);
                    onSubmit?.Invoke(tickNumber, intent);
                }

                world.Tick(SimStep.Continuous(StepSeconds));
            }
        }

        /// <summary>把 <see cref="InMemoryEventAudit.Records"/> 转成事件 key 字符串序列
        /// （<see cref="WorldSnapshot.EventLog"/> 同构），供测试断言两条事件流是否逐项相等。</summary>
        public static IReadOnlyList<string> ToEventLog(InMemoryEventAudit audit)
        {
            var records = audit.Records;
            var log = new List<string>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                log.Add(records[i].Key.Value);
            }
            return log;
        }
    }
}
