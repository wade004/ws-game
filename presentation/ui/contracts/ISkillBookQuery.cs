using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Skill;

namespace Presentation.Ui
{
    /// <summary>
    /// UI 框架对"已知技能 + 冷却"的只读查询需求（见任务书"core/rules/skill（SkillHost.GetKnownSkills/
    /// 冷却查询）"）。
    /// <para>
    /// 判断记录：<c>Core.Rules.Skill.SkillHost.GetKnownSkills</c> 是该具体类的公开方法，不在
    /// <c>Core.Rules.Common.ISkillHost</c> 契约接口上（见 06 第 7 节 <c>SkillHost</c> 原始签名只有
    /// <c>castSkill</c>/<c>getCooldown</c> 等战斗相关方法，已知技能簿是 <c>core/rules/skill</c> 模块
    /// 自行补充的公开能力，见该模块 <c>SkillHost.cs</c> "已知技能 / 技能书"一节）。本模块不修改
    /// <c>core/rules</c>（任务范围禁止），也不能直接把 <c>ISkillHost</c> 塞进
    /// <see cref="SkillBookViewModel"/>（缺 <c>GetKnownSkills</c>）；于是在本模块内定义这个只读窄
    /// 接口，只声明 UI 侧真正用到的两个方法，由 <c>core/SkillHostSkillBookQuery.cs</c> 适配真实的
    /// <c>Core.Rules.Skill.SkillHost</c>，测试则可以直接写一个内存态 Fake，不必搭建
    /// <c>SkillHost</c> 的完整构造依赖链（<c>IDataRegistryView</c>/<c>IRngHost</c>/<c>ICombatHost</c>
    /// 等一整套 rules 模块内部协作对象）。
    /// </para>
    /// <para>
    /// ADR-0050 补充：上一段"缺 <c>GetKnownSkills</c>"的前提已由 ADR-0050《技能宿主契约纳入技能簿
    /// 查询与学习成员》改变——<c>Core.Rules.Common.ISkillHost</c> 现在确有 <c>GetKnownSkills</c>
    /// 默认接口成员，<c>SkillHostSkillBookQuery</c> 因此新增了一个接受 <see cref="Core.Rules.Common.ISkillHost"/>
    /// 的构造函数重载（见其判断记录）。本窄接口本身仍然保留：即便宿主类型已可用接口引用装配，
    /// UI 侧仍不需要 <c>ISkillHost</c> 的施法/冷却等其余大部分成员，窄接口收敛依赖的价值不变。
    /// </para>
    /// </summary>
    public interface ISkillBookQuery
    {
        /// <summary>该单位当前已知技能 id 列表（见 <c>Core.Rules.Skill.SkillHost.GetKnownSkills</c>，
        /// 按 id 序数排序）。</summary>
        IReadOnlyList<Id> GetKnownSkills(Id unitId);

        /// <summary>该技能距下次可用的剩余时间；就绪返回 0（见
        /// <c>Core.Rules.Common.ISkillHost.GetCooldown</c>）。</summary>
        double GetCooldown(Id unitId, Id skillId);

        /// <summary>
        /// 消费方反馈第 3 条（2026-09-20，ADR-0048）：该技能的名称文本键（<c>skill.def.name_key</c>，
        /// 见 <c>Core.Rules.Skill.SkillHost.GetSkillNameKey</c>）；未声明或技能不存在时为 <c>null</c>，
        /// 消费方（<c>ActionBarViewModel</c>/<c>ActionBarPanel</c>）据此不渲染名称，不回退占位文案。
        /// 判断记录：以默认接口成员新增（同 <c>Core.Gameplay.Quest.IQuestHost.
        /// GetObjectiveRequiredCounts</c> 既有 ABI 兼容惯例）——本接口新增方法不破坏既有实现者
        /// （测试用的内存态 Fake）的二进制兼容性，未覆盖时默认返回 <c>null</c>，等价于"未声明"。
        /// </summary>
        Id? GetNameKey(Id skillId) => null;

        /// <summary>
        /// 消费方反馈第三批第 2 条（2026-09-21，[ADR-0057](../../architecture/adr/0057-动作条槽位补冷却总时长充能与结构化不可用原因.md)）：
        /// 技能冷却/充能/公共冷却/使用条件/节拍锁的统一只读就绪快照，转发
        /// <c>Core.Rules.Common.ISkillHost.GetSkillReadiness</c>（该成员已是 <c>ISkillHost</c> 契约
        /// 既有默认接口成员，本次不改 <c>core/rules</c> 任何文件，只是把已有能力经本窄接口透传给
        /// <c>ActionBarViewModel</c>——同类型注释"本模块不修改 core/rules"的既有边界）。
        /// <para>
        /// C# 8 默认接口成员：未显式覆盖时，按 <see cref="GetCooldown"/> 拼一个降级快照——算法与
        /// <c>ISkillHost.GetSkillReadiness</c> 默认实现逐字一致（只看冷却剩余，无法进一步区分
        /// 技能自身/分类冷却/充能，公共冷却/使用条件/节拍锁均不参与判定，其余明细字段一律
        /// <c>null</c>），供未覆盖本成员的 <see cref="ISkillBookQuery"/> 实现（测试用内存态 Fake、
        /// 旧版本编译产物）源码/二进制兼容。生产实现 <c>SkillHostSkillBookQuery</c> 显式覆盖，
        /// 直接转发 <c>ISkillHost.GetSkillReadiness</c> 的完整结果。
        /// </para>
        /// </summary>
        SkillReadiness GetSkillReadiness(Id unitId, Id skillId)
        {
            var remaining = GetCooldown(unitId, skillId);
            var blocking = remaining > 0 ? SkillReadinessBlockers.SkillCooldown : SkillReadinessBlockers.None;
            return new SkillReadiness(
                skillId: skillId,
                isReady: remaining <= 0,
                blockingSources: blocking,
                skillCooldownRemaining: null,
                categoryCooldownRemaining: null,
                globalCooldownRemaining: null,
                maxCharges: null,
                currentCharges: null,
                nextChargeRemaining: null,
                effectiveCooldownDuration: null);
        }

        /// <summary>
        /// 消费方反馈第 1 条（2026-09-21，ADR-0056）：该单位当前正在读条/引导的技能 id（未在读条/
        /// 引导时为 <c>null</c>），供施法条展示"谁在读条"。判断记录（收口，2026-09-21）：落地当时
        /// 本次改动范围明确排除 <c>ISkillHost.cs</c>（同批另一并行任务同时改动该契约文件的
        /// <c>AuraQuery</c>/<c>EffectSink</c> 成员，避免两个分支在同一份契约文件上撞车），因此曾
        /// 暂时只在本窄接口上追加；两条并行分支合入 main 后已收口——<c>GetCastingSkillId</c>/
        /// <c>GetCastingRemaining</c>/<c>GetCastingTotal</c> 现已一并提升进
        /// <c>Core.Rules.Common.ISkillHost</c> 契约本身（同 <c>GetKnownSkills</c> 在 ADR-0050 的
        /// 先例，见 <c>core/rules/common/README.md</c> 判断记录 15），本窄接口继续保留这三个方法
        /// 只是延续既有"UI 侧不需要 <c>ISkillHost</c> 其余大部分成员"的窄接口收敛惯例，不代表底层
        /// 还需要向下转型。
        /// <para>
        /// C# 8 默认接口成员：本默认实现恒返回 <c>null</c>（等价于"未在读条"），供未实现本能力的
        /// <see cref="ISkillBookQuery"/>（测试用的内存态 Fake）二进制兼容。生产适配器
        /// <c>SkillHostSkillBookQuery</c> 经 <c>ISkillHost.GetCastingSkillId</c> 接口成员转发
        /// （不再向下转型到具体类，见该类型判断记录）。
        /// </para>
        /// </summary>
        Id? GetCastingSkillId(Id unitId) => null;

        /// <summary>消费方反馈第 1 条（2026-09-21，ADR-0056）：该单位当前读条/引导的剩余时间；未在
        /// 读条/引导时为 <c>null</c>，判断记录同 <see cref="GetCastingSkillId"/>。</summary>
        double? GetCastingRemaining(Id unitId) => null;

        /// <summary>消费方反馈第 1 条（2026-09-21，ADR-0056）：该单位当前读条/引导的总时长；未在
        /// 读条/引导时为 <c>null</c>，判断记录同 <see cref="GetCastingSkillId"/>。</summary>
        double? GetCastingTotal(Id unitId) => null;
    }
}
