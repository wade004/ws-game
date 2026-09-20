using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// <see cref="ISkillBookQuery"/> 对真实技能宿主的适配器（见该接口判断记录）。生产环境组装代码
    /// 构造好技能宿主后包一层本类型注入 <see cref="PlayerPathProvider"/>/<see cref="SkillBookViewModel"/>；
    /// 测试可以绕过本类直接写内存态 Fake。
    /// <para>
    /// ADR-0050《技能宿主契约纳入技能簿查询与学习成员》：<c>GetKnownSkills</c> 提升为
    /// <see cref="ISkillHost"/> 契约成员之前，本类型只能接受具体类 <c>Core.Rules.Skill.SkillHost</c>
    /// （见既有 <see cref="SkillHostSkillBookQuery(Core.Rules.Skill.SkillHost)"/> 构造函数，ABI
    /// 规则禁止改动既有公开签名，予以保留）；现新增一个接受 <see cref="ISkillHost"/> 的构造函数
    /// 重载——调用方今后可以只持有接口引用装配本适配器，不必再依赖具体类型。两个构造函数功能等价，
    /// 新代码建议优先使用接口版本。
    /// </para>
    /// </summary>
    public sealed class SkillHostSkillBookQuery : ISkillBookQuery
    {
        private readonly ISkillHost _skillHost;

        /// <summary>ABI 兼容重载：保留具体类型参数，转发到接口版本主构造函数（见类型判断记录）。</summary>
        public SkillHostSkillBookQuery(Core.Rules.Skill.SkillHost skillHost)
            : this((ISkillHost)skillHost)
        {
        }

        /// <summary>ADR-0050 新增：面向接口装配，不要求调用方持有具体类 <c>SkillHost</c>。</summary>
        public SkillHostSkillBookQuery(ISkillHost skillHost)
        {
            _skillHost = skillHost ?? throw new ArgumentNullException(nameof(skillHost));
        }

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) => _skillHost.GetKnownSkills(unitId);

        public double GetCooldown(Id unitId, Id skillId) => _skillHost.GetCooldown(unitId, skillId);

        /// <summary>消费方反馈第 3 条（2026-09-20，ADR-0048）：转发 <see cref="ISkillHost.GetSkillNameKey"/>——
        /// 该成员随 ADR-0050 合流口径一并提升进 <see cref="ISkillHost"/> 契约（见
        /// <c>core/rules/common/README.md</c> 判断记录 12），本类型据此可以只持有接口引用调用，
        /// 不要求调用方传入具体类 <c>Core.Rules.Skill.SkillHost</c>。</summary>
        public Id? GetNameKey(Id skillId) => _skillHost.GetSkillNameKey(skillId);

        /// <summary>
        /// 消费方反馈第 1 条（2026-09-21，ADR-0056）：<c>GetCastingSkillId</c>/<c>GetCastingRemaining</c>/
        /// <c>GetCastingTotal</c> 目前只是 <c>Core.Rules.Skill.SkillHost</c> 的公开方法，不在
        /// <c>Core.Rules.Common.ISkillHost</c> 契约上（见 <see cref="ISkillBookQuery.GetCastingSkillId"/>
        /// 判断记录——本次改动范围明确排除 <c>ISkillHost.cs</c>，避免与同批并行任务撞车）；本字段
        /// 因此持有具体类型的向下转型，只有 <see cref="_skillHost"/> 底层确实是
        /// <c>Core.Rules.Skill.SkillHost</c>（生产环境唯一实现，见 <c>PresentationAssembly</c> 装配
        /// 代码）时才能取到非降级结果，其它 <see cref="ISkillHost"/> 实现（测试假实现等）走接口默认
        /// 成员降级为 <c>null</c>——同 ADR-0050 之前 <c>GetKnownSkills</c> 的同一处境，是已知的临时
        /// 窄化，留给后续把这三个成员提升进 <see cref="ISkillHost"/> 时一并根治（届时可删除本向下
        /// 转型，直接转发接口成员）。
        /// </summary>
        private Core.Rules.Skill.SkillHost? CastingHost => _skillHost as Core.Rules.Skill.SkillHost;

        public Id? GetCastingSkillId(Id unitId) => CastingHost?.GetCastingSkillId(unitId);

        public double? GetCastingRemaining(Id unitId) => CastingHost?.GetCastingRemaining(unitId);

        public double? GetCastingTotal(Id unitId) => CastingHost?.GetCastingTotal(unitId);
    }
}
