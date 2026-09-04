using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 一次存档意图请求（10_存档与持久化.md 第 6 节"手动存档：玩家在菜单/存档点主动触发的
    /// 存档意图请求，走与自动存档相同的 SaveSystem 汇总与写入流程，唯一区别是触发来源"——
    /// 本类型是自动/手动存档共用的唯一输入形状，"触发来源"这一区别不体现在本类型里，
    /// 由调用方（上层）自行记录/判断该不该调用 <see cref="ISaveSystem.Save"/>，
    /// 见 <see cref="ISaveSystem.ShouldAutoSave"/>。
    /// </summary>
    public sealed class SaveRequest
    {
        /// <summary>要写入的存档槽。</summary>
        public Id SlotId { get; }

        /// <summary>本次写入的时间戳文本（用于 meta 段 <c>updated_at</c>，新建槽时同时用作
        /// <c>created_at</c>）。本模块不读取系统时间（见本模块 README"设计约束"一节），
        /// 该文本由调用方提供，格式自行约定。</summary>
        public string TimestampText { get; }

        /// <summary>累计游玩时长（秒），可选，写入 meta 段 <c>play_time_seconds</c>。</summary>
        public long? PlayTimeSeconds { get; }

        /// <summary>游戏层自定义摘要字段，写入 meta 段 <c>display_summary</c>；为 null 时该次
        /// 写入省略该字段（等同空摘要）。</summary>
        public IReadOnlyDictionary<string, string>? DisplaySummary { get; }

        /// <summary>难度定义引用，可选，写入 meta 段 <c>difficulty_id</c>。</summary>
        public Id? DifficultyId { get; }

        public SaveRequest(
            Id slotId,
            string timestampText,
            long? playTimeSeconds = null,
            IReadOnlyDictionary<string, string>? displaySummary = null,
            Id? difficultyId = null)
        {
            SlotId = slotId;
            TimestampText = timestampText ?? string.Empty;
            PlayTimeSeconds = playTimeSeconds;
            DisplaySummary = displaySummary;
            DifficultyId = difficultyId;
        }
    }
}
