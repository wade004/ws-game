using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.HookRegistry;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    /// <summary>供本模块测试共用的最小装配帮助（惯例同 <c>core/carriers/creature/tests</c> 的
    /// <c>CreatureTestSupport</c>）。</summary>
    internal static class TestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        public static Core.Foundation.DataRegistry.DataRegistry MakeRegistry(
            IEventBus bus, string defRowsJson, string? levelRowsJson = null)
        {
            var source = new InMemoryDataSource()
                .Add(Core.Gameplay.Encounter.EncounterSchemas.Def.Name,
                    Envelope(Core.Gameplay.Encounter.EncounterSchemas.Def.Name, defRowsJson));
            if (levelRowsJson != null)
            {
                source.Add(Core.Gameplay.Encounter.EncounterSchemas.Level.Name,
                    Envelope(Core.Gameplay.Encounter.EncounterSchemas.Level.Name, levelRowsJson));
            }

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Gameplay.Encounter.EncounterSchemas.Def);
            registry.RegisterSchema(Core.Gameplay.Encounter.EncounterSchemas.Level);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);
            return registry;
        }
    }

    /// <summary>组合假实现：同时承担 <see cref="IUnitAccess"/>（本模块只用到
    /// Exists/GetPosition/SetPosition/GetTemplateId）与 <see cref="ICreatureFactory"/>
    /// （Spawn 按 <c>"creature.inst_&lt;n&gt;"</c> 确定性递增分配 id，供测试精确断言
    /// <c>ai_rotation_override</c> 按具体单位 id 匹配的分支）。</summary>
    internal sealed class FakeWorld : IUnitAccess, ICreatureFactory
    {
        private int _nextCreatureNumber = 1;

        private readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Id> _templates = new Dictionary<string, Id>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vec2> _positions = new Dictionary<string, Vec2>(StringComparer.Ordinal);

        public readonly List<(Id TemplateId, Id MapId, Vec2 Position, double Facing)> SpawnCalls =
            new List<(Id, Id, Vec2, double)>();

        public readonly List<(Id EntityId, string Reason)> DespawnCalls = new List<(Id, string)>();

        public readonly List<(Id SpawnId, Id MapId)> SpawnRequesterCalls = new List<(Id, Id)>();

        public void SetPlayerPosition(Id playerUnitId, Vec2 position)
        {
            _existing.Add(playerUnitId.Value);
            _positions[playerUnitId.Value] = position;
        }

        /// <summary>供测试注入为 <see cref="Core.Gameplay.Encounter.SpawnRequester"/> 委托：按刷新表
        /// 条目 id 生成一个单位，复用与 <see cref="Spawn"/> 相同的 <c>creature.inst_&lt;n&gt;</c>
        /// 编号序列（保证测试里"第一个生成的单位是 creature.inst_1"这类断言在 template/spawn_ref
        /// 两种来源下都成立），不记录模板（刷新表条目不一定对应单一 <c>creature.template</c>）。</summary>
        public IReadOnlyList<Id> SpawnFromRequester(Id spawnId, Id mapId)
        {
            var entityId = new Id($"creature.inst_{_nextCreatureNumber}");
            _nextCreatureNumber++;

            _existing.Add(entityId.Value);
            _positions[entityId.Value] = Vec2.Zero;

            SpawnRequesterCalls.Add((spawnId, mapId));
            return new[] { entityId };
        }

        // ---------------------------------------------------------------
        // ICreatureFactory
        // ---------------------------------------------------------------

        public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null)
        {
            var entityId = new Id($"creature.inst_{_nextCreatureNumber}");
            _nextCreatureNumber++;

            _existing.Add(entityId.Value);
            _templates[entityId.Value] = templateId;
            _positions[entityId.Value] = position;

            SpawnCalls.Add((templateId, mapId, position, facing));
            return entityId;
        }

        public void Despawn(Id entityId, string reason)
        {
            _existing.Remove(entityId.Value);
            DespawnCalls.Add((entityId, reason));
        }

        // ---------------------------------------------------------------
        // IUnitAccess（只实现本模块用到的成员）
        // ---------------------------------------------------------------

        public bool Exists(Id unitId) => _existing.Contains(unitId.Value);

        public IReadOnlyList<Id> AllUnits => throw new NotImplementedException();

        public Vec2 GetPosition(Id unitId) => _positions.TryGetValue(unitId.Value, out var pos) ? pos : Vec2.Zero;

        public void SetPosition(Id unitId, Vec2 position) => _positions[unitId.Value] = position;

        public Id GetFaction(Id unitId) => throw new NotImplementedException();

        public int GetLevel(Id unitId) => throw new NotImplementedException();

        public double GetFacing(Id unitId) => throw new NotImplementedException();

        public bool IsAlive(Id unitId) => throw new NotImplementedException();

        public void SetAlive(Id unitId, bool alive) => throw new NotImplementedException();

        public Id? GetTemplateId(Id unitId) => _templates.TryGetValue(unitId.Value, out var t) ? (Id?)t : null;

        public IReadOnlyList<Id> GetTags(Id unitId) => throw new NotImplementedException();
    }

    /// <summary>最小 <see cref="IAiHost"/> 假实现：只记录 <see cref="SetRotation"/> 调用。</summary>
    internal sealed class FakeAiHost : IAiHost
    {
        public readonly List<(Id UnitId, Id RotationId)> RotationChanges = new List<(Id, Id)>();

        public SkillCastRequest? Evaluate(Id unitId) => throw new NotImplementedException();

        public BehaviorState GetBehaviorState(Id unitId) => throw new NotImplementedException();

        public void ForceState(Id unitId, BehaviorState state) => throw new NotImplementedException();

        public void SetRotation(Id unitId, Id rotationId) => RotationChanges.Add((unitId, rotationId));
    }

    /// <summary>最小 <see cref="IHookRegistry"/> 假实现：只记录 <see cref="Invoke"/> 调用，不要求
    /// 事先 <c>DeclareHookPoint</c>（真实实现要求先声明，见该接口注释；本假实现放宽这一限制，
    /// 只关心"是否被调用、调用了几次、传了哪个 hookId"）。</summary>
    internal sealed class FakeHookRegistry : IHookRegistry
    {
        public readonly List<Id> InvokedHookIds = new List<Id>();

        public void DeclareHookPoint(Id hookId, string signature) => throw new NotImplementedException();

        public void DeclareHookPoint(HookPointDefinition definition) => throw new NotImplementedException();

        public void DeclareFromDefinitions(IEnumerable<HookPointDefinition> definitions) => throw new NotImplementedException();

        public SubscriptionHandle Register(Id hookId, HookCallback callback, int order) => throw new NotImplementedException();

        public void Invoke(Id hookId, HookArgs args) => InvokedHookIds.Add(hookId);

        public IReadOnlyList<HookPointDefinition> HookPoints => throw new NotImplementedException();

        public int CallbackCount(Id hookId) => throw new NotImplementedException();
    }

    /// <summary>最小 <see cref="IRewardDispatcher"/> 假实现：只记录 <see cref="Grant"/> 调用。</summary>
    internal sealed class FakeRewardDispatcher : Core.Gameplay.Common.IRewardDispatcher
    {
        public readonly List<(Id UnitId, Core.Gameplay.Common.RewardBundle Bundle, Id SourceId)> Grants =
            new List<(Id, Core.Gameplay.Common.RewardBundle, Id)>();

        public bool Grant(Id unitId, Core.Gameplay.Common.RewardBundle bundle, Id sourceId)
        {
            Grants.Add((unitId, bundle, sourceId));
            return true;
        }
    }

    /// <summary>可控 <see cref="IExprHostFactory"/> 假实现：<c>group.key</c> -&gt; 固定
    /// <see cref="ExprValue"/> 的查询表，测试按需 <see cref="Values"/> 设值来控制
    /// <c>victory_condition</c>/<c>defeat_condition</c>/<c>trigger_condition</c>/
    /// <c>enter_condition</c> 的求值结果（惯例同 <c>core/gameplay/world_state/tests</c> 的
    /// <c>WorldOnlyExprHost</c>："只覆盖测试实际用到的行为，不代表 RulesExprHostFactory 正式实现
    /// 的完整语义"）。未设置的 key 一律返回 <see cref="ExprValue.OfBool(bool)"/> <c>false</c>。</summary>
    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public readonly Dictionary<string, ExprValue> Values = new Dictionary<string, ExprValue>(StringComparer.Ordinal);

        public void Set(string groupDotKey, bool value) => Values[groupDotKey] = ExprValue.OfBool(value);

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host(this);

        private sealed class Host : IExprHost
        {
            private readonly FakeExprHostFactory _f;

            public Host(FakeExprHostFactory f) => _f = f;

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
                _f.Values.TryGetValue(group + "." + key, out var v) ? v : ExprValue.OfBool(false);
        }
    }
}
