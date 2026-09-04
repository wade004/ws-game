using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// meta 段的强类型视图（见 10_存档与持久化.md 第 2.1 节字段表）。供存档槽 UI 展示摘要，
    /// 不参与模拟。由 <see cref="ISaveSystem"/> 内部读写存档文档 <c>sections.meta</c> 段时
    /// 产出/消费，不实现 <see cref="IPersistable"/>（meta 不经该契约，见 <see cref="SaveSections.Meta"/>
    /// 注释）。
    /// </summary>
    public sealed class SaveMeta
    {
        /// <summary>存档 schema 版本号（10 第 2.1 节 <c>save_version</c>）。写入时等于
        /// <see cref="ISaveSystem.CurrentSaveVersion"/>；读取时等于文档迁移后的版本号，
        /// 与存档文档顶层 <c>save_version</c> 字段保持一致（见本模块 README"文档格式"一节）。</summary>
        public int SaveVersion { get; }

        /// <summary>存档槽标识（10 第 2.1 节 <c>slot_id</c>）。</summary>
        public Id SlotId { get; }

        /// <summary>创建时间，结构化时间文本，格式由调用方约定（10 第 2.1 节 <c>created_at</c>）。
        /// SaveSystem 不读取系统时间，该文本原样来自首次 <see cref="SaveRequest.TimestampText"/>
        /// 且此后覆盖存档时保持不变。</summary>
        public string CreatedAt { get; }

        /// <summary>最近一次写入时间文本（10 第 2.1 节 <c>updated_at</c>）。</summary>
        public string UpdatedAt { get; }

        /// <summary>累计游玩时长（秒），可选（10 第 2.1 节 <c>play_time_seconds</c>）。</summary>
        public long? PlayTimeSeconds { get; }

        /// <summary>游戏层自定义摘要字段（角色名、等级、当前区域名等），键由游戏层约定
        /// （10 第 2.1 节 <c>display_summary</c>）。永不为 null，缺省时为空字典。</summary>
        public IReadOnlyDictionary<string, string> DisplaySummary { get; }

        /// <summary>本存档所属的具体游戏，防止跨游戏误读档（10 第 2.1 节 <c>game_id</c>）。</summary>
        public Id GameId { get; }

        /// <summary>难度定义引用，可选（10 第 2.1 节 <c>difficulty_id</c>）。</summary>
        public Id? DifficultyId { get; }

        private static readonly IReadOnlyDictionary<string, string> EmptyDisplaySummary =
            new Dictionary<string, string>();

        public SaveMeta(
            int saveVersion,
            Id slotId,
            string createdAt,
            string updatedAt,
            long? playTimeSeconds,
            IReadOnlyDictionary<string, string>? displaySummary,
            Id gameId,
            Id? difficultyId)
        {
            SaveVersion = saveVersion;
            SlotId = slotId;
            CreatedAt = createdAt ?? string.Empty;
            UpdatedAt = updatedAt ?? string.Empty;
            PlayTimeSeconds = playTimeSeconds;
            DisplaySummary = displaySummary ?? EmptyDisplaySummary;
            GameId = gameId;
            DifficultyId = difficultyId;
        }
    }
}
