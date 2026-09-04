using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 武器表现档案查询（见 09_表现层.md 第 4.4 节）：本模块只暴露 vfx_sfx 关心的"按技能覆盖命中/
    /// 挥舞特效"两项查询；<c>auto_attack_anim</c>/<c>cast_anim_override</c> 的动作剪辑查询属于
    /// CharacterRig 职责，不在本接口范围（见 <see cref="WeaponStyleDef"/> 类型注释）。
    /// </summary>
    public interface IWeaponStyleResolver
    {
        /// <summary>按 <paramref name="weaponStyleRef"/>（<c>display.map.weapon_style_ref</c>）与
        /// <paramref name="skillId"/> 查 <c>impact_vfx_override</c>；未登记档案或该技能未覆盖时
        /// 返回 null（调用方应回退到技能/光环自身的 DisplayInfo 命中特效）。</summary>
        Id? ResolveImpactVfxOverride(Id weaponStyleRef, Id skillId);

        /// <summary>查 <paramref name="weaponStyleRef"/> 档案的挥舞轨迹特效；未登记档案或该档案
        /// 未声明 <c>swing_vfx</c> 时返回 null。</summary>
        Id? ResolveSwingVfx(Id weaponStyleRef);
    }
}
