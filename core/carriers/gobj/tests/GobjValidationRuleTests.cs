using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>三条校验规则（见 schema/README.md"校验规则"表）各一条正例、一条反例（惯例同
    /// <c>core/rules/skill/tests</c> 的 <c>SkillValidationRuleTests</c>）。</summary>
    public sealed class GobjValidationRuleTests
    {
        private static readonly Id DisplayRef = new Id("display.sample_gobj");

        private static JsonObject Template(string id, string kind, JsonObject typeData, JsonObject? onUse = null)
        {
            var fields = new System.Collections.Generic.List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
                ("kind", J.S(kind)),
                ("type_data", typeData),
                ("display_ref", J.S(DisplayRef.Value)),
            };

            if (onUse != null)
            {
                fields.Add(("on_use", onUse));
            }

            return J.O(fields.ToArray());
        }

        // -----------------------------------------------------------------
        // GobjTypeDataFieldGroupRule
        // -----------------------------------------------------------------

        [Fact]
        public void TypeDataFieldGroup_PassesWithRequiredFields_FailsWhenChestMissingLootTableRef()
        {
            var ok = new GobjWorldBuilder()
                .Template(Template("gobj.sample_chest_ok", "chest", J.O(("loot_table_ref", J.S("loot.sample_a")))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_chest_bad", "chest", J.O()))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_type_data_field_group");
        }

        [Fact]
        public void TypeDataFieldGroup_GatherNodeRequiresBothFields()
        {
            var ok = new GobjWorldBuilder()
                .Template(Template("gobj.sample_node_ok", "gather_node",
                    J.O(("loot_table_ref", J.S("loot.sample_b")), ("respawn_after_use", J.N(30)))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.sample_node_bad", "gather_node", J.O(("loot_table_ref", J.S("loot.sample_c")))))
                .ValidationRule(new GobjTypeDataFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_type_data_field_group");
        }

        // ADR-0019 F1c 退役：GobjOnUseKindRule/GobjLockRequirementFieldGroupRule 两条规则连同这里的
        // 测试一并删除，覆盖场景迁移至 GobjSchemaCoverageTests（variant_discriminator/required_field/
        // reference_integrity 三个内建检查名，见该文件）。

        // -----------------------------------------------------------------
        // GobjLockWorldFlagExpectedRule（P2-03 根治：退役 GobjLockRequirementFieldGroupRule 时遗漏
        // world_flag.expected 的必填/形状校验，见该规则判断记录）
        // -----------------------------------------------------------------

        private static JsonObject WorldFlagLock(string id, JsonValue? expected)
        {
            var fields = new System.Collections.Generic.List<(string, JsonValue)>
            {
                ("kind", J.S("world_flag")),
                ("flag_key", J.S("world.flag.p2_03_sample")),
            };
            if (expected != null)
            {
                fields.Add(("expected", expected));
            }

            return J.O(("id", J.S(id)), ("requirement", J.O(fields.ToArray())));
        }

        [Fact]
        public void P2_03_WorldFlagExpected_MissingIsBlocking_AndNeverReachesLockDefParse()
        {
            var bad = new GobjWorldBuilder()
                .Lock(WorldFlagLock("gobj.lock.p2_03_missing", expected: null))
                .ValidationRule(new GobjLockWorldFlagExpectedRule());
            var report = bad.Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");
        }

        [Fact]
        public void P2_03_WorldFlagExpected_WrongType_ArrayIsBlocking()
        {
            var bad = new GobjWorldBuilder()
                .Lock(WorldFlagLock("gobj.lock.p2_03_array", J.A(J.B(true))))
                .ValidationRule(new GobjLockWorldFlagExpectedRule());
            var report = bad.Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");
        }

        [Fact]
        public void P2_03_WorldFlagExpected_WrongType_StringIsBlocking()
        {
            var bad = new GobjWorldBuilder()
                .Lock(WorldFlagLock("gobj.lock.p2_03_string", J.S("true")))
                .ValidationRule(new GobjLockWorldFlagExpectedRule());
            var report = bad.Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");
        }

        [Fact]
        public void P2_03_WorldFlagExpected_WrongType_IdObjectIsBlocking()
        {
            var bad = new GobjWorldBuilder()
                .Lock(WorldFlagLock("gobj.lock.p2_03_id", J.O(("$id", J.S("world.flag.other")))))
                .ValidationRule(new GobjLockWorldFlagExpectedRule());
            var report = bad.Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");
        }

        [Fact]
        public void P2_03_WorldFlagExpected_Bool_Int_Number_PassAndParse()
        {
            foreach (var (id, expected) in new (string, JsonValue)[]
            {
                ("gobj.lock.p2_03_bool", J.B(true)),
                ("gobj.lock.p2_03_int", J.N(1)),
                ("gobj.lock.p2_03_number", J.N(1.5)),
            })
            {
                var ok = new GobjWorldBuilder()
                    .Lock(WorldFlagLock(id, expected))
                    .ValidationRule(new GobjLockWorldFlagExpectedRule());
                var report = ok.Validate();
                Assert.False(report.IsBlocking);
                Assert.DoesNotContain(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");
            }
        }

        /// <summary>端到端复现（改造自审计探针 GobjLockBoundaryProbe）：正式装配对 expected 缺失应
        /// blocking，绝不能让缺陷流到 LockDef.FromRecord 才抛异常；expected=true 走完正式装配 +
        /// LockDef 解析全链路。</summary>
        [Fact]
        public void P2_03_FormalCatalog_MissingExpected_BlocksBeforeLockDefParse_PositivePasses()
        {
            var missingSource = new Core.Foundation.DataRegistry.InMemoryDataSource().Add(
                "gobj.lock",
                "{\"table\":\"gobj.lock\",\"schema_version\":1,\"rows\":[" +
                "{\"id\":\"gobj.lock.p2_03_formal_missing\",\"requirement\":{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.p2_03_formal\"}}" +
                "]}");
            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(System.Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                missingSource, bus, new Core.Foundation.DataRegistry.DataRegistryOptions { FailOnUnknownTable = true });
            Core.Carriers.Assembly.CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_world_flag_expected");

            var positiveSource = new Core.Foundation.DataRegistry.InMemoryDataSource().Add(
                "gobj.lock",
                "{\"table\":\"gobj.lock\",\"schema_version\":1,\"rows\":[" +
                "{\"id\":\"gobj.lock.p2_03_formal_ok\",\"requirement\":{\"kind\":\"world_flag\",\"flag_key\":\"world.flag.p2_03_formal\",\"expected\":true}}" +
                "]}");
            var positiveBus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(System.Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var positiveRegistry = new Core.Foundation.DataRegistry.DataRegistry(
                positiveSource, positiveBus, new Core.Foundation.DataRegistry.DataRegistryOptions { FailOnUnknownTable = true });
            Core.Carriers.Assembly.CarriersSchemaCatalog.RegisterAll(positiveRegistry);
            var positiveReport = positiveRegistry.LoadAll();
            Assert.False(positiveReport.IsBlocking);

            var positiveRecord = positiveRegistry.Get("gobj.lock", "gobj.lock.p2_03_formal_ok")!;
            var positiveDef = LockDef.FromRecord(positiveRecord);
            Assert.Equal(Core.Carriers.Common.LockRequirementKind.WorldFlag, positiveDef.Requirement.Kind);
        }

        // -----------------------------------------------------------------
        // P2-01 ABI/API 兼容 façade（外部审计 audit-c9ff301-20260909）：GobjOnUseKindRule/
        // GobjLockRequirementFieldGroupRule 两个 1.12 public 类型必须仍然存在、可显式注册、行为
        // 与 1.12 完全一致。
        // -----------------------------------------------------------------
#pragma warning disable CS0618 // 有意调用 [Obsolete] 的 1.12 兼容 façade。

        [Fact]
        public void P2_01_GobjOnUseKindRule_PassesForSkillOrDialog_FailsForUnknownKind()
        {
            var ok = new GobjWorldBuilder()
                .Skill("skill.p2_01_sample_a")
                .Template(Template("gobj.p2_01_sign_ok", "sign", J.O(("text_key", J.S("l10n.p2_01_sample_a.text"))),
                    onUse: J.O(("kind", J.S("skill")), ("ref", J.S("skill.p2_01_sample_a")))))
                .ValidationRule(new GobjOnUseKindRule());
            var okReport = ok.Validate();
            Assert.False(okReport.IsBlocking, string.Join("; ", okReport.Issues));

            var bad = new GobjWorldBuilder()
                .Template(Template("gobj.p2_01_sign_bad", "sign", J.O(("text_key", J.S("l10n.p2_01_sample_b.text"))),
                    onUse: J.O(("kind", J.S("not_a_real_kind")), ("ref", J.S("skill.p2_01_sample_b")))))
                .ValidationRule(new GobjOnUseKindRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_on_use_kind");
        }

        [Fact]
        public void P2_01_GobjLockRequirementFieldGroupRule_WorldFlagRequiresFlagKeyAndExpected()
        {
            var ok = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.p2_01_flag_ok")),
                    ("requirement", J.O(("kind", J.S("world_flag")), ("flag_key", J.S("world.p2_01_sample_b")), ("expected", J.B(true))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            Assert.False(ok.Validate().IsBlocking);

            var bad = new GobjWorldBuilder()
                .Lock(J.O(("id", J.S("gobj.lock.p2_01_flag_bad")), ("requirement", J.O(("kind", J.S("world_flag")), ("flag_key", J.S("world.p2_01_sample_c"))))))
                .ValidationRule(new GobjLockRequirementFieldGroupRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "gobj_lock_requirement_field_group");
        }
#pragma warning restore CS0618
    }
}
