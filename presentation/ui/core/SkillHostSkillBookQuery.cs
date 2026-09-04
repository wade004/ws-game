using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// <see cref="ISkillBookQuery"/> 对真实 <c>Core.Rules.Skill.SkillHost</c> 的适配器（见该接口
    /// 判断记录）。生产环境组装代码构造好 <c>SkillHost</c> 后包一层本类型注入
    /// <see cref="PlayerPathProvider"/>/<see cref="SkillBookViewModel"/>；测试可以绕过本类直接写
    /// 内存态 Fake。
    /// </summary>
    public sealed class SkillHostSkillBookQuery : ISkillBookQuery
    {
        private readonly Core.Rules.Skill.SkillHost _skillHost;

        public SkillHostSkillBookQuery(Core.Rules.Skill.SkillHost skillHost)
        {
            _skillHost = skillHost ?? throw new ArgumentNullException(nameof(skillHost));
        }

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) => _skillHost.GetKnownSkills(unitId);

        public double GetCooldown(Id unitId, Id skillId) => _skillHost.GetCooldown(unitId, skillId);
    }
}
