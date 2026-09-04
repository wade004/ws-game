using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Gameplay.Difficulty;
using Xunit;

namespace Tests.Gameplay.Difficulty
{
    public class DifficultyHostTests
    {
        private static readonly Id NormalTier = new Id("diff.sample_normal");
        private static readonly Id HardTier = new Id("diff.sample_hard");
        private static readonly Id PlayerFaction = new Id("fac.sample_player");
        private static readonly Id MonsterFaction = new Id("fac.sample_monster");
        private static readonly Id MapA = new Id("map.sample_a");
        private static readonly Id MapB = new Id("map.sample_b");

        private static DifficultyHost MakeHost(
            out FakeEffectSink effectSink,
            out FakeFactionMatrix factions,
            out FakeUnitAccess units,
            out Core.Foundation.EventBus.IEventBus bus,
            DifficultyOptions? options = null)
        {
            bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus);
            effectSink = new FakeEffectSink();
            factions = new FakeFactionMatrix();
            factions.SetHostile(MonsterFaction, PlayerFaction);
            units = new FakeUnitAccess();

            return new DifficultyHost(
                registry, bus, effectSink, factions, units,
                options ?? new DifficultyOptions(PlayerFaction));
        }

        // -------------------------------------------------------------
        // Apply：参数校验、成功路径、事件
        // -------------------------------------------------------------

        [Fact]
        public void Apply_UnknownTier_Throws()
        {
            var host = MakeHost(out _, out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.Apply(new Id("diff.sample_nonexistent"), DifficultyScope.Global, null));
        }

        [Fact]
        public void Apply_MapScope_WithoutMapId_Throws()
        {
            var host = MakeHost(out _, out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.Apply(NormalTier, DifficultyScope.Map, null));
        }

        [Fact]
        public void Apply_GlobalScope_WithMapId_Throws()
        {
            var host = MakeHost(out _, out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.Apply(NormalTier, DifficultyScope.Global, MapA));
        }

        [Fact]
        public void Apply_Success_SetsCurrentTierScopeMapId_AndReturnsTrue()
        {
            var host = MakeHost(out _, out _, out _, out _);

            var result = host.Apply(HardTier, DifficultyScope.Map, MapA);

            Assert.True(result);
            Assert.Equal(HardTier, host.CurrentTier);
            Assert.Equal(DifficultyScope.Map, host.CurrentScope);
            Assert.Equal(MapA, host.CurrentMapId);
        }

        [Fact]
        public void Apply_Success_PublishesDifficultyAppliedEvent_WithScopeAndTier()
        {
            var host = MakeHost(out _, out _, out _, out var bus);
            DifficultyAppliedEvent? captured = null;
            bus.Subscribe<DifficultyAppliedEvent>(DifficultyEventKeys.Applied, evt => captured = evt);

            host.Apply(HardTier, DifficultyScope.Global, null);

            Assert.NotNull(captured);
            Assert.Equal(DifficultyHost.GlobalScopeId, captured!.ScopeId);
            Assert.Equal(HardTier, captured.TierId);
        }

        [Fact]
        public void Apply_MidSwitch_Disallowed_ReturnsFalse_AndKeepsPreviousTier()
        {
            var options = new DifficultyOptions(PlayerFaction, allowMidSwitch: false);
            var host = MakeHost(out _, out _, out _, out _, options);
            host.Apply(NormalTier, DifficultyScope.Global, null);

            var result = host.Apply(HardTier, DifficultyScope.Global, null);

            Assert.False(result);
            Assert.Equal(NormalTier, host.CurrentTier);
        }

        [Fact]
        public void Apply_MidSwitch_Allowed_SwitchesToNewTier()
        {
            var options = new DifficultyOptions(PlayerFaction, allowMidSwitch: true);
            var host = MakeHost(out _, out _, out _, out _, options);
            host.Apply(NormalTier, DifficultyScope.Global, null);

            var result = host.Apply(HardTier, DifficultyScope.Global, null);

            Assert.True(result);
            Assert.Equal(HardTier, host.CurrentTier);
        }

        // -------------------------------------------------------------
        // LootMultiplier
        // -------------------------------------------------------------

        [Fact]
        public void LootMultiplier_DefaultsToOne_BeforeApply()
        {
            var host = MakeHost(out _, out _, out _, out _);

            Assert.Equal(1.0, host.LootMultiplier);
        }

        [Fact]
        public void LootMultiplier_ReflectsAppliedTier()
        {
            var host = MakeHost(out _, out _, out _, out _);
            host.Apply(HardTier, DifficultyScope.Global, null);

            Assert.Equal(1.5, host.LootMultiplier);
        }

        // -------------------------------------------------------------
        // creature.spawned 订阅：施加修正光环
        // -------------------------------------------------------------

        [Fact]
        public void CreatureSpawned_BeforeApply_NoAurasApplied()
        {
            var host = MakeHost(out var effectSink, out _, out var units, out var bus);
            var unitId = new Id("creature.inst_1");
            units.AddUnit(unitId, MonsterFaction);

            bus.PublishImmediate(new CreatureSpawnedEvent(unitId, new Id("creature.sample_monster")));

            Assert.Empty(effectSink.AppliedAuras);
        }

        [Fact]
        public void CreatureSpawned_HostileUnit_AfterApply_AppliesAllModifierAuras()
        {
            var host = MakeHost(out var effectSink, out _, out var units, out var bus);
            host.Apply(HardTier, DifficultyScope.Global, null);
            var unitId = new Id("creature.inst_1");
            units.AddUnit(unitId, MonsterFaction);

            bus.PublishImmediate(new CreatureSpawnedEvent(unitId, new Id("creature.sample_monster")));

            Assert.Equal(2, effectSink.AppliedAuras.Count);
            Assert.Equal(unitId, effectSink.AppliedAuras[0].TargetId);
            Assert.Equal(new Id("aura.sample_tough"), effectSink.AppliedAuras[0].AuraDefId);
            Assert.Equal(HardTier, effectSink.AppliedAuras[0].SourceId);
            Assert.Equal(new Id("aura.sample_deadly"), effectSink.AppliedAuras[1].AuraDefId);
        }

        [Fact]
        public void CreatureSpawned_NonHostileUnit_AfterApply_NoAurasApplied()
        {
            var host = MakeHost(out var effectSink, out _, out var units, out var bus);
            host.Apply(HardTier, DifficultyScope.Global, null);
            var unitId = new Id("creature.inst_1");
            units.AddUnit(unitId, PlayerFaction); // 玩家自己的阵营，对自己不敌对

            bus.PublishImmediate(new CreatureSpawnedEvent(unitId, new Id("creature.sample_ally")));

            Assert.Empty(effectSink.AppliedAuras);
        }

        [Fact]
        public void CreatureSpawned_ApplyToAll_AppliesRegardlessOfFaction()
        {
            var options = new DifficultyOptions(PlayerFaction, applyToAll: true);
            var host = MakeHost(out var effectSink, out _, out var units, out var bus, options);
            host.Apply(HardTier, DifficultyScope.Global, null);
            var unitId = new Id("creature.inst_1");
            units.AddUnit(unitId, PlayerFaction);

            bus.PublishImmediate(new CreatureSpawnedEvent(unitId, new Id("creature.sample_ally")));

            Assert.Equal(2, effectSink.AppliedAuras.Count);
        }

        [Fact]
        public void CreatureSpawned_MapScope_OnlyAppliesForMatchingMap()
        {
            var host = MakeHost(out var effectSink, out _, out var units, out var bus);
            host.Apply(HardTier, DifficultyScope.Map, MapA);
            var unitInMapA = new Id("creature.inst_1");
            var unitInMapB = new Id("creature.inst_2");
            units.AddUnit(unitInMapA, MonsterFaction, MapA);
            units.AddUnit(unitInMapB, MonsterFaction, MapB);

            bus.PublishImmediate(new CreatureSpawnedEvent(unitInMapA, new Id("creature.sample_monster")));
            bus.PublishImmediate(new CreatureSpawnedEvent(unitInMapB, new Id("creature.sample_monster")));

            // diff.sample_hard 挂了两条 modifier_aura_refs，命中作用域的 unitInMapA 应收到两次
            // ApplyAura；unitInMapB 不在 Map 作用域内，不应收到任何一次。
            Assert.Equal(2, effectSink.AppliedAuras.Count);
            Assert.All(effectSink.AppliedAuras, applied => Assert.Equal(unitInMapA, applied.TargetId));
        }

        [Fact]
        public void CreatureSpawned_MapScope_UnknownMap_NotApplied()
        {
            var host = MakeHost(out var effectSink, out _, out var units, out var bus);
            host.Apply(HardTier, DifficultyScope.Map, MapA);
            var unitId = new Id("creature.inst_1");
            units.AddUnit(unitId, MonsterFaction); // 未登记 mapId -> GetMapId 返回 null

            bus.PublishImmediate(new CreatureSpawnedEvent(unitId, new Id("creature.sample_monster")));

            Assert.Empty(effectSink.AppliedAuras);
        }

        // -------------------------------------------------------------
        // IPersistable
        // -------------------------------------------------------------

        [Fact]
        public void SectionKey_IsWorldDifficulty()
        {
            var host = MakeHost(out _, out _, out _, out _);

            Assert.Equal("world.difficulty", host.SectionKey);
        }

        [Fact]
        public void Save_Load_RoundTrips_CurrentTierScopeMapId()
        {
            var host = MakeHost(out _, out _, out _, out _);
            host.Apply(HardTier, DifficultyScope.Map, MapA);

            var saved = host.Save();

            var restored = MakeHost(out _, out _, out _, out _);
            restored.Load(saved);

            Assert.Equal(HardTier, restored.CurrentTier);
            Assert.Equal(DifficultyScope.Map, restored.CurrentScope);
            Assert.Equal(MapA, restored.CurrentMapId);
        }

        [Fact]
        public void Load_NullData_ClearsCurrentTier()
        {
            var host = MakeHost(out _, out _, out _, out _);
            host.Apply(NormalTier, DifficultyScope.Global, null);

            host.Load(JsonNull.Instance);

            Assert.Null(host.CurrentTier);
        }

        [Fact]
        public void Load_DoesNotPublishDifficultyAppliedEvent()
        {
            var host = MakeHost(out _, out _, out _, out var bus);
            host.Apply(NormalTier, DifficultyScope.Global, null);
            var saved = host.Save();

            var restored = MakeHost(out _, out _, out _, out var restoredBus);
            var dispatchCount = 0;
            restoredBus.Subscribe<DifficultyAppliedEvent>(DifficultyEventKeys.Applied, _ => dispatchCount++);

            restored.Load(saved);

            Assert.Equal(0, dispatchCount);
        }
    }
}
