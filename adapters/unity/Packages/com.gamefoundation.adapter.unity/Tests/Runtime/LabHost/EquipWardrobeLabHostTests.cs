#nullable enable
// EquipWardrobeLabHostTests：实验室换装场景（衣橱）的引擎宿主验收（手感设计/06 第 3.6 节、08 第 6 节、ADR-0149）。
//
// 期望值全部由数据算出：物品清单、槽位数、纸娃娃件数、轮播格数都取自数据，不写裸数。
//   - 衣橱报告：引擎宿主上跑同一份数据生成的脚本，件数/步数/图层数与数据重放一致，资源核对（图标 + 每个方向 × 姿势键的层剪辑）零不一致；
//   - 轮播模型：遍历一整圈每格恰好一次，总格数 = 纸娃娃件数 × 姿势键数 × 方向数；
//   - 场景：逐件穿戴后纸娃娃图层数 = 已装备槽位里 paperdoll 外观的件数，卸空后为零；预览区的图像对象数跟随图层数。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adapter.Unity.LabHost;
using Adapter.Unity.Ui;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class EquipWardrobeLabHostTests
    {
        private static EngineLabHost Host => LabHostTestSupport.Host;

        [Test]
        public void CarouselModel_OneLapVisitsEveryCellExactlyOnce_AndTotalIsDerivedFromData()
        {
            var template = LabHostTestSupport.Script(EquipWardrobeRunner.TemplateScript);
            using var stage = WardrobeStage.Create(Host.Runner, template);
            var model = new WardrobeCarouselModel(stage.Entries, PaperdollPreview.Directions);
            var paperdoll = stage.Entries.Count(e => e.IsPaperdoll);
            Assert.Greater(paperdoll, 0);
            Assert.AreEqual(paperdoll * EquipWardrobeRunner.Poses.Length * PaperdollPreview.Directions.Length, model.Count);

            var lap = model.Lap();
            Assert.AreEqual(model.Count, lap.Count);
            Assert.AreEqual(model.Count, lap.Distinct().Count(), "一整圈每格恰好经过一次");
            Assert.AreEqual(0, model.Cursor, "走完一圈回到起点");
            foreach (var item in stage.Entries.Where(e => e.IsPaperdoll))
            {
                Assert.AreEqual(EquipWardrobeRunner.Poses.Length * PaperdollPreview.Directions.Length, lap.Count(c => c.Item == item.ItemId));
            }

            var empty = new WardrobeCarouselModel(new List<WardrobeEntry>(), PaperdollPreview.Directions);
            Assert.IsTrue(empty.IsEmpty);
            empty.Next();
            Assert.AreEqual(0, empty.Count);
        }

        [Test]
        public void Runner_RunsTheDataGeneratedScriptOnTheEngineHost_AndEveryResourceAuditMatches()
        {
            var result = EquipWardrobeRunner.Run(Host, "2d_action", writeFiles: false);
            var report = result.Report;
            var usedSlots = result.Entries.Select(e => e.SlotId).Distinct().Count();

            Assert.AreEqual(result.Entries.Count, report.EquipSteps);
            Assert.AreEqual(usedSlots, report.UnequipSteps);
            Assert.AreEqual(result.Entries.Count(e => e.IsPaperdoll), report.PaperdollItems);
            Assert.AreEqual(0, report.StepsFailed);
            Assert.AreEqual(0, report.IconMissing);
            Assert.AreEqual(0, report.LayerMismatch);
            Assert.AreEqual(0, report.SlotMismatch);

            // 轮播核对：每个（装备 × 方向）一格静态层核对 + 每个（装备 × 姿势键 × 方向）一格层剪辑核对，格数由数据算出；每格的资源与帧数与磁盘上的文件一致。
            var expectedCells = result.Entries.Count(e => e.IsPaperdoll) * PaperdollPreview.Directions.Length * (1 + EquipWardrobeRunner.Poses.Length);
            Assert.AreEqual(expectedCells, result.ExpectedCarouselCells);
            Assert.AreEqual(expectedCells, result.Carousel.Count);
            Assert.IsTrue(result.Carousel.All(c => !c.Mismatch), string.Join("; ", result.Carousel.Where(c => c.Mismatch).Select(c => c.Item + "/" + c.Pose + "/" + c.Direction)));
            Assert.AreEqual(0, report.AuditBad, string.Join("; ", report.Problems));
            Assert.Greater(report.AuditTotal, result.Carousel.Count);
            Assert.IsTrue(report.Passed, string.Join("; ", report.Problems));
            Assert.IsNull(result.ReportPath, "writeFiles:false 不写任何本地产物");
            TestContext.Out.WriteLine($"[wardrobe] items={report.Items} slots={result.SlotCount} carousel={result.Carousel.Count} audit={report.AuditTotal}/{report.AuditBad}");
        }

        [UnityTest]
        public IEnumerator Scene_WalksEveryItem_PaperdollLayerCountEqualsWornPaperdollSlots_AndEmptiesOnUnequipAll()
        {
            var go = new GameObject("EquipWardrobeSceneUnderTest");
            try
            {
                var scene = go.AddComponent<EquipWardrobeScene>();
                typeof(EquipWardrobeScene).GetField("autoAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(scene, false);
                scene.Begin(Host);
                yield return null;      // Start 再来一次 Begin：不应重复搭舞台

                var stage = scene.Stage!;
                var panel = scene.Panel!;
                Assert.AreEqual(stage.SlotCount, panel.Cells.Count, "槽位格数 = 数据里的装备槽位数");
                var worn = new Dictionary<string, WardrobeEntry>(StringComparer.Ordinal);
                for (var i = 0; i < stage.Entries.Count; i++)
                {
                    Assert.IsTrue(scene.StepNext());
                    worn[stage.Entries[i].SlotId] = stage.Entries[i];
                    var expectedLayers = worn.Values.Count(w => w.IsPaperdoll);
                    Assert.AreEqual(expectedLayers, stage.Panel.PaperdollLayers.Count);
                    Assert.AreEqual(expectedLayers, panel.Preview.EquipmentLayerCount, "预览区的装备层图像数 = 纸娃娃图层数");
                    Assert.AreEqual(worn.Count, stage.Panel.OccupiedCount);
                    Assert.AreEqual(i + 1, scene.StepIndex);
                }

                Assert.IsFalse(scene.StepNext(), "穿完一圈后下一步是卸空回绕");
                Assert.AreEqual(0, scene.StepIndex);
                Assert.AreEqual(0, stage.Panel.OccupiedCount);
                Assert.AreEqual(0, stage.Panel.PaperdollLayers.Count);
                Assert.AreEqual(0, panel.Preview.EquipmentLayerCount);
                Assert.AreSame(stage, scene.Stage);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
            }

            yield return null;
        }
    }
}
