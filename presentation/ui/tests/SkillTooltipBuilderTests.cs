using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 技能提示框内容（ADR-0175）：名称/类型/读条/冷却/消耗/射程/效果数值/描述全部由 skill.def 数据（与调用方给的当前属性）算出，
    /// 期望值在用例里由同一份数据常量算出，不写死裸数；文案走本地化键，中英文各一份。
    /// </summary>
    public sealed class SkillTooltipBuilderTests
    {
        private static readonly Id Bolt = new Id("skill.stt_bolt");        // 读条 + 冷却 + 消耗 + 射程 + 缩放伤害 + 描述
        private static readonly Id Zap = new Id("skill.stt_zap");          // 瞬发、无冷却、无消耗
        private static readonly Id Mend = new Id("skill.stt_mend");        // 自身（射程 0）、读条、治疗
        private static readonly Id Beam = new Id("skill.stt_beam");        // 引导
        private static readonly Id Bare = new Id("skill.stt_bare");        // 没有名称键/描述键/任何可选项
        private static readonly Id Power = new Id("stat.stt_power");
        private static readonly Id Spirit = new Id("stat.stt_spirit");
        private static readonly Id Mana = new Id("arch.power.stt_mana");

        private const double BoltCast = 1.5;
        private const double BoltCooldown = 6;
        private const double BoltCost = 12;
        private const double BoltRange = 28;
        private const double BoltBase = 16;
        private const double BoltPowerCoeff = 1.0;
        private const double BoltSpiritCoeff = 0.25;
        private const double MendBase = 46;

        private static IDataRegistryView BuildRegistry()
        {
            var world = new UiWorldFixture();
            var source = new InMemoryDataSource()
                .Add("arch.power_type",
                    "{\"table\":\"arch.power_type\",\"schema_version\":1,\"rows\":[{\"id\":\"" + Mana.Value + "\",\"name_key\":\"l10n.stt.mana\"}]}")
                .Add("skill.def",
                    "{\"table\":\"skill.def\",\"schema_version\":1,\"rows\":[" +
                    "{\"id\":\"" + Bolt.Value + "\",\"name_key\":\"l10n.stt.bolt.name\",\"desc_key\":\"l10n.stt.bolt.desc\",\"range\":" + BoltRange +
                    ",\"cast_time\":" + BoltCast + ",\"cooldown_duration\":" + BoltCooldown + ",\"cost\":[{\"power_type\":\"" + Mana.Value + "\",\"amount\":" + BoltCost + "}]," +
                    "\"effects\":[{\"kind\":\"school_damage\",\"params\":{\"base_value\":" + BoltBase + ",\"scaling\":[{\"stat\":\"" + Power.Value + "\",\"coefficient\":" + BoltPowerCoeff +
                    "},{\"stat\":\"" + Spirit.Value + "\",\"coefficient\":" + BoltSpiritCoeff + "}]}}]}," +
                    "{\"id\":\"" + Zap.Value + "\",\"name_key\":\"l10n.stt.zap.name\",\"range\":20,\"cast_time\":0," +
                    "\"effects\":[{\"kind\":\"interrupt\",\"params\":{}}]}," +
                    "{\"id\":\"" + Mend.Value + "\",\"name_key\":\"l10n.stt.mend.name\",\"range\":0,\"cast_time\":1.5," +
                    "\"effects\":[{\"kind\":\"heal\",\"params\":{\"base_value\":" + MendBase + ",\"coefficient\":0}}]}," +
                    "{\"id\":\"" + Beam.Value + "\",\"name_key\":\"l10n.stt.beam.name\",\"range\":15,\"cast_time\":0,\"channel_time\":3,\"effects\":[]}," +
                    "{\"id\":\"" + Bare.Value + "\",\"range\":0,\"cast_time\":0,\"effects\":[]}]}");
            var registry = new DataRegistry(source, world.EventBus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.LoadAll();
            return registry;
        }

        private static Func<Id, string> English(Dictionary<Id, string> table) => key => table.TryGetValue(key, out var v) ? v : string.Empty;

        private static readonly Dictionary<Id, string> Zh = new Dictionary<Id, string>
        {
            [new Id("l10n.stt.bolt.name")] = "火球术",
            [new Id("l10n.stt.bolt.desc")] = "向目标投掷一枚火球。",
            [new Id("l10n.stt.mana")] = "法力",
            [SkillTooltipBuilder.KeyType] = "类型",
            [SkillTooltipBuilder.KeyTypeCast] = "读条",
            [SkillTooltipBuilder.KeyTypeInstant] = "瞬发",
            [SkillTooltipBuilder.KeyCastTime] = "施法时间",
            [SkillTooltipBuilder.KeyCooldown] = "冷却",
            [SkillTooltipBuilder.KeyCost] = "消耗",
            [SkillTooltipBuilder.KeyRange] = "射程",
            [SkillTooltipBuilder.KeyDamage] = "伤害",
            [SkillTooltipBuilder.KeyFmtSeconds] = "{0} 秒",
            [SkillTooltipBuilder.KeyFmtRange] = "{0} 米",
        };

        private static readonly Dictionary<Id, string> En = new Dictionary<Id, string>
        {
            [new Id("l10n.stt.bolt.name")] = "Fireball",
            [new Id("l10n.stt.bolt.desc")] = "Hurls a fireball at the target.",
            [new Id("l10n.stt.mana")] = "Mana",
            [SkillTooltipBuilder.KeyType] = "Type",
            [SkillTooltipBuilder.KeyTypeCast] = "Cast",
            [SkillTooltipBuilder.KeyTypeInstant] = "Instant",
            [SkillTooltipBuilder.KeyCastTime] = "Cast time",
            [SkillTooltipBuilder.KeyCooldown] = "Cooldown",
            [SkillTooltipBuilder.KeyCost] = "Cost",
            [SkillTooltipBuilder.KeyRange] = "Range",
            [SkillTooltipBuilder.KeyDamage] = "Damage",
            [SkillTooltipBuilder.KeyFmtSeconds] = "{0} s",
            [SkillTooltipBuilder.KeyFmtRange] = "{0} m",
        };

        [Fact]
        public void Build_CastSkill_AllFieldsComeFromData_NumbersAndRowsAgree()
        {
            var content = SkillTooltipBuilder.Build(BuildRegistry(), Bolt, English(Zh))!;

            Assert.Equal("火球术", content.Name);
            Assert.Equal("向目标投掷一枚火球。", content.Description);
            Assert.False(content.IsInstant);
            Assert.Equal(BoltCast, content.CastTime);
            Assert.Equal(BoltCooldown, content.CooldownDuration);
            Assert.Equal(BoltRange, content.Range);
            var cost = Assert.Single(content.Costs);
            Assert.Equal(Mana, cost.PowerType);
            Assert.Equal("法力", cost.PowerName);
            Assert.Equal(BoltCost, cost.Amount);
            Assert.Equal(
                new[]
                {
                    "类型=读条",
                    "施法时间=" + BoltCast + " 秒",
                    "冷却=" + BoltCooldown + " 秒",
                    "消耗=" + BoltCost + " 法力",
                    "射程=" + BoltRange + " 米",
                    "伤害=" + BoltBase, // 没给属性读取：只算基础值
                },
                content.Rows.Select(r => r.Label + "=" + r.Value).ToArray());
        }

        [Fact]
        public void Build_English_UsesEnglishWordsAndUnits_NoChineseLeaksWhenTableComplete()
        {
            var content = SkillTooltipBuilder.Build(BuildRegistry(), Bolt, English(En))!;

            Assert.Equal("Fireball", content.Name);
            Assert.Equal("Hurls a fireball at the target.", content.Description);
            var all = content.Name + content.Description + string.Concat(content.Rows.Select(r => r.Label + r.Value));
            Assert.DoesNotContain(all, c => c >= '一' && c <= '鿿');
            Assert.Contains(content.Rows, r => r.Label == "Cast time" && r.Value == BoltCast + " s");
            Assert.Contains(content.Rows, r => r.Label == "Range" && r.Value == BoltRange + " m");
        }

        [Fact]
        public void Build_WithStats_EffectValueIsBasePlusSumOfCoefficientTimesStat_CooldownAndCastUseEffectiveValues()
        {
            const double powerNow = 30;
            const double spiritNow = 8;
            const double effectiveCooldown = 4.5;
            const double effectiveCast = 1.2;
            var context = new SkillTooltipContext
            {
                Stat = id => id.Equals(Power) ? powerNow : id.Equals(Spirit) ? spiritNow : 0,
                CooldownDuration = effectiveCooldown,
                CastTime = effectiveCast,
            };

            var content = SkillTooltipBuilder.Build(BuildRegistry(), Bolt, English(Zh), context)!;

            var expected = BoltBase + BoltPowerCoeff * powerNow + BoltSpiritCoeff * spiritNow;
            var effect = Assert.Single(content.EffectValues);
            Assert.Equal(SkillTooltipBuilder.EffectSchoolDamage, effect.Key);
            Assert.Equal(expected, effect.Value, 9);
            Assert.Equal(effectiveCooldown, content.CooldownDuration);
            Assert.Equal(effectiveCast, content.CastTime);
            Assert.Contains(content.Rows, r => r.Label == "伤害" && r.Value == expected.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Contains(content.Rows, r => r.Label == "冷却" && r.Value == "4.5 秒");
        }

        [Fact]
        public void Build_InstantSkill_HasNoCastTimeRow_NoCooldownRow_NoCostRow()
        {
            var content = SkillTooltipBuilder.Build(BuildRegistry(), Zap, English(Zh))!;

            Assert.True(content.IsInstant);
            Assert.Equal("瞬发", content.TypeText);
            Assert.Equal(0, content.CastTime);
            Assert.Equal(new[] { "类型", "射程" }, content.Rows.Select(r => r.Label).ToArray());
            Assert.Equal(string.Empty, content.Description);
        }

        [Fact]
        public void Build_SelfSkill_RangeRowSaysSelf_HealUsesBaseValue()
        {
            var content = SkillTooltipBuilder.Build(BuildRegistry(), Mend, English(Zh))!;

            Assert.Equal(0, content.Range);
            Assert.Contains(content.Rows, r => r.Label == "射程" && r.Value == SkillTooltipBuilder.TextRangeSelf);
            var heal = Assert.Single(content.EffectValues);
            Assert.Equal(SkillTooltipBuilder.EffectHeal, heal.Key);
            Assert.Equal(MendBase, heal.Value);
        }

        [Fact]
        public void Build_ChannelSkill_TypeIsChannel_CastTimeIsChannelTime()
        {
            var content = SkillTooltipBuilder.Build(BuildRegistry(), Beam, null)!;

            Assert.False(content.IsInstant);
            Assert.Equal(SkillTooltipBuilder.TextTypeChannel, content.TypeText);
            Assert.Equal(3, content.CastTime);
        }

        [Fact]
        public void Build_NoTextFunction_FallsBackToChineseWords_ShortNameWhenNoNameKey_UnknownSkillReturnsNull()
        {
            var registry = BuildRegistry();
            var bare = SkillTooltipBuilder.Build(registry, Bare, null)!;
            Assert.Equal("stt_bare", bare.Name);
            Assert.Equal(string.Empty, bare.Description);
            Assert.Equal(SkillTooltipBuilder.TextTypeInstant, bare.TypeText);

            var bolt = SkillTooltipBuilder.Build(registry, Bolt, null)!;
            Assert.Contains(bolt.Rows, r => r.Label == SkillTooltipBuilder.LabelCooldown);
            Assert.Contains(bolt.Costs, c => c.PowerName == "stt_mana"); // 没传文本函数：资源名退回短名

            Assert.Null(SkillTooltipBuilder.Build(registry, new Id("skill.stt_missing"), null));
        }

        [Fact]
        public void Invariant_EveryRowPresentOnlyWhenItsDataIsPresent_AndBuildIsDeterministic()
        {
            var registry = BuildRegistry();
            foreach (var skill in new[] { Bolt, Zap, Mend, Beam, Bare })
            {
                var a = SkillTooltipBuilder.Build(registry, skill, English(En))!;
                var b = SkillTooltipBuilder.Build(registry, skill, English(En))!;
                Assert.Equal(a.Rows.Select(r => r.Label + r.Value), b.Rows.Select(r => r.Label + r.Value));

                // 行与数值字段互相印证
                Assert.Equal(a.CooldownDuration > 0, a.Rows.Any(r => r.Label == "Cooldown"));
                Assert.Equal(a.Costs.Count > 0, a.Rows.Any(r => r.Label == "Cost"));
                Assert.Equal(!a.IsInstant && a.CastTime > 0, a.Rows.Any(r => r.Label == "Cast time"));
                Assert.Equal(a.EffectValues.Count, a.Rows.Count(r => r.Label == "Damage" || r.Label == SkillTooltipBuilder.LabelHeal));
            }
        }
    }
}
