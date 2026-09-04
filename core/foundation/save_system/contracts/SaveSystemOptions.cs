using Core.Foundation.Common;

namespace Core.Foundation.SaveSystem
{
    /// <summary>存档触发点（见 10_存档与持久化.md 第 6 节表格三行）。</summary>
    public enum AutoSaveTrigger
    {
        /// <summary>存档点物件：世界中的 GameObject 触发的显式存档动作。</summary>
        SavePoint,

        /// <summary>场景路由完成一次地图切换。</summary>
        MapSwitch,

        /// <summary>Quest 状态机进入"完成"节点。</summary>
        QuestComplete,
    }

    /// <summary>
    /// 自动存档触发点策略（10 第 6 节"自动存档时机是口味配置项，基础架构提供触发点机制，
    /// 不强制具体游戏必须启用哪几个"）。默认值照抄 10 第 6 节表格的"默认值（建议）"列。
    /// 本类型只描述"是否响应某个触发点"这一判断依据，把触发点接到场景切换/任务完成等
    /// 具体时机、以及触发后实际调用 <see cref="ISaveSystem.Save"/> 的职责都在上层
    /// （见 <see cref="ISaveSystem.ShouldAutoSave"/> 文档）。
    /// </summary>
    public sealed class AutoSavePolicy
    {
        /// <summary>存档点物件触发时是否自动存档。默认开启。</summary>
        public bool OnSavePoint { get; set; } = true;

        /// <summary>地图切换完成后是否自动存档。默认关闭（避免频繁写盘）。</summary>
        public bool OnMapSwitch { get; set; }

        /// <summary>任务完成时是否自动存档。默认开启。</summary>
        public bool OnQuestComplete { get; set; } = true;
    }

    /// <summary>
    /// <see cref="ISaveSystem"/> 的构造期策略配置（对应 01_分层与依赖.md L0 模块表
    /// <c>save_system</c> 行"策略配置项：自动存档触发时机、存档槽数量上限"）。
    /// </summary>
    public sealed class SaveSystemOptions
    {
        /// <summary>本存档所属的具体游戏（10 第 2.1 节 <c>game_id</c>），写入每次存档的 meta
        /// 段，读档时不做强制校验（是否拒绝跨游戏误读档由调用方决定，见本模块 README）。
        /// 必填，构造函数参数，不提供默认值。</summary>
        public Id GameId { get; }

        /// <summary>当前运行时的存档 schema 版本号（10 第 5 节"save_version 单调递增"）。
        /// 默认 1。</summary>
        public int CurrentSaveVersion { get; set; } = 1;

        /// <summary>存档槽数量上限；0 表示不限。默认 20。达到上限后只能覆盖已存在的槽，
        /// 不能再新建槽（见 <see cref="SaveFailureReason.SlotLimitReached"/>）。</summary>
        public int MaxSlots { get; set; } = 20;

        /// <summary>每个存档槽保留的备份份数；0 表示不备份。默认 1（10 第 4 节"（建议）
        /// 每个存档槽额外保留一份上一次成功存档的备份……具体保留份数为可配置项"）。</summary>
        public int BackupCount { get; set; } = 1;

        /// <summary>存档子目录名，相对 <c>IFileSystem.GetUserDataDir()</c>。默认 <c>"saves"</c>。</summary>
        public string SavesDirName { get; set; } = "saves";

        /// <summary>自动存档触发点策略。默认新建一份 <see cref="AutoSavePolicy"/>（即 10 第 6
        /// 节表格默认值）。</summary>
        public AutoSavePolicy AutoSave { get; set; } = new AutoSavePolicy();

        public SaveSystemOptions(Id gameId)
        {
            GameId = gameId;
        }
    }
}
