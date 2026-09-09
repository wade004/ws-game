#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容 façade，验证 ABI/API 兼容行为。
using Core.Foundation.DataRegistry;
using Core.Gameplay.Economy;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// P2-01 ABI/API 兼容回归测试（外部审计 audit-c9ff301-20260909，全仓复查 1.12→1.13 删除/改签的
    /// 公开成员发现）：<see cref="EconomyContentValidationRule"/> 1.12 的构造签名是
    /// <c>(IExprSchema? conditionSchema = null)</c>，1.13 改成隐式无参构造——老调用点传参数（甚至
    /// 显式零参调用，因为可选参数默认值在旧调用点编译期内联进 IL）在只换 DLL 不重编译时会
    /// <see cref="System.MissingMethodException"/>。本测试验证补回的一参数构造函数存在且行为与
    /// 无参构造函数等价。
    /// </summary>
    public sealed class P2_01_EconomyContentValidationRuleLegacyCtorTests
    {
        [Fact]
        public void LegacyOneParamCtor_IgnoresConditionSchema_BehavesLikeParameterlessCtor()
        {
            var bus = EconomyTestSupport.NewEventBus();
            var badVendorRows = "[{\"id\": \"econ.vendor.p2_01\", \"name_key\": \"l10n.econ.vendor.p2_01\", " +
                "\"sell_items\": [{\"item_id\": \"item.p2_01\", \"price_currency_id\": \"econ.currency.p2_01\", \"price_amount\": -1}]}]";
            var currencyRows = "[{\"id\": \"econ.currency.p2_01\", \"name_key\": \"l10n.econ.currency.p2_01\"}]";

            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, EconomyTestSupport.Envelope(EconomySchemas.Currency.Name, currencyRows))
                .Add(EconomySchemas.Vendor.Name, EconomyTestSupport.Envelope(EconomySchemas.Vendor.Name, badVendorRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule(conditionSchema: null));

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "economy_content");
        }
    }
}
#pragma warning restore CS0618
