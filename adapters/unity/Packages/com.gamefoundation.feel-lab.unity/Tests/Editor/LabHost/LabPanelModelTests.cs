#nullable enable
// LabPanelModelTests：面板模型的覆盖存储与 A/B 逻辑（手感设计/06 第 4 节"面板、覆盖存储与 A/B"）。
// 运行在无头宿主上（面板模型的运行方式由委托给出，覆盖与 A/B 的存储逻辑与引擎宿主上完全相同），所以放在编辑模式。
// 期望值都由规则得出：存储往返、校验问题清单、同一脚本两次相同运行没有差异、A/B 的差异来自覆盖的那个字段所属的度量组。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using FeelLab.Unity;
using Lab;
using NUnit.Framework;

namespace FeelLab.Unity.Tests.Editor
{
    [Category("module:lab")]
    public sealed class LabPanelModelTests
    {
        private const string Script = "feel_buffer_lead";
        private const string Cell = "2d_action";

        private EngineLabHost _host = null!;
        private string _dir = string.Empty;
        private string _store = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _host = EngineLabHost.Open();
            _dir = Path.Combine(Path.GetTempPath(), "gf_lab_panel_" + Guid.NewGuid().ToString("N"));
            _store = Path.Combine(_dir, "overrides.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        private LabPanelModel NewModel()
        {
            var model = new LabPanelModel(
                _host.LoadScripts(),
                script => _host.RunnableCells(script),
                (script, cell, overrides) => new PanelRunResult(_host.RunHeadless(script, cell, overrides), null, new List<ExpectationResult>()),
                _host.HeadlessRunner.Registry,
                _store);
            Assert.IsTrue(model.SelectScript(Script));
            Assert.IsTrue(model.SelectCell(Cell));
            return model;
        }

        [Test]
        public void SetOverride_PersistsToTheLocalStoreFile_AndRoundTrips()
        {
            var model = NewModel();
            Assert.IsFalse(File.Exists(_store), "没有写入之前不应有存储文件");

            var problems = model.SetOverride("narrow", "buffer_ms", "set", "0");
            Assert.IsEmpty(problems);
            Assert.IsTrue(File.Exists(_store), "写入覆盖后应立即落盘");

            var reopened = NewModel();
            var set = reopened.Store.Find("narrow");
            Assert.IsNotNull(set, "新开面板应从文件读回同一覆盖组");
            Assert.AreEqual(1, set!.Writes.Count);
            Assert.AreEqual("buffer_ms", set.Writes[0].Field);
            Assert.AreEqual(model.Store.ToJson(), reopened.Store.ToJson(), "读回的存储与内存里的存储逐字一致");
        }

        [Test]
        public void SetOverride_InvalidWrites_ReturnProblems_AndNeverTouchTheStore()
        {
            var model = NewModel();
            Assert.IsEmpty(model.SetOverride("keep", "buffer_ms", "set", "10"));
            var before = File.ReadAllText(_store);

            Assert.IsNotEmpty(model.SetOverride("bad", "no_such_field", "set", "1"), "未登记字段必须给出问题");
            Assert.IsNotEmpty(model.SetOverride("bad", "buffer_ms", "set", "not_a_number"), "值解析失败必须给出问题");
            Assert.IsNotEmpty(model.SetOverride("bad", "buffer_ms", "wobble", "1"), "未知操作必须给出问题");
            Assert.IsNotEmpty(model.SetOverride(" ", "buffer_ms", "set", "1"), "空组名必须给出问题");

            Assert.IsNull(model.Store.Find("bad"), "校验不通过不应并入存储");
            Assert.AreEqual(before, File.ReadAllText(_store), "校验不通过不应改动存储文件");
        }

        [Test]
        public void SetOverride_SameFieldAndUnit_ReplacesTheOldWrite()
        {
            var model = NewModel();
            Assert.IsEmpty(model.SetOverride("s", "buffer_ms", "set", "10"));
            Assert.IsEmpty(model.SetOverride("s", "buffer_ms", "set", "20"));
            var set = model.Store.Find("s")!;
            Assert.AreEqual(1, set.Writes.Count, "同字段同单位的写入应被替换，不累加");
            Assert.AreEqual(20.0, set.Writes[0].Value.AsNumber(), 1e-9);
            Assert.IsEmpty(model.SetOverride("s", "buffer_ms", "set", "5", "player"));
            Assert.AreEqual(2, model.Store.Find("s")!.Writes.Count, "不同作用单位是另一条写入");
        }

        [Test]
        public void Overrides_DoNotChangeSourceData_OnlyTheStoreFile()
        {
            var model = NewModel();
            var datasetBefore = _host.Runner.Dataset.Hash;
            var scriptPath = Path.Combine(_host.FixturesDir, "scripts", Script + LabFixtures.ScriptSuffix);
            var scriptTextBefore = File.ReadAllText(scriptPath);

            Assert.IsEmpty(model.SetOverride("narrow", "buffer_ms", "set", "0"));
            model.SelectOverride("narrow");
            model.Run();

            Assert.AreEqual(datasetBefore, _host.Runner.Dataset.Hash, "覆盖不得改动数据集内容");
            Assert.AreEqual(scriptTextBefore, File.ReadAllText(scriptPath), "覆盖不得改动脚本源文件");
        }

        [Test]
        public void RunAb_IdenticalSides_HaveNoDifference_AndAnOverrideShowsUpInItsOwnMetricGroup()
        {
            var model = NewModel();
            Assert.IsEmpty(model.SetOverride("narrow", "buffer_ms", "set", "0"));
            Assert.IsEmpty(model.SetOverride("empty_set", "buffer_ms", "set", "0"));

            // 不变量：两侧相同（都无覆盖）没有差异；实时类度量随时钟抖动，不进比较范围，这里只看逻辑与表现类。
            var same = model.RunAb(null, null);
            Assert.IsEmpty(NonRealTime(same), "同一脚本两次相同运行不应有逻辑/表现类差异");

            // 复现：把缓冲窗口收窄到 0，输入缓冲度量组变了。
            var ab = model.RunAb(null, "narrow");
            var differences = NonRealTime(ab);
            Assert.IsNotEmpty(differences, "收窄缓冲窗口后应能看到度量差异");
            Assert.IsTrue(differences.Exists(d => d.Group == "inputbuf"), "差异应落在输入缓冲组");

            // A/B 的方向：B − A 的差值与交换 A、B 后互为相反数。
            var ba = model.RunAb("narrow", null);
            foreach (var d in NonRealTime(ab))
            {
                var mirror = NonRealTime(ba).Find(x => x.FullName == d.FullName);
                Assert.IsNotNull(mirror, d.FullName);
                if (d.Delta.HasValue)
                {
                    Assert.AreEqual(-d.Delta.Value, mirror!.Delta!.Value, 1e-9, d.FullName);
                }
            }
        }

        [Test]
        public void RunAb_UnknownOverrideName_Throws_InsteadOfSilentlyComparingNothing()
        {
            var model = NewModel();
            Assert.Throws<InvalidOperationException>(() => model.RunAb(null, "ghost"));
            Assert.IsFalse(model.SelectOverride("ghost"));
        }

        [Test]
        public void Run_WithOverrideOnANonFeelScript_FailsLoudly()
        {
            var model = NewModel();
            Assert.IsEmpty(model.SetOverride("narrow", "buffer_ms", "set", "0"));
            Assert.IsTrue(model.SelectScript("move_tap"));
            model.SelectOverride("narrow");
            Assert.Throws<LabFormatException>(() => model.Run(), "非手感场景脚本带覆盖运行必须显式失败，不静默忽略");
        }

        [Test]
        public void RemoveWriteAndRemoveSet_UpdateTheFile_AndClearTheActiveSelection()
        {
            var model = NewModel();
            Assert.IsEmpty(model.SetOverride("s", "buffer_ms", "set", "10"));
            Assert.IsTrue(model.SelectOverride("s"));
            Assert.IsTrue(model.RemoveWrite("s", "buffer_ms"));
            Assert.AreEqual(0, OverrideStore.Load(_store).Find("s")!.Writes.Count);
            Assert.IsTrue(model.RemoveOverrideSet("s"));
            Assert.IsNull(OverrideStore.Load(_store).Find("s"));
            Assert.IsNull(model.ActiveOverride, "删掉当前选中的覆盖组后选择应清空");
        }

        [Test]
        public void MetricRows_FollowTheRegistryOrder_AndAreEmptyBeforeAnyRun()
        {
            var model = NewModel();
            Assert.IsEmpty(model.MetricRows());
            model.Run();
            var rows = model.MetricRows();
            Assert.IsNotEmpty(rows);
            Assert.IsTrue(rows.Exists(r => r.FullName.StartsWith("inputbuf.", StringComparison.Ordinal)), "手感脚本应带输入缓冲组度量");
        }

        private static List<MetricDifference> NonRealTime(AbReport report) =>
            new List<MetricDifference>(System.Linq.Enumerable.Where(report.Differences, d => d.Class != MetricClass.RealTime));
    }
}
