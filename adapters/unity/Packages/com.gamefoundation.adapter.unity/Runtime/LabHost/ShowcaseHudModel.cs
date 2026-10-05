#nullable enable
// ShowcaseHudModel：演示场景（ADR-0154）呈现层读模型。
//
// 判断记录（只读逻辑、不回流）：演示场景只改呈现。这份模型全部由 ShowcaseDirector 从逻辑世界已经发出的事件
// （命中确认、动作开始、单位死亡）与实体位置推演出来，HUD（uGUI）与测试都只读它；逻辑世界不读它，所以同一脚本在演示场景与原试玩场景上
// 逻辑指纹逐字节一致。
// 判断记录（体力条是纯呈现）：实验室数据只有生命一种资源（arch.power.health，上限随 stat.stamina），没有体力资源；
// 皮肤包要求 HUD 有体力条，这里的体力条是按玩家真实动作（闪避、攻击）推演的纯呈现量，StaminaIsPresentationOnly 恒为真，不是数据里的资源。
// 判断记录（技能栏冷却条）：实验室技能的 cooldown_duration 都是 0，没有真冷却；技能栏的扫光是该技能动作的总时长（动作开始事件的 DurationTicks × 步长），
// 即"这一下动作锁住输入的时间"，不是冷却。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Adapter.Unity.LabHost
{
    /// <summary>一个飘字（伤害数字）。</summary>
    public sealed class ShowcaseDamageNumber
    {
        public int Tick { get; internal set; }

        public Id Target { get; internal set; }

        /// <summary>事件里的伤害量（未取整）。</summary>
        public double Amount { get; internal set; }

        public bool IsCrit { get; internal set; }

        public bool IsKill { get; internal set; }

        /// <summary>飘字的显示文本（取整，暴击带叹号）。</summary>
        public string Text { get; internal set; } = string.Empty;

        /// <summary>出现处的世界坐标：2D 演示场景是目标头顶的世界点（<see cref="Height"/> 为 0）；2.5D 是目标脚下的地面点，头高放在 <see cref="Height"/>。</summary>
        public Vec2 WorldPos { get; internal set; }

        /// <summary>出现处相对 <see cref="WorldPos"/> 的抬高（世界单位，沿"向上"方向：2.5D 是相机上轴；2D 恒为 0，头高已计入 <see cref="WorldPos"/>）。</summary>
        public float Height { get; internal set; }

        /// <summary>已存活秒数（模拟时间）。</summary>
        public double Age { get; internal set; }

        /// <summary>道次：出字时同一目标已在场的数字个数（0 起），决定左右轮换与逐道抬高，避免连续命中叠在一处。</summary>
        public int Lane { get; internal set; }

        /// <summary>冲击等级（light/medium/heavy/massive），决定字号。</summary>
        public string ImpactClass { get; internal set; } = "light";
    }

    /// <summary>一条头顶血条（敌人）。</summary>
    public sealed class ShowcaseBar
    {
        public Id Entity { get; internal set; }

        public string Label { get; internal set; } = string.Empty;

        public double Hp { get; internal set; }

        public double MaxHp { get; internal set; }

        /// <summary>血量池大到没有意义（训练木桩 10 万）时不画条。</summary>
        public bool Hidden { get; internal set; }

        public bool Alive { get; internal set; } = true;

        /// <summary>被击中后的"最近受击"计时（秒，模拟时间）；用来让血条受击后短暂加亮。</summary>
        public double SinceHit { get; internal set; } = 99.0;

        public double Fraction => MaxHp <= 0.0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, Hp / MaxHp));
    }

    /// <summary>技能栏的一格。</summary>
    public sealed class ShowcaseSkillSlot
    {
        public string Key { get; internal set; } = string.Empty;

        public string Name { get; internal set; } = string.Empty;

        /// <summary>该格对应的技能 id 片段（命中即归到这一格）。</summary>
        public string[] SkillTails { get; internal set; } = Array.Empty<string>();

        /// <summary>动作锁定剩余秒数（模拟时间）；0 表示可用。</summary>
        public double LockRemaining { get; internal set; }

        public double LockTotal { get; internal set; }

        /// <summary>该格最近一次出手的技能名（连招格显示"第几段"）。</summary>
        public string LastSkill { get; internal set; } = string.Empty;

        public int ComboIndex { get; internal set; }

        public double Fraction => LockTotal <= 1e-9 ? 0.0 : Math.Max(0.0, Math.Min(1.0, LockRemaining / LockTotal));
    }

    public sealed class ShowcaseHudModel
    {
        public double PlayerHp { get; internal set; }

        public double PlayerMaxHp { get; internal set; } = 100.0;

        public double PlayerStamina { get; internal set; } = 100.0;

        public double PlayerMaxStamina { get; } = 100.0;

        /// <summary>体力条是纯呈现推演，不是数据里的资源（见文件顶部判断记录）。</summary>
        public bool StaminaIsPresentationOnly => true;

        /// <summary>玩家连击数（玩家连续命中，间隔超过 <see cref="ComboWindow"/> 清零）。</summary>
        public int Combo { get; internal set; }

        public double ComboAge { get; internal set; }

        public double ComboWindow { get; } = 2.0;

        public int BestCombo { get; internal set; }

        public List<ShowcaseDamageNumber> Numbers { get; } = new List<ShowcaseDamageNumber>();

        public int NumbersSpawned { get; internal set; }

        /// <summary>最近一次生成的飘字（测试断言用）。</summary>
        public ShowcaseDamageNumber? LastNumber { get; internal set; }

        public Dictionary<Id, ShowcaseBar> Bars { get; } = new Dictionary<Id, ShowcaseBar>();

        public ShowcaseSkillSlot[] Slots { get; } =
        {
            new ShowcaseSkillSlot { Key = "J", Name = "攻击", SkillTails = new[] { "combo1", "combo2", "combo3", "slash" } },
            new ShowcaseSkillSlot { Key = "K", Name = "闪避", SkillTails = new[] { "dodge" } },
            new ShowcaseSkillSlot { Key = "L", Name = "重击", SkillTails = new[] { "slam" } },
            new ShowcaseSkillSlot { Key = "U", Name = "蓄力", SkillTails = new[] { "charge" } },
        };

        // 计数（测试用）：特效生成次数。
        public int SparksSpawned { get; internal set; }

        public int SlashesSpawned { get; internal set; }

        public int DustSpawned { get; internal set; }

        public int RingsSpawned { get; internal set; }

        /// <summary>该次命中事件里的冲击等级与反应（最近一次命中，测试断言用）。</summary>
        public string LastHitClass { get; internal set; } = string.Empty;

        public string LastHitReaction { get; internal set; } = string.Empty;
    }
}
