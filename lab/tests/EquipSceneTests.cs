using System;
using System.Collections.Generic;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 换装场景（equip_cycle 脚本）的运行期验收：换装链每一段（武器引用、武器族、解析版本、姿势族、界面）逐步对账，
    /// 普攻相位 tick 实测值等于规则算出的期望值，六格逐字一致，既有脚本指纹不出现换装度量组。
    /// 期望值全部在用例里由规则（槽位顺序、时间线毫秒 × 倍率换算）推出，不写死裸数。
    /// </summary>
    public sealed class EquipSceneTests
    {
        private static LabRecording RecordEquip(string cell)
        {
            var recording = LabTestSupport.Runner.Record(LabTestSupport.Script("equip_cycle"), cell);
            Assert.NotNull(recording.Equip);
            return recording;
        }

        [Fact]
        public void EquipCycle_EveryStepExecutes_AndChainSegmentsAgree()
        {
            var equip = RecordEquip("2d_targeted").Equip!;

            Assert.Equal(8, equip.Steps.Count);
            foreach (var s in equip.Steps)
            {
                Assert.True(s.Ok, $"换装步骤 tick {s.Tick} {s.Op} {s.Arg} 应成功");
                // 姿势族与换装链武器族一致；界面装备面板与装备宿主一致。
                Assert.Equal(s.Family, s.PoseFamily);
                Assert.Equal(s.HostSlots, s.UiSlots);
            }

            // 武器变了的步骤：版本恰好递增且恰好发一次 feel.weapon_changed；武器没变的步骤（穿胸甲）一次都不重算。
            foreach (var s in equip.Steps)
            {
                if (s.WeaponChanged)
                {
                    // 恰好一次（换装链是唯一的失效源）：生产装配此前对每次装备/卸下另外无差别失效一次，武器变化会 +2、换护甲 +1。
                    Assert.Equal(1, s.FeelVersionDelta);
                    Assert.Equal(1, s.WeaponChangedEvents);
                }
                else
                {
                    Assert.Equal(0, s.FeelVersionDelta);
                    Assert.Equal(0, s.WeaponChangedEvents);
                }
            }

            var armor = equip.Steps.Find(s => s.Op == "equip" && !s.IsWeapon);
            Assert.NotNull(armor);
            Assert.False(armor!.WeaponChanged);

            // 卸下主手武器：武器引用与武器族清空，姿势回落到不带武器族的 idle/attack。
            var unequip = equip.Steps.Find(s => s.Op == "unequip");
            Assert.NotNull(unequip);
            Assert.Equal(string.Empty, unequip!.MainRef);
            Assert.Equal(string.Empty, unequip.Family);
            Assert.Equal("idle", unequip.IdleKey);
            Assert.Equal("attack", unequip.AttackKey);
        }

        [Fact]
        public void EquipCycle_AttackPhaseTicks_EqualRuleComputedExpectation()
        {
            var equip = RecordEquip("2d_targeted").Equip!;

            Assert.Equal(equip.Steps.Count, equip.Actions.Count);
            foreach (var a in equip.Actions)
            {
                Assert.Equal(a.ExpectedStartup, a.ActiveAt);
                Assert.Equal(a.ExpectedStartup + a.ExpectedActive, a.RecoveryAt);
                Assert.Equal(a.ExpectedStartup + a.ExpectedActive + a.ExpectedRecovery, a.FinishedAt);
                Assert.Equal(a.FinishedAt, a.DurationTicks);
                Assert.Equal(0, a.ReferenceMismatches);
            }

            // 换装之后下一次普攻用的是新主手武器的普攻（空手回落空手普攻），不是上一把的。
            for (var i = 0; i < equip.Steps.Count; i++)
            {
                Assert.Equal(equip.Steps[i].MainRef, equip.Actions[i].MainRef);
            }

            Assert.Contains(equip.Actions, a => a.MainRef.Length == 0);
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in equip.Actions)
            {
                distinct.Add(a.Skill);
            }

            Assert.True(distinct.Count > 1, "不同武器的普攻时间线应当不同");
        }

        [Fact]
        public void EquipCycle_EquipMetricGroup_IdenticalOnAllSixCells()
        {
            var script = LabTestSupport.Script("equip_cycle");
            var first = LabJson.Write(LabTestSupport.Runner.Run(script, LabTestSupport.AllCells[0]).Groups["equip"]);
            foreach (var cell in LabTestSupport.AllCells)
            {
                var fp = LabTestSupport.Runner.Run(script, cell);
                Assert.Equal(first, LabJson.Write(fp.Groups["equip"]));
            }
        }

        [Fact]
        public void EquipCycle_ZeroDiffMetrics_AreZero()
        {
            var fp = LabTestSupport.Runner.Run(LabTestSupport.Script("equip_cycle"), "2d_targeted");
            var text = fp.ToJson();
            foreach (var name in new[]
            {
                "steps_failed", "layer_fallbacks", "unrefreshed_steps", "stale_version_steps", "pose_family_mismatch",
                "icon_missing", "visual_missing", "weapon_style_missing", "weapon_style_mismatch", "ui_mismatch",
                "attack_mismatches", "reference_mismatches",
            })
            {
                Assert.Contains("\"" + name + "\": 0", text);
            }
        }

        [Fact]
        public void ExistingScripts_FingerprintHasNoEquipGroup()
        {
            foreach (var id in new[] { "move_tap", "attack_then_stop" })
            {
                var fp = LabTestSupport.Runner.Run(LabTestSupport.Script(id), "2d_targeted");
                Assert.False(fp.Groups.ContainsKey("equip"), $"{id} 的指纹不应出现 equip 组（既有基线逐字不变）");
            }
        }

        [Fact]
        public void EquipScene_NonSixtyTickRate_PhaseTicksFollowHostStep()
        {
            // 30 Hz 变体：动作时间线的毫秒到 tick 换算跟宿主步长走（生产装配回填 SkillOptions.ActionStepSeconds），不再写死 1/60。
            var at60 = RecordEquip("2d_targeted").Equip!;
            var script30 = LabTestSupport.Script("equip_cycle_tick30");
            Assert.Equal(30, script30.Meta.TickRate);
            var at30 = LabTestSupport.Runner.Record(script30, "2d_targeted").Equip!;

            Assert.Equal(1.0 / 30.0, at30.StepSeconds, 12);
            Assert.Equal(at60.Actions.Count, at30.Actions.Count);
            for (var i = 0; i < at30.Actions.Count; i++)
            {
                var a = at30.Actions[i];
                var b = at60.Actions[i];
                Assert.Equal(b.Skill, a.Skill);
                // 实测相位 tick 等于规则算出的期望值（期望按 30 Hz 换算），且与 60 Hz 的换算值不同（同一时间线毫秒在 30 Hz 下 tick 数减半）。
                Assert.Equal(a.ExpectedStartup, a.ActiveAt);
                Assert.Equal(a.ExpectedStartup + a.ExpectedActive, a.RecoveryAt);
                Assert.Equal(a.ExpectedStartup + a.ExpectedActive + a.ExpectedRecovery, a.FinishedAt);
                Assert.Equal(a.FinishedAt, a.DurationTicks);
                Assert.True(a.FinishedAt < b.FinishedAt, $"第 {i} 次普攻在 30 Hz 下的总 tick 数应少于 60 Hz（{a.FinishedAt} vs {b.FinishedAt}）");
            }
        }

        [Fact]
        public void EquipEvents_OutsideEquipScene_AreRejected()
        {
            var script = LabTestSupport.Build(
                "equip_outside",
                10,
                new[] { new ScriptEvent(1, "item.std_sword_1h", ScriptEventKind.Equip) });
            Assert.Throws<LabFormatException>(() => LabTestSupport.Runner.Record(script, "2d_targeted"));
        }

        [Fact]
        public void EquipScript_UsesFormatVersion2_OthersStayVersion1()
        {
            Assert.Equal(InputScript.ExtendedFormatVersion, LabTestSupport.Script("equip_cycle").EffectiveFormatVersion);
            Assert.Contains("\"formatVersion\": 2", LabTestSupport.Script("equip_cycle").ToJson());
            foreach (var s in LabTestSupport.StandardScripts())
            {
                if (s.Meta.Scene != "equip")
                {
                    Assert.Equal(InputScript.FormatVersion, s.EffectiveFormatVersion);
                    Assert.Contains("\"formatVersion\": 1", s.ToJson());
                }
            }
        }
    }
}
