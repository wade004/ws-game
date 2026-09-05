using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.EndToEnd
{
    /// <summary>
    /// 收边任务补齐（缺口 (a)：<c>encounter.def.combat_mode_override</c> 此前只落地读取，
    /// <c>TimeModelSwitch.SetPendingOverride</c> 从无调用点）：经真实 <see cref="GameWorldFixture"/>
    /// （<see cref="Core.Gameplay.Assembly.GameplayAssembly"/> 全量装配，非本模块自建的裁剪夹具）
    /// 验证 <c>encounter.started</c>/<c>encounter.won</c> 两端接线——见 <c>GameplayAssembly</c> 构造函数
    /// 第 12.5 步判断记录、<c>EncounterHost.TryGetModeOverride</c>、<c>TimeModelSwitch.
    /// PendingCombatModeOverride</c>。<c>data/_sample/found/found.time_model.json</c> 的
    /// <c>combat</c> 作用域默认声明 <c>continuous</c>（08 判断记录里"反向"——覆盖为 continuous 时
    /// 强制维持连续、覆盖为 discrete 时强制切到离散——的另一半在更贴近机制本身的
    /// <c>TimeModelSwitchTests.SetPendingOverride_Continuous_ForcesStayContinuousEvenWhenDefaultCombatModelIsDiscrete</c>
    /// 覆盖，见该处判断记录：全量装配 <see cref="GameWorldFixture"/> 的示例数据集只声明了
    /// <c>combat=continuous</c> 一种默认，要覆盖出"默认已是 discrete"的反向场景需要另一套数据集，
    /// 复用 <c>TimeModelSwitchTests</c> 已有的自建夹具比新增第二套端到端夹具更聚焦）。
    /// </summary>
    public sealed class EncounterModeOverrideTests
    {
        [Fact]
        public void EncounterStarted_WithCombatModeOverrideDiscrete_ForcesDiscreteMode_DespiteSampleDataDefaultContinuous()
        {
            var fx = GameWorldFixture.Build(enableDiscreteTimeModel: true);
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            Assert.NotNull(fx.Gameplay.TimeModelSwitch);
            Assert.Equal(TimeModelMode.Continuous, fx.Gameplay.TimeModelSwitch!.CurrentMode);

            fx.Gameplay.Encounter.Start(
                GameWorldFixture.EncounterBeastFightModeOverrideDiscrete, GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnEncounterAmbusher);
            Assert.NotNull(record);
            Assert.True(record!.EntityId.HasValue);

            // 判断记录（夹具层面，非生产代码缺口）：Encounter.Start 在任何 Tick 之外直接调用，新生成
            // 参战单位的 entity.created 仍在事件队列里未派发——EntitySpatialSyncHost 要等它派发才会
            // 把新实体计入空间索引。若不在这里手动 DispatchPending 一次就直接进入下面的施法循环，
            // 循环第一个 Tick 的 TriggerEvaluation 阶段（EncounterTickHandler）会在空间索引还没追上
            // 的这一瞬间对 victory_condition（enemies.count_in_range(50) == 0）求值，把"敌人还没被
            // 空间索引看见"误判成"敌人已清空"，遭遇被判定为提前胜利，连累 TimeModelSwitch 的 pending
            // 覆盖被 encounter.won 处理器过早清空——这不是本任务要修的生产代码缺口（EncounterHost.
            // Evaluate 的求值时机、EntitySpatialSyncHost 的同步时机都不在收边任务待补清单内），这里
            // 用一次显式 DispatchPending 把 Start() 期间产生的事件（含 entity.created）先落地，让
            // 空间索引与真实世界状态一致后再开始真正的战斗循环。
            fx.Bus.DispatchPending();

            Assert.Equal(true, fx.Gameplay.TimeModelSwitch.PendingCombatModeOverride);

            // 判断记录：不能只在循环结束后检查 TimeModelSwitch.CurrentMode——ambusher 的 HP 可能在
            // 一次命中内就被打死，"进入离散→(unit.died 移除最后一名活跃参战者)→立刻切回连续"整个
            // 往返会在同一个 Tick 内的同一批事件派发中完成（见 TimeModelSwitch.OnUnitDied/
            // SwitchToContinuousIfNoneActive），循环再检查时只会看到已经切回的 Continuous，观测不到
            // 期间确实短暂进入过 Discrete。sim.turn_started 只由 TurnScheduler.BeginCombat/
            // AdvanceToNextActor 发布（见 SimEventKeys 判断记录"四个属于离散模式，由 TurnScheduler
            // 发出"），订阅它作为"确实进入过离散模式"的可靠证据，不依赖循环结束时的瞬时状态。
            var sawDiscreteTurnStarted = false;
            fx.Bus.Subscribe<SimTurnStartedEvent>(SimEventKeys.TurnStarted, _ => sawDiscreteTurnStarted = true);

            // encounter.started 已经把 pending 覆盖落定，但真正的模式切换要等第一次 combat.entered
            // （见 TimeModelSwitch.OnCombatEntered）——反复施法直到命中触发进战，或到达尝试上限。
            for (var i = 0; i < 30 && !sawDiscreteTurnStarted; i++)
            {
                fx.SubmitCast(GameWorldFixture.SkillStrike);
                fx.Tick();
            }

            Assert.True(sawDiscreteTurnStarted, "combat_mode_override: discrete 应至少触发一次 TurnScheduler.BeginCombat（sim.turn_started）");
        }

        [Fact]
        public void EncounterWon_ClearsPendingOverride_DoesNotLeakIntoLaterUnrelatedCombat()
        {
            var fx = GameWorldFixture.Build(enableDiscreteTimeModel: true);
            fx.Gameplay.EnterMap(GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            var instanceId = fx.Gameplay.Encounter.Start(
                GameWorldFixture.EncounterBeastFightModeOverrideDiscrete, GameWorldFixture.MapId, GameWorldFixture.PlayerId);

            // 遭遇已声明覆盖，但本用例不提交任何施法意图——真实战斗从未开始，pending 覆盖处于
            // "已设置、未消费"状态（覆盖 08 判断记录"遭遇结束清除"要防的正是这种情形：一次没有
            // 真正引发战斗的遭遇，其覆盖不应残留到下一次不相关的战斗）。
            Assert.Equal(true, fx.Gameplay.TimeModelSwitch!.PendingCombatModeOverride);

            fx.Bus.PublishImmediate(new EncounterWonEvent(instanceId));
            Assert.Null(fx.Gameplay.TimeModelSwitch.PendingCombatModeOverride);

            var record = fx.Gameplay.Spawn.GetSpawnRecord(GameWorldFixture.SpawnEncounterAmbusher);
            Assert.NotNull(record);
            var beastId = record!.EntityId!.Value;

            // 之后对同一只（已生成、仍存活的）野兽发起一场"不相关"的真实战斗：pending 覆盖已清空，
            // 示例数据默认 combat=continuous，不应被已结束遭遇残留的覆盖影响而误切到离散模式。
            for (var i = 0; i < 30
                && fx.Gameplay.Carriers.Units.Exists(beastId) && fx.Gameplay.Carriers.Units.IsAlive(beastId); i++)
            {
                fx.SubmitCast(GameWorldFixture.SkillStrike);
                fx.Tick();
            }

            Assert.Equal(TimeModelMode.Continuous, fx.Gameplay.TimeModelSwitch.CurrentMode);
        }
    }
}
