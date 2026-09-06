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

        /// <summary>去重（09 勘误）：本模块的 <see cref="PresentationEventKeys.PlaybackFinished"/> 常量
        /// 与 <c>Presentation.FeedbackBinder.Contracts.PlaybackFinishedEvent</c>（实际发布使用的那个
        /// 事件类，见其类型注释）引用的生成物常量必须是同一个字符串值，否则两处"presentation 回放
        /// 完成"的 key 会各说各话。</summary>
        [Fact]
        public void PlaybackFinished_MatchesFeedbackBinderContractsEventKey()
        {
            var evt = new global::Presentation.FeedbackBinder.Contracts.PlaybackFinishedEvent();

            Assert.Equal(PresentationEventKeys.PlaybackFinished, evt.Key);
        }
    }
}
