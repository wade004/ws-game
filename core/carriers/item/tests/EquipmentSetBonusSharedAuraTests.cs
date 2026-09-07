using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// R03 收口（外部审计 5e779c6，P2；复现工程
    /// architecture/落地计划/audit-5e779c6-20260907/repro/Program.cs 的
    /// <c>ReproEquipmentDuplicate</c>）：<c>AllowMultiSourceTiming=false</c>（默认）、
    /// <c>maxStacks=1</c>、<c>StackOverflowPolicy.Replace</c> 下两类场景：
    /// <list type="bullet">
    /// <item>同一件装备的 <c>grants.auras</c> 里重复登记两次同一个 <c>aura_def</c>——第二次施加会
    /// 因为叠层溢出触发 <c>Replace</c>，同一件装备内部换句柄；修复前
    /// <see cref="Core.Carriers.Item.EquipmentHost"/> 会把这次"自己内部换句柄"错当成"多了一个
    /// 外部来源"重复计数，导致卸下装备后光环反而卸不干净（残留）。</item>
    /// <item>普通装备与套装（2 件门槛）恰好授予同一个 <c>aura_def</c>——套装门槛加成
    /// （<c>RecomputeSetBonuses</c>）与装备本身的 <c>grants.auras</c>（<c>ApplyGrants</c>/
    /// <c>RevertGrants</c>）此前各自独立记账，不知道彼此在 <c>AuraHost</c> 内合并成了同一份实例；
    /// 修复前卸下普通装备会无条件把共享的光环实例整个移除，即便套装仍满足 2 件门槛——本文件类型
    /// 注释开头一句话即取自这条外部审计描述。</item>
    /// </list>
    /// 用真实 <see cref="CarriersAssembly"/>（真实 <c>CreatureFactory</c>/<c>AuraHost</c>/
    /// <c>EquipmentHost</c>/<c>InventoryHost</c> 全链路组合，不是任何模块内部的 fake unit access）
    /// 驱动——套装门槛加成、叠层溢出策略换句柄这两件事都发生在真实 <c>AuraHost</c> 内部，任何
    /// 简化版 fake 都无法忠实复现。本文件是本模块专属的新测试文件（不改动既有
    /// <c>EquipmentHostTests.cs</c>/<c>TestSupport.cs</c>，只读复用同目录 <c>EquipmentReplaceHandleTests.cs</c>
    /// 已经验证过的数据组装写法）。
    /// </summary>
    public sealed class EquipmentSetBonusSharedAuraTests
    {
        private static readonly Id MapId = new Id("map.r03_test");
        private static readonly Id PlayerTemplateId = new Id("creature.sample_r03_player");
        private static readonly Id TierNormal = new Id("creature.tier.r03_normal");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id SharedAuraDefId = new Id("skill.aura_def.r03_shared_aura");

        private static readonly Id SlotDuplicate = new Id("item.slot.r03_duplicate");
        private static readonly Id SlotOrdinary = new Id("item.slot.r03_ordinary");
        private static readonly Id SlotSetOne = new Id("item.slot.r03_set_one");
        private static readonly Id SlotSetTwo = new Id("item.slot.r03_set_two");

        private static readonly Id TemplateDuplicate = new Id("item.sample_r03_duplicate");
        private static readonly Id TemplateOrdinary = new Id("item.sample_r03_ordinary");
        private static readonly Id TemplateSetOne = new Id("item.sample_r03_set_one");
        private static readonly Id TemplateSetTwo = new Id("item.sample_r03_set_two");
        private static readonly Id SetId = new Id("item.set.r03_sample");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var statDefinitionRows = "[" +
                "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}" +
                "]";

            var powerTypeRows = "[" +
                "{\"id\": \"" + WellKnownPowers.Health.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": 1000}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]";

            var tierDefinitionRows = "[" +
                "{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.r03_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]";

            var creatureTemplateRows = "[" +
                "{\"id\": \"" + PlayerTemplateId.Value + "\", \"name_key\": \"l10n.creature.r03_player.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.r03_test\", " +
                "\"display_ref\": \"display.r03_player\"}" +
                "]";

            var hitTableRows = "[" +
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}" +
                "]";

            var auraDefRows = "[" +
                "{\"id\": \"" + SharedAuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 100}}]}" +
                "]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + SlotDuplicate.Value + "\", \"name_key\": \"l10n.item.slot.r03_duplicate\"}," +
                "{\"id\": \"" + SlotOrdinary.Value + "\", \"name_key\": \"l10n.item.slot.r03_ordinary\"}," +
                "{\"id\": \"" + SlotSetOne.Value + "\", \"name_key\": \"l10n.item.slot.r03_set_one\"}," +
                "{\"id\": \"" + SlotSetTwo.Value + "\", \"name_key\": \"l10n.item.slot.r03_set_two\"}" +
                "]";

            var itemQualityDefinitionRows = "[" +
                "{\"id\": \"item.quality.r03_common\", \"name_key\": \"l10n.item.quality.r03_common\"}" +
                "]";

            var itemSetRows = "[" +
                "{\"id\": \"" + SetId.Value + "\", \"name_key\": \"l10n.item.set.r03_sample\", " +
                "\"pieces\": [\"" + TemplateSetOne.Value + "\", \"" + TemplateSetTwo.Value + "\"], " +
                "\"bonuses\": [{\"count\": 2, \"aura_ref\": \"" + SharedAuraDefId.Value + "\"}]}" +
                "]";

            // duplicate：grants.auras 里重复登记两次同一个 aura_def（真实 07 数据不应该这么配，但
            // schema 层面不禁止——见 R09 相关判断记录；这里刻意构造来验证运行期不会因此卸不干净）。
            // ordinary/set_one/set_two：ordinary 直接 grants 同一个 aura_def；set_one+set_two 凑齐
            // 2 件门槛后套装本身也 grants 同一个 aura_def——两者在 AllowMultiSourceTiming=false 下
            // 会在 AuraHost 内合并成同一份实例。
            var itemTemplateRows = "[" +
                "{\"id\": \"" + TemplateDuplicate.Value + "\", \"slot\": \"" + SlotDuplicate.Value + "\", " +
                "\"quality\": \"item.quality.r03_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.r03_duplicate\", \"stack_size\": 1, \"name_key\": \"l10n.item.r03_duplicate\", " +
                "\"grants\": {\"auras\": [\"" + SharedAuraDefId.Value + "\", \"" + SharedAuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateOrdinary.Value + "\", \"slot\": \"" + SlotOrdinary.Value + "\", " +
                "\"quality\": \"item.quality.r03_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.r03_ordinary\", \"stack_size\": 1, \"name_key\": \"l10n.item.r03_ordinary\", " +
                "\"grants\": {\"auras\": [\"" + SharedAuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateSetOne.Value + "\", \"slot\": \"" + SlotSetOne.Value + "\", " +
                "\"quality\": \"item.quality.r03_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.r03_set_one\", \"stack_size\": 1, \"name_key\": \"l10n.item.r03_set_one\", " +
                "\"set_id\": \"" + SetId.Value + "\"}," +
                "{\"id\": \"" + TemplateSetTwo.Value + "\", \"slot\": \"" + SlotSetTwo.Value + "\", " +
                "\"quality\": \"item.quality.r03_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.r03_set_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.r03_set_two\", " +
                "\"set_id\": \"" + SetId.Value + "\"}" +
                "]";

            var itemBudgetCurveRows = "[" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]";

            return new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", statDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", powerTypeRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, tierDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, creatureTemplateRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", hitTableRows))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
                .Add("item.set", Envelope("item.set", itemSetRows))
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve", itemBudgetCurveRows));
        }

        private static CarriersAssembly BuildAssembly(out Id playerId)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            // 复现前提（见外部审计复现日志 boundary 一行）：AllowMultiSourceTiming 保持默认 false，
            // StackOverflowPolicy 显式设为 Replace（默认 RefreshOnly 不会换句柄，不构成触发条件）。
            var skillOptions = new SkillOptions { StackOverflowPolicy = StackOverflowPolicy.Replace };
            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial, skillOptions: skillOptions);

            playerId = assembly.Creatures.Spawn(PlayerTemplateId, MapId, new Vec2(0, 0), 0);
            return assembly;
        }

        private static Id EquipFreshInstance(CarriersAssembly assembly, Id playerId, Id templateId, Id slot)
        {
            assembly.Inventory.AddItem(playerId, templateId, 1);
            var items = assembly.Inventory.ListItems(playerId);
            var instanceId = items[items.Count - 1].InstanceId;
            var result = assembly.Equipment.Equip(playerId, instanceId, slot);
            Assert.True(result.Success, $"装备 \"{templateId}\" 到槽位 \"{slot}\" 应当成功：{result.Reason}");
            return instanceId;
        }

        /// <summary>回归用例（外部审计复现日志 <c>ReproEquipmentDuplicate</c> 的 duplicate 分支，
        /// 当时 <c>reproduced:False</c>——即修复前实际行为已经偏离"卸下后应当干净清空"这一期望，
        /// 只是与 R03 描述的"套装光环被误删"不是同一个故障；保留为独立回归用例，防止再次退化）。</summary>
        [Fact]
        public void DuplicateAuraGrantsOnSameItem_Unequip_RemovesAuraCleanly()
        {
            var assembly = BuildAssembly(out var playerId);

            EquipFreshInstance(assembly, playerId, TemplateDuplicate, SlotDuplicate);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            var unequipped = assembly.Equipment.Unequip(playerId, SlotDuplicate);

            Assert.NotNull(unequipped);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "同一件装备内部因叠层溢出触发 Replace 换了句柄，不代表多了一个外部来源；卸下装备后应当干净清空。");
            Assert.Equal(1, assembly.Rules.Stats.GetStat(playerId, StatPower));
        }

        /// <summary>R03 主复现：卸下仍满足套装门槛的普通装备（普通装备与套装 2 件门槛授予同一个
        /// aura_def）不应删除仍应保留的套装光环。</summary>
        [Fact]
        public void UnequipOrdinaryItem_WhileSetBonusStillMet_KeepsSharedAura()
        {
            var assembly = BuildAssembly(out var playerId);

            EquipFreshInstance(assembly, playerId, TemplateOrdinary, SlotOrdinary);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));

            EquipFreshInstance(assembly, playerId, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(assembly, playerId, TemplateSetTwo, SlotSetTwo);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            // 修复前：这里会把普通装备与套装共享的那份光环实例整个移除——即便两件套装件仍然
            // 装备着、2 件门槛仍然满足（外部审计复现日志 set 分支 reproduced:True 的具体行为）。
            var unequippedOrdinary = assembly.Equipment.Unequip(playerId, SlotOrdinary);
            Assert.NotNull(unequippedOrdinary);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "套装 2 件门槛仍然满足，共享光环不应因为卸下普通装备而消失。");
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            // 门槛真正被打破（卸到只剩 1 件套装件）才应当移除光环。
            var unequippedSetOne = assembly.Equipment.Unequip(playerId, SlotSetOne);
            Assert.NotNull(unequippedSetOne);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "套装件数跌破 2 件门槛后，共享光环应当被移除。");
            Assert.Equal(1, assembly.Rules.Stats.GetStat(playerId, StatPower));

            var unequippedSetTwo = assembly.Equipment.Unequip(playerId, SlotSetTwo);
            Assert.NotNull(unequippedSetTwo);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));
        }

        /// <summary>反过来先卸套装件、再卸普通装备：验证共享句柄迁移/记账不偏袒任一侧的施加顺序。</summary>
        [Fact]
        public void UnequipSetPieceFirst_ThenOrdinaryItem_RemovesAuraOnlyAfterAllSourcesGone()
        {
            var assembly = BuildAssembly(out var playerId);

            EquipFreshInstance(assembly, playerId, TemplateOrdinary, SlotOrdinary);
            EquipFreshInstance(assembly, playerId, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(assembly, playerId, TemplateSetTwo, SlotSetTwo);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));

            // 卸到只剩 1 件套装件：套装门槛跌破，但普通装备仍在授予同一个 aura_def，光环应当保留
            // （这一次是套装侧先释放引用，普通装备侧仍持有）。
            var unequippedSetOne = assembly.Equipment.Unequip(playerId, SlotSetOne);
            Assert.NotNull(unequippedSetOne);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "普通装备仍在授予同一个 aura_def，套装门槛跌破不应清空光环。");
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            var unequippedOrdinary = assembly.Equipment.Unequip(playerId, SlotOrdinary);
            Assert.NotNull(unequippedOrdinary);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "最后一个来源（普通装备）也卸下后，共享光环才应当真正清空。");
            Assert.Equal(1, assembly.Rules.Stats.GetStat(playerId, StatPower));
        }
    }
}
