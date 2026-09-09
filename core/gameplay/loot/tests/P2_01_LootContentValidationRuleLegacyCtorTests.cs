#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容 façade，验证 ABI/API 兼容行为。
using Core.Foundation.DataRegistry;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// P2-01 ABI/API 兼容回归测试（外部审计 audit-c9ff301-20260909，全仓复查 1.12→1.13 删除/改签的
    /// 公开成员发现，判断记录同
    /// <c>Core.Gameplay.Economy.P2_01_EconomyContentValidationRuleLegacyCtorTests</c>）：
    /// <see cref="LootContentValidationRule"/> 1.12 的构造签名是
    /// <c>(IExprSchema? conditionSchema = null)</c>，1.13 改成隐式无参构造。本测试验证补回的一参数
    /// 构造函数存在且行为与无参构造函数等价。
    /// </summary>
    public sealed class P2_01_LootContentValidationRuleLegacyCtorTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        [Fact]
        public void LegacyOneParamCtor_IgnoresConditionSchema_BehavesLikeParameterlessCtor()
        {
            var rows = "[{\"id\": \"loot.p2_01_bad_guaranteed\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.p2_01_ore\", \"weight_or_chance\": 0.5, \"count_range\": {\"min\":1,\"max\":1}}" +
                "]}], \"guaranteed_min\": -1}]";

            var source = new InMemoryDataSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, rows));
            var registry = new DataRegistry(source, LootTestSupport.NewEventBus(), new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule(conditionSchema: null));

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "loot_content" && i.Message.Contains("guaranteed_min"));
        }
    }
}
#pragma warning restore CS0618
