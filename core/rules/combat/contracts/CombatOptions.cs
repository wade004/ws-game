using System;
using System.Collections.Generic;
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
        /// 目标承伤乘区来源属性（百分比数值）。T-N1-7 之前是"目标乘区"步骤唯一读取的属性，且不经
        /// <c>scope</c> 过滤；T-N1-7（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段）起，"目标乘区"步骤实际读取的属性集合改为
        /// <c>{DamageTakenPctStat} ∪ DamageTakenPctStats</c>（去重，见 <see cref="DamageTakenPctStats"/>
        /// 判断记录），本属性同样经 <see cref="EffectContext.SourceKind"/> 作用域过滤——
        /// 判断记录（不改变既有回放基线）：既有内容数据从未给 <c>stat.damage_taken_pct</c> 登记过
        /// <c>scope</c> 字段，<see cref="Core.Numbers.StatBlock.IStatHost.GetScope"/> 对未登记属性
        /// 缺省返回 <c>"any"</c>（与 <c>stat.definition.scope</c> 字段本身缺省语义一致），
        /// <c>any</c> 恒匹配一切来源类别，因此本属性新增的作用域过滤对既有内容数据是无操作的
        /// 恒等变换，结果逐位不变，见
        /// <c>ResolverHitTableTests.Resolve_FullChain_MatchesHandCalculatedValue</c>。
        /// </summary>
        public Id DamageTakenPctStat { get; set; } = new Id("stat.damage_taken_pct");

        /// <summary>
        /// T-N1-7（同上决策/章节）"目标乘区"步骤按 <c>scope</c> 匹配 <c>sourceKind</c> 遍历减免
        /// 属性的显式配置清单——与 <see cref="DamageTakenPctStat"/> 求并集（去重）后，逐条经
        /// <see cref="Core.Numbers.StatBlock.IStatHost.GetScope"/> 按 <see cref="EffectContext.SourceKind"/>
        /// 过滤（<c>any</c> 恒匹配；<c>from_player</c> 仅 <see cref="SourceKind.Player"/>；
        /// <c>from_creature</c> 仅 <see cref="SourceKind.Creature"/>）后求和，并入目标乘区
        /// （见 <see cref="Resolver"/> 判断记录"目标乘区"一节）。默认空列表——不改变既有回放基线
        /// （单机场景下不配置本清单，行为与 T-N1-7 之前逐位一致）。
        /// <para>
        /// 判断记录（复核返工：撤回"按 <c>stat.definition.category</c> 批量扫描"的首版实现，
        /// 改为本"显式 id 清单"）：06 第 4.1 节修订段与 ADR-0030 决策 5 原文是"目标乘区步骤按
        /// scope 匹配读取减免属性"——识别"哪些属性属于减免角色"由结算配置显式给出，不是由某个
        /// <c>stat.definition.category</c> 取值批量圈定。首版实现按类别扫描（默认类别
        /// <c>"defense"</c>）在复核中被指出会造成真实内容接入即错的后果：ADR-0030 决策 9 明确把
        /// "护甲"归为 <c>defense</c> 类别的推荐分类，一旦游戏按此分类登记护甲属性，类别扫描会把
        /// 护甲的原始数值（如 300）当成"目标承伤 +300%"直接计入目标乘区——这不是"两个新扫描
        /// 角色共享同一默认类别"那种可以事后靠改配置规避的边界情形，而是默认配置本身与 ADR
        /// 推荐分类相冲突，一接入护甲数据就错，比"重复计入"更严重。显式清单从根本上避免了这一
        /// 冲突：本属性的默认值是空列表，不会漫无目的地圈进任何按内容分类惯例登记的属性，游戏层
        /// 需要哪条属性参与目标乘区，必须逐个显式列出 id——与 <c>ArmorStat</c>/
        /// <c>ResistStatPrefix</c> 等既有"逐条显式引用属性 id"的配置风格一致，也不再需要
        /// <c>category</c> 枚举里挤出一个专用取值。
        /// </para>
        /// </summary>
        public IReadOnlyList<Id> DamageTakenPctStats { get; set; } = Array.Empty<Id>();

        /// <summary>
        /// T-N1-7（同上决策/章节新增介入点；复核返工后改为显式清单，理由同
        /// <see cref="DamageTakenPctStats"/> 判断记录）：命中表"暴击"分支取样（<c>IRngHost.Next</c>）
        /// 之前，逐条经 <see cref="Core.Numbers.StatBlock.IStatHost.GetScope"/> 按
        /// <see cref="EffectContext.SourceKind"/> 过滤后求和，从
        /// <c>ResolveChance(table.Crit, sourceId) + crit_chance_bonus</c> 里扣减（下限 0，不影响
        /// 其余命中表分支与暴击倍率本身，不改变 RNG 流 id、不改变取样次数）。默认空列表——不改变
        /// 既有回放基线。本属性是 T-N1-7 全新介入点，没有"既有单属性配置项"需要兼容（不同于
        /// <see cref="DamageTakenPctStat"/>）。
        /// </summary>
        public IReadOnlyList<Id> CritTakenReductionStats { get; set; } = Array.Empty<Id>();

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
        /// T-N1-8（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 6；06 第 4.2 节修订段）：本次结算使用的等级差规则表 <c>combat.level_diff_table</c>
        /// 记录 id。缺省 <c>null</c>——不接等级差表，<see cref="Resolver.DetermineHit"/> 的未命中率/
        /// 暴击率公式退化为"Δ 加成/压制恒为 0"，行为与 T-N1-8 之前逐位一致（不改变既有回放基线/既有
        /// 内容数据的结算结果）。配置为某条 <c>combat.level_diff_table</c> 记录 id 时才会按 Δ 查表：
        /// 该记录在加载期未找到（表未注册/id 不存在）同样按"Δ 加成/压制=0"退化，不抛异常（同
        /// <see cref="ArmorStat"/> 等既有属性缺失"按 0 处理"的防御姿态）。
        /// </summary>
        public Id? LevelDiffTableId { get; set; } = null;

        /// <summary>
        /// T-N1-8（同上决策/章节，"有效等级是否计入装备等级偏移"策略配置项，默认关闭）：关闭时
        /// "有效等级"恒等于 <see cref="IUnitAccess.GetLevel"/> 返回的角色等级本身；开启时"有效等级
        /// = 角色等级 + <see cref="IGearLevelOffsetProvider.GetGearLevelOffset"/>"（见该接口判断
        /// 记录——本任务范围内没有可用的真实装备等级偏移来源，只落地这个策略项与接口钩子，真实实现
        /// 留给后续阶段）。默认关闭的理由（06 第 4.2 节修订段原文）：命中属性本身来自装备，装备落后
        /// 命中就低，已是自然路径；两者叠加会惩罚两次。
        /// </summary>
        public bool EffectiveLevelIncludesGearOffset { get; set; } = false;

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

        /// <summary>
        /// T-N4-9（[ADR-0034](../../../../architecture/adr/0034-单一货币与价格挂物品等级.md)
        /// 决策 7；数值设计分阶段落地计划拍板 9"'进入战斗时移除坐骑光环'归 CombatOptions"）：
        /// 进入战斗（<see cref="CombatHost.NotifyCombatEvent"/> 由"不在战"转"在战"那一刻，见该
        /// 方法判断记录）时是否移除坐骑光环。默认 <c>true</c>（08/13 号文档"进入战斗时移除坐骑光环
        /// 为策略配置项，默认开"）。<b>只是开关本身</b>——具体"哪些光环算坐骑光环"由
        /// <see cref="MountAuraDispelType"/> 决定，本项为 <c>true</c> 但未配置
        /// <see cref="MountAuraDispelType"/>（或 <see cref="DismountMountAuras"/> 未接线）时不产生
        /// 任何实际移除效果（同本模块一贯的"未接线不阻断"退化惯例）。
        /// </summary>
        public bool DismountOnEnterCombat { get; set; } = true;

        /// <summary>
        /// 坐骑光环的识别方式（契约缺口/临时判断，待设计层确认）：06/08/ADR-0034 只拍板了"进入
        /// 战斗时移除坐骑光环"这条策略本身，未规定"哪些光环算坐骑光环"具体落哪个字段。本模块复用
        /// <c>core/rules/skill</c> 既有的 <c>aura_def.dispel_type</c> 分类机制（同"按类别、数量"
        /// 批量移除光环的既有 <c>dispel</c> 效果原语，见 <c>AuraHost.Dispel</c>），而不是像
        /// <see cref="DamageTakenPctStats"/> 那样新增一份平行的显式 id 清单——两者风险等价（都需要
        /// 内容作者显式在每条 <c>aura_def</c> 上打标签才会命中，不会像"按 category 批量扫描属性"
        /// 那样误吸收无关数据，见判断记录 18），但 <c>dispel_type</c> 是本来就存在、专门用于"给
        /// 光环打一个可批量批量操作的分类标签"这件事的字段，复用它不需要新增契约面。默认
        /// <c>null</c>（未配置，即使 <see cref="DismountOnEnterCombat"/> 为真也不移除任何光环）——
        /// 游戏内容需要显式声明坐骑光环们共用的 <c>dispel_type</c> 取值（如 <c>"mount"</c>），并把
        /// 坐骑技能的 <c>apply_aura</c> 效果指向一条声明了该 <c>dispel_type</c> 的 <c>aura_def</c>。
        /// </summary>
        public Id? MountAuraDispelType { get; set; }

        /// <summary>移除坐骑光环的窄委托签名：<paramref name="unitId"/> 身上全部
        /// <c>aura_def.dispel_type == dispelType</c> 的光环实例应被移除。</summary>
        public delegate void DismountMountAurasDelegate(Id unitId, Id dispelType);

        /// <summary>
        /// 移除坐骑光环的窄契约委托（L2 <c>combat</c> 不直接依赖 <c>core/rules/skill.AuraHost</c>
        /// 具体类型——本模块 README 既有约束"不实现光环/免疫/吸收池的真实存储"——惯例同
        /// <c>core/gameplay/death.DeathPolicyOptions.ReviveUnit</c> 等既有跨模块边界委托）。装配根
        /// <c>core/rules/assembly.RulesAssembly</c> 在 <c>Skill.AuraQuery</c> 运行期确实是
        /// <c>AuraHost</c>（真实装配的唯一实现，测试替身可能不是）时才接线到
        /// <c>AuraHost.Dispel(unitId, dispelType, int.MaxValue)</c>（防御性 <c>is</c> 模式判断，
        /// 同 <c>GameplayAssembly</c> 第 10.5 步 <c>world is WorldSim</c> 一贯做法），未接线（为
        /// <c>null</c>）时进战下马退化为"不移除任何光环，不抛异常"。
        /// </summary>
        public DismountMountAurasDelegate? DismountMountAuras { get; set; }
    }
}
