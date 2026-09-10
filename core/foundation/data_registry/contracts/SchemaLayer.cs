namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"表级归属元数据"）：一张表的架构分层归属，取值与
    /// <c>architecture/01_分层与依赖.md</c> 第 4 节 L-1～L5 的分层一一对应（英文标识符取该文档"层"
    /// 列使用的模块群英文名，见该文档第 4 节标题行）。只用于登记，不参与任何依赖方向的运行时强制——
    /// 依赖方向仍由 01 文档与代码组织（哪个物理目录/程序集）约束，本枚举只是把已经成立的事实登记为
    /// 数据，供 <see cref="TableSchema.Layer"/>、<c>toolchain/validator --list-tables --json</c> 与
    /// 消费方工具（如编辑器导航树）读取。
    /// </summary>
    public enum SchemaLayer
    {
        /// <summary>L-1 引擎适配层。数据表登记通常不会用到这一层（引擎适配层不持有内容数据），
        /// 保留仅为与 01 文档分层枚举完整对齐。</summary>
        EngineAdapter,

        /// <summary>L0 基础层 Foundation。</summary>
        Foundation,

        /// <summary>L1 数值层 Numbers。</summary>
        Numbers,

        /// <summary>L2 规则层 Rules。</summary>
        Rules,

        /// <summary>L3 载体层 Carriers。</summary>
        Carriers,

        /// <summary>L4 玩法层 Gameplay。</summary>
        Gameplay,

        /// <summary>L5 表现层 Presentation。</summary>
        Presentation,
    }
}
