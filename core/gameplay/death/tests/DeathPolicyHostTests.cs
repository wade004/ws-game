using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Death;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Death
{
    /// <summary>
    /// <see cref="DeathPolicyHost"/> 的模块单测（脱离引擎，惯例同 <c>core/gameplay/spawn/tests</c>）：
    /// 三种死亡复活策略各至少两条用例，且覆盖"仅玩家单位触发、AI/生物死亡不触发"（DECISIONS 拍板 3）。
    /// </summary>
    public sealed class DeathPolicyHostTests
    {
        private static readonly Id PlayerId = new Id("unit.sample_player");
        private static readonly Id CreatureId = new Id("unit.sample_creature");
        private static readonly Id MapId = new Id("world.sample_field");
        private static readonly Id FactionPlayer = new Id("faction.player");
        private static readonly Id ArchetypeSample = new Id("arch.class.sample");
        private static readonly Id CreatureTemplate = new Id("creature.sample");
        private static readonly Id AutosaveSlot = new Id("slot.autosave");

        private sealed class Fixture
        {
            public IEventBus Bus = default!;
            public WorldSim World = default!;
            public StubFileSystem Fs = default!;
            public Core.Foundation.SaveSystem.SaveSystem SaveSystem = default!;
            public AppStateHost AppState = default!;
            public DeathPolicyHost Host = default!;
            public List<(Id UnitId, Vec2 Position, double HealthFraction)> Revived = new();
            public List<UnitRespawnedEvent> RespawnedEvents = new();
            public InMemoryDeathPolicyDiagnostics Diagnostics = new();

            public void Died(Id unitId, Id? mapId = null, Id? killerId = null) =>
                Bus.PublishImmediate(new UnitDiedEvent(unitId, killerId, mapId ?? MapId));

            public void Tick() => Host.Execute(SimStep.Continuous(0), World);
        }

        private static Fixture Build(
            RespawnPolicy defaultPolicy = RespawnPolicy.RespawnPoint,
            DeathPolicyOptions? options = null,
            bool wireRevive = true,
            bool wireResolve = true)
        {
            var bus = DeathTestSupport.NewEventBus();
            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.sample")));
            var appState = new AppStateHost(bus, AppStateMachineConfig.Default());

            world.AddEntity(new PlayerUnit(PlayerId, MapId, FactionPlayer, ArchetypeSample) { Position = new Vec2(0, 0) });
            world.AddEntity(new CreatureUnit(CreatureId, MapId, new Id("faction.hostile"), CreatureTemplate) { Position = new Vec2(1, 1) });

            var fx = new Fixture { Bus = bus, World = world, Fs = fs, SaveSystem = saveSystem, AppState = appState };

            var resolvedOptions = options ?? new DeathPolicyOptions();
            if (wireRevive)
            {
                resolvedOptions.ReviveUnit ??= (unitId, position, healthFraction) =>
                    fx.Revived.Add((unitId, position, healthFraction));
            }

            if (wireResolve)
            {
                resolvedOptions.ResolveDefaultSpawn ??= mapId => (mapId, new Vec2(5, 6));
            }

            fx.Host = new DeathPolicyHost(bus, world, saveSystem, appState, defaultPolicy, resolvedOptions, fx.Diagnostics);
            bus.Subscribe<UnitRespawnedEvent>(RulesEventKeys.UnitRespawned, e => fx.RespawnedEvents.Add(e));
            return fx;
        }

        // ==== respawn_point ======================================================

        [Fact]
        public void RespawnPoint_PlayerDies_RevivesAtResolvedSpawn_AfterOneTick_AndPublishesRespawnedEvent()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.RespawnPoint);

            fx.Died(PlayerId);
            Assert.Empty(fx.Revived); // 死亡当次不立即复活（延迟按选项，默认下一 tick）。

            fx.Tick();

            var revived = Assert.Single(fx.Revived);
            Assert.Equal(PlayerId, revived.UnitId);
            Assert.Equal(new Vec2(5, 6), revived.Position);
            Assert.Equal(1.0, revived.HealthFraction); // 06 第 4.6 节"满状态复活"默认值。

            var respawned = Assert.Single(fx.RespawnedEvents);
            Assert.Equal(PlayerId, respawned.UnitId);
            Assert.Equal(RespawnPolicy.RespawnPoint, respawned.Policy);
        }

        [Fact]
        public void RespawnPoint_LongerDelay_DoesNotReviveBeforeDelayElapses()
        {
            var options = new DeathPolicyOptions { RespawnDelayTicks = 3 };
            var fx = Build(defaultPolicy: RespawnPolicy.RespawnPoint, options: options);

            fx.Died(PlayerId);
            fx.Tick(); // 1/3
            Assert.Empty(fx.Revived);
            fx.Tick(); // 2/3
            Assert.Empty(fx.Revived);
            fx.Tick(); // 3/3：到点复活。
            Assert.Single(fx.Revived);
        }

        [Fact]
        public void RespawnPoint_CreatureDies_DoesNotEnqueueRespawn_AiDeathNotTriggered()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.RespawnPoint);

            fx.Died(CreatureId);
            fx.Tick();
            fx.Tick();

            Assert.Empty(fx.Revived);
            Assert.Empty(fx.RespawnedEvents);
        }

        [Fact]
        public void RespawnPoint_ResolveDefaultSpawnFails_RecordsErrorAndDoesNotThrow()
        {
            var options = new DeathPolicyOptions { ResolveDefaultSpawn = _ => null };
            var fx = Build(defaultPolicy: RespawnPolicy.RespawnPoint, options: options, wireResolve: false);

            var ex = Record.Exception(() =>
            {
                fx.Died(PlayerId);
                fx.Tick();
            });

            Assert.Null(ex);
            Assert.Empty(fx.Revived);
            Assert.NotEmpty(fx.Diagnostics.Errors);
        }

        // ==== reload_save =========================================================

        [Fact]
        public void ReloadSave_PlayerDies_LoadsAutosaveSlot()
        {
            // 先手工落一份"自动存档"：world_state_flags 段写入一个可观察的标志，验证死亡后
            // ReloadSave 策略确实调用了 ISaveSystem.Load(AutosaveSlotId)。
            var bus = DeathTestSupport.NewEventBus();
            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.sample")));
            var appState = new AppStateHost(bus, AppStateMachineConfig.Default());
            world.AddEntity(new PlayerUnit(PlayerId, MapId, FactionPlayer, ArchetypeSample));

            var worldState = new Core.Gameplay.WorldState.WorldState(bus);
            saveSystem.RegisterPersistable(worldState);
            var flagKey = new Id("world.sample_field.marker");
            worldState.Set(flagKey, Core.Foundation.Expr.ExprValue.OfBool(true), new Id("test.death"));
            Assert.True(saveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            // 存档之后再清空标志，模拟"死亡时世界状态已经与存档不同"——ReloadSave 应该把它读回来。
            worldState.Remove(flagKey, new Id("test.death"));
            Assert.False(worldState.Has(flagKey));

            var options = new DeathPolicyOptions();
            var host = new DeathPolicyHost(bus, world, saveSystem, appState, RespawnPolicy.ReloadSave, options);

            bus.PublishImmediate(new UnitDiedEvent(PlayerId, null, MapId));

            Assert.True(worldState.Has(flagKey));
            Assert.True(worldState.Get(flagKey).AsBool);
        }

        [Fact]
        public void ReloadSave_AutosaveSlotMissing_RecordsErrorAndDoesNotThrow()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.ReloadSave);

            var ex = Record.Exception(() => fx.Died(PlayerId));

            Assert.Null(ex);
            Assert.NotEmpty(fx.Diagnostics.Errors);
        }

        [Fact]
        public void ReloadSave_CreatureDies_DoesNotTriggerLoad()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.ReloadSave);

            var ex = Record.Exception(() => fx.Died(CreatureId));

            Assert.Null(ex);
            Assert.Empty(fx.Diagnostics.Errors); // 未触达 Load，不会因槽缺失而记错误。
        }

        /// <summary>
        /// C12 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
        /// <c>reload_save</c> 读档成功此前只调用 Load 本身，从未发布 <see cref="UnitRespawnedEvent"/>
        /// ——表现层的动画状态机没有任何信号能清理"死亡"这一终态锁，玩家会一直卡在死亡姿态。根治后：
        /// 成功分支恰好发布一次该事件，且是 <c>Enqueue</c>（不是 <c>PublishImmediate</c>）——必须等
        /// 当前这一批 <c>unit.died</c> 全部订阅方都处理完毕、下一次 <see cref="IEventBus.DispatchPending"/>
        /// 才真正可见，不能在 <see cref="Bus.PublishImmediate(IEvent)"/> 的同一次调用栈内同步发出
        /// （否则会被订阅顺序晚于 <see cref="DeathPolicyHost"/> 的下游处理覆盖，见下一条用例）。
        /// </summary>
        [Fact]
        public void ReloadSave_PlayerDies_LoadSucceeds_PublishesUnitRespawnedEvent_DeferredNotImmediate()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.ReloadSave);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            fx.Died(PlayerId);

            // 核心断言：不是同步立即可见——PublishImmediate(unit.died) 那次调用栈刚返回时，
            // 复活信号还只是排在队列里，尚未真正派发给订阅方。
            Assert.Empty(fx.RespawnedEvents);

            fx.Bus.DispatchPending();

            var respawned = Assert.Single(fx.RespawnedEvents);
            Assert.Equal(PlayerId, respawned.UnitId);
            Assert.Equal(RespawnPolicy.ReloadSave, respawned.Policy);
        }

        /// <summary>
        /// C12 根治的时序保证：用一个订阅顺序晚于 <see cref="DeathPolicyHost"/>（<see cref="Build"/>
        /// 构造 <c>fx.Host</c> 之后才追加订阅，同真实表现层动画状态机相对
        /// <c>core/gameplay/death</c> 的装配顺序）的最小"终态锁"模拟——收到 <c>unit.died</c> 就锁定，
        /// 收到 <c>unit.respawned</c> 才解锁——验证 <see cref="DeathPolicyHost"/> 补发的复活信号确实
        /// 晚于这个下游处理，不会被它事后覆盖（若改用 <c>PublishImmediate</c> 同步补发，这个用例会
        /// 失败：复活信号会在终态锁自己的 <c>unit.died</c> 处理器运行之前就已经发出，随后终态锁一
        /// 处理死亡又重新锁上，永远没有机会解锁）。
        /// </summary>
        [Fact]
        public void ReloadSave_PlayerDies_RespawnedEventArrivesAfterAllUnitDiedSubscribersProcessed_NotOverwrittenByLateDeathHandler()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.ReloadSave);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            var isDead = false;
            fx.Bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, _ => isDead = true);
            fx.Bus.Subscribe<UnitRespawnedEvent>(RulesEventKeys.UnitRespawned, _ => isDead = false);

            fx.Died(PlayerId);
            Assert.True(isDead); // 死亡处理已经在同一次同步派发里跑完。

            fx.Bus.DispatchPending(); // 复活信号在这里才真正派发——严格晚于上面的死亡处理。

            Assert.False(isDead); // 复活信号成功解除终态锁，没有被"迟到的死亡处理"覆盖。
        }

        // ==== permadeath ==========================================================

        [Fact]
        public void Permadeath_PlayerDies_DeletesCurrentSlot_AndRequestsMainMenu()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.Permadeath);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);
            Assert.True(fx.SaveSystem.SlotExists(AutosaveSlot));

            fx.Died(PlayerId);

            Assert.False(fx.SaveSystem.SlotExists(AutosaveSlot));
            Assert.Equal(AppState.MainMenu, fx.AppState.GetState());
        }

        [Fact]
        public void Permadeath_CurrentSlotIdProvider_OverridesAutosaveSlot()
        {
            var otherSlot = new Id("slot.character_a");
            var options = new DeathPolicyOptions { CurrentSlotIdProvider = () => otherSlot };
            var fx = Build(defaultPolicy: RespawnPolicy.Permadeath, options: options);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(otherSlot, "t1")).Success);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            fx.Died(PlayerId);

            Assert.False(fx.SaveSystem.SlotExists(otherSlot));
            Assert.True(fx.SaveSystem.SlotExists(AutosaveSlot)); // 未被误删。
        }

        [Fact]
        public void Permadeath_CreatureDies_DoesNotDeleteSlot_OrTransition()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.Permadeath);
            Assert.True(fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            fx.Died(CreatureId);

            Assert.True(fx.SaveSystem.SlotExists(AutosaveSlot));
            Assert.Equal(AppState.Boot, fx.AppState.GetState());
        }

        // ==== EffectivePolicy 覆盖 ================================================

        [Fact]
        public void EffectivePolicy_OptionsOverridesCombatDefault()
        {
            var options = new DeathPolicyOptions { Policy = RespawnPolicy.Permadeath };
            var fx = Build(defaultPolicy: RespawnPolicy.RespawnPoint, options: options);

            Assert.Equal(RespawnPolicy.Permadeath, fx.Host.EffectivePolicy);
        }

        [Fact]
        public void EffectivePolicy_FallsBackToCombatDefault_WhenOptionsPolicyNull()
        {
            var fx = Build(defaultPolicy: RespawnPolicy.ReloadSave);

            Assert.Equal(RespawnPolicy.ReloadSave, fx.Host.EffectivePolicy);
        }
    }
}
