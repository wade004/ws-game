using System.Collections.Generic;
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
    /// T-N4-9（[ADR-0034](../../../../architecture/adr/0034-单一货币与价格挂物品等级.md) 决策 6；
    /// 06 第 4.6 节 2026-09-14 修订段"复活费"）：<c>respawn_point</c> 分支的复活费策略项与窄委托——
    /// 余额为零时仍照常复活且扣零、<c>fixed_by_level</c> 曲线值超过余额时扣至零、<c>pct_of_balance</c>
    /// 按百分比计算、策略为 <see cref="DeathPolicyOptions.RespawnFeePolicy.None"/>（默认）与扣费
    /// 委托未接线两种场景均不产生任何货币副作用、复活永不被阻断。
    /// </summary>
    public sealed class T_N4_9_RespawnFeeTests
    {
        private static readonly Id PlayerId = new Id("unit.sample_player");
        private static readonly Id MapId = new Id("world.sample_field");
        private static readonly Id FactionPlayer = new Id("faction.player");
        private static readonly Id ArchetypeSample = new Id("arch.class.sample");
        private static readonly Id GoldId = new Id("econ.gold");

        private sealed class Fixture
        {
            public IEventBus Bus = default!;
            public WorldSim World = default!;
            public DeathPolicyHost Host = default!;
            public List<(Id UnitId, Vec2 Position, double HealthFraction)> Revived = new();
            public List<(Id UnitId, Id CurrencyId, long Amount)> Charged = new();
            public Dictionary<Id, long> Balances = new();

            public void Died(Id unitId) => Bus.PublishImmediate(new UnitDiedEvent(unitId, null, MapId));

            public void Tick() => Host.Execute(SimStep.Continuous(0), World);
        }

        private static Fixture Build(
            DeathPolicyOptions.RespawnFeePolicy policy,
            double percentage = 0.0,
            DeathPolicyOptions.RespawnFeeFixedAmountDelegate? fixedAmount = null,
            long initialBalance = 0,
            bool wireCharge = true)
        {
            var bus = DeathTestSupport.NewEventBus();
            var world = new WorldSim(bus);
            var fs = new StubFileSystem();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.sample")));
            var appState = new AppStateHost(bus, AppStateMachineConfig.Default());

            world.AddEntity(new PlayerUnit(PlayerId, MapId, FactionPlayer, ArchetypeSample) { Position = new Vec2(0, 0) });

            var fx = new Fixture { Bus = bus, World = world };
            fx.Balances[PlayerId] = initialBalance;

            var options = new DeathPolicyOptions
            {
                ReviveUnit = (unitId, position, healthFraction) => fx.Revived.Add((unitId, position, healthFraction)),
                ResolveDefaultSpawn = mapId => (mapId, new Vec2(5, 6)),
                RespawnFee = policy,
                RespawnFeePercentage = percentage,
                RespawnFeeCurrencyId = GoldId,
                RespawnFeeFixedAmount = fixedAmount,
                RespawnFeeBalance = (unitId, currencyId) => fx.Balances.TryGetValue(unitId, out var b) ? b : 0L,
            };

            if (wireCharge)
            {
                options.RespawnFeeCharge = (unitId, currencyId, amount) =>
                {
                    fx.Charged.Add((unitId, currencyId, amount));
                    fx.Balances[unitId] = (fx.Balances.TryGetValue(unitId, out var b) ? b : 0L) - amount;
                    return true;
                };
            }

            fx.Host = new DeathPolicyHost(bus, world, saveSystem, appState, RespawnPolicy.RespawnPoint, options);
            return fx;
        }

        [Fact]
        public void RespawnPoint_ZeroBalance_RevivesAsUsual_AndChargesZero()
        {
            var fx = Build(DeathPolicyOptions.RespawnFeePolicy.FixedByLevel, fixedAmount: _ => 50, initialBalance: 0);

            fx.Died(PlayerId);
            fx.Tick();

            Assert.Single(fx.Revived); // 复活永远不被金钱阻断，余额为零也照常复活。
            var charge = Assert.Single(fx.Charged); // 无条件调用扣费委托——扣零，不是跳过整个流程。
            Assert.Equal(0L, charge.Amount);
            Assert.Equal(GoldId, charge.CurrencyId);
            Assert.Equal(0L, fx.Balances[PlayerId]);
        }

        [Fact]
        public void RespawnPoint_FixedByLevel_FeeExceedsBalance_ChargesDownToZeroBalance()
        {
            var fx = Build(DeathPolicyOptions.RespawnFeePolicy.FixedByLevel, fixedAmount: _ => 50, initialBalance: 30);

            fx.Died(PlayerId);
            fx.Tick();

            Assert.Single(fx.Revived);
            var charge = Assert.Single(fx.Charged);
            Assert.Equal(30L, charge.Amount); // min(50, 30) = 30，扣至零，不阻断复活。
            Assert.Equal(0L, fx.Balances[PlayerId]);
        }

        [Fact]
        public void RespawnPoint_PctOfBalance_ChargesPercentageOfCurrentBalance()
        {
            var fx = Build(DeathPolicyOptions.RespawnFeePolicy.PctOfBalance, percentage: 0.1, initialBalance: 100);

            fx.Died(PlayerId);
            fx.Tick();

            Assert.Single(fx.Revived);
            var charge = Assert.Single(fx.Charged);
            Assert.Equal(10L, charge.Amount); // 100 * 0.1 = 10，恒 <= 余额，min 对该分支是无操作。
            Assert.Equal(90L, fx.Balances[PlayerId]);
        }

        [Fact]
        public void RespawnPoint_PolicyNone_DoesNotChargeAnything()
        {
            var fx = Build(DeathPolicyOptions.RespawnFeePolicy.None, initialBalance: 100);

            fx.Died(PlayerId);
            fx.Tick();

            Assert.Single(fx.Revived);
            Assert.Empty(fx.Charged); // 默认策略，逐位保持"不收复活费"的既有行为。
            Assert.Equal(100L, fx.Balances[PlayerId]);
        }

        [Fact]
        public void RespawnPoint_ChargeDelegateNotWired_RevivesWithoutThrowing_AndNoChargeRecorded()
        {
            var fx = Build(
                DeathPolicyOptions.RespawnFeePolicy.FixedByLevel, fixedAmount: _ => 50, initialBalance: 100,
                wireCharge: false);

            var ex = Record.Exception(() =>
            {
                fx.Died(PlayerId);
                fx.Tick();
            });

            Assert.Null(ex);
            Assert.Single(fx.Revived); // 未接线不阻断复活。
            Assert.Empty(fx.Charged);
        }
    }
}
