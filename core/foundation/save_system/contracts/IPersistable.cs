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
        /// <para>
        /// CR150-03 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：本段已注册、但正在
        /// 读取的存档文档里整段缺失（旧存档产生于本段引入之前，或候选文档压根没有这个 key）时，
        /// 只要 <see cref="KeepStateWhenSectionMissing"/> 为默认值 <c>false</c>，<see
        /// cref="ISaveSystem.Load"/> 仍然会调用一次本方法、参数传 <see cref="JsonNull.Instance"/>
        /// ——不会因为"文档里根本没有这一段"就整段跳过调用。修复前 <c>SaveSystem</c> 只对文档里
        /// 实际存在的段安排 <c>Load</c>，导致本模块当前已经积累的运行期状态（如未交付掉落余量）
        /// 在读到一份还没有这个段的旧档后原样保留，污染恢复后的世界与紧接着的下一次存档（外部
        /// 审计复现：真实 <c>SaveSystem.Save</c> 生成合法档案后只移除本段、读档，段缺失的
        /// <see cref="Load"/> 从未被调用，pending 数量原样保留而不是归零）。因此实现方必须像
        /// 处理"段存在但内容为空"一样正确处理 <c>data is JsonNull</c>（多数实现已经如此，见
        /// <see cref="Core.Carriers.Gobj.GobjPendingLootPersistable"/>）。
        /// </para>
        /// </summary>
        void Load(JsonValue data);

        /// <summary>
        /// 见 <see cref="Load"/> 判断记录：本段已注册、但存档文档中整段缺失时的默认语义是
        /// "视为收到一次 <see cref="JsonNull"/>，清空/归零到从未发生过的默认态"（<c>false</c>，
        /// 绝大多数段的正确语义——不能让"碰巧读到一份还没有这个段的旧档"变成"继续携带读档前的
        /// 脏状态"）。
        /// <para>
        /// 极少数段需要相反语义——"文档缺失该段时保留当前状态、完全不调用 <see cref="Load"/>"
        /// （例如某个可选能力这次读档压根没有被启用，缺段不代表"这个能力的状态应该被清空"，而是
        /// "这个能力目前不参与存档过程"）——由具体实现覆盖为 <c>true</c> 并在覆盖处写明理由。C#8
        /// 默认接口方法：不覆盖时对全部既有实现完全透明，不需要逐一改动既有 <see
        /// cref="IPersistable"/> 实现签名。
        /// </para>
        /// </summary>
        bool KeepStateWhenSectionMissing => false;
    }
}
