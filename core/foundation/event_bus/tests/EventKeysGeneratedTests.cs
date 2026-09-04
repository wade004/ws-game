using System.Linq;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Events
{
    /// <summary>
    /// 校验 <see cref="EventKeys"/>（由 toolchain/gen_event_constants.py 从
    /// data/_sample/found/found.event_catalog.json 生成，见 core/foundation/event_bus/generated/EventKeys.g.cs）
    /// 的基本不变量。不依赖登记表的精确行数（登记表允许后续增补事件），只断言下限与内部一致性。
    /// </summary>
    public class EventKeysGeneratedTests
    {
        // 落地计划 T1-8 编写脚本时，found.event_catalog.json 恰有 41 行；后续新增事件只会增长，不会减少。
        private const int MinExpectedCount = 41;

        [Fact]
        public void All_HasAtLeastTheKnownRowCount()
        {
            Assert.True(
                EventKeys.All.Length >= MinExpectedCount,
                $"EventKeys.All.Length={EventKeys.All.Length}，应不少于 {MinExpectedCount}"
                    + "（见 data/_sample/found/found.event_catalog.json）");
        }

        [Fact]
        public void All_HasNoDuplicateKeys()
        {
            var distinctCount = EventKeys.All.Select(id => id.Value).Distinct().Count();
            Assert.Equal(EventKeys.All.Length, distinctCount);
        }

        [Fact]
        public void All_EveryKeyHasNonEmptyDomain()
        {
            foreach (var id in EventKeys.All)
            {
                Assert.False(string.IsNullOrEmpty(id.Domain), $"key '{id.Value}' 的 Domain 不应为空");
            }
        }

        [Fact]
        public void CombatDamageDealt_HasExpectedValue()
        {
            Assert.Equal("combat.damage_dealt", EventKeys.CombatDamageDealt.Value);
        }

        [Fact]
        public void SkillCastStart_HasExpectedValue()
        {
            Assert.Equal("skill.cast_start", EventKeys.SkillCastStart.Value);
        }

        [Fact]
        public void PresentationPlaybackFinished_HasExpectedValue()
        {
            Assert.Equal("presentation.playback_finished", EventKeys.PresentationPlaybackFinished.Value);
        }
    }
}
