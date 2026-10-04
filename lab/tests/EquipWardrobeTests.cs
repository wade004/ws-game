using System;
using System.Collections.Generic;
using System.Linq;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 衣橱（EquipWardrobe，手感设计/06 第 3.6 节、ADR-0149）：脚本与报告全部由数据算出——物品清单取自数据、
    /// 步数 = 物品数 + 用到的槽位数、纸娃娃图层数由数据重放核对；单件事实与 equip_cycle 一致；不新增度量组、既有基线不动。
    /// </summary>
    public sealed class EquipWardrobeTests
    {
        private static InputScript Template => LabTestSupport.Script("equip_cycle");

        private static (InputScript Script, List<WardrobeEntry> Entries, int Slots) Plan()
        {
            var script = EquipWardrobe.Plan(LabTestSupport.Runner, Template, out var entries, out var slots);
            return (script, entries, slots);
        }

        private static EquipRecording Record(InputScript script) =>
            LabTestSupport.Runner.Record(script, "2d_targeted").Equip ?? throw new InvalidOperationException("衣橱脚本没有换装记录");

        [Fact]
        public void Plan_ListsEveryEquippableItem_AndBuildsEquipThenUnequipEvents()
        {
            var (script, entries, slots) = Plan();

            // equip_cycle 里穿过的每件装备都在衣橱里（衣橱是数据里全部可装备物品，不是手写清单）。
            var cycleItems = Template.Events.Where(e => e.Kind == ScriptEventKind.Equip).Select(e => e.Action).Distinct().ToList();
            Assert.NotEmpty(entries);
            foreach (var item in cycleItems)
            {
                Assert.Contains(entries, e => e.ItemId == item);
            }

            // 次序：槽位 sort_weight 非降；同一物品只出现一次。
            for (var i = 1; i < entries.Count; i++)
            {
                Assert.True(entries[i - 1].SortWeight <= entries[i].SortWeight);
            }

            Assert.Equal(entries.Count, entries.Select(e => e.ItemId).Distinct().Count());
            Assert.True(slots >= entries.Select(e => e.SlotId).Distinct().Count());

            // 事件 = 每件一次 equip + 每个用到的槽位一次 unequip；tick 严格递增且落在时长内。
            var usedSlots = entries.Select(e => e.SlotId).Distinct().ToList();
            Assert.Equal(entries.Count, script.Events.Count(e => e.Kind == ScriptEventKind.Equip));
            Assert.Equal(usedSlots.Count, script.Events.Count(e => e.Kind == ScriptEventKind.Unequip));
            for (var i = 1; i < script.Events.Count; i++)
            {
                Assert.True(script.Events[i].Tick > script.Events[i - 1].Tick);
            }

            Assert.True(script.Meta.DurationTicks > script.Events[script.Events.Count - 1].Tick);
            Assert.Equal(EquipWardrobe.ScriptId, script.Meta.ScriptId);

            // 序列化往返不丢事件；模板脚本不被改动。
            var round = InputScript.Parse(script.ToJson());
            Assert.Equal(script.Events.Count, round.Events.Count);
            Assert.NotEqual(EquipWardrobe.ScriptId, Template.Meta.ScriptId);
        }

        [Fact]
        public void Plan_ClassifiesVisuals_FromEquipVisualData()
        {
            var (_, entries, _) = Plan();
            var probe = LabHost.BuildProbe(LabTestSupport.Runner.DatasetFor(Template).HostOptions);
            var byItem = new Dictionary<string, Core.Foundation.DataRegistry.DataRecord>(StringComparer.Ordinal);
            foreach (var row in probe.Registry.GetAll("display.equip_visual"))
            {
                if (row.TryGetString("item_id", out var item))
                {
                    byItem[item] = row;
                }
            }

            foreach (var entry in entries)
            {
                if (!byItem.TryGetValue(entry.ItemId, out var visual))
                {
                    Assert.Equal("none", entry.VisualKind);
                    continue;
                }

                var paperdoll = visual.TryGetString("mode", out var mode) && mode == "slot_mesh"
                    && visual.TryGetId("mesh_ref", out var mesh) && mesh.Value.StartsWith("paperdoll.", StringComparison.Ordinal);
                Assert.Equal(paperdoll ? "paperdoll" : "model", entry.VisualKind);
            }
        }

        [Fact]
        public void Wardrobe_EveryStepRuns_AndReportIsCleanOnPlaceholderEquipSet()
        {
            var (script, entries, slots) = Plan();
            var equip = Record(script);
            var report = EquipWardrobe.BuildReport(entries, slots, equip);

            var usedSlots = entries.Select(e => e.SlotId).Distinct().Count();
            Assert.Equal(entries.Count + usedSlots, equip.Steps.Count);
            Assert.Equal(entries.Count, report.EquipSteps);
            Assert.Equal(usedSlots, report.UnequipSteps);
            Assert.Equal(entries.Count, report.Rows.Count);
            Assert.Equal(entries.Count(e => e.IsPaperdoll), report.PaperdollItems);
            Assert.Equal(entries.Count(e => e.IsWeapon), report.WeaponItems);
            Assert.Equal(entries.Count, report.PaperdollItems + report.ModelItems + report.NoVisualItems);

            // 占位装备集（参照实现）下"缺失/不一致"类计数恒为 0。
            Assert.Equal(0, report.StepsFailed);
            Assert.Equal(0, report.IconMissing);
            Assert.Equal(0, report.VisualMissing);
            Assert.Equal(0, report.SlotMismatch);
            Assert.Equal(0, report.LayerMismatch);
            Assert.True(report.Passed, string.Join("; ", report.Problems));
            Assert.Equal(report.Items, report.Rows.Count);
        }

        [Fact]
        public void Wardrobe_PanelLayerCountInvariant_HoldsAfterEveryStep_AndEmptiesAfterUnequipAll()
        {
            var (script, entries, slots) = Plan();
            var equip = Record(script);

            foreach (var s in equip.Steps)
            {
                // 面板：图层 + 非 2D 外观 + 无外观 = 已装备槽位数，且等于装备宿主里的件数。
                Assert.Equal(s.PanelOccupied, s.PanelLayers + s.PanelNonSpriteVisuals + s.PanelNoVisuals);
                var hostCount = s.HostSlots.Length == 0 ? 0 : s.HostSlots.Split(',').Length;
                Assert.Equal(hostCount, s.PanelOccupied);
                Assert.Equal(s.HostSlots, s.PanelSlots);
            }

            // 全卸之后面板空。
            var last = equip.Steps[equip.Steps.Count - 1];
            Assert.Equal(0, last.PanelOccupied);
            Assert.Equal(0, last.PanelLayers);

            // 复现：数据重放算出的期望图层数与面板读数逐步一致（把重放里的 paperdoll 判定篡改，报告必须出现图层不一致）。
            var tampered = entries.Select(e => new WardrobeEntry
            {
                ItemId = e.ItemId, SlotId = e.SlotId, SortWeight = e.SortWeight, IsWeapon = e.IsWeapon,
                VisualKind = e.IsPaperdoll ? "model" : e.VisualKind,
            }).ToList();
            var bad = EquipWardrobe.BuildReport(tampered, slots, equip);
            Assert.True(bad.LayerMismatch > 0 || entries.All(e => !e.IsPaperdoll));
        }

        [Fact]
        public void Wardrobe_SingleItemFacts_EqualEquipCycleFacts()
        {
            var (script, _, _) = Plan();
            var wardrobe = Record(script);
            var cycle = LabTestSupport.Runner.Record(Template, "2d_targeted").Equip!;

            // 单件事实：武器逐项相同（物品|槽位|族|外观|图标|武器表现档案）；非武器（族取决于当时主手，不比）比槽位/外观/图标。
            foreach (var c in cycle.Steps.Where(s => s.Op == "equip"))
            {
                var w = wardrobe.Steps.First(s => s.Op == "equip" && s.Arg == c.Arg);
                Assert.Equal(c.Slot, w.Slot);
                Assert.Equal(c.IsWeapon, w.IsWeapon);
                Assert.Equal(c.Visual, w.Visual);
                Assert.Equal(c.Icon, w.Icon);
                if (c.IsWeapon)
                {
                    Assert.Equal(c.Family, w.Family);
                    Assert.Equal(c.WeaponStyle, w.WeaponStyle);
                    Assert.Equal(c.MainRef, w.MainRef);
                }
            }
        }

        [Fact]
        public void Stage_BagAndInstanceEquip_FollowTheRealCarriers_AndLocalizedNamesComeFromData()
        {
            using var stage = WardrobeStage.Create(LabTestSupport.Runner, Template);
            var entry = stage.Entries.First(e => e.IsPaperdoll);
            Assert.Empty(stage.Bag.Slots);

            var instance = stage.AddToBag(entry.ItemId);
            Assert.Single(stage.Bag.Slots);
            Assert.Equal(instance, stage.Bag.Slots[0].InstanceId);
            Assert.Equal(0, stage.Panel.OccupiedCount);

            // 穿到不匹配的槽位被载体拒绝（结果原样带出失败原因），背包与装备都不变。
            var other = stage.Panel.Slots.First(s => s.SlotId.Value != entry.SlotId).SlotId;
            var rejected = stage.EquipInstance(instance, other);
            Assert.False(rejected.Success);
            Assert.Equal(Core.Carriers.Common.EquipFailureReason.SlotMismatch, rejected.Reason);
            Assert.Equal(0, stage.Panel.OccupiedCount);
            Assert.Single(stage.Bag.Slots);

            var accepted = stage.EquipInstance(instance, new Core.Foundation.Common.Id(entry.SlotId));
            Assert.True(accepted.Success);
            Assert.Equal(1, stage.Panel.OccupiedCount);
            Assert.Empty(stage.Bag.Slots);

            // 卸下后回到背包。
            Assert.True(stage.Unequip(entry.SlotId));
            Assert.Single(stage.Bag.Slots);

            // 物品名取自 l10n 数据行：模板的 name_key 经本地化宿主取到文案，不是 id。
            var nameKey = stage.Registry.Get("item.template", entry.ItemId)!.GetId("name_key");
            var text = stage.L10n.Text(nameKey);
            Assert.False(string.IsNullOrEmpty(text));
            Assert.DoesNotContain("std_", text);
        }

        [Fact]
        public void Stage_WalksEveryItem_PanelFollowsRealEquipmentState_AndEmptiesAfterUnequipAll()
        {
            using var stage = WardrobeStage.Create(LabTestSupport.Runner, Template);
            Assert.NotEmpty(stage.Entries);
            var worn = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
            foreach (var entry in stage.Entries)
            {
                Assert.True(stage.Equip(entry.ItemId), entry.ItemId + " 应能穿上");
                worn[entry.SlotId] = entry;

                // 面板的数量由真实装备状态与数据算出：已装备槽位数 = 数据重放的占用槽位数；图层数 = 其中 paperdoll 外观的件数。
                Assert.Equal(worn.Count, stage.Panel.OccupiedCount);
                Assert.Equal(worn.Values.Count(w => w.IsPaperdoll), stage.Panel.PaperdollLayers.Count);
                Assert.Equal(stage.Panel.OccupiedCount, stage.Panel.PaperdollLayers.Count + stage.Panel.NonSpriteVisualCount + stage.Panel.NoVisualCount);
            }

            // 槽位清单取自数据：面板槽位数 = 数据里装备槽位总数。
            Assert.Equal(stage.SlotCount, stage.Panel.Slots.Count);

            stage.UnequipAll();
            Assert.Equal(0, stage.Panel.OccupiedCount);
            Assert.Empty(stage.Panel.PaperdollLayers);
        }

        [Fact]
        public void Wardrobe_ReportJson_IsDeterministic_AndTemplateMustBeEquipScene()
        {
            var (script, entries, slots) = Plan();
            var a = EquipWardrobe.BuildReport(entries, slots, Record(script)).ToJson();
            var b = EquipWardrobe.BuildReport(entries, slots, Record(script)).ToJson();
            Assert.Equal(a, b);
            Assert.Contains("\"layerMismatch\": 0", a);

            var notEquip = LabTestSupport.Script("move_tap");
            Assert.Throws<LabFormatException>(() => EquipWardrobe.BuildScript(notEquip, entries));
        }
    }
}
