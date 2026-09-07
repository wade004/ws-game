using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Ai;
using Core.Rules.Common;

namespace Tests.Rules.Ai
{
    /// <summary>把 <see cref="AiHost"/> 的全部依赖装配到一起的测试脚手架：真实
    /// <see cref="FactionMatrix"/>/<see cref="PowerHost"/>/<see cref="RngHost"/>/<see cref="EventBus"/>/
    /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/>（<see cref="InMemoryDataSource"/>），
    /// Fake <see cref="ISkillHost"/>/<see cref="IThreatTable"/>/<see cref="IExprHostFactory"/>/
    /// <see cref="IUnitAccess"/>，<see cref="StubSpatialQuery"/>（桩空间查询）。</summary>
    internal sealed class AiTestHarness
    {
        public readonly FakeUnitAccess Units;
        public readonly FakeSkillHost Skills;
        public readonly FakeThreatTable Threat;
        public readonly FakeExprHostFactory ExprFactory;
        public readonly PowerHost Powers;
        public readonly FactionMatrix Factions;
        public readonly RngHost Rng;
        public readonly IEventBus Bus;
        public readonly StubSpatialQuery SpatialQuery;
        public readonly AiOptions Options;
        public readonly AiHost Host;

        public readonly List<AiStateChangedEvent> StateChanges = new List<AiStateChangedEvent>();
        public readonly List<AiDecisionMadeEvent> Decisions = new List<AiDecisionMadeEvent>();

        private AiTestHarness(
            FakeUnitAccess units, FakeSkillHost skills, FakeThreatTable threat, FakeExprHostFactory exprFactory,
            PowerHost powers, FactionMatrix factions, RngHost rng, IEventBus bus,
            StubSpatialQuery spatialQuery, AiOptions options, AiHost host)
        {
            Units = units;
            Skills = skills;
            Threat = threat;
            ExprFactory = exprFactory;
            Powers = powers;
            Factions = factions;
            Rng = rng;
            Bus = bus;
            SpatialQuery = spatialQuery;
            Options = options;
            Host = host;

            Bus.Subscribe<AiStateChangedEvent>(RulesEventKeys.AiStateChanged, e => StateChanges.Add(e));
            Bus.Subscribe<AiDecisionMadeEvent>(RulesEventKeys.AiDecisionMade, e => Decisions.Add(e));
        }

        public static AiTestHarness Build(
            string profilesJson,
            string rotationsJson,
            string patrolsJson = "[]",
            AiOptions? options = null,
            double maxHealth = 100,
            ulong rngSeed = 12345,
            string? skillDefJson = null,
            string? targetChainDefJson = null)
        {
            var bus = AiTestSupport.CreateBus();
            var registry = skillDefJson != null || targetChainDefJson != null
                ? AiTestSupport.MakeRegistry(bus, profilesJson, rotationsJson, patrolsJson, skillDefJson ?? "[]", targetChainDefJson ?? "[]")
                : AiTestSupport.MakeRegistry(bus, profilesJson, rotationsJson, patrolsJson);
            var factions = AiTestSupport.MakeFactionMatrix(bus);
            var powers = AiTestSupport.MakePowerHost(bus, maxHealth);
            var units = new FakeUnitAccess();
            var skills = new FakeSkillHost();
            var threat = new FakeThreatTable();
            var exprFactory = new FakeExprHostFactory();
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(rngSeed);
            var opts = options ?? new AiOptions();

            var host = new AiHost(registry, units, factions, powers, spatial, skills, threat, exprFactory, bus, rng,
                navigation: null, options: opts);

            return new AiTestHarness(units, skills, threat, exprFactory, powers, factions, rng, bus, spatial, opts, host);
        }

        /// <summary>把本次调用累积的待处理事件（含 ai.state_changed / ai.decision_made）派发给订阅者。</summary>
        public void Dispatch() => Bus.DispatchPending();

        /// <summary>登记一个测试单位：同时写入 <see cref="Units"/>（<see cref="IUnitAccess"/> 视角）与
        /// <see cref="SpatialQuery"/>（<see cref="Core.Foundation.EngineAdapter.ISpatialQuery"/> 视角）——
        /// 二者是两个独立的假/桩实现，AiHost 的感知（<c>FindNearestHostile</c>）先经 SpatialQuery 取候选，
        /// 再经 IUnitAccess/IFactionMatrix 过滤存活与阵营，两边必须保持位置同步，否则感知永远找不到目标。</summary>
        public void AddUnit(Id id, Vec2 position, Id faction, bool alive = true, double radius = 0.1)
        {
            Units.Add(id, position, faction, alive);
            SpatialQuery.Register(id, position, radius);
        }

        /// <summary>模拟"上一次 Step 产生的 move 意图已被移动系统应用"：同步更新 Units 与 SpatialQuery
        /// 两侧的位置。</summary>
        public void MoveUnit(Id id, Vec2 position, double radius = 0.1)
        {
            Units.SetPosition(id, position);
            SpatialQuery.Register(id, position, radius);
        }
    }
}
