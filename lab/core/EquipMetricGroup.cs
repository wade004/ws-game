using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lab
{
    /// <summary>
    /// 换装解析度量组（手感设计/06 第 3.6 节"换装解析"组）：换装脚本里每次穿脱之后，换装链每一段的运行期事实
    /// ——武器引用、武器族、解析版本增量、冲击等级、顿帧 tick、音效层、姿势族与 idle/attack 姿势键、武器表现档案、
    /// 图标、外观映射、界面视图模型——以及换装后下一次普通攻击的相位 tick 实测值与由规则算出的期望值。
    /// <para>
    /// 判断记录（仅换装场景适用）：只有记录带 <see cref="LabRecording.Equip"/> 才计算（<see cref="IConditionalMetricGroup"/>），
    /// 既有脚本的指纹里没有这一组，既有基线逐字不变。全部度量都是逻辑类、逐字节比较——运行期事实只依赖模拟 tick 与数据。
    /// </para>
    /// <para>
    /// 判断记录（"缺失/不一致"类度量的期望恒为 0）：<c>icon_missing</c>、<c>visual_missing</c>、<c>layer_fallbacks</c>（音效层材质回落）、
    /// <c>ui_mismatch</c>、<c>pose_family_mismatch</c>、<c>unrefreshed_steps</c>、<c>stale_version_steps</c>、<c>attack_mismatches</c>
    /// 等是"运行期检查与导入校验静态报告零差异"的运行期一侧：占位装备集（参照实现）下它们必须为 0，基线里记的就是 0，
    /// 谁让它们变非 0 就是换装链断了一段。
    /// </para>
    /// </summary>
    public sealed class EquipMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("steps", MetricClass.Logic, "换装步骤数（穿/脱脚本事件）"),
            MetricSpec.Exact("steps_failed", MetricClass.Logic, "穿脱失败的步骤数"),
            MetricSpec.Exact("main_refs", MetricClass.Logic, "每步之后的主手武器手感引用（分号分隔，空手为 -）"),
            MetricSpec.Exact("families", MetricClass.Logic, "每步之后换装链对账出的武器族"),
            MetricSpec.Exact("pose_families", MetricClass.Logic, "每步之后姿势选择器里的武器族"),
            MetricSpec.Exact("idle_keys", MetricClass.Logic, "每步之后 idle 解析出的姿势集键"),
            MetricSpec.Exact("attack_keys", MetricClass.Logic, "每步之后 attack 解析出的姿势集键"),
            MetricSpec.Exact("impact_classes", MetricClass.Logic, "每步之后解析出的冲击等级"),
            MetricSpec.Exact("hitstop_ticks", MetricClass.Logic, "每步之后解析出的 攻击方/受击方 顿帧 tick"),
            MetricSpec.Exact("sfx_layers", MetricClass.Logic, "每步之后 swing>impact 音效层行 id"),
            MetricSpec.Exact("layer_fallbacks", MetricClass.Logic, "音效层材质行缺失而回落 generic 的层数合计"),
            MetricSpec.Exact("feel_version_deltas", MetricClass.Logic, "每步手感解析版本号增量"),
            MetricSpec.Exact("weapon_changes", MetricClass.Logic, "主手武器引用发生变化的步数"),
            MetricSpec.Exact("family_changes", MetricClass.Logic, "武器族发生变化的步数"),
            MetricSpec.Exact("feel_version_changes", MetricClass.Logic, "手感解析版本号递增的步数"),
            MetricSpec.Exact("unrefreshed_steps", MetricClass.Logic, "武器变了但版本没递增或没恰好发一次 feel.weapon_changed 的步数（应恒为 0）"),
            MetricSpec.Exact("stale_version_steps", MetricClass.Logic, "武器没变却重算了解析（版本递增）的步数（应恒为 0）"),
            MetricSpec.Exact("pose_family_mismatch", MetricClass.Logic, "姿势族与换装链武器族不一致的步数（应恒为 0）"),
            MetricSpec.Exact("icon_missing", MetricClass.Logic, "穿上的物品没有图标 id 的步数（应恒为 0）"),
            MetricSpec.Exact("visual_missing", MetricClass.Logic, "穿上的物品没有外观映射的步数（应恒为 0）"),
            MetricSpec.Exact("weapon_style_missing", MetricClass.Logic, "穿上武器后武器表现档案为空的步数（应恒为 0）"),
            MetricSpec.Exact("weapon_style_mismatch", MetricClass.Logic, "武器表现档案与武器手感行不同 id 的步数（应恒为 0）"),
            MetricSpec.Exact("ui_mismatch", MetricClass.Logic, "界面装备面板槽位与装备宿主不一致的步数（应恒为 0）"),
            MetricSpec.Exact("item_facts", MetricClass.Logic, "每次穿上的事实：物品|槽位|是否武器|族|外观|图标|武器表现档案"),
            MetricSpec.Exact("attacks", MetricClass.Logic, "换装场景里玩家的普通攻击动作数"),
            MetricSpec.Exact("attack_skills", MetricClass.Logic, "每次普攻的技能 id"),
            MetricSpec.Exact("attack_expected_ticks", MetricClass.Logic, "每次普攻按规则算出的 前摇/判定/后摇 tick（时间线毫秒 × 解析出的相位倍率，换算）"),
            MetricSpec.Exact("attack_measured_ticks", MetricClass.Logic, "每次普攻实测的 前摇/判定/后摇 tick（相位切换事件的 tick 差）"),
            MetricSpec.Exact("attack_durations", MetricClass.Logic, "每次普攻 action.started 携带的总时长 tick"),
            MetricSpec.Exact("attack_mismatches", MetricClass.Logic, "实测与期望（或与总时长）不一致的普攻数（应恒为 0）"),
            MetricSpec.Exact("reference_mismatches", MetricClass.Logic, "普攻技能时间线毫秒与武器 timeline_reference 不一致的相位数（应恒为 0）"),
            MetricSpec.Exact("unarmed_attacks", MetricClass.Logic, "空手（主手无武器行）发起的普攻数"),
        };

        public string Name => "equip";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Equip != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var equip = recording.Equip ?? throw new InvalidOperationException("equip 度量组只适用于带换装记录的运行");
            var steps = equip.Steps;

            var failed = 0;
            var weaponChanges = 0;
            var familyChanges = 0;
            var versionChanges = 0;
            var unrefreshed = 0;
            var stale = 0;
            var poseMismatch = 0;
            var iconMissing = 0;
            var visualMissing = 0;
            var styleMissing = 0;
            var styleMismatch = 0;
            var uiMismatch = 0;
            var fallbacks = 0;
            var previousFamily = string.Empty;

            var mainRefs = new List<string>();
            var families = new List<string>();
            var poseFamilies = new List<string>();
            var idleKeys = new List<string>();
            var attackKeys = new List<string>();
            var impacts = new List<string>();
            var hitstops = new List<string>();
            var sfx = new List<string>();
            var deltas = new List<string>();
            var facts = new List<string>();

            foreach (var s in steps)
            {
                if (!s.Ok) failed++;
                if (s.WeaponChanged) weaponChanges++;
                if (!string.Equals(s.Family, previousFamily, StringComparison.Ordinal)) familyChanges++;
                previousFamily = s.Family;
                if (s.FeelVersionDelta > 0) versionChanges++;
                if (s.WeaponChanged && (s.FeelVersionDelta <= 0 || s.WeaponChangedEvents != 1)) unrefreshed++;
                if (!s.WeaponChanged && s.FeelVersionDelta != 0) stale++;
                if (!string.Equals(s.PoseFamily, s.Family, StringComparison.Ordinal)) poseMismatch++;
                if (!string.Equals(s.UiSlots, s.HostSlots, StringComparison.Ordinal)) uiMismatch++;
                fallbacks += s.SfxFallbacks;

                var isEquip = string.Equals(s.Op, "equip", StringComparison.Ordinal);
                if (isEquip && s.Icon.Length == 0) iconMissing++;
                if (isEquip && s.Visual.Length == 0) visualMissing++;
                if (isEquip && s.IsWeapon)
                {
                    if (s.WeaponStyle.Length == 0)
                    {
                        styleMissing++;
                    }
                    else if (!string.Equals(Suffix(s.WeaponStyle, "display.weapon_style."), Suffix(s.MainRef, "feel.weapon."), StringComparison.Ordinal))
                    {
                        styleMismatch++;
                    }
                }

                mainRefs.Add(Dash(s.MainRef));
                families.Add(Dash(s.Family));
                poseFamilies.Add(Dash(s.PoseFamily));
                idleKeys.Add(Dash(s.IdleKey));
                attackKeys.Add(Dash(s.AttackKey));
                impacts.Add(Dash(s.ImpactClass));
                hitstops.Add(s.AttackerHitstopTicks.ToString(CultureInfo.InvariantCulture) + "/" + s.TargetHitstopTicks.ToString(CultureInfo.InvariantCulture));
                sfx.Add(Dash(s.SwingSfx) + ">" + Dash(s.ImpactSfx));
                deltas.Add(s.FeelVersionDelta.ToString(CultureInfo.InvariantCulture));
                if (isEquip)
                {
                    facts.Add(string.Join("|", s.Arg, s.Slot, s.IsWeapon ? "1" : "0", s.IsWeapon ? Dash(s.Family) : "-", Dash(s.Visual), Dash(s.Icon), s.IsWeapon ? Dash(s.WeaponStyle) : "-"));
                }
            }

            var attackSkills = new List<string>();
            var expected = new List<string>();
            var measured = new List<string>();
            var durations = new List<string>();
            var mismatches = 0;
            var referenceMismatches = 0;
            var unarmed = 0;
            foreach (var a in equip.Actions)
            {
                attackSkills.Add(a.Skill);
                var m1 = a.ActiveAt;
                var m2 = a.ActiveAt >= 0 && a.RecoveryAt >= 0 ? a.RecoveryAt - a.ActiveAt : -1;
                var m3 = a.RecoveryAt >= 0 && a.FinishedAt >= 0 ? a.FinishedAt - a.RecoveryAt : -1;
                expected.Add(Triple(a.ExpectedStartup, a.ExpectedActive, a.ExpectedRecovery));
                measured.Add(Triple(m1, m2, m3));
                durations.Add(a.DurationTicks.ToString(CultureInfo.InvariantCulture));
                var sum = a.ExpectedStartup + a.ExpectedActive + a.ExpectedRecovery;
                if (m1 != a.ExpectedStartup || m2 != a.ExpectedActive || m3 != a.ExpectedRecovery || a.DurationTicks != sum)
                {
                    mismatches++;
                }

                referenceMismatches += a.ReferenceMismatches;
                if (a.MainRef.Length == 0) unarmed++;
            }

            sink.Add("steps", steps.Count);
            sink.Add("steps_failed", failed);
            sink.Add("main_refs", string.Join(";", mainRefs));
            sink.Add("families", string.Join(";", families));
            sink.Add("pose_families", string.Join(";", poseFamilies));
            sink.Add("idle_keys", string.Join(";", idleKeys));
            sink.Add("attack_keys", string.Join(";", attackKeys));
            sink.Add("impact_classes", string.Join(";", impacts));
            sink.Add("hitstop_ticks", string.Join(";", hitstops));
            sink.Add("sfx_layers", string.Join(";", sfx));
            sink.Add("layer_fallbacks", fallbacks);
            sink.Add("feel_version_deltas", string.Join(";", deltas));
            sink.Add("weapon_changes", weaponChanges);
            sink.Add("family_changes", familyChanges);
            sink.Add("feel_version_changes", versionChanges);
            sink.Add("unrefreshed_steps", unrefreshed);
            sink.Add("stale_version_steps", stale);
            sink.Add("pose_family_mismatch", poseMismatch);
            sink.Add("icon_missing", iconMissing);
            sink.Add("visual_missing", visualMissing);
            sink.Add("weapon_style_missing", styleMissing);
            sink.Add("weapon_style_mismatch", styleMismatch);
            sink.Add("ui_mismatch", uiMismatch);
            sink.Add("item_facts", string.Join(";", facts));
            sink.Add("attacks", equip.Actions.Count);
            sink.Add("attack_skills", string.Join(";", attackSkills));
            sink.Add("attack_expected_ticks", string.Join(";", expected));
            sink.Add("attack_measured_ticks", string.Join(";", measured));
            sink.Add("attack_durations", string.Join(";", durations));
            sink.Add("attack_mismatches", mismatches);
            sink.Add("reference_mismatches", referenceMismatches);
            sink.Add("unarmed_attacks", unarmed);
        }

        private static string Dash(string value) => value.Length == 0 ? "-" : value;

        private static string Suffix(string value, string prefix) =>
            value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;

        private static string Triple(int a, int b, int c) =>
            a.ToString(CultureInfo.InvariantCulture) + "/" + b.ToString(CultureInfo.InvariantCulture) + "/" + c.ToString(CultureInfo.InvariantCulture);
    }
}
