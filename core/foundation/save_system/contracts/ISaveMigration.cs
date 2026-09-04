using Core.Foundation.Common.Json;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 版本迁移函数契约（见 10_存档与持久化.md 第 5 节 <c>SaveMigration</c>）。每次存档 schema
    /// 发生不兼容变更时登记一个实现，形成迁移函数链；<see cref="ISaveSystem"/> 读档时按
    /// 文档 <c>save_version</c> 依次查找 <see cref="FromVersion"/> 匹配的迁移函数串联执行，
    /// 直到达到 <see cref="ISaveSystem.CurrentSaveVersion"/>。<see cref="ISettingsStore"/> 复用
    /// 同一形状的迁移契约处理设置文件自己的版本号（见 10 第 7 节"可以有自己独立的版本号
    /// 用于设置项的迁移"）。
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>本迁移函数适用的起始版本号。</summary>
        int FromVersion { get; }

        /// <summary>本迁移函数执行后达到的版本号，必须大于 <see cref="FromVersion"/>。</summary>
        int ToVersion { get; }

        /// <summary>
        /// 是否不可逆（10 第 5 节"若某次变更导致无法逆向迁移，必须在迁移函数里显式标注
        /// 不可逆"）。本架构不提供反向迁移执行入口，该标记只供上层/工具在展示或校验时
        /// 读取判断，不影响 <see cref="ISaveSystem"/> 的正向迁移执行。
        /// </summary>
        bool Irreversible { get; }

        /// <summary>
        /// 把一份完整存档文档（信封形态 <c>{ save_version, sections }</c>，见本模块 README
        /// "文档格式"一节）从 <see cref="FromVersion"/> 迁移到 <see cref="ToVersion"/>，
        /// 返回迁移后的新文档。实现只需要关心 <c>sections</c> 内容的结构变化（改字段名、
        /// 拆分/合并段等）；调用方（<see cref="ISaveSystem"/>）会在调用后强制把结果文档的
        /// <c>save_version</c> 覆盖为 <see cref="ToVersion"/>，因此实现无需（也不必）自己
        /// 正确设置该字段。抛出的任何异常都会被调用方捕获并转成
        /// <see cref="LoadStatus.MigrationFailed"/>，原始存档文件不受影响。
        /// </summary>
        JsonObject Migrate(JsonObject document);
    }
}
