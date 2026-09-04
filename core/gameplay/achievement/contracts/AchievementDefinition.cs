using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Common;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// 一条 <c>achv.def</c> 记录的强类型视图（见 08 第 6.1 节字段表 + 任务书拍板补录
    /// <c>name_key</c>）。从 <see cref="DataRecord"/> 构造，非法数据在构造期即抛
    /// <see cref="DataFieldException"/>/<see cref="FormatException"/>（惯例同本仓库其它
    /// <c>FromRecord</c> 静态工厂）。<c>rewards</c> 复用 <see cref="RewardBundle"/>（见 08 第 6.1
    /// 节"同 quest 奖励结构"）。
    /// </summary>
    public sealed class AchievementDefinition
    {
        public Id Id { get; }

        public Id NameKey { get; }

        public IReadOnlyList<AchievementCriterion> Criteria { get; }

        public RewardBundle Rewards { get; }

        private AchievementDefinition(Id id, Id nameKey, IReadOnlyList<AchievementCriterion> criteria, RewardBundle rewards)
        {
            Id = id;
            NameKey = nameKey;
            Criteria = criteria;
            Rewards = rewards;
        }

        public static AchievementDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");

            var criteriaArray = record.GetArray("criteria");
            if (criteriaArray.Count == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "criteria", "至少需要一条达成条件");
            }

            var criteria = new List<AchievementCriterion>(criteriaArray.Count);
            for (var i = 0; i < criteriaArray.Count; i++)
            {
                if (!(criteriaArray[i] is JsonObject criterionJson))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, $"criteria[{i}]", "必须是对象");
                }
                criteria.Add(AchievementCriterion.FromRecord(record, criterionJson, i));
            }

            var rewards = record.TryGetObject("rewards", out var rewardsObj) ? RewardBundle.FromRecord(rewardsObj) : RewardBundle.Empty;

            return new AchievementDefinition(id, nameKey, criteria, rewards);
        }
    }
}
