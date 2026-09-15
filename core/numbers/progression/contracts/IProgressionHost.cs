using Core.Foundation.Common;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// 等级与经验契约（见 01_分层与依赖.md L1 模块表 <c>progression</c> 行"契约接口名：
    /// ProgressionHost"，本模块按仓库既有惯例——如 <c>IDataRegistry</c>/<c>DataRegistry</c>、
    /// <c>IL10nHost</c>/<c>L10nHost</c>——把契约命名为 <c>IProgressionHost</c>、默认实现命名为
    /// <see cref="ProgressionHost"/>）：单位注册到某条等级曲线、查询等级/经验、记入经验并按
    /// 曲线判定升级。
    /// </summary>
    public interface IProgressionHost
    {
        /// <summary>把 <paramref name="unitId"/> 挂到曲线 <paramref name="curveId"/> 上，初始等级为
        /// <paramref name="startLevel"/>（默认 1）、当前等级内经验为 0。<paramref name="startLevel"/>
        /// 超出 <c>[1, 曲线.max_level]</c> 时抛 <see cref="System.ArgumentException"/>。重复调用会
        /// 用新状态整体覆盖该单位既有的挂载状态（见判断记录：本方法定位是"初始化/读档到已知等级"，
        /// 不隐式重算成长——是否需要把成长一并写回由调用方决定，见 <see cref="ProgressionHost"/>
        /// 判断记录）。<paramref name="startLevel"/> &gt; 1 时，调用方如果需要把"从 2 级到
        /// <paramref name="startLevel"/> 级"的曲线成长一次性写为修正，紧随其后显式调用
        /// <see cref="ApplyGrowthToCurrentLevel"/>（消费方反馈第 36 条根治：两者刻意分离成两步，
        /// 由调用方自己决定要不要这一步，见该方法判断记录）。</summary>
        void RegisterUnit(Id unitId, Id curveId, int startLevel = 1);

        /// <summary>
        /// 消费方反馈第 36 条根治：按 <paramref name="unitId"/> 当前已注册的等级，重新聚合
        /// "2 级到当前等级"的曲线成长量并整体写回属性宿主（来源 <c>prog.growth</c>）——内部复用与
        /// <see cref="AddXp"/> 升级路径、<see cref="RestoreState"/> 读档路径末尾完全相同的一段聚合
        /// 逻辑（先 <see cref="StatModifierRemover"/> 清空旧的 <c>prog.growth</c> 修正、再按当前等级
        /// 重新算出整段累计值经 <see cref="StatModifierWriter"/> 写入），不是另外维护一份公式——三条
        /// 路径（升级、读档、本方法）就此共用同一份实现，成长量只有这一个来源，不会出现"基础值已经
        /// 含一段成长，修正又整段重写覆盖同一段"的重复计入（消费方反馈第 36 条现象）。
        /// <para>
        /// 语义上既不是"升级"（不发 <see cref="LevelUpEvent"/>——没有发生真实的等级提升）也不是
        /// "读档恢复"（不发 <see cref="ProgressionRestoredEvent"/>——该事件语义是"状态被存档覆盖"，
        /// 见其判断记录"语义诚实"，本方法的典型调用方是单位刚创建、从未有过存档，硬套这个事件反而
        /// 语义不诚实），本方法不发任何事件——需要下游联动的调用方自行处理（如
        /// <see cref="Core.Carriers.Creature.CreatureFactory.Spawn"/> 用法）。
        /// </para>
        /// 幂等：可重复调用，每次都是"先移除旧修正、再按当前等级整体重算写入"，不会随调用次数累加。
        /// <paramref name="unitId"/> 未经 <see cref="RegisterUnit"/> 注册时抛
        /// <see cref="System.ArgumentException"/>（<see cref="ProgressionHost"/> 的真实实现如此；
        /// 见下方"默认实现"判断记录——未覆盖本方法的旧实现方不受这条约束）。
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁要求"公开 API 只能新增"，惯例同
        /// <c>core/foundation/event_bus.IEventBus.SuppressDispatch</c>）：默认体是空操作，本接口
        /// 目前只有 <see cref="ProgressionHost"/> 一个生产实现（它显式覆盖本方法，见该类型判断记录
        /// "成长唯一来源"）；其它实现方（测试假实现、未来的第三方实现，若存在）不因新增本成员而
        /// 编译失败或运行期抛 <see cref="System.MissingMethodException"/>，只是丧失"出生等级 &gt; 1
        /// 时补写曲线成长"这一项能力（<see cref="Core.Carriers.Creature.CreatureFactory.Spawn"/>
        /// 调用本方法后不产生任何效果，退化为本次改动之前"出生态没有额外成长"的行为），不是必须
        /// 支持的抽象成员。
        /// </para>
        /// </summary>
        void ApplyGrowthToCurrentLevel(Id unitId) { }

        /// <summary>当前等级。<paramref name="unitId"/> 未 <see cref="RegisterUnit"/> 时抛
        /// <see cref="System.ArgumentException"/>（下同）。</summary>
        int GetLevel(Id unitId);

        /// <summary>当前等级内已累计的经验（不是总经验）。</summary>
        long GetXp(Id unitId);

        /// <summary>升到下一级所需的经验；已满级时返回 0（T-N4-2 起：判定不再只依赖曲线最后一条
        /// 记录的 <c>xp_to_next</c> 数据约定，而是显式按"有效满级"（<see cref="ProgressionOptions.MaxLevel"/>
        /// 为 0 时等于曲线自身 <c>max_level</c>；为正值时取该值与曲线 <c>max_level</c> 的较小者，见
        /// <see cref="ProgressionHost"/> 判断记录）判断——即便某条曲线在有效满级对应的条目上误填了
        /// 非零 <c>xp_to_next</c>，本方法仍返回 0，ADR-0033 决策 9"满级后……<c>getXpToNext</c> 返回
        /// 零"是行为契约，不是数据自律）。</summary>
        long GetXpToNext(Id unitId);

        /// <summary>
        /// 直接记入一笔最终经验数值（不做来源换算，换算见 <see cref="GrantFromSource"/>/
        /// <see cref="GrantXp"/>）。已满级（同 <see cref="GetXpToNext"/> 的"有效满级"判定）时整笔
        /// 丢弃、记一条诊断警告、不发任何事件（见判断记录）；否则发 <see cref="XpGainedEvent"/>，
        /// 随后按曲线判定是否升级——一次调用可连跨多级，每跨一级发一次 <see cref="LevelUpEvent"/>；
        /// 跨级完成后把"从 2 级到最终等级"的成长累计值经 <see cref="StatModifierRemover"/> +
        /// <see cref="StatModifierWriter"/> 写入（来源 <c>prog.growth</c>），一次调用内只写一次
        /// （不是每跨一级各写一次）。
        /// <paramref name="amount"/> 为负抛 <see cref="System.ArgumentOutOfRangeException"/>。
        /// </summary>
        void AddXp(Id unitId, Id sourceId, long amount);

        /// <summary>按 <c>prog.xp_source</c> 表算出最终经验数值后转发给 <see cref="AddXp"/>（不
        /// 经 <see cref="XpGainedEvent"/> 之外的任何事件，返回值同 <see cref="AddXp"/> 的满级/事件
        /// 语义）。<paramref name="xpSourceId"/> 未登记时抛 <see cref="System.ArgumentException"/>。
        /// <para>
        /// T-N4-2 起分两种算法（ADR-0033 决策 3；拍板 4"<c>base_curve_ref</c> 存在时优先"）：
        /// 该来源没有 <c>base_curve_ref</c> 时，逐位保留本方法在 T-N4-2 之前的旧算法——
        /// <c>base_xp × weight × multiplier</c>（四舍五入到 <see cref="long"/>，负值钳为 0）——这是
        /// 拍板 4"保留一个版本周期"的兼容语义，不受本次改动影响。该来源存在 <c>base_curve_ref</c>
        /// 时，改走与 <see cref="GrantXp"/> 相同的曲线折算路径，但本方法的签名没有
        /// <see cref="XpContext"/> 参数可以指定"来源等级"——按"该来源与领取者当前等级相同"处理
        /// （即隐式 <c>sourceLevel</c> = 领取者调用时的当前等级，等级差 Δ 因此恒为 0 起算，
        /// <paramref name="multiplier"/> 参数直接乘在换算结果上，不经
        /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/> 钩子）。**契约疑点上报/临时
        /// 判断**：旧签名 <see cref="GrantFromSource"/> 从未设计成携带"怪物/任务/区域等级"这类新
        /// 信息，06 第 2.5 节与 ADR-0033 也没有对"旧入口撞上新曲线字段该怎么算"给出字面结论；真正
        /// 需要按怪物/任务/区域等级折算的场景应改走 <see cref="GrantXp"/> 显式传入
        /// <see cref="XpContext.SourceLevel"/>（T-N4-3/T-N4-4 的击杀/任务/探索监听器即改走新方法），
        /// 本方法此分支只是不让"内容作者给旧来源补了 <c>base_curve_ref</c> 却还在用旧调用方"这条
        /// 路径行为未定义/抛异常，取的是"能想到的、不引入额外未声明输入"的最小定义，供设计层复核
        /// 是否需要修正。
        /// </para>
        /// </summary>
        void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1);

        /// <summary>
        /// 三种来源（<c>kill</c>/<c>quest</c>/<c>discovery</c>）统一入口（分阶段落地计划 T-N4-2；
        /// ADR-0033 决策 1/3/9；06 第 2.5 节契约原文
        /// <c>grantXp(unitId: Id, sourceId: Id, context: XpContext): Number</c>）：按
        /// <c>prog.xp_source.kind</c> 与 <paramref name="context"/> 算出最终经验数值后转发给
        /// <see cref="AddXp"/>，返回实际入账值（即传给 <see cref="AddXp"/> 的 <c>amount</c>；已满级
        /// 时恒为 0，见下方"满级"小节）。<paramref name="sourceId"/> 未登记时抛
        /// <see cref="System.ArgumentException"/>。
        /// <para>
        /// 折算公式（ADR-0033 决策 3；<paramref name="sourceId"/> 必须有 <c>base_curve_ref</c>，
        /// 否则按 <see cref="GrantFromSource"/> 同一条"旧算法"分支处理，<paramref name="context"/>
        /// 此时只用其 <see cref="XpContext.SourceLevel"/> 无意义，等价于 <c>multiplier=1</c> 的
        /// <see cref="GrantFromSource"/>）：设 <c>baseAmount = prog.xp_base_curve[base_curve_ref]
        /// .Evaluate(context.SourceLevel)</c>，<c>Δ = context.SourceLevel − 领取者当前有效等级</c>，
        /// <c>ΔFactor = level_diff_ref 未登记时恒 1，否则 combat.level_diff_table[level_diff_ref]
        /// .xp_factor.Evaluate(Δ)</c>：
        /// <list type="bullet">
        /// <item><description><c>kind=kill</c>（或 <c>kind</c> 未登记的兼容兜底，见判断记录）：
        /// <c>baseAmount × (ExtraXpMultiplierProvider(unitId, sourceId) ?? 1) × ΔFactor</c>。</description></item>
        /// <item><description><c>kind=quest</c>：<c>(context.Equivalent ?? 1) × baseAmount ×
        /// ΔFactor</c>。</description></item>
        /// <item><description><c>kind=discovery</c>：<c>(context.Equivalent ?? 1) × baseAmount</c>
        /// ——刻意不接 <c>ΔFactor</c>（ADR-0033 决策 3 原文的探索公式没有等级差项，即便该来源同时
        /// 登记了 <c>level_diff_ref</c> 也不生效）。</description></item>
        /// </list>
        /// 结果四舍五入到 <see cref="long"/>（<see cref="System.MidpointRounding.AwayFromZero"/>），
        /// 负值钳为 0。
        /// </para>
        /// <para>
        /// 满级：判定同 <see cref="GetXpToNext"/>/<see cref="AddXp"/> 的"有效满级"——已满级时不计算
        /// 上述公式、直接返回 0，也不发 <see cref="XpGainedEvent"/>（ADR-0033 决策 9）。
        /// </para>
        /// <para>
        /// 判断记录（<see cref="XpContext.TierId"/> 本方法不消费）：见 <see cref="XpContext"/> 类型
        /// 注释"契约疑点上报"。
        /// </para>
        /// <para>
        /// 判断记录（默认实现，C#8 默认接口方法，ABI 门禁"公开 API 只能新增"，惯例同
        /// <see cref="ApplyGrowthToCurrentLevel"/>）：默认体固定返回 0、不产生任何副作用，本接口
        /// 目前只有 <see cref="ProgressionHost"/> 一个生产实现（它显式覆盖本方法）；既有测试假实现
        /// （<c>FakeProgressionHost</c> 一类，未覆盖本方法）不因新增本成员而编译失败，只是调用
        /// 本方法时静默返回 0、不推进任何等级状态——它们此前也不需要支持"按 <c>XpContext</c> 折算"
        /// 这项能力。
        /// </para>
        /// </summary>
        long GrantXp(Id unitId, Id sourceId, XpContext context) => 0;

        /// <summary>
        /// 设计层裁定（T-N4-4 附带任务）：<c>prog.xp_source</c> 的 <paramref name="sourceId"/> 是否
        /// 已登记——供调用方（<see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/>/
        /// <see cref="Core.Gameplay.ProgressionBridge.AreaTriggerDiscoveryXpListener"/>）在发放经验
        /// 前显式查询，取代此前"直接调用 <see cref="GrantXp"/>、用
        /// <c>try/catch (ArgumentException)</c> 兜未登记来源"的写法——两个监听器改为先查、查到才发，
        /// 不再依赖异常控制流表达"这个来源没配置"这一正常场景（异常应该保留给真正意外的情形）。
        /// <para>
        /// 判断记录（默认实现返回 <c>true</c>，不是 <c>false</c>）：ABI 门禁"公开 API 只能新增"——
        /// 本接口已发布，<see cref="ProgressionHost"/> 一个生产实现（显式覆盖本方法，读内部
        /// <c>prog.xp_source</c> 索引）；其它实现方（测试假实现、未来第三方实现，若存在）不因
        /// 新增本成员而编译失败。默认值取 <c>true</c> 而不是更"保守"的 <c>false</c>：本成员新增
        /// 之前，调用方（两个监听器）对任何 <see cref="IProgressionHost"/> 实现都无条件尝试调用
        /// <see cref="GrantXp"/>（不管来源是否登记）；若默认值改为 <c>false</c>，未覆盖本方法的
        /// 既有实现方（如测试替身）会让调用方从"总是尝试发放"静默退化为"总是跳过"，这是一次行为
        /// 倒退，不是"新增能力对旧实现透明"。默认值 <c>true</c> 保持"不知道就假设已登记、照常尝试"
        /// 这一与新增本成员之前完全一致的行为，只有显式覆盖本方法的 <see cref="ProgressionHost"/>
        /// 才获得"真正按注册表判断"的精确能力。
        /// </para>
        /// </summary>
        bool HasXpSource(Id sourceId) => true;
    }
}
