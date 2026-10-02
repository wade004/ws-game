using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>手感落地 M4-D（手感设计/04 第 10 节）：<c>display.anim_set</c> 的 <c>blend_ms</c>（逐键）与 <c>blends</c>（每对键）
    /// 解析、继承合并、优先级与校验。期望值由数据行声明推出，不写死裸数。</summary>
    public class AnimSetBlendTests
    {
        private const string Main = @"{
          ""id"": ""display.anim_set.m"",
          ""clips"": {
            ""idle"":      {""resource_ref"": ""anim.m_idle"",  ""events"": [], ""blend_ms"": 120},
            ""move.run"":  {""resource_ref"": ""anim.m_run"",   ""events"": [], ""blend_ms"": 100},
            ""attack"":    {""resource_ref"": ""anim.m_atk"",   ""events"": [], ""blend_ms"": 0},
            ""hit"":       {""resource_ref"": ""anim.m_hit"",   ""events"": []}
          },
          ""blends"": [
            {""from"": ""attack"", ""to"": ""idle"", ""blend_ms"": 250},
            {""from"": ""hit"", ""to"": ""idle"", ""blend_ms"": 0}
          ]
        }";

        private static (IDataRegistry Registry, ValidationReport Report) Build(params string[] rows) =>
            DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + string.Join(",", rows) + "]" },
                new IValidationRule[] { new AnimSetBlendRule() });

        private static AnimSetDef Def(IDataRegistry registry, string id) =>
            AnimSetDef.FromRecord(registry.Get("display.anim_set", id)!, registry);

        [Fact]
        public void BlendMs_ParsedPerClip_AbsentIsNull_ExplicitZeroIsKept()
        {
            var (registry, report) = Build(Main);
            Assert.False(report.IsBlocking);
            var def = Def(registry, "display.anim_set.m");

            Assert.Equal(120.0, def.Clips["idle"].BlendMs);
            Assert.Equal(0.0, def.Clips["attack"].BlendMs);        // 显式 0 = 硬切，不等于缺省
            Assert.Null(def.Clips["hit"].BlendMs);                  // 缺省 = 调用方默认
            Assert.Null(new AnimClipDef(new Id("anim.x")).BlendMs); // 旧构造：缺省 null
        }

        [Fact]
        public void TryGetBlendSeconds_PairBeatsPerKeyBeatsUndeclared()
        {
            var (registry, _) = Build(Main);
            var def = Def(registry, "display.anim_set.m");
            var idle = def.Clips["idle"].ResourceRef;
            var run = def.Clips["move.run"].ResourceRef;
            var attack = def.Clips["attack"].ResourceRef;
            var hit = def.Clips["hit"].ResourceRef;

            // 每对键 > 逐键：attack -> idle 的对声明 250，而 idle 的逐键值是 120
            Assert.True(def.TryGetBlendSeconds(attack, idle, out var pair));
            Assert.Equal(def.Blends.Single(b => b.FromKey == "attack").BlendMs / 1000.0, pair);
            // 没有对声明：取目标剪辑的逐键值
            Assert.True(def.TryGetBlendSeconds(run, idle, out var perKey));
            Assert.Equal(def.Clips["idle"].BlendMs!.Value / 1000.0, perKey);
            Assert.True(def.TryGetBlendSeconds(null, idle, out var first));    // 此前没有播放过剪辑：只看逐键值
            Assert.Equal(perKey, first);
            // 对声明 0（硬切）压过逐键值
            Assert.True(def.TryGetBlendSeconds(hit, idle, out var hard));
            Assert.Equal(0.0, hard);
            // 都没有声明：返回 false（调用方用默认）
            Assert.False(def.TryGetBlendSeconds(idle, hit, out _));
            // 目标剪辑显式声明 0：true 且为 0（与"未声明"可区分）
            Assert.True(def.TryGetBlendSeconds(idle, attack, out var zero));
            Assert.Equal(0.0, zero);
        }

        [Fact]
        public void SetWithoutAnyBlendData_NeverDeclares_SoCallerKeepsItsDefault()
        {
            const string plain = @"{ ""id"": ""display.anim_set.plain"", ""clips"": { ""idle"": {""resource_ref"": ""anim.p_idle"", ""events"": []} } }";
            var (registry, report) = Build(plain);
            Assert.False(report.IsBlocking);
            var def = Def(registry, "display.anim_set.plain");

            Assert.Empty(def.Blends);
            Assert.False(def.TryGetBlendSeconds(null, def.Clips["idle"].ResourceRef, out _));
            Assert.False(def.TryGetBlendSeconds(def.Clips["idle"].ResourceRef, def.Clips["idle"].ResourceRef, out _));
        }

        [Fact]
        public void Extends_ChildInheritsBlendMsAndPairsAndResolvesToItsOwnResourceRefs()
        {
            const string heavy = @"{
              ""id"": ""display.anim_set.m_heavy"", ""extends"": ""display.anim_set.m"",
              ""clips"": {
                ""idle"":   {""resource_ref"": ""anim.h_idle"", ""events"": []},
                ""attack"": {""resource_ref"": ""anim.h_atk"",  ""events"": [], ""blend_ms"": 77}
              }
            }";
            var (registry, report) = Build(Main, heavy);
            Assert.False(report.IsBlocking);
            var parent = Def(registry, "display.anim_set.m");
            var def = Def(registry, "display.anim_set.m_heavy");

            // 子集覆盖同名键且没有声明 blend_ms：沿用被覆盖键的值；声明了的以子集为准
            Assert.Equal(parent.Clips["idle"].BlendMs, def.Clips["idle"].BlendMs);
            Assert.Equal(77.0, def.Clips["attack"].BlendMs);
            Assert.NotEqual(parent.Clips["idle"].ResourceRef, def.Clips["idle"].ResourceRef);
            // 父集的每对键声明换算到子集的资源引用（attack -> idle 的对按子集的 h_atk / h_idle 查）
            Assert.True(def.TryGetBlendSeconds(def.Clips["attack"].ResourceRef, def.Clips["idle"].ResourceRef, out var s));
            Assert.Equal(parent.Blends.Single(b => b.FromKey == "attack").BlendMs / 1000.0, s);
            // 子集不覆盖的键沿用父集的资源与值
            Assert.Equal(parent.Clips["move.run"].ResourceRef, def.Clips["move.run"].ResourceRef);
            Assert.Equal(parent.Clips["move.run"].BlendMs, def.Clips["move.run"].BlendMs);
            Assert.Equal(parent.Blends.Count, def.Blends.Count);
        }

        [Fact]
        public void Extends_ChildPairOverridesParentPairOfTheSameKeys()
        {
            const string child = @"{
              ""id"": ""display.anim_set.m_c"", ""extends"": ""display.anim_set.m"", ""clips"": {},
              ""blends"": [ {""from"": ""attack"", ""to"": ""idle"", ""blend_ms"": 30} ]
            }";
            var (registry, _) = Build(Main, child);
            var def = Def(registry, "display.anim_set.m_c");

            Assert.True(def.TryGetBlendSeconds(def.Clips["attack"].ResourceRef, def.Clips["idle"].ResourceRef, out var s));
            Assert.Equal(30 / 1000.0, s);
            Assert.Equal(2, def.Blends.Count);   // 同一对被覆盖不重复
        }

        [Fact]
        public void Schema_RejectsOutOfRangeBlendMs_AndMalformedBlendPair()
        {
            const string badRange = @"{ ""id"": ""display.anim_set.b1"", ""clips"": { ""idle"": {""resource_ref"": ""anim.b1"", ""events"": [], ""blend_ms"": 5000} } }";
            Assert.True(Build(badRange).Report.IsBlocking);
            const string negative = @"{ ""id"": ""display.anim_set.b2"", ""clips"": { ""idle"": {""resource_ref"": ""anim.b2"", ""events"": [], ""blend_ms"": -1} } }";
            Assert.True(Build(negative).Report.IsBlocking);
            const string missing = @"{ ""id"": ""display.anim_set.b3"", ""clips"": { ""idle"": {""resource_ref"": ""anim.b3"", ""events"": []} },
              ""blends"": [ {""from"": ""idle"", ""blend_ms"": 10} ] }";
            Assert.True(Build(missing).Report.IsBlocking);
        }

        [Fact]
        public void FromRecord_MalformedBlendsThrow_NotSilentlyDropped()
        {
            var table = new TableSchema("display.anim_set", "id", 1, new List<FieldSchema>());
            var raw = (JsonObject)JsonReader.Parse("{\"id\": \"display.anim_set.bad\", \"clips\": {}, \"blends\": [ {} ]}");
            var bad = new DataRecord(table, "display.anim_set.bad", new Id("display.anim_set.bad"), raw);
            Assert.Throws<DataFieldException>(() => AnimSetDef.FromRecord(bad));
        }

        [Fact]
        public void BlendRule_WarnsOnDanglingAndDuplicatePairs_NeverBlocks()
        {
            const string row = @"{
              ""id"": ""display.anim_set.r"",
              ""clips"": { ""idle"": {""resource_ref"": ""anim.r_idle"", ""events"": []}, ""attack"": {""resource_ref"": ""anim.r_atk"", ""events"": []} },
              ""blends"": [
                {""from"": ""attack"", ""to"": ""idle"", ""blend_ms"": 10},
                {""from"": ""attack"", ""to"": ""idle"", ""blend_ms"": 20},
                {""from"": ""attack"", ""to"": ""nope"", ""blend_ms"": 30}
              ]
            }";
            var (registry, report) = Build(row);

            Assert.False(report.IsBlocking);
            var checks = report.Issues.Select(i => i.Check).ToList();
            Assert.Contains(AnimSetBlendRule.CheckDangling, checks);
            Assert.Contains(AnimSetBlendRule.CheckDuplicate, checks);
            Assert.All(report.Issues.Where(i => i.Check.StartsWith("anim_set_blend", StringComparison.Ordinal)),
                i => Assert.Equal(ValidationSeverity.Warning, i.Severity));
            // 悬空的对在运行期被忽略：attack -> idle 取后声明的 20（同一对重复，后者覆盖）
            var def = Def(registry, "display.anim_set.r");
            Assert.True(def.TryGetBlendSeconds(def.Clips["attack"].ResourceRef, def.Clips["idle"].ResourceRef, out var s));
            Assert.Equal(20 / 1000.0, s);
        }
    }
}
