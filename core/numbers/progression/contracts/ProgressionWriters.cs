using Core.Foundation.Common;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// 把升级带来的属性成长写入属性宿主的具名委托（见任务书"并行开发期不引用属性宿主模块
    /// （stat_block）的具体类型，通过具名委托注入，由上层把真实 <c>IStatHost</c>
    /// 接进来实现"）。<see cref="IProgressionHost"/> 只按等级曲线算出该写什么，不知道、也不依赖
    /// 真正的属性宿主是谁。
    /// </summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="stat">要修改的属性 id（<c>stat.*</c>）。</param>
    /// <param name="op">修正运算类型，本模块固定传 <c>"flat"</c>（见 11_工程规范与测试.md 第 3 节
    /// "常量/枚举值"约定的 StatMod 运算类型示例）。</param>
    /// <param name="value">修正数值。</param>
    /// <param name="sourceId">修正来源 id，供属性宿主按来源分组叠加/移除，本模块固定传
    /// <c>prog.growth</c>（见 <see cref="ProgressionHost"/> 判断记录）。</param>
    public delegate void StatModifierWriter(Id unitId, Id stat, string op, double value, Id sourceId);

    /// <summary>移除某个来源在某单位身上写入的全部属性修正（用于升级时先清空旧成长再按
    /// 新等级重新写入累计值，见 <see cref="StatModifierWriter"/>）。</summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="sourceId">要移除的修正来源 id。</param>
    public delegate void StatModifierRemover(Id unitId, Id sourceId);

    /// <summary>
    /// CORE-170-02 根治（architecture/落地计划/audit-8160178-20260908，P2）：把 <see
    /// cref="ProgressionHost"/> 内部权威等级同步写回调用方持有的单位实体等级字段的具名委托——惯例同
    /// <see cref="StatModifierWriter"/>"并行开发期不引用具体类型，通过具名委托注入，由上层把真实
    /// 实现接进来"：<c>core/numbers/progression</c>（L1）不知道、也不该知道 <c>Unit</c>/<c>PlayerUnit</c>
    /// （L3 载体层）这个类型的存在，见 <see cref="ProgressionHost"/> 判断记录"选定 Progression 为
    /// 单位等级的唯一权威"。
    /// <para>
    /// 背景——修复前 <see cref="ProgressionHost.AddXp"/>/<see cref="ProgressionHost.RestoreState"/>
    /// 只更新 <see cref="ProgressionHost"/> 自己内部的等级状态，从不同步任何外部实体字段；生产装配
    /// （<c>games/_template/Runtime/GameBootstrap.cs</c>/<c>FrameworkResidentHost.cs</c>/
    /// <c>GameFoundationBootstrap.cs</c>）构造 <c>PlayerUnit</c> 时也从未写入 <c>PlayerUnit.Level</c>
    /// （构造函数默认值 1），只把等级传给 <c>RulesAssembly.RegisterUnit</c>——<c>Core.Carriers.Unit.
    /// WorldUnitAccess.GetLevel</c>（<c>IUnitAccess</c> 的真实实现，供装备需求判断等运行期消费者
    /// 调用）直接读取 <c>Unit.Level</c> 实体字段，与 <see cref="ProgressionHost"/> 内部权威等级各自
    /// 独立、互不同步：真实探针复现 <c>rules_progression_level=2;entity_level=1</c>，等级 2 的装备
    /// 需求判断因此读到过期的实体等级 1，返回 <c>RequirementNotMet</c>。
    /// </para>
    /// <para>
    /// 根治方式：选定 <see cref="ProgressionHost"/> 为单位等级的唯一权威——它是升级（<see
    /// cref="ProgressionHost.AddXp"/>）与读档恢复（<see cref="ProgressionHost.RestoreState"/>）两条
    /// 等级变化路径唯一真正发生的地方，其它任何"等级"字段（含 <c>Unit.Level</c>）都只是它的一份
    /// 只读投影。<see cref="ProgressionHost"/> 在三个等级会变化/确立的时机（<see
    /// cref="ProgressionHost.RegisterUnit"/> 首次注册、<see cref="ProgressionHost.AddXp"/> 升级、
    /// <see cref="ProgressionHost.RestoreState"/> 读档恢复）末尾统一调用本委托（未注入时为
    /// <c>null</c>，行为与本次改动之前完全一致，见该三个方法各自判断记录），由 <c>RulesAssembly</c>
    /// 转发注入、<c>CarriersAssembly</c> 提供真正实现（写 <c>WorldUnitAccess.SetLevel</c> →
    /// <c>Unit.Level</c>）——单位存在于 <c>IWorldSim</c> 时同步写入实体字段，单位尚未注册到世界
    /// （极早期装配阶段的孤立调用，正常生产链路不会出现）时安全 no-op，不抛异常。<c>WorldUnitAccess.
    /// GetLevel</c> 自此读到的 <c>Unit.Level</c> 与 <see cref="ProgressionHost.GetLevel"/> 恒一致，
    /// 不需要改写 <c>GetLevel</c> 本身去反查 <see cref="ProgressionHost"/>（那会要求
    /// <c>core/carriers/unit</c> 反向依赖 <c>core/numbers/progression</c> 具体实现，且对没有配置
    /// 进度曲线的单位——如多数 NPC/怪物——GetLevel 该读什么曲线本就无从谈起；"写入时同步"比"读取时
    /// 反查"更符合本仓库既有的"L1 不知道 L3 具体类型、只经具名委托注入"分层惯例）。
    /// </para>
    /// </summary>
    /// <param name="unitId">目标单位。</param>
    /// <param name="level">该单位刚刚确立/变为的权威等级（<see cref="ProgressionHost.GetLevel"/>
    /// 此时会返回同一个值）。</param>
    public delegate void LevelSync(Id unitId, int level);
}
