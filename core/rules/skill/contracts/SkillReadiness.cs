using System;
using Core.Foundation.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 消费方反馈（2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"，见
    /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md）：<see
    /// cref="ISkillHost.GetSkillReadiness"/> 返回的裁决来源标志位——标识某次查询"若此刻
    /// <c>CastSkill</c>，会被冷却/充能/公共冷却中的哪一项（或哪几项）挡下"，不含 06 第 3.6 节步骤
    /// 1/2/5/6/7（存活/控制、学派锁定、资源、目标、距离与视线）等其它拒绝原因——那些原因不属于本查询
    /// 覆盖范围，见 <see cref="ISkillHost.GetSkillReadiness"/> 方法文档。<see cref="None"/> 之外的位可
    /// 按位或组合：例如技能自身冷却与公共冷却可能同时未就绪，本类型如实呈现两者，即便
    /// <c>CastSkill</c> 的九步管线会在步骤 3（冷却）先行短路、永远不会真正跑到步骤 4（公共冷却）
    /// 判定。
    /// </summary>
    [Flags]
    public enum SkillReadinessBlockers
    {
        /// <summary>未被冷却/充能/公共冷却中的任何一项阻塞。</summary>
        None = 0,

        /// <summary>技能自身冷却剩余 &gt; 0（<see cref="CooldownTracker.GetSkillCooldownRemaining"/>），
        /// 仅对未配置 <c>charges</c> 的技能有意义——有充能配置的技能改用 <see cref="NoCharges"/>，
        /// 见 <see cref="CooldownTracker.IsSkillReady"/> 判断记录"HasCharges 时不检查技能自身/分类
        /// 冷却，只检查充能数"。</summary>
        SkillCooldown = 1 << 0,

        /// <summary>技能所属冷却分类剩余 &gt; 0（<see cref="CooldownTracker.GetCategoryCooldownRemaining"/>），
        /// 仅对未配置 <c>charges</c> 且声明了 <c>cooldown_category</c> 的技能有意义——理由同
        /// <see cref="SkillCooldown"/>：有充能配置的技能在这台引擎里从不检查分类冷却（<see
        /// cref="CooldownTracker.StartCooldown"/> 的充能分支也从不写入分类冷却账本）。</summary>
        CategoryCooldown = 1 << 1,

        /// <summary>公共冷却（GCD）剩余 &gt; 0，且当前满足"公共冷却会被检查"的全部前提（<c>
        /// SkillOptions.GcdEnabled</c> 开启、<c>skill.def.respects_gcd</c> 为真、当前不处于离散步——
        /// 三者同 <see cref="CastPipeline"/> 步骤 4 判定条件）。</summary>
        GlobalCooldown = 1 << 2,

        /// <summary>技能配置了 <c>charges</c> 且当前充能数为 0。</summary>
        NoCharges = 1 << 3,
    }

    /// <summary>
    /// 消费方反馈（同上）：技能所属冷却分类与其剩余时间的只读快照——<see
    /// cref="SkillReadiness.CategoryCooldownRemaining"/> 用本类型而不是裸 <c>double</c>，让"这个剩余
    /// 时间属于哪个分类"随查询结果一并携带，调用方不需要另外记住/回查 <c>skill.def.cooldown_category</c>。
    /// 不可变值类型。
    /// </summary>
    public readonly struct CategoryCooldownStatus
    {
        /// <summary>冷却分类 id（<c>skill.def.cooldown_category</c> 引用，见 06 第 3.1/3.5 节）。</summary>
        public Id CategoryId { get; }

        /// <summary>该分类当前剩余冷却时间，恒 &gt;= 0；单位与 <see cref="ISkillHost.GetCooldown"/>
        /// 一致（按当前生效时间模型系数折算后的计时单位，见 <see cref="CooldownTracker"/> 类型
        /// 判断记录）。</summary>
        public double Remaining { get; }

        public CategoryCooldownStatus(Id categoryId, double remaining)
        {
            CategoryId = categoryId;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// 消费方反馈（2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"，见
    /// architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md）：<see
    /// cref="ISkillHost.GetSkillReadiness"/> 返回的不可变只读快照——把此前只能通过 <see
    /// cref="ISkillHost.GetCooldown"/>（不能区分技能/分类/公共冷却、不能呈现当前充能数与下次恢复
    /// 剩余）间接、不完整地推断的状态，一次性完整呈现。
    /// <para>
    /// 覆盖范围与 <see cref="ISkillHost.CastSkill"/> 步骤 3（冷却/充能）、步骤 4（公共冷却）的裁决
    /// 口径一致（<see cref="IsReady"/>/<see cref="BlockingSources"/> 与随后一次 <c>CastSkill</c> 在同
    /// 一状态下的裁决结论对应，见该接口方法判断记录），<b>不包含</b> 06 第 3.6 节步骤 1/2/5/6/7（存活
    /// /控制、学派锁定、资源是否够、目标合法性、距离与视线）等其它拒绝原因——<see cref="IsReady"/> 为
    /// <c>true</c> 只表示"不会被冷却/充能/公共冷却挡下"，不代表此刻 <c>CastSkill</c> 一定成功（可能仍
    /// 因资源不足/无合法目标等原因失败）；<see cref="ISkillHost.CastSkill"/> 的返回值才是唯一的最终
    /// 裁决。查询本身只读取 host 现有账本，不推进时间、不创建第二套计时器、不修改任何状态。
    /// </para>
    /// <para>
    /// 时间单位：<see cref="SkillCooldownRemaining"/>/<see cref="CategoryCooldownStatus.Remaining"/>/
    /// <see cref="GlobalCooldownRemaining"/>/<see cref="NextChargeRemaining"/>/<see
    /// cref="EffectiveCooldownDuration"/> 均与 <see cref="ISkillHost.GetCooldown"/> 同一口径——按
    /// 当前生效时间模型（连续模式为秒、离散模式为回合，见 <see cref="CooldownTracker"/> 类型判断
    /// 记录 <c>_currentFactor</c>）折算后的计时单位，不是数据 authoring 时的原始规范单位。
    /// </para>
    /// <para>
    /// 可空字段的含义：<see cref="ISkillHost.GetSkillReadiness"/> 的默认接口实现（未被具体宿主显式
    /// 覆盖时的降级兜底）只能从 <see cref="ISkillHost.GetCooldown"/> 粗略推断 <see cref="IsReady"/>/
    /// <see cref="BlockingSources"/>，其余字段一律为 <c>null</c>，表示"未知"（不是"不存在"）；真正
    /// 实现（<c>Core.Rules.Skill.SkillHost</c>）则按技能是否配置 <c>charges</c> 决定 <c>null</c> 的
    /// 另一层含义——未配置 <c>charges</c> 的技能，<see cref="MaxCharges"/>/<see
    /// cref="CurrentCharges"/>/<see cref="NextChargeRemaining"/> 为 <c>null</c>（没有充能机制可言，
    /// 不是"充能数未知"）；未声明 <c>cooldown_category</c> 的技能，<see
    /// cref="CategoryCooldownRemaining"/> 为 <c>null</c>（没有所属分类）。
    /// </para>
    /// </summary>
    public sealed class SkillReadiness
    {
        /// <summary>本次查询的技能 id（原样回显调用参数，不做 <c>override_skill</c> 重定向——见
        /// <see cref="ISkillHost.GetSkillReadiness"/> 判断记录"与 <c>GetCooldown</c> 同一惯例"）。</summary>
        public Id SkillId { get; }

        /// <summary>是否不受冷却/充能/公共冷却阻塞（等价于 <see cref="BlockingSources"/> ==
        /// <see cref="SkillReadinessBlockers.None"/>）。不代表此刻 <c>CastSkill</c> 一定成功，见类型
        /// 文档"覆盖范围"一节。</summary>
        public bool IsReady { get; }

        /// <summary>此刻正在阻塞该技能的全部来源（可按位或组合）；<see cref="IsReady"/> 为 <c>true</c>
        /// 时恒为 <see cref="SkillReadinessBlockers.None"/>。</summary>
        public SkillReadinessBlockers BlockingSources { get; }

        /// <summary>技能自身冷却剩余；未配置 <c>charges</c> 时才有意义，见 <see
        /// cref="SkillReadinessBlockers.SkillCooldown"/> 判断记录。默认接口实现降级为 <c>null</c>
        /// （未知）。</summary>
        public double? SkillCooldownRemaining { get; }

        /// <summary>所属冷却分类与其剩余；技能未声明 <c>cooldown_category</c>，或技能配置了
        /// <c>charges</c>（该引擎充能技能从不检查分类冷却，见 <see
        /// cref="SkillReadinessBlockers.CategoryCooldown"/> 判断记录）时为 <c>null</c>。</summary>
        public CategoryCooldownStatus? CategoryCooldownRemaining { get; }

        /// <summary>公共冷却（GCD）剩余；公共冷却当前不适用（<c>SkillOptions.GcdEnabled</c> 关闭、
        /// <c>respects_gcd</c> 为假、或处于离散步）时恒为 <c>0</c>（"不适用"与"适用但已就绪"对
        /// <see cref="IsReady"/> 的效果相同，均不阻塞，见 <see cref="ISkillHost.GetSkillReadiness"/>
        /// 实现判断记录）。默认接口实现降级为 <c>null</c>（未知）。</summary>
        public double? GlobalCooldownRemaining { get; }

        /// <summary>修饰后（<c>charges</c> 维度 SpellMod 修正后）的有效充能上限；技能未配置
        /// <c>charges</c> 时为 <c>null</c>。</summary>
        public int? MaxCharges { get; }

        /// <summary>当前充能数；技能未配置 <c>charges</c> 时为 <c>null</c>。</summary>
        public int? CurrentCharges { get; }

        /// <summary>距下一次充能恢复完成的剩余时间；满充能（含从未消耗过）时为 <c>0</c>，技能未配置
        /// <c>charges</c> 时为 <c>null</c>（无充能机制可言）。</summary>
        public double? NextChargeRemaining { get; }

        /// <summary>修饰后的完整冷却/恢复周期：未配置 <c>charges</c> 的技能是 <c>cooldown_duration</c>
        /// 经 <c>cooldown</c> 维度 SpellMod 修正后的完整冷却时长；配置了 <c>charges</c> 的技能是单次
        /// 充能恢复时长（<c>charges.recharge_time</c> 经 <c>charges</c> 维度 SpellMod 修正后的值）——
        /// 两者是各自技能类型下"一个完整周期"的对应量。默认接口实现降级为 <c>null</c>（未知）。</summary>
        public double? EffectiveCooldownDuration { get; }

        public SkillReadiness(
            Id skillId,
            bool isReady,
            SkillReadinessBlockers blockingSources,
            double? skillCooldownRemaining,
            CategoryCooldownStatus? categoryCooldownRemaining,
            double? globalCooldownRemaining,
            int? maxCharges,
            int? currentCharges,
            double? nextChargeRemaining,
            double? effectiveCooldownDuration)
        {
            SkillId = skillId;
            IsReady = isReady;
            BlockingSources = blockingSources;
            SkillCooldownRemaining = skillCooldownRemaining;
            CategoryCooldownRemaining = categoryCooldownRemaining;
            GlobalCooldownRemaining = globalCooldownRemaining;
            MaxCharges = maxCharges;
            CurrentCharges = currentCharges;
            NextChargeRemaining = nextChargeRemaining;
            EffectiveCooldownDuration = effectiveCooldownDuration;
        }
    }
}
