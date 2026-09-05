using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;
using Xunit;
using AppStateEnum = Core.Foundation.AppLifecycle.AppState;
using TimeModelSwitchType = Core.Gameplay.Assembly.TimeModelSwitch;

namespace Tests.Gameplay.Discrete
{
    /// <summary>
    /// <see cref="TimeModelSwitchType"/>（03 第 3.3 节、ADR-0013 决策 5）的直接单元测试：不经完整
    /// 战斗流程，直接发布 <c>combat.entered</c>/<c>combat.left</c> 事件驱动切换，覆盖
    /// <see cref="DiscreteTickTests"/> 未覆盖的边角——组合层不含离散配置时不切换（向后兼容）、
    /// 全局计时器换算、<c>combat_mode_override</c> 覆盖。
    /// </summary>
    public sealed class TimeModelSwitchTests
    {
        private static readonly Id Hero = new Id("unit.tms_hero");

        private sealed class Harness
        {
            public IEventBus Bus = null!;
            public IDataRegistry Registry = null!;
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public StubSpatialQuery Spatial = null!;
            public SimClockHost Clock = null!;
            public IAppStateHost AppState = null!;
            public Core.Foundation.SimLoop.TurnScheduler Scheduler = null!;
            public TimeModelSwitchType Switch = null!;
        }

        private static Harness Build(string combatMode)
        {
            var definitions = new List<EventDefinition>(EventKeys.All.Length);
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var bus = new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false });

            var timeModelJson = @"
            {
                ""table"": ""found.time_model"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""found.time_model.tms_exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" },
                    { ""id"": ""found.time_model.tms_combat"", ""scope"": ""combat"", ""mode"": """ + combatMode + @"""" +
                        (combatMode == "discrete"
                            ? @", ""seconds_per_turn"": 6, ""initiative_policy"": ""fixed_order"", ""movement_budget_rule"": ""distance"""
                            : "") + @" }
                ]
            }";

            var source = new InMemoryDataSource().Add("found.time_model", timeModelJson);
            var registry = new DataRegistry(source, bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException("TimeModelSwitchTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var world = new WorldSim(bus);
            world.AddEntity(new PlayerUnit(Hero, new Id("map.tms"), new Id("fac.tms_player"), new Id("arch.class.tms")) { Position = new Vec2(0, 0) });

            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();
            spatial.Register(Hero, new Vec2(0, 0), 0.1);

            var config = AppStateMachineConfig.Default();
            var awaitingInput = config.AddCustomSubState("AwaitingInput");
            var playingBack = config.AddCustomSubState("PlayingBack");
            config.AllowSubTransition(SubStateId.Combat, awaitingInput);
            config.AllowSubTransition(awaitingInput, SubStateId.Combat);
            config.AllowSubTransition(SubStateId.Combat, playingBack);
            config.AllowSubTransition(playingBack, SubStateId.Combat);

            var appState = new AppStateHost(bus, config);
            appState.RequestTransition(AppStateEnum.MainMenu);
            appState.RequestTransition(AppStateEnum.Loading);
            appState.RequestTransition(AppStateEnum.InWorld);

            var clock = new SimClockHost(world);
            var scheduler = new Core.Foundation.SimLoop.TurnScheduler(world, id => 0, id => id.Equals(Hero), bus);

            var timeModelSwitch = new TimeModelSwitchType(scheduler, clock, appState, world, units, spatial, bus, registry);

            return new Harness
            {
                Bus = bus,
                Registry = registry,
                World = world,
                Units = units,
                Spatial = spatial,
                Clock = clock,
                AppState = appState,
                Scheduler = scheduler,
                Switch = timeModelSwitch,
            };
        }

        [Fact]
        public void CombatEntered_WithContinuousCombatModel_DoesNotSwitch()
        {
            var h = Build("continuous");

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            Assert.Equal(TimeModelMode.Continuous, h.Switch.CurrentMode);
            Assert.Equal(TimeModelMode.Continuous, h.Clock.Mode);
        }

        [Fact]
        public void CombatEntered_WithDiscreteCombatModel_SwitchesToDiscrete_AndBeginsCombat()
        {
            var h = Build("discrete");

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            Assert.Equal(TimeModelMode.Discrete, h.Switch.CurrentMode);
            Assert.Equal(TimeModelMode.Discrete, h.Clock.Mode);
            Assert.Contains(Hero, h.Scheduler.GetOrder());
        }

        [Fact]
        public void CombatEntered_PushesCombatSubState()
        {
            var h = Build("discrete");

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            Assert.Equal(SubStateId.Combat, h.AppState.CurrentSubState);
        }

        [Fact]
        public void SwitchToDiscrete_RescalesGlobalTimers_BySecondsPerTurnFactor()
        {
            var h = Build("discrete");
            var handle = h.World.Timers.Create(12.0); // 12 秒的剩余时长。

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            // seconds_per_turn = 6：12 秒 → 2 回合。
            Assert.Equal(2.0, h.World.Timers.Remaining(handle), 9);
        }

        [Fact]
        public void SwitchBackToContinuous_RescalesGlobalTimers_BackToSeconds()
        {
            var h = Build("discrete");
            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            var handle = h.World.Timers.Create(2.0); // 2 回合。
            h.Bus.PublishImmediate(new CombatLeftEvent(Hero));

            // seconds_per_turn = 6：2 回合 → 12 秒。
            Assert.Equal(12.0, h.World.Timers.Remaining(handle), 9);
            Assert.Equal(TimeModelMode.Continuous, h.Switch.CurrentMode);
        }

        [Fact]
        public void SetPendingOverride_Discrete_ForcesSwitchEvenWhenDefaultCombatModelIsContinuous()
        {
            var h = Build("continuous");

            // 08 combat_mode_override 语义：某次具体遭遇临时覆盖默认 combat_time_model 的判断
            // （见 TimeModelSwitch.SetPendingOverride 注释）——found.time_model.combat 本身仍声明
            // continuous，但本次遭遇要求按 discrete 打。
            h.Switch.SetPendingOverride("discrete");
            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));

            Assert.Equal(TimeModelMode.Discrete, h.Switch.CurrentMode);
        }

        [Fact]
        public void UnitDied_RemovesFromActiveCombatants_AndSwitchesBackWhenLast()
        {
            var h = Build("discrete");
            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero));
            Assert.Equal(TimeModelMode.Discrete, h.Switch.CurrentMode);

            h.Bus.PublishImmediate(new UnitDiedEvent(Hero, killerId: null));

            Assert.Equal(TimeModelMode.Continuous, h.Switch.CurrentMode);
        }

        // 收边任务补齐：combat.entered.hostileId 优先于半径 + 阵营近似（见 TimeModelSwitch.
        // ResolveParticipants 判断记录"参与者解析"）——远在默认搜索半径（30.0）之外的对手，此前
        // 版本的半径查询完全找不到它，现在凭事件携带的精确 hostileId 仍能正确纳入参与者集合。
        [Fact]
        public void CombatEntered_WithHostileIdFarOutsideSearchRadius_StillIncludesHostileInTurnOrder()
        {
            var h = Build("discrete");
            var farHostile = new Id("unit.tms_far_hostile");
            h.World.AddEntity(new PlayerUnit(farHostile, new Id("map.tms"), new Id("fac.tms_enemy"), new Id("arch.class.tms"))
            {
                Position = new Vec2(1000, 0), // 远超默认 ParticipantSearchRadius = 30.0。
            });
            h.Spatial.Register(farHostile, new Vec2(1000, 0), 0.1);

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero, farHostile));

            Assert.Equal(TimeModelMode.Discrete, h.Switch.CurrentMode);
            Assert.Contains(Hero, h.Scheduler.GetOrder());
            Assert.Contains(farHostile, h.Scheduler.GetOrder());
        }

        // hostileId 指向一个不存在的单位（如已被销毁）时被安全忽略，不影响正常切换。
        [Fact]
        public void CombatEntered_WithHostileIdForNonExistentUnit_IgnoresItSafely()
        {
            var h = Build("discrete");
            var ghost = new Id("unit.tms_ghost");

            h.Bus.PublishImmediate(new CombatEnteredEvent(Hero, ghost));

            Assert.Equal(TimeModelMode.Discrete, h.Switch.CurrentMode);
            Assert.Contains(Hero, h.Scheduler.GetOrder());
            Assert.DoesNotContain(ghost, h.Scheduler.GetOrder());
        }
    }
}
