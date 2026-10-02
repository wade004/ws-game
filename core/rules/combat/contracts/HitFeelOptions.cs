using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <see cref="HitFeelHost"/> 的策略配置（手感设计/03 第 3/4 节、05 第 9 节）。全部有缺省值；缺省档案下顿帧为 0、受击裁决
    /// 不改变既有结果由手感档案本身保证（<c>rpg_classic</c> 预设全零），本类只放"属性引用、策略开关、冲击等级映射"。
    /// </summary>
    public sealed class HitFeelOptions
    {
        /// <summary>
        /// 目标韧性（poise）读哪个属性（手感设计/03 第 4 节"属性，可选；未声明视为 0"）。属性未登记或单位未注册按 0 处理，
        /// 不抛异常也不记警告（韧性本来就是可选能力）。
        /// </summary>
        public Id PoiseStat { get; set; } = new Id("stat.poise");

        /// <summary>
        /// 光环类霸体：目标身上带有该 <c>aura_def</c> 即视为霸体（时间线 <c>armor_start/armor_end</c> 经
        /// <see cref="IActionStateQuery.IsSuperArmor"/> 读取，与本项并存、任一成立即霸体）。缺省 null 即没有光环类霸体。
        /// </summary>
        public Id? SuperArmorAuraDef { get; set; }

        /// <summary>霸体期间受击方是否仍吃顿帧（手感设计/03 第 4 节"视 super_armor_hitstop 策略"）；缺省是——仍扣血、仍有打击停顿。</summary>
        public bool SuperArmorTargetHitstop { get; set; } = true;

        /// <summary>
        /// 判断一个技能是不是时间线（timeline）结算：是则 instant 适配器不为它的 <c>combat.damage_dealt</c> 合成
        /// <c>combat.hit_confirmed</c>（时间线命中由空间命中切片自己发）。缺省 null——全部视为 instant。
        /// 参数为伤害事件携带的技能 id（普通攻击为保留 id）。
        /// </summary>
        public Func<Id?, bool>? IsTimelineSkill { get; set; }

        /// <summary>
        /// 是否处于离散（回合制）时间模型：为真时顿帧与硬直整体不生效（手感设计/00 第 7 节、03 第 6 节）。缺省 null 即连续模式。
        /// 装配根可传 <c>() =&gt; clockHost.Mode == TimeModelMode.Discrete</c>。
        /// </summary>
        public Func<bool>? IsDiscreteMode { get; set; }

        /// <summary>是否让 instant（目标选择式）命中也走顿帧与受击裁决（接 <c>combat.damage_dealt</c>/<c>combat.attack_avoided</c> 合成 <c>combat.hit_confirmed</c>）。缺省开。</summary>
        public bool InstantModeConfirmations { get; set; } = true;

        /// <summary>
        /// 冲击等级 → 击退距离倍率（手感设计/02 第 6 节"× 冲击等级倍率"）。击退距离 = 攻击方 <c>knockback_distance</c>（标定后世界单位）
        /// × (1 − 目标击退抗性) × 本表倍率；表里没有的等级取 1。<b>试调起点，未经试玩</b>。
        /// </summary>
        public IReadOnlyDictionary<string, double> KnockbackImpactMultipliers { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["light"] = 0.5,
            ["medium"] = 0.75,
            ["heavy"] = 1.0,
            ["massive"] = 1.5,
        };

        /// <summary>
        /// 冲击等级 → 动态韧性伤害倍率（手感落地 M4-W3，口径同 <see cref="KnockbackImpactMultipliers"/>：表里没有的等级取 1）。命中声明的 <c>poise_damage</c> 乘以
        /// 攻击方 <c>impact_class</c> 对应的倍率后才从目标的韧性池里扣（<c>combat.poise_changed</c> 的 <c>Damage</c> 报告乘后的有效值）。
        /// <b>缺省空表——不缩放，与此前逐位一致</b>；游戏要"重击更削韧"时自己填表，例如与击退同一组 light 0.5 / medium 0.75 / heavy 1 / massive 1.5（试调起点，未经试玩）。
        /// 只作用于动态韧性（命中声明了 <c>poise_damage</c> 的路径），静态韧性规则不读它。
        /// </summary>
        public IReadOnlyDictionary<string, double> PoiseDamageImpactMultipliers { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// 冲击等级 → 受击反应（手感设计/03 第 4 节：light → stagger_light；medium → stagger；heavy → knockback；massive → knockdown）。
        /// 冲击等级可扩，表里没有的等级取 <see cref="UnknownImpactReaction"/>。
        /// </summary>
        public IReadOnlyDictionary<string, HitReaction> ImpactReactions { get; set; } = new Dictionary<string, HitReaction>(StringComparer.Ordinal)
        {
            ["light"] = HitReaction.StaggerLight,
            ["medium"] = HitReaction.Stagger,
            ["heavy"] = HitReaction.Knockback,
            ["massive"] = HitReaction.Knockdown,
        };

        /// <summary>扩展冲击等级在 <see cref="ImpactReactions"/> 里没有映射时的受击反应，缺省 <see cref="HitReaction.Stagger"/>。</summary>
        public HitReaction UnknownImpactReaction { get; set; } = HitReaction.Stagger;

        /// <summary>击退总时长（秒），≤ 0 表示用运动层的缺省击退时长（<c>MovementOptions.KnockbackDurationSeconds</c>）。</summary>
        public double KnockbackDurationSeconds { get; set; } = 0.0;
    }
}
