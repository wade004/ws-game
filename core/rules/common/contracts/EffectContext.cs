using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Rules.Common
{
    /// <summary>
    /// 结算管线的输入（见 06 第 4.1 节"每一步只依赖上一步的输出与只读的 StatHost/PowerHost 查询，
    /// 不允许跨步骤回填修改，保证同种子同输入同结果"）。不可变：全部成员为构造期设定的只读属性，
    /// 没有任何方法可以在构造后修改本实例——结算管线的确定性要求（拍板决策 3）落到类型层面就是
    /// "输入对象本身不能变"。
    /// </summary>
    public sealed class EffectContext
    {
        private static readonly JsonObject EmptyParams = new JsonObjectBuilder().Build();
        private static readonly IReadOnlyList<Id> EmptyTags = Array.Empty<Id>();

        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id SkillId { get; }

        public EffectKind Kind { get; }

        /// <summary>学派（见 06 第 3.1 节 <c>school</c>，用于免疫、抗性、护甲曲线按学派区分）。</summary>
        public Id School { get; }

        public double BaseValue { get; }

        public double Coefficient { get; }

        public JsonObject Params { get; }

        /// <summary>周期效果（<c>periodic_damage</c>/<c>periodic_heal</c>）来源的光环实例 id；
        /// 非周期效果为 null。</summary>
        public Id? AuraInstanceId { get; }

        /// <summary>是否为光环周期性触发（区别于技能效果的一次性结算）。</summary>
        public bool IsPeriodic { get; }

        /// <summary>本次结算是否参与暴击判定（见 06 第 4.2 节命中表 <c>crit</c> 行）。</summary>
        public bool CanCrit { get; }

        /// <summary>本次结算是否参与命中判定（部分效果如治疗、周期伤害可豁免未命中判定）。</summary>
        public bool CanMiss { get; }

        /// <summary>
        /// 集成任务补齐的契约缺口：本次结算所属技能的标签集合（来自 <c>skill.def.tags</c>，见
        /// 06 第 3.1 节），供 <see cref="EffectDispatcher"/> 在应用 <c>effect_value</c>/
        /// <c>crit_chance</c> 维度的 SpellMod（见 <see cref="SkillFilter.Matches"/>）时按标签维度
        /// 过滤——原契约的 <see cref="EffectContext"/> 不携带标签，调用点只能传空列表，标签维度的
        /// SpellMod 过滤在效果落地这一步完全不生效（见 <c>core/rules/skill</c> 模块
        /// <c>EffectDispatcher.ApplyDamageOrHeal</c> 改动前的判断记录）。未提供时为空列表（不是
        /// null），周期性光环效果等脱离具体 <c>skill.def</c> 上下文的调用点可以继续不传。
        /// </summary>
        public IReadOnlyList<Id> Tags { get; }

        /// <summary>
        /// RC-01 收边补齐：本次结算所属的触发链深度（0 = 由正常施法管线步骤 8/9 产生的"根"结算，
        /// 未经任何 Proc/<c>trigger_spell</c> 触发；N &gt; 0 = 经 N 层触发链产生）。判断记录：
        /// 06 第 3.4/3.6 节只规定"触发链递归深度超过 <see cref="Core.Rules.Skill.SkillOptions.MaxTriggerDepth"/>
        /// 时拒绝"，未规定深度如何在"效果结算落地事件"（<c>combat.damage_dealt</c>/
        /// <c>combat.heal_done</c> 等）经 <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 异步
        /// 派发、再被 <c>ProcHost</c> 处理触发下一层 <c>trigger_spell</c> 这条路径上传播——原实现只在
        /// <c>CastPipeline.TriggerCast</c> 内部维护一个 ambient 计数器，进入自增/退出自减；但事件总线是
        /// "Enqueue 入队 + 下一个 DispatchPending pass 才派发"（见 <c>core/foundation/event_bus</c>
        /// README"同步派发 + tick 末批处理"），当 Proc 由异步派发的事件触发时，产生该事件的那次
        /// <c>TriggerCast</c> 调用早已返回、ambient 计数器已经归零，深度预算形同虚设（见审计
        /// RC-01：无 ICD 的 <c>heal_done → trigger_skill(heal)</c> 可跨 <c>DispatchPending</c> pass
        /// 无限循环，只靠 <see cref="Core.Foundation.EventBus.EventBusOptions.MaxDispatchPasses"/> 这个
        /// 与技能触发链语义无关的全局熔断兜底）。本字段把"触发链深度"从 ambient 状态改为随
        /// 结算输入/输出显式传播的数据：<c>CastPipeline.ExecuteEffectsOnly</c> 用它构造
        /// <see cref="EffectContext"/>，<c>EffectDispatcher.ApplyDamageOrHeal</c> 把它原样转发进
        /// <c>outbound</c> 上下文，<c>Resolver.Resolve</c> 把它戳到落地事件（<c>CombatDamageDealtEvent</c>/
        /// <c>CombatHealDoneEvent</c>）上；<c>ProcHost.OnEvent</c> 从触发它的事件上读回这个深度（见
        /// <c>EventCorrelation.GetTriggerChainDepth</c>），传给下一次 <c>TriggerCast</c> 调用做真正的
        /// 跨 pass 预算判定。未显式传入（绝大多数不涉及触发链的调用点，如周期性光环效果、纯脚本/
        /// 测试构造）时为 0，语义等价于"根结算"，不改变既有行为。
        /// </summary>
        public int TriggerChainDepth { get; }

        /// <summary>
        /// PR140-04 遗留根治（<c>architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md</c>
        /// "攻击实例 id"）：本次结算所属的施法/攻击实例 id——同一次
        /// <c>Core.Rules.Skill.CastPipeline.ExecuteEffectsOnly</c> 调用（一次技能效果对其解析目标的
        /// 一次性应用，含瞬发、引导读条完成、引导周期跳一次、<c>TriggerCast</c> 触发的嵌套施法）内
        /// 产生的全部效果共享同一个值；不同调用（哪怕是同一施法者的两次独立攻击落在同一未释放的
        /// 命中帧同步窗口内）各自取不同值。落地事件（<c>combat.damage_dealt</c>/<c>combat.heal_done</c>，
        /// 见 <see cref="Events.CombatDamageDealtEvent"/>/<see cref="Events.CombatHealDoneEvent"/>）据此
        /// 原样携带，供 <c>Presentation.FeedbackBinder.Core.HitFrameSyncPolicy</c> 用作命中帧同步的
        /// 批次键（见该类型判断记录"攻击实例 id"），取代此前"同一攻击者当前是否还有未释放批次"这一
        /// 时序代理——同一命中帧同步窗口内，不同攻击即便命中同一批目标也不会再被误合批。
        /// <para>
        /// 未经 <c>CastPipeline</c> 产生的结算（如光环周期效果——<c>AuraHost.FirePeriodic</c> 自行
        /// 构造 <see cref="EffectContext"/>、不经过 <c>CastPipeline</c>，纯脚本/测试直接构造等）为
        /// null——本字段不强制要求全部结算路径都提供；<c>HitFrameSyncPolicy</c>/<c>FeedbackBinder</c>
        /// 在缺失时退回旧的"同一攻击者未释放窗口"合批兜底并记一次诊断，不阻断命中帧同步本身。
        /// </para>
        /// </summary>
        public Id? AttackInstanceId { get; }

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：本次结算所属的地面坐标施法请求落点（见
        /// <see cref="Core.Rules.Common.GroundCastRequest"/>）——经
        /// <c>Core.Rules.Skill.CastPipeline.CastSkillAtGround</c> 产生的结算，把效果落地那一刻实际
        /// 使用的坐标（已按 <see cref="GroundCastSnapshotPolicy"/> 决议、已通过释放时再校验，见该
        /// 管线方法判断记录）原样戳到这里；单位目标 <see cref="Core.Rules.Common.ISkillHost.CastSkill"/>
        /// 路径产生的结算恒为 <c>null</c>。供需要知道"效果是以哪个地面点为原点结算的"的下游消费方
        /// （如按距落点远近做伤害衰减的自定义 <see cref="IEffectExtension"/> 实现）读取，本层
        /// （结算管线固定步骤）本身不解释、不使用本字段。
        /// </summary>
        public Vec2? GroundPoint { get; }

        /// <summary>
        /// T-N1-6（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段）：本次结算所属施法者的来源类别（玩家单位/生物
        /// 单位，由载体类型给出），"目标乘区"步骤据此按 <c>stat.definition.scope</c> 匹配减免属性。
        /// 经既有十五/十六参构造函数（不带本参数，ABI 规则禁止改动其签名）构造的实例恒为
        /// <see cref="SourceKind.Unknown"/>（见 <see cref="SourceKind"/> 类型注释——
        /// 这是"未显式提供"的保守占位，不是契约默认语义本身；生产路径必须改经本类型新增的十七参
        /// 构造函数、显式传入经 <see cref="IUnitAccess.GetSourceKind"/> 查得的真实值）。
        /// </summary>
        public SourceKind SourceKind { get; }

        /// <summary>
        /// T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：本次结算所属目标在
        /// 群体目标超出 <c>max_targets</c> 时分得的"分配系数"（见
        /// <see cref="Core.Rules.Targeting.TargetHost"/>/<c>ITargetHost.ResolveWithCoefficients</c>/
        /// <see cref="TargetResolution"/> 判断记录），<see cref="EffectDispatcher"/> 用它缩放群体
        /// 效果值（<c>value × TargetCoefficient</c>）。经既有十五/十六/十七参构造函数（不带本参数，
        /// ABI 规则禁止改动其签名）构造的实例恒为 1.0——语义等价于"未超出策略参与/单目标"，不改变
        /// 既有行为（同 <see cref="SourceKind"/> 属性判断记录同一条 ABI 兼容惯例）。
        /// </summary>
        public double TargetCoefficient { get; }

        /// <summary>
        /// 深度复审 C-S2（2026-09-16）新增：本次结算对应的效果原语在其所属 <c>skill.aura_def.effects</c>
        /// 数组里的下标——只有经 <c>Core.Rules.Skill.AuraHost.FirePeriodic</c> 产生的周期性光环结算
        /// （<see cref="IsPeriodic"/> 为真且 <see cref="AuraInstanceId"/> 非空）才会显式提供；其余全部
        /// 调用点（含经既有构造函数构造的实例）恒为 <c>null</c>。
        /// <para>
        /// 用途：<c>EffectDispatcher._lastPeriodicEffectValue</c> 冻结缓存键此前只有
        /// <c>(AuraInstanceId, Kind, School)</c> 三元组——如果同一个 <c>skill.aura_def</c> 声明两条
        /// "同类型同学派"的周期效果（如两段 <c>periodic_damage</c> 都标 <c>school.physical</c> 但
        /// <c>base_value</c>/<c>coefficient</c> 不同），来源单位注销之后两条效果会共享同一个缓存
        /// 槛位——后写入的一条覆盖前一条，其中一条的贡献被静默丢弃（见该字段判断记录）。补上本字段
        /// 后，缓存键变为 <c>(AuraInstanceId, Kind, School, EffectEntryIndex)</c>，两条效果各自独立
        /// 冻结，不再互相覆盖。
        /// </para>
        /// </summary>
        public int? EffectEntryIndex { get; }

        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params = null,
            Id? auraInstanceId = null,
            bool isPeriodic = false,
            bool canCrit = true,
            bool canMiss = true,
            IReadOnlyList<Id>? tags = null,
            int triggerChainDepth = 0,
            Id? attackInstanceId = null)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
            TriggerChainDepth = triggerChainDepth;
            AttackInstanceId = attackInstanceId;
            GroundPoint = null;
            // T-N1-6：本构造函数不带 sourceKind 参数（ABI 规则禁止改动既有签名），恒取保守默认值
            // SourceKind.Unknown（见 SourceKind 属性/类型判断记录）。
            SourceKind = SourceKind.Unknown;
            // T-N3-8：本构造函数不带 targetCoefficient 参数（ABI 规则禁止改动既有签名），恒取 1.0
            // （见 TargetCoefficient 属性判断记录）。
            TargetCoefficient = 1.0;
            EffectEntryIndex = null;
        }

        /// <summary>
        /// ADR-0027 新增重载：携带 <see cref="GroundPoint"/>，供 <c>CastPipeline.CastSkillAtGround</c>
        /// 产生的结算使用。判断记录（不是给既有构造函数加可选参数）：既有十五参数构造函数已经有
        /// 八个尾部可选参数，直接追加第十六个可选参数会改变它的物理 IL 签名（参数个数从 15 变 16），
        /// 对已编译好、以"省略部分可选参数"方式调用该构造函数的外部消费方二进制是破坏性变更——同
        /// <c>Core.Rules.Skill.SkillHost</c> 十七/十八参数构造函数判断记录、
        /// <c>Core.Foundation.DataRegistry.FieldSchema</c>"新能力一律用新增成员/新构造重载承载，
        /// 不再改动既有构造函数的参数列表"同一条 ABI 兼容惯例。本重载十六个参数全部不带默认值
        /// （同上述判断记录"若也写成可选参数会与既有构造函数在调用点产生重载二义性"），与既有构造
        /// 函数在参数个数上不重叠（15 对 16），互不冲突。
        /// </summary>
        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params,
            Id? auraInstanceId,
            bool isPeriodic,
            bool canCrit,
            bool canMiss,
            IReadOnlyList<Id>? tags,
            int triggerChainDepth,
            Id? attackInstanceId,
            Vec2? groundPoint)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
            TriggerChainDepth = triggerChainDepth;
            AttackInstanceId = attackInstanceId;
            GroundPoint = groundPoint;
            // T-N1-6：同上一构造函数判断记录，本构造函数（ADR-0027 补充 groundPoint 时新增）同样
            // 不带 sourceKind 参数，恒取保守默认值 SourceKind.Unknown。
            SourceKind = SourceKind.Unknown;
            // T-N3-8：同上一构造函数判断记录，本构造函数同样不带 targetCoefficient 参数，恒取 1.0。
            TargetCoefficient = 1.0;
            EffectEntryIndex = null;
        }

        /// <summary>
        /// T-N1-6 新增重载（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
        /// 决策 5；06 第 4.1 节 2026-09-14 修订段"来源类别进入结算上下文"）：携带 <see cref="SourceKind"/>。
        /// 判断记录（不是给既有构造函数加可选参数，同上一构造函数"ADR-0027 新增重载"判断记录同一条
        /// ABI 兼容惯例）：既有十六参数构造函数（含 <c>groundPoint</c>）已经没有任何可选参数，直接
        /// 追加第十七个参数会改变其物理 IL 签名，对已编译好的外部消费方二进制是破坏性变更；本重载
        /// 十七个参数全部不带默认值（理由同上一构造函数：写成可选参数会与既有构造函数在调用点产生
        /// 重载二义性），与既有两个构造函数在参数个数上不重叠（15/16 对 17），互不冲突。
        /// <para>
        /// 生产路径（<c>CastPipeline.ExecuteEffectsOnly</c>/<c>EffectDispatcher.ApplyDamageOrHeal</c>
        /// 重建 outbound 上下文/<c>AuraHost.FirePeriodic</c>/<c>ProjectileHost.ApplyOnHitEffects</c>）
        /// 一律改经本构造函数，<paramref name="sourceKind"/> 来自 <see cref="IUnitAccess.GetSourceKind"/>
        /// 查询结果（<c>EffectDispatcher.ApplyDamageOrHeal</c> 例外：原样转发入参 <c>context.SourceKind</c>，
        /// 不重新查询，同该方法对 <see cref="TriggerChainDepth"/>/<see cref="AttackInstanceId"/> 的既有
        /// 转发惯例）；测试夹具按需显式指定，其余仍可继续使用既有两个构造函数（得到
        /// <see cref="SourceKind.Unknown"/>）。
        /// </para>
        /// </summary>
        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params,
            Id? auraInstanceId,
            bool isPeriodic,
            bool canCrit,
            bool canMiss,
            IReadOnlyList<Id>? tags,
            int triggerChainDepth,
            Id? attackInstanceId,
            Vec2? groundPoint,
            SourceKind sourceKind)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
            TriggerChainDepth = triggerChainDepth;
            AttackInstanceId = attackInstanceId;
            GroundPoint = groundPoint;
            SourceKind = sourceKind;
            // T-N3-8：本构造函数（T-N1-6 补充 sourceKind 时新增）同样不带 targetCoefficient 参数，
            // 恒取 1.0（见下一构造函数）。
            TargetCoefficient = 1.0;
            EffectEntryIndex = null;
        }

        /// <summary>
        /// T-N3-8 新增重载（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：携带
        /// <see cref="TargetCoefficient"/>。判断记录（不是给既有构造函数加可选参数，同上一构造函数
        /// "T-N1-6 新增重载"判断记录同一条 ABI 兼容惯例）：既有十七参数构造函数（含
        /// <c>sourceKind</c>）已经没有任何可选参数，直接追加第十八个参数会改变其物理 IL 签名，对
        /// 已编译好的外部消费方二进制是破坏性变更；本重载十八个参数全部不带默认值（理由同上一构造
        /// 函数：写成可选参数会与既有构造函数在调用点产生重载二义性），与既有三个构造函数在参数
        /// 个数上不重叠（15/16/17 对 18），互不冲突。
        /// <para>
        /// 生产路径（<c>CastPipeline.ExecuteEffectsOnly</c>）改经本构造函数，<paramref
        /// name="targetCoefficient"/> 来自 <c>ITargetHost.ResolveWithCoefficients</c> 解析结果（未
        /// 走群体目标超出策略的调用点——显式目标、地面坐标施法、<c>TriggerCast</c> 触发链——恒传
        /// 1.0，见 <c>CastPipeline.ExecuteEffectsOnly</c> 判断记录）；<c>EffectDispatcher.ApplyDamageOrHeal</c>
        /// 重建 outbound 上下文时原样转发 <c>context.TargetCoefficient</c>（同该方法对
        /// <see cref="TriggerChainDepth"/>/<see cref="AttackInstanceId"/>/<see cref="SourceKind"/> 的
        /// 既有转发惯例——本字段已经被用于缩放 <c>value</c>，转发只是为了让下游/日志仍能看到"这次
        /// 结算的分配系数是多少"这一信息，不会被重复应用）；测试夹具按需显式指定，其余仍可继续
        /// 使用既有构造函数（得到 1.0，语义上等价于"未超出策略参与"）。
        /// </para>
        /// </summary>
        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params,
            Id? auraInstanceId,
            bool isPeriodic,
            bool canCrit,
            bool canMiss,
            IReadOnlyList<Id>? tags,
            int triggerChainDepth,
            Id? attackInstanceId,
            Vec2? groundPoint,
            SourceKind sourceKind,
            double targetCoefficient)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
            TriggerChainDepth = triggerChainDepth;
            AttackInstanceId = attackInstanceId;
            GroundPoint = groundPoint;
            SourceKind = sourceKind;
            TargetCoefficient = targetCoefficient;
            EffectEntryIndex = null;
        }

        /// <summary>
        /// 深度复审 C-S2 新增重载（2026-09-16）：携带 <see cref="EffectEntryIndex"/>。判断记录（同
        /// 上一构造函数"T-N3-8 新增重载"同一条 ABI 兼容惯例）：既有十八参数构造函数（含
        /// <c>targetCoefficient</c>）已经没有任何可选参数，直接追加第十九个参数会改变其物理 IL
        /// 签名，对已编译好的外部消费方二进制是破坏性变更；本重载十九个参数全部不带默认值（理由
        /// 同上：写成可选参数会与既有构造函数在调用点产生重载二义性），与既有四个构造函数在参数
        /// 个数上不重叠（15/16/17/18 对 19），互不冲突。
        /// <para>
        /// 生产路径只有 <c>Core.Rules.Skill.AuraHost.FirePeriodic</c> 改经本构造函数，<paramref
        /// name="effectEntryIndex"/> 是该效果在 <c>AuraDef.Effects</c> 数组里的下标（见 <see
        /// cref="EffectEntryIndex"/> 判断记录）；其余调用点（<c>CastPipeline.ExecuteEffectsOnly</c>/
        /// <c>ProjectileHost.ApplyOnHitEffects</c>/纯脚本测试构造等）不涉及"同一光环内多条同类型
        /// 同学派周期效果互相覆盖"这一问题场景，继续使用既有构造函数（得到 <c>null</c>，语义上
        /// 等价于"不适用/未提供"）即可，不强制迁移。
        /// </para>
        /// </summary>
        public EffectContext(
            Id sourceId,
            Id targetId,
            Id skillId,
            EffectKind kind,
            Id school,
            double baseValue,
            double coefficient,
            JsonObject? @params,
            Id? auraInstanceId,
            bool isPeriodic,
            bool canCrit,
            bool canMiss,
            IReadOnlyList<Id>? tags,
            int triggerChainDepth,
            Id? attackInstanceId,
            Vec2? groundPoint,
            SourceKind sourceKind,
            double targetCoefficient,
            int? effectEntryIndex)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            Kind = kind;
            School = school;
            BaseValue = baseValue;
            Coefficient = coefficient;
            Params = @params ?? EmptyParams;
            AuraInstanceId = auraInstanceId;
            IsPeriodic = isPeriodic;
            CanCrit = canCrit;
            CanMiss = canMiss;
            Tags = tags ?? EmptyTags;
            TriggerChainDepth = triggerChainDepth;
            AttackInstanceId = attackInstanceId;
            GroundPoint = groundPoint;
            SourceKind = sourceKind;
            TargetCoefficient = targetCoefficient;
            EffectEntryIndex = effectEntryIndex;
        }
    }
}
