using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary><see cref="AnimSetDef.FromRecord"/> 用例（ADR-0017 决策 c）。</summary>
    public class AnimSetDefTests
    {
        [Fact]
        public void FromRecord_ParsesClipsAndEvents()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.anim_set", "display.anim_set.stone_golem")!;
            var def = AnimSetDef.FromRecord(record);

            Assert.Equal(new Id("display.anim_set.stone_golem"), def.Id);
            Assert.Equal(2, def.Clips.Count);

            var attack = def.Clips["attack"];
            Assert.Equal(new Id("anim.stone_golem.attack"), attack.ResourceRef);
            Assert.Single(attack.Events);
            Assert.Equal("hit", attack.Events[0].Name);
            Assert.Equal(0.6, attack.Events[0].TimePct);

            var move = def.Clips["move"];
            Assert.Equal(new Id("anim.stone_golem.move"), move.ResourceRef);
            Assert.Empty(move.Events);
        }

        [Fact]
        public void FromRecord_ClipWithoutEventsField_YieldsEmptyEventsList()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.no_events"",
              ""clips"": { ""idle"": {""resource_ref"": ""anim.no_events.idle""} }
            }";

            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.anim_set"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.anim_set", "display.anim_set.no_events")!;
            var def = AnimSetDef.FromRecord(record);

            Assert.Empty(def.Clips["idle"].Events);
        }

        [Fact]
        public void FromRecord_NoClipsField_YieldsEmptyClipMap()
        {
            const string row = @"{ ""id"": ""display.anim_set.empty"", ""clips"": {} }";

            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.anim_set"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.anim_set", "display.anim_set.empty")!;
            var def = AnimSetDef.FromRecord(record);

            Assert.Empty(def.Clips);
        }

        /// <summary>ADR-0024 第二批登记：<c>clips[].events[].name</c> 现由 <see cref="DisplaySchemas.AnimSet"/>
        /// 登记为必填子字段，经完整 <see cref="Core.Foundation.DataRegistry.DataRegistry.LoadAll()"/>
        /// 校验管线会在 <c>required_field</c> 检查项就报错阻断（<c>report.IsBlocking</c> 变为
        /// <c>true</c>，<c>registry.Get</c> 在阻断态会抛 <see cref="System.InvalidOperationException"/>，
        /// 不能再像此前那样先走完整加载管线拿到记录）——直接构造 <see cref="Core.Foundation.DataRegistry.DataRecord"/>
        /// （绕过 <see cref="Core.Foundation.DataRegistry.DataRegistry"/> 校验管线，只用
        /// <see cref="DisplaySchemas.AnimSet"/> 作为其 <c>Table</c>）保留对
        /// <see cref="AnimSetDef.FromRecord"/> 自身防御代码路径的独立覆盖，与该方法类型注释"本方法
        /// 自身也做最小防御"一致——两条防线（登记表结构校验 + FromRecord 自身防御）都要覆盖，不是
        /// 二选一。</summary>
        [Fact]
        public void FromRecord_EventMissingName_ThrowsDataFieldException()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad_event"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad.attack"", ""events"": [{""time_pct"": 0.5}]} }
            }";

            var raw = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(row);
            var record = new Core.Foundation.DataRegistry.DataRecord(DisplaySchemas.AnimSet, "display.anim_set.bad_event", null, raw);

            Assert.Throws<Core.Foundation.DataRegistry.DataFieldException>(() => AnimSetDef.FromRecord(record));
        }

        /// <summary>补一条回归：同一份非法数据经完整加载管线时，现在在加载期（而不是等到
        /// <see cref="AnimSetDef.FromRecord"/>）就被 <c>required_field</c> 拦下——这正是 ADR-0024
        /// 登记子结构的价值所在（同 ADR-0021 决策 1 背景"反馈强度与类型错误一致"的既有惯例）。</summary>
        [Fact]
        public void FromRecord_EventMissingName_CaughtAtLoadTime_RequiredField()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad_event"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad.attack"", ""events"": [{""time_pct"": 0.5}]} }
            }";

            var (_, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.anim_set"] = "[" + row + "]",
            });

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "required_field" && i.Field == "clips[attack].events[0].name");
        }
    }
}
