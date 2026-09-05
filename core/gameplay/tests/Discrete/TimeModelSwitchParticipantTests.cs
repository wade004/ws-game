using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Discrete
{
    /// <summary>
    /// ADR-0013 补齐任务：<see cref="Core.Gameplay.Assembly.TimeModelSwitch"/> 在离散模式中途收到
    /// <c>combat.entered</c>/<c>unit.died</c> 时，实时把参与者同步进
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler"/> 当前轮的行动顺序（见该类型判断记录
    /// "中途加入/离场"），不再是"只在 0→1 那一刻一次性传入整份名单，此后新进战/死亡单位不影响
    /// 行动顺序"。复用 <see cref="DiscreteFightWorldBuilder"/> 现成的两单位战斗夹具，额外手工引入
    /// 第三个单位模拟"战斗已经打响后又有人加入/死亡"。
    /// </summary>
    public sealed class TimeModelSwitchParticipantTests
    {
        private static readonly Id ThirdUnitId = new Id("unit.d_third");

        /// <summary>把战斗推进到离散模式，并额外注册一个第三方生物单位（不接 AI，纯粹作为
        /// "中途加入/离场"的测试对象，不参与实际行动，同阵营为 <see cref="DiscreteFightWorldBuilder.FactionWildlife"/>
        /// ——与玩家互为敌对，满足 <see cref="Core.Gameplay.Assembly.TimeModelSwitch"/> 参与者解析
        /// 的阵营过滤条件）。</summary>
        private static DiscreteFightWorldBuilder.Fixture BuildInDiscreteModeWithThirdUnit()
        {
            var fx = DiscreteFightWorldBuilder.Build();

            for (var i = 0; i < 20 && fx.TimeModelSwitch.CurrentMode == TimeModelMode.Continuous; i++)
            {
                fx.ContinuousTickWithPlayerCast(DiscreteFightWorldBuilder.SkillStrike);
            }

            Assert.Equal(TimeModelMode.Discrete, fx.TimeModelSwitch.CurrentMode);

            var third = new Core.Carriers.Unit.CreatureUnit(
                ThirdUnitId, DiscreteFightWorldBuilder.MapId, DiscreteFightWorldBuilder.FactionWildlife,
                DiscreteFightWorldBuilder.ClassSample)
            { Position = new Vec2(1.5, 0) };
            fx.World.AddEntity(third);
            fx.Rules.RegisterUnit(ThirdUnitId, DiscreteFightWorldBuilder.ClassSample, raceId: null, level: 1);
            fx.Spatial.Register(ThirdUnitId, new Vec2(1.5, 0), 0.1);

            return fx;
        }

        [Fact]
        public void CombatEntered_MidDiscreteFight_AddsToCurrentTurnOrder()
        {
            var fx = BuildInDiscreteModeWithThirdUnit();
            Assert.DoesNotContain(ThirdUnitId, fx.Scheduler.GetOrder());

            fx.Rules.Combat.NotifyCombatEvent(ThirdUnitId);
            fx.Bus.DispatchPending();

            Assert.Contains(ThirdUnitId, fx.Scheduler.GetOrder());
            // 仍处于同一场离散战斗，不是重新 BeginCombat（原有两名参与者仍在，顺序未被打乱重来）。
            Assert.Contains(DiscreteFightWorldBuilder.PlayerId, fx.Scheduler.GetOrder());
            Assert.Contains(DiscreteFightWorldBuilder.NpcId, fx.Scheduler.GetOrder());
        }

        [Fact]
        public void UnitDied_MidDiscreteFight_RemovesFromCurrentTurnOrder()
        {
            var fx = BuildInDiscreteModeWithThirdUnit();
            fx.Rules.Combat.NotifyCombatEvent(ThirdUnitId);
            fx.Bus.DispatchPending();
            Assert.Contains(ThirdUnitId, fx.Scheduler.GetOrder());

            fx.Bus.PublishImmediate(new UnitDiedEvent(ThirdUnitId, killerId: DiscreteFightWorldBuilder.PlayerId));

            Assert.DoesNotContain(ThirdUnitId, fx.Scheduler.GetOrder());
            // 整场战斗仍在进行（原有两名参与者都还活着），不应因第三方死亡而误切回连续模式。
            Assert.Equal(TimeModelMode.Discrete, fx.TimeModelSwitch.CurrentMode);
        }

        [Fact]
        public void CombatLeft_MidDiscreteFight_RemovesFromCurrentTurnOrder()
        {
            var fx = BuildInDiscreteModeWithThirdUnit();
            fx.Rules.Combat.NotifyCombatEvent(ThirdUnitId);
            fx.Bus.DispatchPending();
            Assert.Contains(ThirdUnitId, fx.Scheduler.GetOrder());

            // 个体脱战（非死亡）：见 TimeModelSwitch.OnCombatLeft 补齐段落判断记录——脱战单位同样
            // 应该立即从当前轮行动顺序移除，不再被轮到。
            fx.Bus.PublishImmediate(new CombatLeftEvent(ThirdUnitId));

            Assert.DoesNotContain(ThirdUnitId, fx.Scheduler.GetOrder());
            // 整场战斗仍在进行（原有两名参与者都还在场），不应因第三方脱战而误切回连续模式。
            Assert.Equal(TimeModelMode.Discrete, fx.TimeModelSwitch.CurrentMode);
        }

        /// <summary>
        /// <see cref="Core.Gameplay.Assembly.TimeModelSwitch.ResolveParticipants"/> 首次解析参与者
        /// 时（0→1 那一刻）的阵营过滤（见该方法判断记录"参与者解析"）：半径内一个与玩家/野生动物
        /// 阵营都不互为敌对的中立旁观者（<see cref="DiscreteFightWorldBuilder.FactionNeutral"/>）
        /// 不应被拉进战斗——本任务之前的实现半径内任何存活单位一律收进参与者名单，不做阵营过滤，
        /// 这是一个真实的精度缺口（见类型注释）。
        /// </summary>
        [Fact]
        public void ResolveParticipants_ExcludesNeutralBystanderWithinRadius_ByFaction()
        {
            var fx = DiscreteFightWorldBuilder.Build();

            var neutral = new Core.Carriers.Unit.CreatureUnit(
                ThirdUnitId, DiscreteFightWorldBuilder.MapId, DiscreteFightWorldBuilder.FactionNeutral,
                DiscreteFightWorldBuilder.ClassSample)
            { Position = new Vec2(0.5, 0) }; // 位于玩家与 NPC 之间，落在默认搜索半径内。
            fx.World.AddEntity(neutral);
            fx.Rules.RegisterUnit(ThirdUnitId, DiscreteFightWorldBuilder.ClassSample, raceId: null, level: 1);
            fx.Spatial.Register(ThirdUnitId, new Vec2(0.5, 0), 0.1);

            for (var i = 0; i < 20 && fx.TimeModelSwitch.CurrentMode == TimeModelMode.Continuous; i++)
            {
                fx.ContinuousTickWithPlayerCast(DiscreteFightWorldBuilder.SkillStrike);
            }

            Assert.Equal(TimeModelMode.Discrete, fx.TimeModelSwitch.CurrentMode);
            Assert.Contains(DiscreteFightWorldBuilder.PlayerId, fx.Scheduler.GetOrder());
            Assert.Contains(DiscreteFightWorldBuilder.NpcId, fx.Scheduler.GetOrder());
            Assert.DoesNotContain(ThirdUnitId, fx.Scheduler.GetOrder());
        }
    }
}
