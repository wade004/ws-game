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
    }
}
