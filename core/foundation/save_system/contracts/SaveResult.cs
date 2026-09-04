namespace Core.Foundation.SaveSystem
{
    /// <summary>失败原因分类（见 <see cref="ISaveSystem.Save"/>）。</summary>
    public enum SaveFailureReason
    {
        /// <summary>写入正式文件失败（<c>IFileSystem.WriteTextAtomic</c> 返回 false），
        /// 旧存档文件保持不变（见 10_存档与持久化.md 第 4 节原子写入）。</summary>
        WriteFailed,

        /// <summary>目标槽不存在，且已存在槽数量已达 <see cref="SaveSystemOptions.MaxSlots"/>
        /// 上限（覆盖已存在的槽不受此限制）。</summary>
        SlotLimitReached,

        /// <summary>某个已注册 <see cref="IPersistable.Save"/> 抛出异常，未写入任何文件。</summary>
        PersistableThrew,
    }

    /// <summary>
    /// <see cref="ISaveSystem.Save"/> 的结果（不可变值对象，用工厂方法构造，
    /// 与本仓库"接口一律用 Bool/Optional 表达失败"的适配层约定同一惯例，
    /// 但存档失败原因需要区分三种情形，因此用枚举 + 可选文本消息而非单一 Bool）。
    /// </summary>
    public sealed class SaveResult
    {
        public bool Success { get; }

        public SaveFailureReason? Reason { get; }

        public string? Message { get; }

        private SaveResult(bool success, SaveFailureReason? reason, string? message)
        {
            Success = success;
            Reason = reason;
            Message = message;
        }

        public static SaveResult Ok() => new SaveResult(true, null, null);

        public static SaveResult Fail(SaveFailureReason reason, string message) =>
            new SaveResult(false, reason, message);
    }
}
