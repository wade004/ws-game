using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Localization
{
    /// <summary>
    /// 本地化契约（见 01_分层与依赖.md L0 模块表 <c>localization</c> 行、03_运行时骨架.md
    /// 第 9 节 <c>L10nHost</c> 签名）：文本查询与变量代入、语言切换。<c>Text</c>/
    /// <c>SetLocale</c>/<c>GetLocale</c> 三个方法与 03 第 9 节签名一一对应；
    /// <see cref="HasText"/>/<see cref="SupportedLocales"/>/<see cref="DefaultLocale"/> 是
    /// 任务书显式拍板的补充（03 未给出"如何得知一个键是否有文案""如何得知支持哪些语言/
    /// 默认语言是谁"的方法，见本模块 README 判断记录）。
    /// </summary>
    public interface IL10nHost
    {
        /// <summary>
        /// 按当前语言取 <paramref name="key"/> 对应文案：当前语言缺失则沿回退链查找，
        /// 回退链耗尽仍缺失则查默认语言，仍缺失按 <see cref="L10nOptions.MissingKeyPolicy"/>
        /// 返回并记警告（见 04 第 7.2 节）。找到文案后，用 <paramref name="vars"/> 做
        /// <c>{变量名}</c> 占位替换；不支持嵌套；变量缺失时原样保留占位符并记警告。
        /// </summary>
        string Text(Id key, IReadOnlyDictionary<string, string>? vars = null);

        /// <summary>切换当前语言；<paramref name="locale"/> 不是已声明的支持语言时抛
        /// <see cref="System.ArgumentException"/>；实际发生变化时发出
        /// <see cref="L10nLanguageChangedEvent"/>（<c>PublishImmediate</c>）。</summary>
        void SetLocale(Id locale);

        Id GetLocale();

        /// <summary>
        /// <paramref name="key"/> 沿"当前语言 → 回退链 → 默认语言"能否找到真实文案
        /// （不考虑 <see cref="L10nOptions.MissingKeyPolicy"/> 的兜底返回值——那是"找不到时
        /// 怎么办"，本方法回答"找不找得到"）。
        /// </summary>
        bool HasText(Id key);

        /// <summary>全部已声明支持的语言 id（<c>l10n.locale</c> 表的全部记录）。</summary>
        IReadOnlyList<Id> SupportedLocales { get; }

        /// <summary><c>l10n.locale</c> 中 <c>is_default = true</c> 的那一条语言 id。</summary>
        Id DefaultLocale { get; }
    }
}
