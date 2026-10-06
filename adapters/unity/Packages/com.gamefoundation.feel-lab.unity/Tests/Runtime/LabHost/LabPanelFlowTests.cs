#nullable enable
// LabPanelFlowTests：按钮折行规划（纯函数）的守卫——批处理下也能跑，补 LabPlaygroundTextLayoutTests（需要 OnGUI 的真实布局度量）的不足。
using Adapter.Unity;
using System.Collections.Generic;
using System.Linq;
using FeelLab.Unity;
using NUnit.Framework;

namespace FeelLab.Unity.Tests
{
    [Category("module:lab")]
    public sealed class LabPanelFlowTests
    {
        [Test]
        public void Plan_NeverExceedsMaxWidth_AndKeepsOrderAndCount()
        {
            var widths = new List<float>();
            for (var i = 0; i < 37; i++)
            {
                widths.Add(40f + (i * 13) % 90);
            }

            const float max = 336f;
            var rows = LabPanelFlow.Plan(widths, max, LabPanelFlow.Spacing);
            var flat = rows.SelectMany(r => r).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(0, widths.Count).ToList(), flat, "顺序与数量不变");
            foreach (var row in rows)
            {
                var total = row.Sum(i => widths[i]) + LabPanelFlow.Spacing * (row.Length - 1);
                Assert.LessOrEqual(total, max + 1e-3f, "每行总宽不超过可用宽度");
            }

            Assert.Greater(rows.Count, 1, "37 个按钮一排放不下，必须折行");
        }

        [Test]
        public void Plan_UnbreakableWideButton_GetsItsOwnRow_WithoutWideningOthers()
        {
            var rows = LabPanelFlow.Plan(new[] { 80f, 500f, 80f }, 300f, LabPanelFlow.Spacing);
            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEqual(new[] { 1 }, rows[1]);
        }

        [Test]
        public void Plan_Empty_YieldsNoRows()
        {
            Assert.AreEqual(0, LabPanelFlow.Plan(new float[0], 300f, 4f).Count);
        }
    }
}
