namespace Core.Foundation.Localization
{
    /// <summary>缺失文案时的回退策略（见 01_分层与依赖.md L0 模块表 <c>localization</c> 行
    /// 策略配置项"缺失文案回退策略"）。</summary>
    public enum MissingKeyPolicy
    {
        /// <summary>返回文本键本身（<c>Id.Value</c>）。默认。</summary>
        ReturnKey,

        /// <summary>返回空字符串。</summary>
        ReturnEmpty,

        /// <summary>返回 <c>"[[key]]"</c> 形式的标记，便于在界面上肉眼识别缺失文案。</summary>
        ReturnMarker,
    }

    /// <summary>
    /// <see cref="IL10nHost"/> 默认实现的构造期策略配置。
    /// </summary>
    public sealed class L10nOptions
    {
        public MissingKeyPolicy MissingKeyPolicy { get; set; } = MissingKeyPolicy.ReturnKey;

        /// <summary>变量代入时缺失变量是否记警告（见 04 第 7.2 节"替换失败（变量缺失）时
        /// 原样保留占位符并记录警告"）。默认 true。</summary>
        public bool WarnOnMissingVar { get; set; } = true;
    }
}
