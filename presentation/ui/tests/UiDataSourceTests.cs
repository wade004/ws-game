using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;
using Core.Rules.Common;
using Xunit;

namespace Tests.PresentationUi
{
    public class UiDataSourceTests
    {
        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Strength = new Id("stat.strength");

        [Fact]
        public void Query_player_power_current_and_max()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.PlayerId, new[] { Health });
            world.PowerHost.SetForTest(world.PlayerId, Health, 40, 100);

            Assert.Equal(40, world.DataSource.Query($"player.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(100, world.DataSource.Query($"player.power.{Health}.max")!.Value.AsNumber);
        }

        [Fact]
        public void Query_player_stat()
        {
            var world = new UiWorldFixture();
            world.StatHost.SetBase(world.PlayerId, Strength, 12);

            var value = world.DataSource.Query($"player.stat.{Strength}");
            Assert.Equal(12, value!.Value.AsNumber);
        }

        [Fact]
        public void Query_player_level_xp_and_xp_to_next()
        {
            var world = new UiWorldFixture();
            world.Progression.SetForTest(world.PlayerId, 5, 120, 500);

            Assert.Equal(5, world.DataSource.Query("player.level")!.Value.AsInt);
            Assert.Equal(120, world.DataSource.Query("player.xp")!.Value.AsInt);
            Assert.Equal(500, world.DataSource.Query("player.xp_to_next")!.Value.AsInt);
        }

        [Fact]
        public void Query_inventory_count_and_indexed_fields()
        {
            var world = new UiWorldFixture();
            var templateId = new Id("item.iron_sword");
            var instanceId = world.Inventory.AddItemForTest(world.PlayerId, templateId, 3);

            Assert.Equal(1, world.DataSource.Query("player.inventory.count")!.Value.AsInt);
            Assert.Equal(templateId, world.DataSource.Query("player.inventory[0].template")!.Value.AsId);
            Assert.Equal(3, world.DataSource.Query("player.inventory[0].count")!.Value.AsInt);
            Assert.Equal(instanceId, world.DataSource.Query("player.inventory[0].instance")!.Value.AsId);

            Assert.Null(world.DataSource.Query("player.inventory[5].template"));
        }

        [Fact]
        public void Query_equipment_slot()
        {
            var world = new UiWorldFixture();
            var slot = new Id("equip.main_hand");
            var instanceId = new Id("item.instance_0");
            world.Equipment.Equip(world.PlayerId, instanceId, slot);

            Assert.Equal(instanceId, world.DataSource.Query($"player.equipment.{slot}")!.Value.AsId);
            Assert.Null(world.DataSource.Query("player.equipment.equip.off_hand"));
        }

        /// <summary>
        /// ADR-0063（消费方反馈——游戏接入方第五批第 2 条）：<c>player.equipment.&lt;slot&gt;.template</c>/
        /// <c>.instance</c> 子路径——装备面板要显示"槽位名 + 已装备物品名"，需要已装备物品的模板 id。
        /// 期望值取自测试自己经 <see cref="FakeEquipmentHost.TemplatesByInstance"/> 登记的模板 id（不
        /// 写死裸数）；裸路径 <c>player.equipment.&lt;slot&gt;</c>（既有行为）与新增 <c>.instance</c>
        /// 子路径应当返回同一个实例 id。
        /// </summary>
        [Fact]
        public void Query_equipment_slot_template_and_instance_subpaths()
        {
            var world = new UiWorldFixture();
            var slot = new Id("equip.main_hand");
            var instanceId = new Id("item.instance_0");
            var templateId = new Id("item.iron_sword");
            world.Equipment.TemplatesByInstance[instanceId] = templateId;
            world.Equipment.Equip(world.PlayerId, instanceId, slot);

            Assert.Equal(instanceId, world.DataSource.Query($"player.equipment.{slot}")!.Value.AsId);
            Assert.Equal(instanceId, world.DataSource.Query($"player.equipment.{slot}.instance")!.Value.AsId);
            Assert.Equal(templateId, world.DataSource.Query($"player.equipment.{slot}.template")!.Value.AsId);
        }

        /// <summary>空槽（从未装备）与卸下后的 <c>.template</c>/<c>.instance</c> 子路径均返回"无"，
        /// 与背包空格既有口径（<c>inventory[5].template</c> 为 null）一致。</summary>
        [Fact]
        public void Query_equipment_slot_template_and_instance_subpaths_AreNull_WhenEmptyOrUnequipped()
        {
            var world = new UiWorldFixture();
            var slot = new Id("equip.main_hand");
            var instanceId = new Id("item.instance_0");
            var templateId = new Id("item.iron_sword");

            // 空槽：从未装备过。
            Assert.Null(world.DataSource.Query($"player.equipment.{slot}.template"));
            Assert.Null(world.DataSource.Query($"player.equipment.{slot}.instance"));

            // 装备后卸下：子路径回到"无"。
            world.Equipment.TemplatesByInstance[instanceId] = templateId;
            world.Equipment.Equip(world.PlayerId, instanceId, slot);
            Assert.NotNull(world.DataSource.Query($"player.equipment.{slot}.template"));

            world.Equipment.Unequip(world.PlayerId, slot);

            Assert.Null(world.DataSource.Query($"player.equipment.{slot}"));
            Assert.Null(world.DataSource.Query($"player.equipment.{slot}.template"));
            Assert.Null(world.DataSource.Query($"player.equipment.{slot}.instance"));
        }

        [Fact]
        public void Query_quest_state_and_objective()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.find_the_missing_child");
            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 2, 0 });

            Assert.Equal("Active", world.DataSource.Query($"player.quest.{questId}.state")!.Value.AsString);
            Assert.Equal(2, world.DataSource.Query($"player.quest.{questId}.objective[0]")!.Value.AsInt);
            Assert.Null(world.DataSource.Query($"player.quest.{questId}.objective[9]"));
        }

        /// <summary>消费方反馈第六批（阻塞）根治：<c>player.quest.&lt;questId&gt;.title_key</c>/
        /// <c>.objective_description_key[i]</c> 两条新子路径应转发 <see cref="IQuestHost.GetQuestTitleKey"/>/
        /// <see cref="IQuestHost.GetObjectiveDescriptionKey"/>；未设置文本键时返回"无"
        /// （<c>Query</c> 结果为 <c>null</c>），同既有 <c>objective[9]</c> 越界口径一致。</summary>
        [Fact]
        public void Query_quest_title_key_and_objective_description_key()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.find_the_missing_child");
            var titleKey = new Id("l10n.quest.find_the_missing_child.title");
            var descKey0 = new Id("l10n.quest.find_the_missing_child.objective_0");
            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 2, 0 });
            world.Quest.SeedTitleKeyForTest(questId, titleKey);
            world.Quest.SeedObjectiveDescriptionKeyForTest(questId, 0, descKey0);

            Assert.Equal(titleKey, world.DataSource.Query($"player.quest.{questId}.title_key")!.Value.AsId);
            Assert.Equal(descKey0, world.DataSource.Query($"player.quest.{questId}.objective_description_key[0]")!.Value.AsId);
            // 第二条目标未 Seed 描述键 == 数据未填，降级为"无"；未知任务同样降级为"无"。
            Assert.Null(world.DataSource.Query($"player.quest.{questId}.objective_description_key[1]"));
            Assert.Null(world.DataSource.Query("player.quest.quest.never_registered.title_key"));
        }

        [Fact]
        public void Query_currency_balance()
        {
            var world = new UiWorldFixture();
            var gold = new Id("econ.currency.gold");
            world.Economy.SetBalanceForTest(world.PlayerId, gold, 250);

            Assert.Equal(250, world.DataSource.Query($"player.currency.{gold}")!.Value.AsInt);
        }

        [Fact]
        public void Query_skills_index_and_skill_cooldown()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.LearnForTest(world.PlayerId, fireball);
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 1.5);

            Assert.Equal(fireball, world.DataSource.Query("player.skills[0]")!.Value.AsId);
            Assert.Null(world.DataSource.Query("player.skills[1]"));
            Assert.Equal(1.5, world.DataSource.Query($"player.skill.{fireball}.cooldown")!.Value.AsNumber);
        }

        [Fact]
        public void Query_target_returns_null_without_diagnostics_when_no_target_selected()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = null;

            Assert.Null(world.DataSource.Query($"target.power.{Health}.current"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_target_resolves_through_target_resolver()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = world.TargetId;
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 30, 60);

            Assert.Equal(30, world.DataSource.Query($"target.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(60, world.DataSource.Query($"target.power.{Health}.max")!.Value.AsNumber);
        }

        /// <summary>消费方反馈第 3 条（2026-09-20，ADR-0048）：<c>target.id</c> 新叶子路径——
        /// 有目标时原样返回其身份 Id，无目标时同既有 <c>target.*</c> 惯例返回 <c>null</c> 且不记
        /// 诊断（不是路径错误）。</summary>
        [Fact]
        public void Query_target_id_returns_current_target_identity()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = world.TargetId;

            Assert.Equal(world.TargetId, world.DataSource.Query("target.id")!.Value.AsId);
        }

        [Fact]
        public void Query_target_id_returns_null_without_diagnostics_when_no_target_selected()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = null;

            Assert.Null(world.DataSource.Query("target.id"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_unit_by_id_resolves_power_and_stat()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 10, 20);
            world.StatHost.SetBase(world.TargetId, Strength, 7);

            Assert.Equal(10, world.DataSource.Query($"unit.{world.TargetId}.power.{Health}.current")!.Value.AsNumber);
            Assert.Equal(7, world.DataSource.Query($"unit.{world.TargetId}.stat.{Strength}")!.Value.AsNumber);
        }

        [Fact]
        public void Query_unknown_root_returns_null_and_records_diagnostic()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("shrine.blessing"));
            Assert.Single(world.Diagnostics.Warnings);
        }

        [Fact]
        public void Query_malformed_path_returns_null_and_records_diagnostic()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("player..level"));
            Assert.Single(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 1 条（2026-09-21，ADR-0056）：无人读条时 <c>player.casting.*</c>
        /// 三条叶子路径均返回 <c>null</c>，且不记诊断——同既有 <c>target.id</c> 无目标时的"合法查询、
        /// 无值"惯例，不是路径错误。</summary>
        [Fact]
        public void Query_player_casting_returns_null_without_diagnostics_when_not_casting()
        {
            var world = new UiWorldFixture();

            Assert.Null(world.DataSource.Query("player.casting.skill"));
            Assert.Null(world.DataSource.Query("player.casting.remaining"));
            Assert.Null(world.DataSource.Query("player.casting.total"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 1 条：读条中，三条叶子路径原样转发 <c>ISkillBookQuery</c> 的取值。</summary>
        [Fact]
        public void Query_player_casting_reports_skill_remaining_and_total_while_casting()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.SetCastingForTest(world.PlayerId, fireball, remaining: 1.5, total: 2.0);

            Assert.Equal(fireball, world.DataSource.Query("player.casting.skill")!.Value.AsId);
            Assert.Equal(1.5, world.DataSource.Query("player.casting.remaining")!.Value.AsNumber);
            Assert.Equal(2.0, world.DataSource.Query("player.casting.total")!.Value.AsNumber);
            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 1 条（AGENTS.md §3"运行时路径不静默降级"）：技能宿主适配层确认
        /// "正在读条"（<c>GetCastingSkillId</c> 非空）却取不到剩余时长，这是数据不一致，不是"当前
        /// 没有人在读条"——断言诊断计数从 0 变成 1，且返回值仍是 <c>null</c>（不把这种情况悄悄
        /// 包装成一个看起来正常的空值）。</summary>
        [Fact]
        public void Query_player_casting_remaining_warnsOnce_whenSkillPresentButRemainingMissing()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.SetCastingForTest(world.PlayerId, fireball, remaining: null, total: null);

            Assert.Empty(world.Diagnostics.Warnings);

            var value = world.DataSource.Query("player.casting.remaining");

            Assert.Null(value);
            Assert.Single(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 1 条：<c>target.casting.*</c> 与 <c>player.casting.*</c> 复用同一段
        /// 解析逻辑，唯一差异是 unitId 来源（当前目标而非玩家自身）。</summary>
        [Fact]
        public void Query_target_casting_reports_skill_remaining_and_total_while_casting()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = world.TargetId;
            var frostbolt = new Id("skill.frostbolt");
            world.SkillBook.SetCastingForTest(world.TargetId, frostbolt, remaining: 0.6, total: 3.0);

            Assert.Equal(frostbolt, world.DataSource.Query("target.casting.skill")!.Value.AsId);
            Assert.Equal(0.6, world.DataSource.Query("target.casting.remaining")!.Value.AsNumber);
            Assert.Equal(3.0, world.DataSource.Query("target.casting.total")!.Value.AsNumber);
        }

        /// <summary>消费方反馈第 4 条（2026-09-21，ADR-0056）：<c>player.auras.count</c>/
        /// <c>player.auras[i].&lt;field&gt;</c> 原样转发 <c>IAuraQuery.GetActiveAuraSnapshots</c>
        /// 的每一项字段，顺序即该查询返回的顺序（不在表现层重新排序）。</summary>
        [Fact]
        public void Query_player_auras_reports_identity_stacks_remaining_total_and_name_key()
        {
            var world = new UiWorldFixture();
            var poison = new Id("skill.aura_def.poison");
            var weakness = new Id("skill.aura_def.weakness");
            world.AuraQuery.SetSnapshotsForTest(world.PlayerId, new[]
            {
                new AuraSnapshot(poison, stacks: 3, remaining: 4.2, total: 10.0, nameKey: new Id("l10n.aura.poison.name")),
                new AuraSnapshot(weakness, stacks: 1, remaining: 8.0, total: 8.0, nameKey: null),
            });

            Assert.Equal(2, world.DataSource.Query("player.auras.count")!.Value.AsInt);

            Assert.Equal(poison, world.DataSource.Query("player.auras[0].def")!.Value.AsId);
            Assert.Equal(3, world.DataSource.Query("player.auras[0].stacks")!.Value.AsInt);
            Assert.Equal(4.2, world.DataSource.Query("player.auras[0].remaining")!.Value.AsNumber);
            Assert.Equal(10.0, world.DataSource.Query("player.auras[0].total")!.Value.AsNumber);
            Assert.Equal(new Id("l10n.aura.poison.name"), world.DataSource.Query("player.auras[0].name_key")!.Value.AsId);

            Assert.Equal(weakness, world.DataSource.Query("player.auras[1].def")!.Value.AsId);
            Assert.Null(world.DataSource.Query("player.auras[1].name_key"));

            Assert.Null(world.DataSource.Query("player.auras[2].def"));
            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>
        /// 一个发现的交付缺口（2026-09-21，ADR-0060）：<c>player.auras[i].polarity</c>/
        /// <c>player.auras[i].icon_ref</c> 转发 <see cref="AuraSnapshot.Polarity"/>/
        /// <see cref="AuraSnapshot.IconRef"/>；<see cref="AuraSnapshot.Polarity"/> 是结构化枚举
        /// <see cref="AuraPolarity"/>，路径查询这一层没有枚举值类型（<c>ExprValue</c> 判别联合只有
        /// Bool/Int/Number/String/Id 五种），按既有惯例降级为该枚举在数据表上对应的文本（由
        /// <c>AuraPolarityNames.ToText</c> 转出，不是原样透传数据文件字面量）；未声明
        /// （<see cref="AuraPolarity.Undeclared"/>/<c>null</c>）时路径查询结果为"无"，同既有
        /// <c>name_key</c> 一贯口径，不记诊断。
        /// </summary>
        [Fact]
        public void Query_player_auras_reports_polarity_and_icon_ref()
        {
            var world = new UiWorldFixture();
            var buff = new Id("skill.aura_def.sample_buff");
            var debuff = new Id("skill.aura_def.sample_debuff");
            var buffIcon = new Id("icon.aura.sample_fortify");
            world.AuraQuery.SetSnapshotsForTest(world.PlayerId, new[]
            {
                new AuraSnapshot(buff, stacks: 1, remaining: 8.0, total: 8.0, nameKey: null, polarity: AuraPolarity.Beneficial, iconRef: buffIcon),
                new AuraSnapshot(debuff, stacks: 1, remaining: 6.0, total: 6.0, nameKey: null, polarity: AuraPolarity.Harmful, iconRef: null),
            });

            Assert.Equal("beneficial", world.DataSource.Query("player.auras[0].polarity")!.Value.AsString);
            Assert.Equal(buffIcon, world.DataSource.Query("player.auras[0].icon_ref")!.Value.AsId);

            Assert.Equal("harmful", world.DataSource.Query("player.auras[1].polarity")!.Value.AsString);
            Assert.Null(world.DataSource.Query("player.auras[1].icon_ref"));

            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 4 条：无光环时 <c>auras.count</c> 为 0，不记诊断。</summary>
        [Fact]
        public void Query_player_auras_count_isZero_withNoDiagnostics_whenNoActiveAuras()
        {
            var world = new UiWorldFixture();

            Assert.Equal(0, world.DataSource.Query("player.auras.count")!.Value.AsInt);
            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>消费方反馈第 4 条：<c>target.auras.*</c> 与 <c>player.auras.*</c> 复用同一段解析
        /// 逻辑。</summary>
        [Fact]
        public void Query_target_auras_reports_snapshots()
        {
            var world = new UiWorldFixture();
            world.CurrentTarget = world.TargetId;
            var silence = new Id("skill.aura_def.silence");
            world.AuraQuery.SetSnapshotsForTest(world.TargetId, new[]
            {
                new AuraSnapshot(silence, stacks: 1, remaining: 2.0, total: 4.0, nameKey: new Id("l10n.aura.silence.name")),
            });

            Assert.Equal(1, world.DataSource.Query("target.auras.count")!.Value.AsInt);
            Assert.Equal(silence, world.DataSource.Query("target.auras[0].def")!.Value.AsId);
            Assert.Equal(2.0, world.DataSource.Query("target.auras[0].remaining")!.Value.AsNumber);
        }

        [Fact]
        public void Subscribe_forwards_to_event_bus_and_fires_on_publish()
        {
            var world = new UiWorldFixture();
            var key = new Id("power.changed");
            var received = 0;
            var handle = world.DataSource.Subscribe(key, evt => received++);

            world.EventBus.PublishImmediate(new TestEvent(key));
            Assert.Equal(1, received);

            world.DataSource.Unsubscribe(handle);
            world.EventBus.PublishImmediate(new TestEvent(key));
            Assert.Equal(1, received);
        }

        private sealed class TestEvent : Core.Foundation.EventBus.IEvent
        {
            public TestEvent(Id key) => Key = key;
            public Id Key { get; }
        }
    }
}
