using System;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Death;
using Core.Numbers.Progression;
using Core.Rules.Common;
using Tests.Gameplay.ProgressionBridge;
using Xunit;

namespace Tests.Gameplay.Death
{
    /// <summary>
    /// T-N4-5（ADR-0033 决策 6"从不扣经验"；06 第 2.5 节"从不扣经验：无论 13 的死亡策略选哪种，
    /// 经验不减"）：三种死亡复活策略（<see cref="RespawnPolicy"/> 枚举，respawn_point/reload_save/
    /// permadeath）各一组回归——死亡（及各自对应的复活/读档/删档结算）前后，一个与
    /// <see cref="DeathPolicyHost"/> 完全独立装配的 <see cref="ProgressionHost"/> 对同一玩家单位的
    /// <see cref="ProgressionHost.GetXp"/>/<see cref="ProgressionHost.GetLevel"/> 必须逐字节不变。
    /// <para>
    /// 判断记录：<see cref="DeathPolicyHost"/> 按本模块 README"依赖"一节只依赖 L0（event_bus/
    /// app_lifecycle/save_system/sim_loop）与 L2 <c>core/rules/common</c>，从未持有、也不引用
    /// <see cref="IProgressionHost"/>/<see cref="ProgressionHost"/> 任何类型——三种策略的结算逻辑
    /// （延迟复活、读档、删当前槽）本身就不触达经验/等级状态，这组用例把"死亡不扣经验"这条 ADR
    /// 决策落成一条可执行的回归锁：把两个宿主分别独立装配在同一个玩家单位 id 上，走完整套死亡
    /// 结算后断言 Progression 状态原样不动，防止未来有人误在死亡路径上"顺手"接一条扣经验/降级的
    /// 分支。
    /// </para>
    /// </summary>
    public sealed class T_N4_5_RespawnPolicyDoesNotAffectXpTests
    {
        private static readonly Id PlayerId = new Id("unit.n4_5_player");
        private static readonly Id MapId = new Id("world.n4_5_field");
        private static readonly Id FactionPlayer = new Id("faction.player");
        private static readonly Id ArchetypeSample = new Id("arch.class.sample");
        private static readonly Id AutosaveSlot = new Id("slot.autosave");
        private static readonly Id CurveId = new Id("prog.curve.pb");
        private static readonly Id KillSourceId = new Id("prog.xp_source.kill");

        // -----------------------------------------------------------------
        // Progression 侧夹具：与死亡侧完全独立装配（不同的 IEventBus/IDataRegistryView 实例），
        // 只共享同一个玩家单位 id——用来验证两个模块之间没有任何隐藏耦合。
        // -----------------------------------------------------------------

        private static ProgressionHost BuildProgression(int startLevel, long grantedXp)
        {
            var bus = ProgressionBridgeTestSupport.NewEventBus();
            const string xpSourceRows =
                "[{\"id\":\"prog.xp_source.kill\",\"kind\":\"kill\",\"base_xp\":0," +
                "\"base_curve_ref\":\"prog.xp_base_curve.pb\"}]";
            var registry = ProgressionBridgeTestSupport.MakeProgressionRegistry(bus, xpSourceRows);
            var progression = ProgressionBridgeTestSupport.MakeProgressionHost(registry, bus);
            progression.RegisterUnit(PlayerId, CurveId, startLevel);
            if (grantedXp > 0)
            {
                progression.AddXp(PlayerId, KillSourceId, grantedXp);
            }

            return progression;
        }

        // -----------------------------------------------------------------
        // 死亡侧夹具：惯例同 DeathPolicyHostTests.Build，本组用例只需要最小子集。
        // -----------------------------------------------------------------

        private sealed class DeathFixture
        {
            public IEventBus Bus = default!;
            public WorldSim World = default!;
            public Core.Foundation.SaveSystem.SaveSystem SaveSystem = default!;
            public DeathPolicyHost Host = default!;

            public void Died() => Bus.PublishImmediate(new UnitDiedEvent(PlayerId, null, MapId));

            public void Tick() => Host.Execute(SimStep.Continuous(0), World);
        }

        private static DeathFixture BuildDeath(RespawnPolicy policy)
        {
            var bus = DeathTestSupport.NewEventBus();
            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.n4_5")));
            var appState = new AppStateHost(bus, AppStateMachineConfig.Default());

            world.AddEntity(new PlayerUnit(PlayerId, MapId, FactionPlayer, ArchetypeSample) { Position = new Vec2(0, 0) });

            var options = new DeathPolicyOptions
            {
                ReviveUnit = (unitId, position, healthFraction) => { },
                ResolveDefaultSpawn = mapId => (mapId, new Vec2(5, 6)),
            };

            var host = new DeathPolicyHost(bus, world, saveSystem, appState, policy, options);
            return new DeathFixture { Bus = bus, World = world, SaveSystem = saveSystem, Host = host };
        }

        [Fact]
        public void RespawnPoint_PlayerDiesAndRespawns_XpAndLevelUnchanged()
        {
            var progression = BuildProgression(startLevel: 3, grantedXp: 250);
            var beforeXp = progression.GetXp(PlayerId);
            var beforeLevel = progression.GetLevel(PlayerId);

            var death = BuildDeath(RespawnPolicy.RespawnPoint);
            death.Died();
            death.Tick(); // 延迟复活队列到期，真正复活（见 DeathPolicyHost 判断记录 2）。

            Assert.Equal(beforeXp, progression.GetXp(PlayerId));
            Assert.Equal(beforeLevel, progression.GetLevel(PlayerId));
        }

        [Fact]
        public void ReloadSave_PlayerDiesAndReloads_XpAndLevelUnchanged()
        {
            var progression = BuildProgression(startLevel: 5, grantedXp: 0);
            var beforeXp = progression.GetXp(PlayerId);
            var beforeLevel = progression.GetLevel(PlayerId);

            var death = BuildDeath(RespawnPolicy.ReloadSave);
            Assert.True(death.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            death.Died();

            Assert.Equal(beforeXp, progression.GetXp(PlayerId));
            Assert.Equal(beforeLevel, progression.GetLevel(PlayerId));
        }

        [Fact]
        public void Permadeath_PlayerDies_XpAndLevelUnchanged()
        {
            var progression = BuildProgression(startLevel: 1, grantedXp: 50);
            var beforeXp = progression.GetXp(PlayerId);
            var beforeLevel = progression.GetLevel(PlayerId);

            var death = BuildDeath(RespawnPolicy.Permadeath);
            Assert.True(death.SaveSystem.Save(new SaveRequest(AutosaveSlot, "t1")).Success);

            death.Died();

            Assert.Equal(beforeXp, progression.GetXp(PlayerId));
            Assert.Equal(beforeLevel, progression.GetLevel(PlayerId));
        }
    }
}
