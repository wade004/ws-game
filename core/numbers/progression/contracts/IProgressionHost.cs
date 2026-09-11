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

        /// <summary>升到下一级所需的经验；已满级时返回曲线最后一条记录的 <c>xp_to_next</c>
        /// （数据约定该值在满级条目上恒为 0，见 <c>schema/README.md</c>）。</summary>
        long GetXpToNext(Id unitId);

        /// <summary>
        /// 直接记入一笔最终经验数值（不做来源换算，换算见 <see cref="GrantFromSource"/>）。
        /// 已满级时整笔丢弃、记一条诊断警告、不发任何事件（见判断记录）；否则发
        /// <see cref="XpGainedEvent"/>，随后按曲线判定是否升级——一次调用可连跨多级，每跨一级发
        /// 一次 <see cref="LevelUpEvent"/>；跨级完成后把"从 2 级到最终等级"的成长累计值经
        /// <see cref="StatModifierRemover"/> + <see cref="StatModifierWriter"/> 写入（来源
        /// <c>prog.growth</c>），一次调用内只写一次（不是每跨一级各写一次）。
        /// <paramref name="amount"/> 为负抛 <see cref="System.ArgumentOutOfRangeException"/>。
        /// </summary>
        void AddXp(Id unitId, Id sourceId, long amount);

        /// <summary>按 <c>prog.xp_source</c> 表算出最终经验数值（<c>base_xp × weight ×
        /// multiplier</c>，四舍五入到 <see cref="long"/>）后转发给 <see cref="AddXp"/>。
        /// <paramref name="xpSourceId"/> 未登记时抛 <see cref="System.ArgumentException"/>。</summary>
        void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1);
    }
}
