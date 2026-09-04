using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Foundation.Localization
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json 的 <c>l10n.language_changed</c> 行）。</summary>
    public static class L10nEventKeys
    {
        public static readonly Id LanguageChanged = new Id("l10n.language_changed");
    }

    /// <summary>
    /// <see cref="IL10nHost.SetLocale"/> 切换到与当前不同的语言时发出（见 03 第 9 节
    /// <c>L10nHost.setLocale</c>）。用 <see cref="IEventBus.PublishImmediate"/> 立即派发——
    /// 语言切换是玩家在设置界面发起的一次性交互操作，不是 tick 内产生的高频事件。
    /// </summary>
    public sealed class L10nLanguageChangedEvent : IEvent
    {
        public Id Key => L10nEventKeys.LanguageChanged;

        /// <summary>切换后的语言 id，与 <c>found.event_catalog.json</c> 登记的字段
        /// <c>locale</c> 对应。</summary>
        public Id Locale { get; }

        public L10nLanguageChangedEvent(Id locale)
        {
            Locale = locale;
        }
    }
}
