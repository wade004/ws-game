using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    public class PresentationEventKeysTests
    {
        [Fact]
        public void PlaybackFinished_KeyValue_MatchesEventCatalog()
        {
            Assert.Equal("presentation.playback_finished", PresentationEventKeys.PlaybackFinished.Value);
        }

        [Fact]
        public void PlaybackFinishedEvent_KeyMatchesConstant()
        {
            var evt = new PlaybackFinishedEvent();

            Assert.Equal(PresentationEventKeys.PlaybackFinished, evt.Key);
        }
    }
}
