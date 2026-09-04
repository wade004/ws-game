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
        /// 判断记录）。</summary>
        void RegisterUnit(Id unitId, Id curveId, int startLevel = 1);

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
