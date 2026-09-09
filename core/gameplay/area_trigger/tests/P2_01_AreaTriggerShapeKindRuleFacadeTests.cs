#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容 façade，验证 ABI/API 兼容行为。
using Core.Foundation.Common.Json;
using Core.Gameplay.AreaTrigger;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// P2-01 ABI/API 兼容回归测试（外部审计 audit-c9ff301-20260909）：ADR-0019/F1b 退役的
    /// <see cref="AreaTriggerShapeKindRule"/>（1.12→1.13 全仓复查发现的第六个退役 public 类型，见
    /// AreaTriggerValidationRules.cs 顶部"P2-01 ABI/API 兼容 façade 补漏"判断记录）必须仍然存在、
    /// 可显式注册、行为与 1.12 完全一致。
    /// </summary>
    public sealed class P2_01_AreaTriggerShapeKindRuleFacadeTests
    {
        [Fact]
        public void AreaTriggerShapeKindRule_LegalKind_NoIssue()
        {
            var row = AreaTriggerTestSupport.MapTransitionRow("area.p2_01_ok", "world.p2_01_map", "world.p2_01_target");
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, row);
            registry.RegisterValidationRule(new AreaTriggerShapeKindRule());

            var report = registry.LoadAll();

            Assert.DoesNotContain(report.Issues, i => i.Check == "area_trigger_shape_kind");
        }

        [Fact]
        public void AreaTriggerShapeKindRule_IllegalKind_ReportsError()
        {
            var row = J.O(
                ("id", J.S("area.p2_01_bad")),
                ("map_id", J.S("world.p2_01_map")),
                ("shape", J.O(("kind", J.S("not_a_real_shape")))),
                ("trigger_type", J.S("map_transition")),
                ("params", J.O(("target_map", J.S("world.p2_01_target")))));
            var bus = AreaTriggerTestSupport.NewEventBus();
            var registry = AreaTriggerTestSupport.BuildRegistry(bus, row);
            registry.RegisterValidationRule(new AreaTriggerShapeKindRule());

            var report = registry.LoadAll();

            Assert.Contains(report.Issues, i => i.Check == "area_trigger_shape_kind");
        }
    }
}
#pragma warning restore CS0618
