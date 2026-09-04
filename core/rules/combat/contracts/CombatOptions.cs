using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <see cref="CombatHost"/> 的构造期策略配置（见 06_规则层_属性技能战斗AI.md 第 4.7 节
    /// "策略配置项：命中表各项开关、护甲抗性曲线系数、死亡复活策略、脱战判定时长"、
    /// 01_分层与依赖.md L2 <c>combat</c> 行"命中表启用项、进出战斗判定规则"）。具体命中表各分支
    /// 的开关本身在 <c>combat.hit_table_config</c> 数据表里（见 <see cref="HitTableConfigId"/>
    /// 指向哪一条），本类只收敛"引用哪些属性 id、系数是多少"这一层配置。
    /// </summary>
    public sealed class CombatOptions
    {
        /// <summary>本次结算使用的命中表配置 id（见 04 第 1.1 节示例 <c>combat.hit_table.default</c>）。</summary>
        public Id HitTableConfigId { get; set; } = new Id("combat.hit_table.default");

        /// <summary>物理学派 id（见 06 第 4.3 节"school.physical 用护甲，其余用抗性"），
        /// 判断记录见本模块 README——设为可配置项而非硬编码常量，避免游戏层改学派命名时
        /// 需要改本模块源码。</summary>
        public Id PhysicalSchool { get; set; } = new Id("school.physical");

        /// <summary>物理学派的减免来源属性。</summary>
        public Id ArmorStat { get; set; } = new Id("stat.armor");

        /// <summary>非物理学派抗性属性的 id 前缀，实际查询属性 = <c>ResistStatPrefix + 学派末段名</c>
        /// （如学派 <c>school.fire</c>、前缀 <c>stat.resist_</c> → 查询属性 <c>stat.resist_fire</c>）。</summary>
        public string ResistStatPrefix { get; set; } = "stat.resist_";

        /// <summary>施法者伤害加成乘区来源属性（百分比数值，如 10 表示 +10%）。</summary>
        public Id DamageDonePctStat { get; set; } = new Id("stat.damage_done_pct");

        /// <summary>目标承伤乘区来源属性（百分比数值）。</summary>
        public Id DamageTakenPctStat { get; set; } = new Id("stat.damage_taken_pct");

        /// <summary>施法者治疗加成乘区来源属性（百分比数值）。</summary>
        public Id HealingDonePctStat { get; set; } = new Id("stat.healing_done_pct");

        /// <summary>治疗产生仇恨的系数（见 06 第 4.4 节"治疗按（可配置系数的）数值增加仇恨值"），默认 0.5。</summary>
        public double HealThreatCoefficient { get; set; } = 0.5;

        /// <summary>脱战判定时长（以数据集声明的时间单位计，见 06 第 4.5 节），默认 5。</summary>
        public double LeaveCombatDelay { get; set; } = 5.0;

        /// <summary>仇恨表单个单位最多保留的来源条目数（见落地方案 T2-8 行"禁止仇恨表大小无上限
        /// 增长"），默认 16。</summary>
        public int MaxThreatEntries { get; set; } = 16;

        /// <summary>死亡复活策略（见 06 第 4.6 节），本模块只在死亡结算时暴露该配置供 L3/L4 消费，
        /// 不在本模块内处理复活（见本模块 README"不负责什么"）。默认 <see cref="RespawnPolicy.RespawnPoint"/>。</summary>
        public RespawnPolicy DeathPolicy { get; set; } = RespawnPolicy.RespawnPoint;

        /// <summary>命中判定与暴击判定共用的随机数流（见 06 第 4.1 节"骰子 IRngHost.Next(RngStream)"）。</summary>
        public Id RngStream { get; set; } = new Id("combat.hit");
    }
}
