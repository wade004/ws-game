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

        [Fact]
        public void FromRecord_EventMissingName_ThrowsDataFieldException()
        {
            const string row = @"
            {
              ""id"": ""display.anim_set.bad_event"",
              ""clips"": { ""attack"": {""resource_ref"": ""anim.bad.attack"", ""events"": [{""time_pct"": 0.5}]} }
            }";

            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.anim_set"] = "[" + row + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.anim_set", "display.anim_set.bad_event")!;
            Assert.Throws<Core.Foundation.DataRegistry.DataFieldException>(() => AnimSetDef.FromRecord(record));
        }
    }
}
