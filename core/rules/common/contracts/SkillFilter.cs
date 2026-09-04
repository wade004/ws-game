using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// <c>SpellModDef.affects</c> 的类型（见 06 第 3.5 节"affects: SkillFilter // 按学派/标签/具体
    /// 技能id过滤影响范围"）。三个维度各自是一份"清单"，<see cref="Matches"/> 的判断记录见其注释。
    /// </summary>
    public readonly struct SkillFilter
    {
        private readonly IReadOnlyList<Id>? _schools;
        private readonly IReadOnlyList<Id>? _tags;
        private readonly IReadOnlyList<Id>? _skillIds;

        public IReadOnlyList<Id> Schools => _schools ?? Array.Empty<Id>();

        public IReadOnlyList<Id> Tags => _tags ?? Array.Empty<Id>();

        public IReadOnlyList<Id> SkillIds => _skillIds ?? Array.Empty<Id>();

        public SkillFilter(
            IReadOnlyList<Id>? schools = null,
            IReadOnlyList<Id>? tags = null,
            IReadOnlyList<Id>? skillIds = null)
        {
            _schools = schools ?? Array.Empty<Id>();
            _tags = tags ?? Array.Empty<Id>();
            _skillIds = skillIds ?? Array.Empty<Id>();
        }

        /// <summary>三个维度全部为空时（未设置任何过滤条件）匹配一切技能。</summary>
        public static readonly SkillFilter MatchAll = default;

        /// <summary>
        /// 判断规则（06 原文只给出三个维度是什么，没有给出跨维度的布尔组合方式，属任务书"判断记录"
        /// 事项）：三个维度中，任一维度的清单非空即视为该维度参与过滤；<paramref name="skillId"/> 只要
        /// 满足"参与过滤的维度中至少一个匹配"就算整体匹配（维度间取 OR，不是 AND）。理由：
        /// <c>affects</c> 是"影响范围"的并集式声明（例如"影响火系技能，或者影响这个特定技能"这种
        /// 常见口味搭配），若改成 AND 会导致同时填学派与标签时把范围越填越窄、与常见 SpellMod 用法
        /// （用多个维度分别圈定互不相关的技能子集）相悖；三个维度全为空（<see cref="MatchAll"/>）时
        /// 视为"未设置过滤条件"，匹配一切技能，而不是"一个都不匹配"。
        /// </summary>
        public bool Matches(Id skillId, Id school, IReadOnlyList<Id> tags)
        {
            var schools = Schools;
            var filterTags = Tags;
            var skillIds = SkillIds;

            if (schools.Count == 0 && filterTags.Count == 0 && skillIds.Count == 0)
            {
                return true;
            }

            if (skillIds.Count > 0 && Contains(skillIds, skillId))
            {
                return true;
            }

            if (schools.Count > 0 && Contains(schools, school))
            {
                return true;
            }

            if (filterTags.Count > 0 && tags != null)
            {
                for (var i = 0; i < tags.Count; i++)
                {
                    if (Contains(filterTags, tags[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Contains(IReadOnlyList<Id> list, Id value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
