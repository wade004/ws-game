using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Economy;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>供本模块测试共用的最小装配帮助（惯例同
    /// <c>core/carriers/creature/tests/CreatureTestSupport.cs</c>）。</summary>
    internal static class EconomyTestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static IEventBus NewEventBus() =>
            new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        public static DataRegistry MakeRegistry(IEventBus bus, string currencyRowsJson, string vendorRowsJson)
        {
            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, currencyRowsJson))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendorRowsJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }
    }

    /// <summary>最小 <see cref="IExprHostFactory"/> 假实现：支持 <c>self.level</c>（RulesExprSchema
    /// 精确登记的既有键，返回 Int），按 <see cref="Levels"/> 以 <c>selfId</c> 为 key 返回配置值；供
    /// <c>buy_price_rule</c> 一类需要数值型 <c>self.*</c> 结果的测试用例使用。</summary>
    internal sealed class FakeNumericExprHostFactory : IExprHostFactory
    {
        public readonly Dictionary<Id, long> Levels = new Dictionary<Id, long>();

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host(this, selfId);

        private sealed class Host : IExprHost
        {
            private readonly FakeNumericExprHostFactory _f;
            private readonly Id _self;

            public Host(FakeNumericExprHostFactory f, Id self)
            {
                _f = f;
                _self = self;
            }

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
            {
                if (group == ExprGroups.Self && key == "level")
                {
                    return ExprValue.OfInt(_f.Levels.TryGetValue(_self, out var v) ? v : 0);
                }

                return ExprValue.OfBool(false);
            }
        }
    }

    /// <summary>只处理 <c>player</c> 分组、其余分组一律返回 Bool(false) 的最小 <see cref="IExprHost"/>
    /// （惯例同 <c>core/gameplay/world_state.tests.WorldOnlyExprHost</c>），把 <c>player</c> 委托给注入的
    /// <see cref="IExprGroupProvider"/>（通常是 <see cref="PlayerCurrencyExprGroupProvider"/> 或
    /// <see cref="ChainedExprGroupProvider"/>）。</summary>
    internal sealed class PlayerOnlyExprHost : IExprHost
    {
        private readonly IExprGroupProvider _playerGroup;

        public PlayerOnlyExprHost(IExprGroupProvider playerGroup)
        {
            _playerGroup = playerGroup;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) =>
            group == ExprGroups.Player ? _playerGroup.Query(key, args) : ExprValue.OfBool(false);
    }
}
