#nullable enable
// LabPlaygroundTextLayoutTests：试玩面板"按钮/条文字空白"缺陷的复现用例（ADR-0154）。
//
// 缺陷：场景页里"武器/体型/预设/效果开关"各是一排不换行的切换按钮，数据里模板很多时这一排比面板宽得多，
// 滚动区的内容宽度被它撑大，同页其它按钮（出靶子、清场、精英出手、时间尺度……）被拉到同样的宽度，居中的文字落在可见区之外——
// 看上去就是一排排没有字的空按钮和空条。根因是布局（内容比面板宽），不是字体缺字。
// 本用例在真实重绘里量滚动区的内容宽度：修复前内容宽远大于面板宽（失败），修复后不超过面板宽（通过）。
using System;
using System.Collections;
using System.IO;
using Adapter.Unity.LabHost;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class LabPlaygroundTextLayoutTests
    {
        private GameObject? _go;
        private LabPlayground? _pg;
        private string _saveDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_layout_test_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            _pg?.End();
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }

            if (Directory.Exists(_saveDir))
            {
                Directory.Delete(_saveDir, true);
            }
        }

        private LabPlayground NewPlayground()
        {
            _go = new GameObject("LabPlaygroundTextLayoutTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure("2d_action");
            pg.ManualDrive = true;
            pg.InputSource = new Adapters.Stub.StubInput();
            pg.PadReader = _ => false;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.Begin(), pg.Model.Status);
            _pg = pg;
            return pg;
        }

        private IEnumerator SampleTab(LabPlayground pg, LabTab tab)
        {
            if (Application.isBatchMode)
            {
                // 批处理模式下引擎不派发 OnGUI（没有可重绘的窗口），量不到真实布局；有窗口的编辑器里照常跑。批处理下的守卫见 LabPanelFlowTests。
                Assert.Ignore("批处理模式不派发 OnGUI 重绘，跳过真实布局度量（窗口模式下执行）。");
            }

            pg.Model.PanelVisible = true;
            pg.SetTab(tab);
            var before = pg.PanelLayoutSamples;
            for (var i = 0; i < 40 && (pg.PanelLayoutSamples < before + 2 || pg.PanelLayoutTab != tab); i++)
            {
                pg.Tick(1.0 / 60.0);
                pg.FinishFrame();
                yield return null;
            }

            Assert.AreEqual(tab, pg.PanelLayoutTab, "重绘样本应来自当前页");
            Assert.Greater(pg.PanelLayoutSamples, before, "OnGUI 重绘没有跑");
        }

        [UnityTest]
        public IEnumerator SceneTab_ContentFitsPanelWidth_SoButtonTextStaysVisible()
        {
            var pg = NewPlayground();
            yield return SampleTab(pg, LabTab.Scene);
            Assert.LessOrEqual(
                pg.PanelContentWidth, pg.PanelViewportWidth + 0.5f,
                "场景页内容宽 " + pg.PanelContentWidth + " 超过面板宽 " + pg.PanelViewportWidth + "：同页按钮被撑宽，居中文字落到可见区外");
        }

        [UnityTest]
        public IEnumerator EveryTab_ContentFitsPanelWidth()
        {
            var pg = NewPlayground();
            foreach (var tab in new[] { LabTab.Scene, LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating })
            {
                yield return SampleTab(pg, tab);
                Assert.LessOrEqual(pg.PanelContentWidth, pg.PanelViewportWidth + 0.5f, tab + " 页内容宽 " + pg.PanelContentWidth + " > 面板宽 " + pg.PanelViewportWidth);
            }
        }
    }
}
