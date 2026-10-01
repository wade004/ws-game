using System;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Rules.Skill;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 武器切换后的普通攻击技能映射（<see cref="IActionSkillBinding"/> 契约注释点名的"武器切换后的普攻技能"，
    /// 手感设计/08 第 1 节"主手武器 → 判定"、03 第 1 节 <c>auto_attack_timeline_ref</c>）：<c>attack</c> 类输入
    /// 映射到单位当前主手武器的 <c>feel.weapon.auto_attack_timeline_ref</c>；武器没声明（或空手）时落到构造时给定的
    /// 空手普攻技能；两者都没有则不映射（返回 false，缓冲记录保留）。
    /// <para>
    /// 判断记录（只映射 attack 类）：其它类别（技能、闪避、交互）的输入动作 → 技能映射是游戏装配层自己的事，
    /// 本类不越界；游戏可把本类包进自己的复合映射里。
    /// </para>
    /// <para>
    /// 判断记录（每次读最新装备）：本类无缓存，每次经 <see cref="IFeelEquipmentProvider"/> 读当前主手武器引用，
    /// 因此换装后下一次映射立即使用新武器的普攻，不依赖换装链是否已对账。
    /// </para>
    /// </summary>
    public sealed class WeaponActionBinding : IActionSkillBinding
    {
        private readonly IFeelEquipmentProvider _equipment;
        private readonly FeelWeaponCatalog _catalog;
        private readonly Id? _unarmedAttackSkill;

        public WeaponActionBinding(IFeelEquipmentProvider equipment, FeelWeaponCatalog catalog, Id? unarmedAttackSkill = null)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _unarmedAttackSkill = unarmedAttackSkill;
        }

        public bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId)
        {
            if (intent.Class != ActionClass.Attack)
            {
                skillId = default;
                return false;
            }

            return TryResolveAttackSkill(actorId, out skillId);
        }

        /// <summary>单位当前普通攻击对应的技能（主手武器的 <c>auto_attack_timeline_ref</c>，否则空手普攻）。</summary>
        public bool TryResolveAttackSkill(Id actorId, out Id skillId)
        {
            var main = _equipment.GetMainWeaponRef(actorId);
            if (main != null && _catalog.TryGet(main, out var info) && info.AutoAttackTimelineRef.HasValue)
            {
                skillId = info.AutoAttackTimelineRef.Value;
                return true;
            }

            if (_unarmedAttackSkill.HasValue)
            {
                skillId = _unarmedAttackSkill.Value;
                return true;
            }

            skillId = default;
            return false;
        }
    }
}
