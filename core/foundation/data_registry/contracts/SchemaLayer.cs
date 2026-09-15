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

        /// <summary>
        /// T-N6-2a（ADR-0035 决策 4）：框架工具（无头仿真），04 第 1.1 节表清单"层"列对
        /// <c>sim.scenario</c>/<c>sim.anchor</c> 两表的取值原文——不对应 01 文档 L-1～L5 中任何一层
        /// （见 01 第 4 节 2026-09-16 勘误"<c>core/sim</c>……不对应 L0～L4 中的某一层"），是
        /// <c>core/sim</c> 装配根专属的表级归属标签，只给这两张表用，供 ADR-0022 <c>table_ownership</c>
        /// 门禁判定"Layer 已登记"。新增枚举成员是纯粹的类型表面扩容（不删改任何既有成员），不构成
        /// 破坏性变更；仓库内无任何对 <see cref="SchemaLayer"/> 的穷尽 switch（已核实），新增成员不会
        /// 让既有代码产生未处理分支。</summary>
        Sim,
    }
}
