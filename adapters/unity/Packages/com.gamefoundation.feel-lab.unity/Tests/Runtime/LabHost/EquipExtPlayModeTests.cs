#nullable enable
// EquipExtPlayModeTests：副手与挂点装备夹具（lab/fixtures/data/equip_ext，ADR-0153，手感设计/06 第 3.6 节、08 第 2/3 节）的装备面板与纸娃娃预览验收。
//
// 数据是独立数据根里的占位副手匕首（副手槽位，paperdoll 层 hand_off）与占位提灯（饰品槽位，socket_attach 挂点外观），经换装脚本
// equip_ext_cycle 声明的数据集进入衣橱舞台；面板是生产类 EquipmentPanel 绑生产类 EquipmentViewModel。期望值全部由数据算出，不写裸数：
//   - 槽位格数 = 数据里装备槽位总数，副手格与饰品格存在，格子顺序 = 槽位 sort_weight 次序；
//   - 逐件穿戴后预览区的装备层名集合 = 已装备槽位里 paperdoll 外观行 slot_id 的末段集合（主手 hand_main 与副手 hand_off 是两个不同的层）；
//   - 提灯是挂点附着型：不是 2D 图层（预览区层数不增），计入非 2D 外观数；
//   - 卸下副手后只少副手那一层，主手层不受影响。
// 渲染隔离：每个用例自建场景对象与 UiRoot，teardown 销毁；不读写任何资产文件（占位副手匕首没有入库的图片资产，预览层只核对层名与精灵集名）。
using Adapter.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FeelLab.Unity;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.DataRegistry;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FeelLab.Unity.Tests
{
    [Category("module:ui")]
    [Category("module:lab")]
    public sealed class EquipExtPlayModeTests
    {
        private const string TemplateScript = "equip_ext_cycle";

        private static EngineLabHost Host => LabHostTestSupport.Host;

        private static GameObject NewScene(out EquipWardrobeScene scene)
        {
            var go = new GameObject("EquipExtSceneUnderTest");
            scene = go.AddComponent<EquipWardrobeScene>();
            typeof(EquipWardrobeScene).GetField("autoAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(scene, false);
            scene.TemplateScriptId = TemplateScript;
            scene.Begin(Host);
            return go;
        }

        /// <summary>某件物品的纸娃娃层名（display.equip_visual 行 slot_id 的末段）；非 paperdoll 外观为 null。</summary>
        private static string? LayerOf(IDataRegistryView registry, WardrobeEntry entry)
        {
            if (!entry.IsPaperdoll)
            {
                return null;
            }

            foreach (var row in registry.GetAll("display.equip_visual"))
            {
                if (row.TryGetString("item_id", out var item) && item == entry.ItemId && row.TryGetString("slot_id", out var slot))
                {
                    return slot.Substring(slot.LastIndexOf('.') + 1);
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator Panel_HasOffhandAndTrinketCells_InSortWeightOrder_AndOccupiedCellsFollowTheWornItems()
        {
            var go = NewScene(out var scene);
            try
            {
                yield return null;
                var stage = scene.Stage!;
                var panel = scene.Panel!;

                Assert.AreEqual(stage.SlotCount, panel.Cells.Count, "槽位格数 = 数据里装备槽位总数");
                var ids = panel.Cells.Select(c => c.SlotId.Value).ToList();
                CollectionAssert.Contains(ids, "item.slot.std_off_hand");
                CollectionAssert.Contains(ids, "item.slot.std_trinket");
                // 格子顺序 = 视图模型槽位序 = 槽位 sort_weight 再按 id：副手紧跟主手之后，饰品在胸甲之后。
                CollectionAssert.AreEqual(stage.Panel.Slots.Select(s => s.SlotId.Value).ToList(), ids);
                Assert.Less(ids.IndexOf("item.slot.std_main_hand"), ids.IndexOf("item.slot.std_off_hand"));
                Assert.Less(ids.IndexOf("item.slot.std_chest"), ids.IndexOf("item.slot.std_trinket"));

                var worn = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
                for (var i = 0; i < stage.Entries.Count; i++)
                {
                    Assert.IsTrue(scene.StepNext());
                    worn[stage.Entries[i].SlotId] = stage.Entries[i];
                    for (var c = 0; c < panel.Cells.Count; c++)
                    {
                        var occupied = worn.ContainsKey(panel.Cells[c].SlotId.Value);
                        Assert.AreEqual(occupied, panel.Cells[c].Quality.enabled, panel.Cells[c].SlotId.Value + " 的品质框随占用显示");
                        Assert.AreEqual(!occupied, panel.Cells[c].Label.text.Length > 0, panel.Cells[c].SlotId.Value + " 的空槽显示槽位名");
                    }
                }

                Assert.AreEqual(worn.Count, stage.Panel.OccupiedCount);
                Assert.IsTrue(worn.ContainsKey("item.slot.std_off_hand") && worn.ContainsKey("item.slot.std_trinket"), "数据清单里副手与饰品槽位各有物品");
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Paperdoll_OffhandLayerIsADistinctLayer_LanternSocketItemIsNotA2dLayer_UnequipOffhandDropsOnlyThatLayer()
        {
            var go = NewScene(out var scene);
            try
            {
                yield return null;
                var stage = scene.Stage!;
                var panel = scene.Panel!;
                var registry = stage.Registry;

                var worn = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
                for (var i = 0; i < stage.Entries.Count; i++)
                {
                    Assert.IsTrue(scene.StepNext());
                    worn[stage.Entries[i].SlotId] = stage.Entries[i];

                    var expectedLayers = worn.Values.Select(e => LayerOf(registry, e)).Where(l => l != null).Select(l => l!).OrderBy(l => l, StringComparer.Ordinal).ToList();
                    var actualLayers = panel.Preview.EquipmentLayers.Select(l => l.Layer).OrderBy(l => l, StringComparer.Ordinal).ToList();
                    CollectionAssert.AreEqual(expectedLayers, actualLayers, "预览区装备层名集合 = 已装备槽位里 paperdoll 外观的层名");
                    Assert.AreEqual(worn.Values.Count(e => e.VisualKind == "model"), stage.Panel.NonSpriteVisualCount, "挂点附着等 model 外观计入非 2D 外观数");
                    Assert.AreEqual(actualLayers.Count, stage.Panel.PaperdollLayers.Count);
                }

                // 主手层与副手层是两个不同的层（副手匕首的层名 hand_off 来自数据，不是主手 hand_main）。
                var layers = panel.Preview.EquipmentLayers.Select(l => l.Layer).ToList();
                CollectionAssert.Contains(layers, "hand_off");
                CollectionAssert.Contains(layers, "hand_main");
                var offhand = stage.Entries.Single(e => e.ItemId == "item.std_offhand_dagger");
                var lantern = stage.Entries.Single(e => e.ItemId == "item.std_lantern");
                Assert.AreEqual("model", lantern.VisualKind);
                Assert.IsTrue(offhand.IsPaperdoll && offhand.IsWeapon);

                // 卸下副手：只少副手那一层，主手层与非 2D 外观数不变。
                var before = panel.Preview.EquipmentLayerCount;
                var nonSpriteBefore = stage.Panel.NonSpriteVisualCount;
                Assert.IsTrue(stage.Unequip(offhand.SlotId));
                scene.Panel!.RefreshUi();
                Assert.AreEqual(before - 1, panel.Preview.EquipmentLayerCount);
                CollectionAssert.DoesNotContain(panel.Preview.EquipmentLayers.Select(l => l.Layer).ToList(), "hand_off");
                CollectionAssert.Contains(panel.Preview.EquipmentLayers.Select(l => l.Layer).ToList(), "hand_main");
                Assert.AreEqual(nonSpriteBefore, stage.Panel.NonSpriteVisualCount);

                // 卸下提灯：层数不变（它本来就不是 2D 图层），非 2D 外观数减一。
                var layersBeforeLantern = panel.Preview.EquipmentLayerCount;
                Assert.IsTrue(stage.Unequip(lantern.SlotId));
                scene.Panel.RefreshUi();
                Assert.AreEqual(layersBeforeLantern, panel.Preview.EquipmentLayerCount);
                Assert.AreEqual(nonSpriteBefore - 1, stage.Panel.NonSpriteVisualCount);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }

            yield return null;
        }
    }
}
