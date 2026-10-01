using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>手感音效分层（手感设计/07 第 3 节）：<c>sfx.def</c> 的 feel_* 字段解析与 <see cref="SfxLayerIndex"/> 回落链。</summary>
    public class SfxLayerIndexTests
    {
        private static SfxDef Row(string id, SfxFeelLayer layer, int tier, string? material = null) =>
            new SfxDef(new Id(id), "combat", null, null, new Id("res." + id), false, layer, tier, material);

        private static SfxLayerIndex Index(PresentationDiagnosticsRecorder diag, params SfxDef[] rows) => new SfxLayerIndex(rows, diag);

        [Fact]
        public void ExactMaterialAndTier_Hits_WithoutFallback()
        {
            var index = Index(new PresentationDiagnosticsRecorder(),
                Row("sfx.hit_t2_metal", SfxFeelLayer.Impact, 2, "metal"),
                Row("sfx.hit_t2", SfxFeelLayer.Impact, 2));

            Assert.True(index.TryResolve(SfxFeelLayer.Impact, 2, "metal", out var r));

            Assert.Equal(new Id("sfx.hit_t2_metal"), r.SfxId);
            Assert.False(r.FellBack);
        }

        [Fact]
        public void MissingMaterialLayer_FallsBackToGenericOfSameTier_NoDiagnostic()
        {
            var diag = new PresentationDiagnosticsRecorder();
            var index = Index(diag, Row("sfx.hit_t2", SfxFeelLayer.Impact, 2), Row("sfx.hit_t1_metal", SfxFeelLayer.Impact, 1, "metal"));

            Assert.True(index.TryResolve(SfxFeelLayer.Impact, 2, "metal", out var r));

            // 先降材质、后降档：同档通用行优先于低一档的精确材质行。
            Assert.Equal(new Id("sfx.hit_t2"), r.SfxId);
            Assert.Equal(2, r.Tier);
            Assert.Equal(SfxLayerIndex.GenericMaterial, r.Material);
            Assert.True(r.FellBack);
            Assert.Empty(diag.Warnings);
        }

        [Fact]
        public void MissingTier_FallsBackDownwardOnly_NeverUpward()
        {
            var index = Index(new PresentationDiagnosticsRecorder(),
                Row("sfx.hit_t1", SfxFeelLayer.Impact, 1), Row("sfx.hit_t4", SfxFeelLayer.Impact, 4));

            Assert.True(index.TryResolve(SfxFeelLayer.Impact, 3, null, out var r));

            Assert.Equal(new Id("sfx.hit_t1"), r.SfxId);
            Assert.Equal(1, r.Tier);
        }

        [Fact]
        public void NothingRegistered_ReturnsFalse_AndRecordsOneDeduplicatedDiagnostic()
        {
            var diag = new PresentationDiagnosticsRecorder();
            var index = Index(diag, Row("sfx.whiff_t1", SfxFeelLayer.Whiff, 1));

            Assert.False(index.TryResolve(SfxFeelLayer.Impact, 2, "metal", out _));
            Assert.False(index.TryResolve(SfxFeelLayer.Impact, 2, "metal", out _));
            Assert.False(index.TryResolve(SfxFeelLayer.Impact, 2, "wood", out _));

            Assert.Equal(2, diag.Warnings.Count); // (Impact,2,metal) 一条、(Impact,2,wood) 一条
        }

        [Fact]
        public void NonPositiveTier_MeansLayerOff_NoDiagnostic()
        {
            var diag = new PresentationDiagnosticsRecorder();
            var index = Index(diag, Row("sfx.hit_t1", SfxFeelLayer.Impact, 1));

            Assert.False(index.TryResolve(SfxFeelLayer.Impact, 0, null, out _));

            Assert.Empty(diag.Warnings);
        }

        [Fact]
        public void DuplicateRows_TakeFirstByIdOrder_AndRecordDiagnostic()
        {
            var diag = new PresentationDiagnosticsRecorder();
            var index = Index(diag, Row("sfx.b_dup", SfxFeelLayer.Swing, 1), Row("sfx.a_dup", SfxFeelLayer.Swing, 1));

            Assert.True(index.TryResolve(SfxFeelLayer.Swing, 1, null, out var r));

            Assert.Equal(new Id("sfx.a_dup"), r.SfxId);
            Assert.Single(diag.Warnings);
            Assert.Equal(1, index.Count);
        }

        [Fact]
        public void RowsWithoutFeelLayer_AreNotIndexed()
        {
            var plain = new SfxDef(new Id("sfx.plain"), "combat", null, null, new Id("res.plain"), false);
            var index = new SfxLayerIndex(new[] { plain }, new PresentationDiagnosticsRecorder());

            Assert.Equal(0, index.Count);
        }

        // ------------------------------------------------------------------ sfx.def 数据表

        [Fact]
        public void SfxDef_FromRecord_ParsesFeelFields_AndOmittedFieldsStayNull()
        {
            const string rows = @"[
              { ""id"": ""sfx.hit_metal_t2"", ""layer"": ""combat"", ""resource_ref"": ""res.sfx.hit_metal_t2"",
                ""feel_layer"": ""impact"", ""feel_tier"": 2, ""feel_material"": ""metal_light"" },
              { ""id"": ""sfx.plain"", ""layer"": ""combat"", ""resource_ref"": ""res.sfx.plain"" }
            ]";
            var (registry, report) = VfxSfxTestSupport.BuildRegistry(new Dictionary<string, string> { ["sfx.def"] = rows });
            Assert.False(report.IsBlocking);

            var feel = SfxDef.FromRecord(registry.Get("sfx.def", "sfx.hit_metal_t2")!);
            var plain = SfxDef.FromRecord(registry.Get("sfx.def", "sfx.plain")!);

            Assert.Equal(SfxFeelLayer.Impact, feel.FeelLayer);
            Assert.Equal(2, feel.FeelTier);
            Assert.Equal("metal_light", feel.FeelMaterial);
            Assert.Null(plain.FeelLayer);
            Assert.Null(plain.FeelTier);
            Assert.Null(plain.FeelMaterial);
        }

        [Fact]
        public void SfxFeelLayers_NamesRoundTrip_AndUnknownNameIsRejected()
        {
            foreach (var layer in System.Enum.GetValues(typeof(SfxFeelLayer)).Cast<SfxFeelLayer>())
            {
                Assert.True(SfxFeelLayers.TryParse(SfxFeelLayers.ToName(layer), out var back));
                Assert.Equal(layer, back);
            }

            Assert.False(SfxFeelLayers.TryParse("nonsense", out _));
        }
    }
}
