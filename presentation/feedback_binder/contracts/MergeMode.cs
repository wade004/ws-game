namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>飘字合并展示形式（见 09_表现层.md 第 6.3 节"求和后单条 / 简单折叠为 xN"）。</summary>
    public enum MergeMode
    {
        /// <summary>求和后单条展示（如三段伤害 12+8+5 合并展示为一条 "25"）。</summary>
        Sum,

        /// <summary>折叠为 "首次文本 xN" 形式。</summary>
        Fold,
    }
}
