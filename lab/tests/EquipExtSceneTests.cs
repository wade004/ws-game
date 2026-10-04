using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>
    /// 副手与挂点装备场景（equip_ext_cycle 脚本，ADR-0153，手感设计/06 第 3.6 节、08 第 2 节）的运行期验收：
    /// 数据在独立数据根 <c>lab/fixtures/data/equip_ext</c>（占位副手匕首 + 占位挂点提灯 + 副手槽位 + 饰品槽位），<c>data/_equip</c> 不动。
    /// 期望值全部由数据与规则推出：副手叠加值 = 主手武器行 <c>writes</c> 的取值 再按副手武器行 <c>offhand_writes</c> 的 add/multiply 叠加，
    /// 面板计数由"已装备且外观是 paperdoll/挂点/无外观"的数据分类算出，不写裸数。
    /// </summary>
    public sealed class EquipExtSceneTests
    {
        private const string ScriptId = "equip_ext_cycle";
        private const string Cell = "2d_targeted";

        private static InputScript Script => LabTestSupport.Script(ScriptId);

        private static EquipRecording Record(string cell = Cell) =>
            LabTestSupport.Runner.Record(Script, cell).Equip ?? throw new InvalidOperationException("没有换装记录");

        private static IDataRegistryView Registry() => LabHost.BuildProbe(LabTestSupport.Runner.DatasetFor(Script).HostOptions).Registry;

        private sealed class WriteRow
        {
            public WriteRow(string field, string op, double value)
            {
                Field = field;
                Op = op;
                Value = value;
            }

            public string Field { get; }

            public string Op { get; }

            public double Value { get; }
        }

        private static List<WriteRow> Writes(DataRecord row, string key)
        {
            var list = new List<WriteRow>();
            if (!row.TryGetArray(key, out var array))
            {
                return list;
            }

            for (var i = 0; i < array.Count; i++)
            {
                var item = (JsonObject)array[i];
                item.TryGetValue("field", out var field);
                item.TryGetValue("op", out var op);
                item.TryGetValue("value", out var value);
                if (value is JsonNumber number)
                {
                    list.Add(new WriteRow(((JsonString)field!).Value, ((JsonString)op!).Value, number.Value));
                }
            }

            return list;
        }

        /// <summary>规则：主手武器行 writes 里 set 的取值，再逐条按副手武器行 offhand_writes 的 add/multiply 叠加（只叠加 offhand_stackable 字段）。</summary>
        private static double Expected(IDataRegistryView registry, string mainRef, string offhandRef, string field)
        {
            var main = registry.Get("feel.weapon", mainRef) ?? throw new InvalidOperationException(mainRef);
            var value = Writes(main, "writes").Last(w => w.Field == field && w.Op == "set").Value;
            if (offhandRef.Length == 0)
            {
                return value;
            }

            var offhand = registry.Get("feel.weapon", offhandRef) ?? throw new InvalidOperationException(offhandRef);
            foreach (var w in Writes(offhand, "offhand_writes").Where(w => w.Field == field))
            {
                value = w.Op == "add" ? value + w.Value : value * w.Value;
            }

            return value;
        }

        [Fact]
        public void EquipExt_EveryStepExecutes_AndHostUiPanelAgree_AndPanelCountsAddUp()
        {
            var equip = Record();
            var scriptSteps = Script.Events.Count(e => e.Kind == ScriptEventKind.Equip || e.Kind == ScriptEventKind.Unequip);
            Assert.Equal(scriptSteps, equip.Steps.Count);
            foreach (var s in equip.Steps)
            {
                Assert.True(s.Ok, $"换装步骤 tick {s.Tick} {s.Op} {s.Arg} 应成功");
                Assert.Equal(s.Family, s.PoseFamily);
                Assert.Equal(s.HostSlots, s.UiSlots);
                Assert.Equal(s.HostSlots, s.PanelSlots);
                Assert.Equal(s.PanelOccupied, s.PanelLayers + s.PanelNonSpriteVisuals + s.PanelNoVisuals);
                var hostCount = s.HostSlots.Length == 0 ? 0 : s.HostSlots.Split(',').Length;
                Assert.Equal(hostCount, s.PanelOccupied);
            }
        }

        [Fact]
        public void EquipExt_OffhandSlotAndSocketItems_ComeFromTheExtDataRootOnly_EquipRootUntouched()
        {
            var registry = Registry();
            var slots = registry.GetAll("item.slot_definition").Where(r => r.TryGetBool("is_equipment", out var e) && e).Select(r => r.Key).ToList();
            Assert.Contains("item.slot.std_off_hand", slots);
            Assert.Contains("item.slot.std_trinket", slots);
            // 副手槽位是第二个武器槽（按槽位 id 序数，同生产装配的主手/副手缺省规则）。
            var weaponSlots = registry.GetAll("item.slot_definition")
                .Where(r => r.TryGetBool("is_weapon", out var w) && w).Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "item.slot.std_main_hand", "item.slot.std_off_hand" }, weaponSlots);

            // 独立数据根不进占位装备集：data/_equip 里没有任何一行指向副手槽位或挂点。
            var equipRoot = System.IO.Path.Combine(LabTestSupport.RepoRoot(), "data", "_equip");
            foreach (var file in System.IO.Directory.EnumerateFiles(equipRoot, "*.json", System.IO.SearchOption.AllDirectories))
            {
                var text = System.IO.File.ReadAllText(file);
                Assert.DoesNotContain("std_off_hand", text);
                Assert.DoesNotContain("socket_attach", text);
            }
        }

        [Fact]
        public void EquipExt_OffhandWrites_StackOnMainHandValues_ByRule()
        {
            var registry = Registry();
            var equip = Record();
            var stacked = 0;
            var plain = 0;
            foreach (var s in equip.Steps.Where(s => s.MainRef.Length > 0))
            {
                var tier = Expected(registry, s.MainRef, s.OffhandRef, FeelFieldNames.SfxSweetenerTier);
                var scale = Expected(registry, s.MainRef, s.OffhandRef, FeelFieldNames.ImpactVfxScale);
                Assert.Equal(tier, s.SweetenerTier);
                Assert.Equal(scale, s.ImpactVfxScale, 9);
                if (s.OffhandRef.Length > 0) stacked++; else plain++;
            }

            // 场景有意义：既有"带副手叠加"的步骤，也有"无副手"的对照步骤，且叠加后的值确实不同于无副手的值。
            Assert.True(stacked > 0 && plain > 0);
            var withOff = equip.Steps.First(s => s.OffhandRef.Length > 0);
            var withoutOff = equip.Steps.First(s => s.MainRef.Length > 0 && s.OffhandRef.Length == 0);
            Assert.NotEqual(withoutOff.SweetenerTier, withOff.SweetenerTier);
            Assert.NotEqual(withoutOff.ImpactVfxScale, withOff.ImpactVfxScale);
        }

        [Fact]
        public void EquipExt_OffhandOnlyStacksOffhandStackableFields_NotTheOffhandRowsOwnWrites()
        {
            // 副手武器行自己的 writes（它作为主手时的取值）不参与副手叠加：命中特效强度的 writes 取值与叠加结果不同，读数必须是叠加结果。
            var registry = Registry();
            var equip = Record();
            var step = equip.Steps.First(s => s.OffhandRef.Length > 0);
            var offhand = registry.Get("feel.weapon", step.OffhandRef)!;
            var ownScale = Writes(offhand, "writes").Last(w => w.Field == FeelFieldNames.ImpactVfxScale).Value;
            var stackedScale = Expected(registry, step.MainRef, step.OffhandRef, FeelFieldNames.ImpactVfxScale);
            Assert.NotEqual(ownScale, stackedScale);
            Assert.Equal(stackedScale, step.ImpactVfxScale, 9);
            // 副手武器行里非 offhand_stackable 字段的写入不出现在 offhand_writes（数据层校验另有规则，这里确认夹具遵守）。
            var stackable = new[] { FeelFieldNames.SfxSweetenerTier, FeelFieldNames.ImpactVfxScale };
            Assert.All(Writes(offhand, "offhand_writes"), w => Assert.Contains(w.Field, stackable));
        }

        [Fact]
        public void EquipExt_OffhandEquip_RecalculatesExactlyOnce_AndIsNotCountedAsStale()
        {
            // 复现：换装链的状态含副手引用，穿副手武器时主手不变、解析仍恰好重算一次（版本 +1、一次 feel.weapon_changed）。
            // 度量组此前只认主手变化，会把这一步误记为"武器没变却重算"（stale_version_steps = 1）。
            var equip = Record();
            var offhandStep = equip.Steps.First(s => s.Op == "equip" && s.OffhandChanged);
            Assert.False(offhandStep.WeaponChanged, "穿副手不改变主手引用");
            Assert.Equal(1, offhandStep.FeelVersionDelta);
            Assert.Equal(1, offhandStep.WeaponChangedEvents);

            var fp = LabTestSupport.Runner.Run(Script, Cell);
            var group = (JsonObject)fp.Groups["equip"];
            Assert.Equal(0.0, ((JsonNumber)Get(group, "stale_version_steps")).Value);
            Assert.Equal(0.0, ((JsonNumber)Get(group, "unrefreshed_steps")).Value);
        }

        [Fact]
        public void EquipExt_SocketAttachItem_IsNonSpriteVisual_NotAPaperdollLayer_AndSocketIsDeclaredByAModelRow()
        {
            var registry = Registry();
            var equip = Record();
            var visualRows = registry.GetAll("display.equip_visual").Where(r => r.TryGetString("item_id", out _)).ToDictionary(r => r.Raw.TryGetValue("item_id", out var v) ? ((JsonString)v).Value : string.Empty, r => r, StringComparer.Ordinal);

            foreach (var s in equip.Steps)
            {
                // 面板里的计数由"已装备槽位里每件物品的外观行"算出：paperdoll（slot_mesh + paperdoll. 前缀）= 图层，其余有外观行的 = 非 2D，没有外观行的 = 无外观。
                var worn = s.HostSlots.Length == 0 ? new string[0] : s.HostSlots.Split(',').Select(p => p.Split('=')[1]).ToArray();
                var layers = worn.Count(item => visualRows.TryGetValue(item, out var v) && IsPaperdoll(v));
                var nonSprite = worn.Count(item => visualRows.TryGetValue(item, out var v) && !IsPaperdoll(v));
                Assert.Equal(layers, s.PanelLayers);
                Assert.Equal(nonSprite, s.PanelNonSpriteVisuals);
            }

            // 提灯是挂点附着：挂点 id 必须被某个 model 型 display.map 行声明（与导入校验 equip_model_socket_unknown 同一规则）。
            var lanternStep = equip.Steps.First(s => s.Arg == "item.std_lantern");
            Assert.StartsWith("socket_attach|", lanternStep.Visual);
            var socket = lanternStep.Visual.Split('|')[1];
            var declared = registry.GetAll("display.map")
                .Where(r => r.TryGetString("kind", out var k) && k == "model")
                .SelectMany(r => r.TryGetIdList("sockets", out var ids) ? ids.Select(i => i.Value) : Enumerable.Empty<string>())
                .ToList();
            Assert.Contains(socket, declared);
        }

        private static bool IsPaperdoll(DataRecord visual) =>
            visual.TryGetString("mode", out var mode) && mode == "slot_mesh"
            && visual.TryGetId("mesh_ref", out var mesh) && mesh.Value.StartsWith("paperdoll.", StringComparison.Ordinal);

        [Fact]
        public void EquipExt_OffhandMetricGroup_AppearsOnlyWhereOffhandOrSocketItemsAreWorn_ExistingEquipFingerprintsUntouched()
        {
            var ext = LabTestSupport.Runner.Run(Script, Cell);
            Assert.True(ext.Groups.ContainsKey("equip_offhand"));
            var offhand = (JsonObject)ext.Groups["equip_offhand"];
            Assert.Equal(0.0, ((JsonNumber)Get(offhand, "panel_count_mismatch")).Value);

            foreach (var id in new[] { "equip_cycle", "equip_cycle_tick30", "move_tap" })
            {
                var fp = LabTestSupport.Runner.Run(LabTestSupport.Script(id), Cell);
                Assert.False(fp.Groups.ContainsKey("equip_offhand"), $"{id} 的指纹不应出现 equip_offhand 组（既有基线逐字不变）");
            }
        }

        [Fact]
        public void EquipExt_ZeroDiffMetrics_AreZero_AndGroupsIdenticalOnAllSixCells()
        {
            var first = LabTestSupport.Runner.Run(Script, LabTestSupport.AllCells[0]);
            foreach (var cell in LabTestSupport.AllCells)
            {
                var fp = LabTestSupport.Runner.Run(Script, cell);
                Assert.Equal(LabJson.Write(first.Groups["equip"]), LabJson.Write(fp.Groups["equip"]));
                Assert.Equal(LabJson.Write(first.Groups["equip_offhand"]), LabJson.Write(fp.Groups["equip_offhand"]));
                var text = fp.ToJson();
                foreach (var name in new[]
                {
                    "steps_failed", "layer_fallbacks", "unrefreshed_steps", "stale_version_steps", "pose_family_mismatch",
                    "icon_missing", "visual_missing", "weapon_style_missing", "weapon_style_mismatch", "ui_mismatch",
                    "attack_mismatches", "reference_mismatches", "panel_count_mismatch",
                })
                {
                    Assert.Contains("\"" + name + "\": 0", text);
                }
            }
        }

        [Fact]
        public void EquipExt_WardrobeOverTheExtDataset_WalksOffhandAndSocketItems_ReportIsClean()
        {
            // 衣橱由数据生成：模板换成 equip_ext_cycle，清单里自动出现副手匕首（paperdoll）与提灯（model），报告零不一致。
            var script = EquipWardrobe.Plan(LabTestSupport.Runner, Script, out var entries, out var slots);
            Assert.Contains(entries, e => e.ItemId == "item.std_offhand_dagger" && e.IsWeapon && e.IsPaperdoll);
            Assert.Contains(entries, e => e.ItemId == "item.std_lantern" && !e.IsWeapon && e.VisualKind == "model");

            // 数据集按脚本 id 缓存：衣橱脚本的 id 固定为 equip_wardrobe，与 equip_cycle 模板的衣橱共用同一 id 会取到另一份数据集，所以改一个独有的 id。
            var ext = InputScript.Parse(script.ToJson());
            ext.Meta.ScriptId = "equip_wardrobe_ext";
            var equip = LabTestSupport.Runner.Record(ext, Cell).Equip!;
            var report = EquipWardrobe.BuildReport(entries, slots, equip);
            Assert.True(report.Passed, string.Join("; ", report.Problems));
            Assert.Equal(entries.Count(e => e.VisualKind == "model"), report.ModelItems);
            Assert.Equal(0, report.LayerMismatch);
            Assert.Equal(0, report.SlotMismatch);
        }

        private static JsonValue Get(JsonObject obj, string key)
        {
            Assert.True(obj.TryGetValue(key, out var value), key);
            return value;
        }
    }
}
