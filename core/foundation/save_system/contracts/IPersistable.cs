using Core.Foundation.Common.Json;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// 存档段契约（见 10_存档与持久化.md 第 3 节 <c>Persistable</c>）。任何持有需要跨读档保留
    /// 状态的模块（Progression、Inventory、QuestState、WorldState……）都实现本契约，并在构造
    /// 完成后向 <see cref="ISaveSystem.RegisterPersistable"/> 登记自己的存档段 key
    /// （<see cref="SectionKey"/>）。SaveSystem 汇总各已注册段拼装成一份完整存档文档，
    /// 不要求任何单个模块了解整份文档的结构。
    /// </summary>
    public interface IPersistable
    {
        /// <summary>
        /// 本段在存档文档 <c>sections</c> 字典中的 key。已知段固定值见
        /// <see cref="SaveSections"/>；自定义段可以是任意非空字符串，但不能是
        /// <see cref="SaveSections.Meta"/>（<c>"meta"</c> 段由 <see cref="ISaveSystem"/> 自身
        /// 管理，不经本契约）。同一 <see cref="ISaveSystem"/> 实例内该值必须唯一。
        /// </summary>
        string SectionKey { get; }

        /// <summary>
        /// 把本模块当前状态序列化为一个 <see cref="JsonValue"/>（通常是 <see cref="JsonObject"/>
        /// 或 <see cref="JsonArray"/>，具体形状由实现方自行决定）。存档写入时调用；
        /// 抛出的任何异常都会被 <see cref="ISaveSystem.Save"/> 捕获并转成
        /// <see cref="SaveFailureReason.PersistableThrew"/>，本次存档不写入任何文件。
        /// </summary>
        JsonValue Save();

        /// <summary>
        /// 用存档文档中本段对应的数据恢复模块状态。存档读取时按
        /// <see cref="SaveSections"/> 固定顺序（自定义段在已知段之后按序数排序）依次调用；
        /// 抛出的任何异常都会被 <see cref="ISaveSystem.Load"/> 捕获并转成
        /// <see cref="LoadStatus.PersistableThrew"/>——此时之前已成功调用 <see cref="Load"/>
        /// 的其它段不会被回滚，调用方需要自行处理"部分加载"的一致性问题（见本模块 README）。
        /// </summary>
        void Load(JsonValue data);
    }
}
