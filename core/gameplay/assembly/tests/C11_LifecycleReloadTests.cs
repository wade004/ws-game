using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Death;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// C11 根治回归（architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md，
    /// 消费方反馈 <c>M-C11_进出战斗死亡复活通用能力反馈.md</c>，基线 1.22.0）：脱离引擎，用真实
    /// <see cref="GameplayAssembly"/>/<see cref="SaveSystem"/>/<see cref="WorldSim"/>/
    /// <see cref="WorldUnitAccess"/>/<see cref="StubSpatialQuery"/> 复现并回归三项修复：
    /// <list type="number">
    /// <item>同图读档未同步空间索引（<c>C11-RELOAD</c>）。</item>
    /// <item>CombatHost 与 PowerHost 战斗态不一致（<c>C11-RELOAD</c>）。</item>
    /// <item>延迟复活 pending 未在读档/实体销毁/ClearAll/Dispose 时失效（<c>C11-PENDING-LOAD</c>/
    /// <c>C11-CLEANUP</c>）。</item>
    /// </list>
    /// 复用 <see cref="GameplayAssemblyDeathReloadTests"/> 同一套脱离引擎的装配惯例（本文件不重复
    /// 抄录该文件的判断记录，只在有差异处另行注释）。
    /// </summary>
    public sealed class C11_LifecycleReloadTests
    {
        private static readonly Id PlayerId = new Id("unit.c11_lifecycle_player");
        private static readonly Id PlayerFactionId = new Id("fac.c11_lifecycle_player");
        private static readonly Id HostileFactionId = new Id("fac.c11_lifecycle_hostile");
        private static readonly Id ArchetypeSample = new Id("arch.class.c11_lifecycle_sample");
        private static readonly Id MapA = new Id("world.c11_lifecycle_map_a");
        private static readonly Id AutosaveSlot = new Id("slot.c11_lifecycle_autosave");

        private const string StatDefinitionRows =
            "[{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.max_health.name\", \"group\": \"primary\", \"default_base\": 100}]";

        private const string PowerTypeRows =
            "[{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.health.name\", " +
            "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}, \"start_full\": true}]";

        private const string LevelCurveRows =
            "[{\"id\": \"prog.level_curve.c11_lifecycle_sample\", \"max_level\": 1, " +
            "\"entries\": [{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}]}]";

        private const string ArchClassRows =
            "[{\"id\": \"" + "arch.class.c11_lifecycle_sample" + "\", \"name_key\": \"l10n.arch.class.c11_lifecycle_sample.name\", " +
            "\"primary_stat\": \"stat.max_health\", \"base_stats\": {}, " +
            "\"power_types\": [\"arch.power.health\"], " +
            "\"level_curve_ref\": \"prog.level_curve.c11_lifecycle_sample\"}]";

        private static string WorldMapRow(Id mapId, string sceneRef, string navRef) =>
            "{\"id\": \"" + mapId.Value + "\", \"scene_ref\": \"" + sceneRef + "\", \"nav_ref\": \"" + navRef + "\", " +
            "\"spawn_points\": [{\"id\": \"spawn." + mapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public StubSpatialQuery Spatial = null!;
            public StubFileSystem Fs = null!;
            public SaveSystem SaveSystem = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;

            /// <summary>同 <see cref="GameplayAssemblyDeathReloadTests.Fixture.KillPlayer"/>：模拟
            /// 死亡结算那一刻的最小可观察后果（砍血到 0、Alive=false、发布 UnitDiedEvent），不依赖
            /// core/rules/combat 的完整命中判定。</summary>
            public void KillPlayer(Id mapId)
            {
                var current = Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health);
                if (current > 0)
                {
                    Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -current, PlayerId);
                }
                Gameplay.Carriers.Units.SetAlive(PlayerId, false);
                Bus.PublishImmediate(new UnitDiedEvent(PlayerId, null, mapId, Gameplay.Carriers.Units.GetPosition(PlayerId)));
            }
        }

        private static Fixture Build(RespawnPolicy policy = RespawnPolicy.RespawnPoint, int respawnDelayTicks = 1000)
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", PowerTypeRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", LevelCurveRows))
                .Add("arch.class", Envelope("arch.class", ArchClassRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("item.budget_curve", Envelope("item.budget_curve",
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);

            registry.RegisterSchema(Core.Foundation.SceneRouter.WorldMapSchema.Table);
            source.Add("world.map", Envelope("world.map", "[" + WorldMapRow(MapA, "scene.c11_lifecycle_a", "nav.c11_lifecycle_a") + "]"));

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var fs = new StubFileSystem();
            var saveSystem = new SaveSystem(fs, new SaveSystemOptions(new Id("game.c11_lifecycle_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId,
                deathPolicyOptions: new DeathPolicyOptions
                {
                    Policy = policy,
                    AutosaveSlotId = AutosaveSlot,
                    RespawnDelayTicks = respawnDelayTicks,
                });

            var player = new PlayerUnit(PlayerId, MapA, PlayerFactionId, ArchetypeSample) { Position = new Vec2(5, 5) };
            world.AddEntity(player);
            bus.DispatchPending(); // entity.created 落地：EntitySpatialSyncHost 把玩家登记进空间索引。

            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ArchetypeSample, raceId: null, level: 1);
            gameplay.RegisterPersistables(saveSystem, player);

            return new Fixture
            {
                Bus = bus,
                World = world,
                Spatial = spatial,
                Fs = fs,
                SaveSystem = saveSystem,
                Gameplay = gameplay,
                Player = player,
            };
        }

        /// <summary>存 (5,5)/HP 37 → 移动到 (9,9)（经 IUnitAccess.SetPosition，正常同步空间索引）→
        /// 致死 → 同图 <see cref="GameplayAssembly.RestoreFromSlot"/>（模拟玩家从菜单手工"读取存档"，
        /// 不经 <c>DeathPolicyHost</c> 的 <c>reload_save</c> 策略）——四条断言共用的公共前置状态，
        /// 各 Fact 只关心其中一项，故拆成独立方法而非一个巨大 Fact，便于单独定位失败。</summary>
        private static Fixture SaveMoveKillAndReload(RespawnPolicy policy = RespawnPolicy.RespawnPoint, int respawnDelayTicks = 1000)
        {
            var fx = Build(policy, respawnDelayTicks);

            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(PlayerId, WellKnownPowers.Health, -63, PlayerId); // 100 -> 37
            Assert.Equal(37.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
            Assert.Equal(new Vec2(5, 5), fx.Player.Position);
            Assert.False(fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId));
            Assert.False(fx.Gameplay.Carriers.Rules.Powers.IsInCombat(PlayerId));

            var saveResult = fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1"));
            Assert.True(saveResult.Success, saveResult.Message);

            fx.Gameplay.Carriers.Units.SetPosition(PlayerId, new Vec2(9, 9));
            Assert.Equal(new Vec2(9, 9), fx.Player.Position);
            Assert.Single(fx.Spatial.QueryRadius(new Vec2(9, 9), 0.05, QueryFilter.None));

            // 移动并"进战"（同消费方反馈第 2 项复现路径"移动并致死"隐含的战斗过程）：真实命中链路
            // 会在造成/受到伤害时调用 NotifyCombatEvent，本文件用最小可观察后果直接调用同一入口。
            fx.Gameplay.Carriers.Rules.Combat.NotifyCombatEvent(PlayerId);
            fx.Bus.DispatchPending();
            Assert.True(fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId));
            Assert.True(fx.Gameplay.Carriers.Rules.Powers.IsInCombat(PlayerId));

            fx.KillPlayer(MapA);
            fx.Bus.DispatchPending();

            var loadResult = fx.Gameplay.RestoreFromSlot(AutosaveSlot);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);

            return fx;
        }

        // -----------------------------------------------------------------
        // 1) 同图读档未同步空间索引。
        // -----------------------------------------------------------------

        [Fact]
        public void SameMapLoad_RestoresSpatialIndexToSavedPosition()
        {
            var fx = SaveMoveKillAndReload();

            // 逻辑位置本身在修复前已经正确（10 号文档段恢复没有问题），断言仅作基线。
            Assert.Equal(new Vec2(5, 5), fx.Player.Position);

            // 根治前：空间索引仍登记在移动后的 (9,9)（UnitPersistable.CurrentPositionPersistable.Load
            // 直接写 _player.Position，绕开 IUnitAccess.SetPosition，未调用 ISpatialQuery.
            // UpdatePosition），此处查询 (5,5) 应为空集合（真实探针复现："spatial_index_count=0"）。
            var atSavedPosition = fx.Spatial.QueryRadius(new Vec2(5, 5), 0.05, QueryFilter.None);
            Assert.Single(atSavedPosition);
            Assert.Equal(PlayerId, atSavedPosition[0]);

            // 旧位置不应再查到玩家（未同步的旧登记会一直留在 (9,9)）。
            var atOldPosition = fx.Spatial.QueryRadius(new Vec2(9, 9), 0.05, QueryFilter.None);
            Assert.Empty(atOldPosition);
        }

        // -----------------------------------------------------------------
        // 2) CombatHost 与 PowerHost 战斗态不一致。
        // -----------------------------------------------------------------

        [Fact]
        public void SameMapLoad_CombatAndPowerHostAgreeOutOfCombat_AndStayConsistentNextTick()
        {
            var fx = SaveMoveKillAndReload();

            // 存档时刻两者均为 false（见 SaveMoveKillAndReload 断言）；根治前：CombatHost 仍残留
            // "死亡前一刻进战"的运行期状态（true），PlayerVitalsPersistable.Load 只改了 PowerHost
            // （false）——两者分歧。
            Assert.False(fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId), "CombatHost 应恢复为存档时刻的非战斗状态");
            Assert.False(fx.Gameplay.Carriers.Rules.Powers.IsInCombat(PlayerId), "PowerHost 应恢复为存档时刻的非战斗状态");
            Assert.Equal(
                fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId),
                fx.Gameplay.Carriers.Rules.Powers.IsInCombat(PlayerId));

            // 下一 tick：脱战延迟到期也不应产生分歧或重新进战（威胁表已在 BeforeLoad 清空）。
            fx.World.Tick(SimStep.Continuous(0.1));
            Assert.Equal(
                fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId),
                fx.Gameplay.Carriers.Rules.Powers.IsInCombat(PlayerId));
            Assert.False(fx.Gameplay.Carriers.Rules.Combat.IsInCombat(PlayerId));
        }

        // -----------------------------------------------------------------
        // 3A) 延迟复活 pending 未因读档失效：读档后下一 tick 不应被旧的延迟复活覆盖。
        // -----------------------------------------------------------------

        [Fact]
        public void SameMapLoad_NextTick_PositionAndHealthStaySavedState_NotOverriddenByStalePendingRespawn()
        {
            // RespawnDelayTicks=2：死亡时入队一条 2 tick 后执行的延迟复活（respawn_point 默认复活点
            // 是 (0,0)/满血，与存档 (5,5)/HP 37 明显不同，能清楚区分"是否被旧 pending 覆盖"）。
            var fx = SaveMoveKillAndReload(RespawnPolicy.RespawnPoint, respawnDelayTicks: 2);

            // 根治前：DeathPolicyHost._pending 里仍有一条死亡时入队、指向本单位的记录，读档不会清空
            // 它——推进到原定的复活 tick，会把刚读档恢复好的 (5,5)/HP 37 覆盖成默认复活点 (0,0)/满血。
            fx.World.Tick(SimStep.Continuous(0.1));
            fx.World.Tick(SimStep.Continuous(0.1));
            fx.Bus.DispatchPending();

            Assert.True(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));
            Assert.Equal(new Vec2(5, 5), fx.Player.Position);
            Assert.Equal(37.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }

        // -----------------------------------------------------------------
        // 3B) WorldSim.ClearAll 后下一 tick 不应因残留 pending 抛异常。
        // -----------------------------------------------------------------

        [Fact]
        public void ClearAll_NextTick_DoesNotThrow_EvenWithPendingRespawnForDestroyedUnit()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, respawnDelayTicks: 2);

            fx.KillPlayer(MapA); // 入队一条 2 tick 后执行的延迟复活。
            fx.Bus.DispatchPending();

            fx.World.Tick(SimStep.Continuous(0.1)); // TicksRemaining: 2 -> 1，尚未到期。

            // ClearAll 同步移除全部实体（含玩家），只把 entity.destroyed 排入待处理队列，不立即派发
            // （见 IWorldSim.ClearAll 判断记录）——本方法调用返回时，DeathPolicyHost 的
            // entity.destroyed 订阅（C11-CLEANUP 新增）尚未来得及摘除这条 pending 记录。
            fx.World.ClearAll();
            Assert.Null(fx.World.GetEntity(PlayerId));

            // 根治前：本次 Tick 的 TriggerEvaluation 阶段执行 DeathPolicyHost.Execute 时倒计时到 0，
            // 直接调用 ReviveUnit -> WorldUnitAccess.Revive -> IWorldSim.GetEntity 找不到单位，抛
            // InvalidOperationException（真实探针复现，见消费方反馈第 3 项 B 条）。
            var exception = Record.Exception(() => fx.World.Tick(SimStep.Continuous(0.1)));
            Assert.Null(exception);
        }

        // -----------------------------------------------------------------
        // 补充回归：pending 在实体销毁事件真正派发之后也会失效（不依赖 ClearAll 的时序窗口）。
        // -----------------------------------------------------------------

        [Fact]
        public void EntityDestroyedEvent_RemovesMatchingPendingRespawn()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, respawnDelayTicks: 5);

            fx.KillPlayer(MapA);
            fx.Bus.DispatchPending(); // UnitDiedEvent 落地 -> EnqueueRespawn，pending 里有一条记录。

            // 单位以"标记待销毁 + 正常推进一次 tick"的方式销毁（非 ClearAll）：WorldSim.Tick 会在
            // 阶段 8（生命周期清理）真正移除实体、Enqueue 一条 entity.destroyed，并在方法末尾自行
            // DispatchPending 一次（见 WorldSim.Tick 判断记录），本次 Tick 返回时
            // OnEntityDestroyedForPending 应该已经摘除了对应 pending 记录。
            fx.World.MarkForDestruction(PlayerId);
            fx.World.Tick(SimStep.Continuous(0.1));
            Assert.Null(fx.World.GetEntity(PlayerId));

            // 继续推进到原定复活 tick：pending 已被摘除，不应再调用 ReviveUnit（也没有任何存在的
            // 单位可供复活），不抛异常。
            for (var i = 0; i < 5; i++)
            {
                fx.World.Tick(SimStep.Continuous(0.1));
            }

            Assert.Null(fx.World.GetEntity(PlayerId));
        }

        // -----------------------------------------------------------------
        // 补充回归：DeathPolicyHost.Dispose 清空 pending 且取消订阅（幂等）。
        // -----------------------------------------------------------------

        [Fact]
        public void Dispose_ClearsPendingRespawns_AndIsIdempotent()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, respawnDelayTicks: 5);

            fx.KillPlayer(MapA);
            fx.Bus.DispatchPending();

            fx.Gameplay.Death.Dispose();
            fx.Gameplay.Death.Dispose(); // 幂等：不应抛异常。

            fx.Gameplay.Carriers.Units.SetAlive(PlayerId, false); // Dispose 后手工把玩家标为死亡。
            for (var i = 0; i < 5; i++)
            {
                fx.World.Tick(SimStep.Continuous(0.1));
            }
            // Dispose 已清空 pending 且取消订阅：不会有任何复活发生，玩家仍是死亡状态。
            Assert.False(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));
        }

        // -----------------------------------------------------------------
        // 补充回归：存档时刻已处于死亡未复活状态——读档不应凭空复活玩家（无 pending 持久化时的语义，
        // 见 DeathPolicyHost.ClearPending 判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void SavedWhileDead_SameMapLoad_PlayerStaysDeadAtSavedHealth()
        {
            var fx = Build(RespawnPolicy.RespawnPoint, respawnDelayTicks: 1000);

            fx.KillPlayer(MapA); // 存档前先死亡（未复活）。
            fx.Bus.DispatchPending();
            Assert.False(fx.Gameplay.Carriers.Units.IsAlive(PlayerId));
            Assert.Equal(0.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));

            var saveResult = fx.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 存档之后玩家实际上通过既有的延迟复活队列复活了（respawn_point 兜底路径不受影响）。
            fx.World.Tick(SimStep.Continuous(0.1)); // 无论 delay 多大，本用例不推进到复活那一刻。

            var loadResult = fx.Gameplay.RestoreFromSlot(AutosaveSlot);
            Assert.Equal(LoadStatus.Loaded, loadResult.Status);

            Assert.False(fx.Gameplay.Carriers.Units.IsAlive(PlayerId), "存档时已死亡，读档后应保持死亡（按存档 HP 状态，不会凭空复活）");
            Assert.Equal(0.0, fx.Gameplay.Carriers.Rules.Powers.GetPower(PlayerId, WellKnownPowers.Health));
        }
    }
}
