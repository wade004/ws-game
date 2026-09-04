namespace Core.Foundation.SaveSystem
{
    /// <summary>读档结果分类（见 <see cref="ISaveSystem.Load"/>）。</summary>
    public enum LoadStatus
    {
        /// <summary>正式文件读取、解析、（如需要）迁移、全部已注册段 Load 均成功。</summary>
        Loaded,

        /// <summary>正式文件损坏/缺失，从备份文件成功恢复并完成加载（见 10_存档与持久化.md
        /// 第 4 节"每个存档槽额外保留一份上一次成功存档的备份……当校验发现最新文件损坏时
        /// 自动回退读取备份"）。</summary>
        LoadedFromBackup,

        /// <summary>目标存档槽不存在（正式文件与全部备份均不存在）。</summary>
        NotFound,

        /// <summary>正式文件与全部备份均无法解析为合法存档文档，或 meta 段缺失/非法；
        /// 原始文件不会被覆盖或删除（见 10 第 5 节损坏存档处理）。</summary>
        Corrupted,

        /// <summary>版本迁移失败：存档版本高于当前运行时版本（不承诺向前兼容），或迁移链
        /// 缺少衔接版本的迁移函数，或某个迁移函数执行时抛出异常；原始文件不会被覆盖或删除。</summary>
        MigrationFailed,

        /// <summary>某个已注册 <see cref="IPersistable.Load"/> 抛出异常；此前已成功调用
        /// <see cref="IPersistable.Load"/> 的段不会被回滚（见 <see cref="IPersistable.Load"/>
        /// 文档"调用方需要自行处理"部分加载"的一致性问题"）。</summary>
        PersistableThrew,
    }

    /// <summary>
    /// <see cref="ISaveSystem.Load"/> 的结果（不可变值对象，用工厂方法构造）。
    /// </summary>
    public sealed class LoadResult
    {
        public LoadStatus Status { get; }

        /// <summary>成功（<see cref="LoadStatus.Loaded"/>/<see cref="LoadStatus.LoadedFromBackup"/>/
        /// <see cref="LoadStatus.PersistableThrew"/>）时非空：<see cref="LoadStatus.PersistableThrew"/>
        /// 发生前 meta 段总是已经解析成功，因此仍然携带 meta。其余失败状态下为 null。</summary>
        public SaveMeta? Meta { get; }

        /// <summary>发生过版本迁移时，迁移前的原始版本号；未迁移（含未成功迁移）时为 null。</summary>
        public int? MigratedFromVersion { get; }

        /// <summary>非成功状态下的诊断消息；成功状态下通常为 null。</summary>
        public string? Message { get; }

        private LoadResult(LoadStatus status, SaveMeta? meta, int? migratedFromVersion, string? message)
        {
            Status = status;
            Meta = meta;
            MigratedFromVersion = migratedFromVersion;
            Message = message;
        }

        public static LoadResult Loaded(SaveMeta meta, int? migratedFromVersion, LoadStatus status) =>
            new LoadResult(status, meta, migratedFromVersion, null);

        public static LoadResult NotFound() => new LoadResult(LoadStatus.NotFound, null, null, null);

        public static LoadResult Corrupted(string message) =>
            new LoadResult(LoadStatus.Corrupted, null, null, message);

        public static LoadResult MigrationFailed(string message) =>
            new LoadResult(LoadStatus.MigrationFailed, null, null, message);

        public static LoadResult PersistableThrew(SaveMeta meta, int? migratedFromVersion, string message) =>
            new LoadResult(LoadStatus.PersistableThrew, meta, migratedFromVersion, message);
    }
}
