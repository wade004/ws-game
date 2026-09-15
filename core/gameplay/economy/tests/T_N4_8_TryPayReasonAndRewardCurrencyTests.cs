using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// 分阶段落地计划 T-N4-8 验收（ADR-0034 决策 5；08 第 7.4 节修订段"tryCharge/TryPay(unitId,
    /// currencyId, amount, reason)——余额足够时一次性扣除并发 economy.charged{unitId, currencyId,
    /// amount, reason}；不足时不扣、不发、返回 false"）：原子性 2 组（成功扣费并发带 reason 的
    /// <see cref="EconomyChargedEvent"/>；余额不足不扣不发）+ 旧无 reason 签名转发新签名同样发事件
    /// 1 组（<see cref="EconomyHost.TryPay(Id,Id,long)"/> 判断记录）+ 任务奖励货币经
    /// <see cref="IEconomyHost.Add"/> 入账 1 组（<see cref="CurrencyGranters.ViaEconomyHost"/>）。
    /// </summary>
    public sealed class T_N4_8_TryPayReasonAndRewardCurrencyTests
    {
        private const string OneCurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
            "\"cap\": 1000, \"display_ref\": \"display.sample_coin\"}]";

        private static readonly Id UnitId = new Id("player.sample_1");
        private static readonly Id CurrencyId = new Id("econ.currency.sample_coin");

        private static EconomyHost NewHost(out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = EconomyTestSupport.NewEventBus();
            var registry = EconomyTestSupport.MakeRegistry(bus, OneCurrencyRow, "[]");
            var inventory = new FakeInventoryHost();
            return new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());
        }

        [Fact]
        public void TryPay_WithReason_SufficientBalance_DeductsAndPublishesChargedEvent()
        {
            var host = NewHost(out var bus);
            EconomyChargedEvent? received = null;
            bus.Subscribe<EconomyChargedEvent>(EconomyEventKeys.Charged, e => received = e);
            host.Add(UnitId, CurrencyId, 50, sourceId: UnitId);
            bus.DispatchPending();

            var ok = host.TryPay(UnitId, CurrencyId, 30, "respawn_fee");
            bus.DispatchPending();

            Assert.True(ok);
            Assert.Equal(20, host.GetBalance(UnitId, CurrencyId));
            Assert.NotNull(received);
            Assert.Equal(UnitId, received!.UnitId);
            Assert.Equal(CurrencyId, received.CurrencyId);
            Assert.Equal(30, received.Amount);
            Assert.Equal("respawn_fee", received.Reason);
        }

        [Fact]
        public void TryPay_WithReason_InsufficientBalance_DoesNotDeductOrPublish()
        {
            var host = NewHost(out var bus);
            var chargedCount = 0;
            bus.Subscribe<EconomyChargedEvent>(EconomyEventKeys.Charged, _ => chargedCount++);
            host.Add(UnitId, CurrencyId, 10, sourceId: UnitId);
            bus.DispatchPending();

            var ok = host.TryPay(UnitId, CurrencyId, 30, "respawn_fee");
            bus.DispatchPending();

            Assert.False(ok);
            Assert.Equal(10, host.GetBalance(UnitId, CurrencyId));
            Assert.Equal(0, chargedCount);
        }

        /// <summary>判断记录（见 <see cref="EconomyHost.TryPay(Id,Id,long)"/>）：旧无 reason 三参数
        /// 签名从 T-N4-8 起转发带 reason 的新签名、传入占位 <c>"unspecified"</c>——旧调用路径同样会
        /// 发 <see cref="EconomyChargedEvent"/>，不是"发生扣费但不发事件"的另一分支。</summary>
        [Fact]
        public void TryPay_WithoutReason_DelegatesToReasonOverload_PublishesChargedEventWithUnspecifiedReason()
        {
            var host = NewHost(out var bus);
            EconomyChargedEvent? received = null;
            bus.Subscribe<EconomyChargedEvent>(EconomyEventKeys.Charged, e => received = e);
            host.Add(UnitId, CurrencyId, 50, sourceId: UnitId);
            bus.DispatchPending();

            var ok = host.TryPay(UnitId, CurrencyId, 30);
            bus.DispatchPending();

            Assert.True(ok);
            Assert.Equal(20, host.GetBalance(UnitId, CurrencyId));
            Assert.NotNull(received);
            Assert.Equal(30, received!.Amount);
            Assert.Equal("unspecified", received.Reason);
        }

        /// <summary>验收标准"任务奖励货币经 IEconomyHost.Add 入账 1 组"：<see
        /// cref="RewardDispatcher"/> 用 <see cref="CurrencyGranters.ViaEconomyHost"/> 构造的
        /// <see cref="CurrencyGranter"/> 发放货币奖励，货币真正落地在 <see cref="EconomyHost"/> 的
        /// 余额表里（经 <see cref="EconomyHost.Add"/>，不是某个绕开经济宿主的旁路），且触发
        /// <see cref="CurrencyChangedEvent"/>（<see cref="EconomyHost.Add"/> 的既有账本事件）。</summary>
        [Fact]
        public void RewardDispatcher_GrantCurrency_ViaEconomyHost_DepositsThroughAdd()
        {
            var host = NewHost(out var bus);
            CurrencyChangedEvent? received = null;
            bus.Subscribe<CurrencyChangedEvent>(EconomyEventKeys.CurrencyChanged, e => received = e);

            var dispatcher = new RewardDispatcher(currencyGranter: CurrencyGranters.ViaEconomyHost(host));
            var sourceId = new Id("quest.sample_quest");
            var bundle = new RewardBundle(
                items: Array.Empty<ItemStack>(),
                xp: 0,
                currency: new[] { (CurrencyId, 40L) },
                skills: Array.Empty<Id>(),
                worldFlags: Array.Empty<(Id, ExprValue)>(),
                talentPoints: 0);

            var granted = dispatcher.Grant(UnitId, bundle, sourceId);
            bus.DispatchPending();

            Assert.True(granted);
            Assert.Equal(40, host.GetBalance(UnitId, CurrencyId));
            Assert.NotNull(received);
            Assert.Equal(0, received!.OldValue);
            Assert.Equal(40, received.NewValue);
        }

        [Fact]
        public void CurrencyGranters_ViaEconomyHost_RejectsNullHost()
        {
            Assert.Throws<ArgumentNullException>(() => CurrencyGranters.ViaEconomyHost(null!));
        }
    }
}
