using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 实体 → 武器风格引用（ADR-0017 决策 e）：按实体 id 查询其当前应使用的
    /// <c>display.weapon_style</c> 行 id（<c>display.map.weapon_style_ref</c>，见 09_表现层.md
    /// 第 4.4 节），供 <see cref="IWeaponStyleResolver"/>/<c>AnimClipResolver</c>（W6-B）一类消费方
    /// 按 <c>weaponStyleRef</c> 查表，不必各自重复"从实体身上找到当前主手武器"这一段装备查询逻辑。
    /// <para>
    /// 判断记录（与 <see cref="IWeaponStyleResolver"/> 的关注点分工）：<see cref="IWeaponStyleResolver"/>
    /// 已知 <c>weaponStyleRef</c> 之后按技能 id 查覆盖表（"weaponStyleRef → 具体特效/剪辑"这一段查表），
    /// 本接口负责前置的"实体 id → weaponStyleRef"这一段（"这个实体现在算哪一套武器表现档案"），二者
    /// 组合起来才是完整的"实体身上换了武器 → 播放不同表现"链路（09 第 4.4 节"关联方式"一段）。
    /// </para>
    /// </summary>
    public interface IWeaponStyleSource
    {
        /// <summary>该实体当前主手武器对应的 <c>display.weapon_style</c> id；实体未装备任何主手武器、
        /// 该武器物品模板未声明 <c>weapon_style_ref</c>，或查不到该实体的装备状态时返回 null（不抛
        /// 异常，同表现层一贯"缺表现资源不阻断游戏"的宽容策略）。</summary>
        Id? GetWeaponStyleRef(Id entityId);
    }

    /// <summary>
    /// 按单位 id 查询其当前装备的主手武器物品模板 id 的窄契约委托（同
    /// <see cref="Presentation.FeedbackBinder.Contracts.EntityLogicalIdResolver"/>/
    /// <see cref="AnchorResolver"/> 一类"按需窄契约"惯例）：本模块——presentation/vfx_sfx——不直接
    /// 依赖 <c>core/carriers/item</c> 的具体装备宿主实现（避免表现层反向耦合到某一种具体的装备存储
    /// 形状），由装配层按具体游戏使用的装备宿主实现（通常包一层
    /// <c>EquipmentHost.GetAllEquippedInstances(unitId)[mainHandSlotId].TemplateId</c>，"主手槽位"
    /// 具体是哪个 id 由该实现自行决定，不属于本委托关心的范围）构造并注入
    /// <see cref="Presentation.VfxSfx.Core.EquipmentWeaponStyleSource"/>。
    /// </summary>
    public delegate Id? MainHandWeaponTemplateResolver(Id unitId);
}
