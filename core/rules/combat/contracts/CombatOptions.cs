using System;
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

        /// <summary>
        /// 目标承伤乘区来源属性（百分比数值）。T-N1-7 之前是"目标乘区"步骤唯一读取的属性；
        /// T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段）起，"目标乘区"步骤额外遍历
        /// <see cref="DamageTakenCategory"/> 类别下按 <see cref="EffectContext.SourceKind"/> 作用域
        /// 匹配的全部属性并入同一乘区（见 <see cref="Resolver"/> 判断记录"目标乘区"一节）。
        /// <para>
        /// 判断记录（与新扫描机制的兼容关系——两者都生效，同一属性不重复计入，不是"非空则只用它"
        /// 二选一）：<see cref="Resolver"/> 对新扫描机制的遍历结果显式排除本属性 id（即便游戏层把
        /// 本属性自己的 <c>stat.definition.category</c> 也配置成 <see cref="DamageTakenCategory"/>，
        /// 也只会被计入一次），因此本属性与新扫描机制是"求和叠加"关系——硬约束"不改变既有回放
        /// 基线"因此天然满足：既有内容数据从未给任何属性配置过
        /// <c>category==DamageTakenCategory 默认值("defense")</c>，新扫描结果恒为空，行为与
        /// T-N1-7 之前逐位一致，见本模块 README"T-N1-7"一节判断记录与
        /// <c>ResolverHitTableTests.Resolve_FullChain_MatchesHandCalculatedValue</c>。</para>
        /// </summary>
        public Id DamageTakenPctStat { get; set; } = new Id("stat.damage_taken_pct");

        /// <summary>
        /// T-N1-7（同上决策/章节）："目标乘区"步骤新扫描机制按此类别筛选 <c>stat.definition</c>：
        /// 遍历目标单位全部 <c>category == DamageTakenCategory</c> 且 <c>scope</c> 与结算上下文
        /// <see cref="EffectContext.SourceKind"/> 匹配（<c>any</c> 恒匹配；<c>from_player</c> 仅
        /// <see cref="SourceKind.Player"/>；<c>from_creature</c> 仅 <see cref="SourceKind.Creature"/>）
        /// 的属性，与 <see cref="DamageTakenPctStat"/> 求和后一次性并入目标乘区（<see cref="Resolver"/>
        /// 判断记录）。默认 <c>"defense"</c>——判断记录（类别选择与潜在的内容层职责边界）：
        /// <c>stat.definition.category</c> 的合法取值只有 <c>primary/derived/percent/defense/misc</c>
        /// 五个（<see cref="Core.Numbers.StatBlock.StatSchemas.CategoryValues"/>），06 第 4.1 节修订段
        /// 与 ADR-0030 决策 5 只规定"目标乘区步骤按 scope 匹配读取减免属性"，未规定该用哪个既有
        /// category 值筛选候选集合（架构缺口，已如实上报，见本模块 README"T-N1-7"一节"契约疑点"）；
        /// 选择 <c>defense</c> 是因为它是 ADR-0030 决策 9 推荐分类"防御"一类的既有落点，且当前
        /// <c>data/_sample</c>/<c>games/_template</c>/本模块测试夹具都不存在任何
        /// <c>category=="defense"</c> 的属性定义（唯一潜在的同类别属性——护甲/抗性——现有样例数据
        /// 的 <c>category</c> 实际落在 <c>derived</c>/<c>misc</c>，见样例数据判断记录），新扫描机制
        /// 默认零命中，不改变既有回放基线。<b>内容层职责</b>：若某个游戏的护甲/抗性属性也配置成
        /// <c>category=="defense"</c> 且 <c>scope</c> 非空，会被本扫描一并计入目标乘区（与"减免"
        /// 步骤已经消费的护甲/抗性曲线输入是两回事，叠加是否符合该游戏设计意图由内容作者判断）——
        /// 框架不做"排除已知护甲/抗性属性 id"这类隐式豁免（那属于自行发明契约），只提供本类别配置项
        /// 供游戏层按自己的内容规划选用不同类别值规避潜在的语义重叠。
        /// </summary>
        public string DamageTakenCategory { get; set; } = "defense";

        /// <summary>
        /// 被暴击减免属性类别（T-N1-7，同上决策/章节新增介入点）：命中表"暴击"分支掷骰之前，
        /// <see cref="Resolver"/> 遍历目标单位全部 <c>category == CritTakenReductionCategory</c> 且
        /// <c>scope</c> 与 <see cref="EffectContext.SourceKind"/> 匹配的属性求和，从攻击者暴击率里
        /// 扣减（下限 0，不影响其余命中表分支与暴击倍率本身），介入点在取样（<c>IRngHost.Next</c>）
        /// 之前——不改变 RNG 流 id、不改变取样次数。默认 <c>"defense"</c>，理由与零基线变化保证同
        /// <see cref="DamageTakenCategory"/> 判断记录（当前样例数据/测试夹具均不存在
        /// <c>category=="defense"</c> 的属性，扫描默认零命中）。本属性是 T-N1-7 全新介入点，没有
        /// "既有单属性配置项"需要兼容（不同于 <see cref="DamageTakenPctStat"/>）。
        /// <para>
        /// 契约疑点（如实上报，未在本任务内自行发明修复）：<see cref="DamageTakenCategory"/> 与本属性
        /// 默认值都是 <c>"defense"</c>——五值 category 枚举（<see
        /// cref="Core.Numbers.StatBlock.StatSchemas.CategoryValues"/>）里，经逐一核对
        /// <c>data/_sample</c>/<c>games/_template</c>/本模块与 Replay 测试夹具的既有内容，
        /// <c>defense</c> 是唯一当前确认零占用的取值（<c>primary</c>/<c>derived</c>/<c>percent</c>
        /// 均已被既有主属性/派生属性/评级属性占用，<c>misc</c> 被既有"次要属性无 is_rating"迁移
        /// 结果大量占用——含 Replay 夹具自身的既有属性，若拿 <c>misc</c> 做默认值会直接吃进这些
        /// 既有属性、污染回放基线），因此两个新扫描类别默认值目前只能共享同一个安全取值，
        /// 无法各自独立选一个互不相干的默认类别。后果：若某游戏同时使用两种减免机制并且都用默认
        /// 类别值 <c>defense</c>，一条属性只要落在该类别下就会被两个扫描各计入一次（一条数值被
        /// 同时当"目标承伤减免"与"被暴击减免"两种量纲消费，语义不同、通常不是内容作者的本意）——
        /// 框架现阶段的应对是把两者都做成独立可配置项，要求需要二者并存的游戏显式把其中一个改成
        /// 另一个类别值（如自定义一个游戏侧含义相近的 <c>misc</c> 子集，或等待后续 ADR 视需要给
        /// <c>stat.definition</c> 增加更细的分类字段）；本任务不新增 category 枚举值或新字段——
        /// 那是 ADR-0030 决策 1 已拍板的枚举集合，扩展需要走 <c>architecture/12_扩展与变更流程.md</c>
        /// 的 ADR 流程，不是 T-N1-7 任务范围内的实现细节。
        /// </para>
        /// </summary>
        public string CritTakenReductionCategory { get; set; } = "defense";

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

        /// <summary>
        /// 结算追踪回调（消费方反馈 2026-09-11 编辑器第 31 条，见
        /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md"方案 1"）：仅供内容工具/
        /// 诊断消费，不改变结算管线本身。<see cref="Resolver.Resolve"/> 是全部伤害/治疗效果原语
        /// （<c>school_damage</c>/<c>weapon_damage_pct</c>/<c>heal</c>）落地的唯一出口（见
        /// <c>core/rules/skill/core/EffectDispatcher.ApplyDamageOrHeal</c> 恒调用
        /// <c>ICombatHost.ResolveEffect</c>），因此技能瞬发/读条完成/引导 tick、光环周期效果
        /// （<c>periodic_damage</c>/<c>periodic_heal</c>）、Proc 触发的嵌套施法（<c>TriggerCast</c>）、
        /// 弹道命中后效果全部经同一出口，不需要在各调用点分别接线。
        /// <para>
        /// 判断记录：
        /// (1) 本回调在 <see cref="Resolver.Resolve"/> 每一条返回路径（"目标已死亡"短路、
        /// miss/dodge/parry 判定终止短路、完整九步落地）返回前恰好调用一次，传入本次结算的
        /// <see cref="EffectContext"/> 与完整的 <see cref="ResolveResult"/>（含
        /// <see cref="ResolveResult.Steps"/>，与 06 第 4.1 节固定步骤一一对应）。
        /// </para>
        /// <para>
        /// (2) 未设置（缺省 <c>null</c>）时零开销——<see cref="Resolver.Resolve"/> 只多一次 null
        /// 判断，不分配、不改变既有输出（<c>ResolveResult</c>/事件/仇恨表/Stat/Power 变化逐字节
        /// 不变）。
        /// </para>
        /// <para>
        /// (3) 回调本身抛出的任何异常都会被 <see cref="Resolver.Resolve"/> 捕获后经
        /// <see cref="ICombatDiagnostics.Warn"/> 记一次警告并继续——调用方（工具/诊断代码）里的
        /// bug 不应打断真实游戏结算，本次结算的落地、事件、仇恨表不受回调异常影响。
        /// </para>
        /// </summary>
        public Action<EffectContext, ResolveResult>? ResolveTrace { get; set; }
    }
}
