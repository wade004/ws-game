using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>
    /// 把 <c>combat.hit_table_config</c>/<c>combat.resist_curve</c> 从
    /// <see cref="IDataRegistryView"/> 读成强类型字典，供 <see cref="CombatHost"/> 构造期调用。
    /// 两张表任一不存在都直接抛异常（惯例同 <c>StatHost</c> 对 <c>stat.definition</c> 的处理：
    /// 本模块的核心行为离不开这两张表）。
    /// </summary>
    internal static class CombatDataLoader
    {
        public static IReadOnlyDictionary<Id, HitTableConfig> LoadHitTables(IDataRegistryView registry)
        {
            RequireTable(registry, "combat.hit_table_config");

            var result = new Dictionary<Id, HitTableConfig>();
            foreach (var record in registry.GetAll("combat.hit_table_config"))
            {
                var config = new HitTableConfig(record);
                result[config.Id] = config;
            }

            return result;
        }

        public static IReadOnlyDictionary<Id, ResistCurve> LoadResistCurvesBySchool(IDataRegistryView registry)
        {
            RequireTable(registry, "combat.resist_curve");

            var result = new Dictionary<Id, ResistCurve>();
            foreach (var record in registry.GetAll("combat.resist_curve"))
            {
                var curve = new ResistCurve(record);
                // 判断记录：按 school 建索引而非按记录 id——Resolver 的减免步骤（06 第 4.3 节）
                // 是"该学派用哪条曲线"，一个学派同一时间只应有一条生效曲线；若数据里同一 school
                // 出现多条记录，后加载的覆盖先加载的（不阻断构造，具体去重策略由内容校验流程负责）。
                result[curve.School] = curve;
            }

            return result;
        }

        private static void RequireTable(IDataRegistryView registry, string table)
        {
            var tables = registry.Tables;
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] == table)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"CombatHost 需要 \"{table}\" 表（见 06 第 4.7 节数据表），但数据注册表中未加载该表");
        }
    }
}
