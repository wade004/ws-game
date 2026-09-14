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

        /// <summary>
        /// T-N1-8（ADR-0030 决策 6；06 第 4.2 节修订段）：加载 <c>combat.level_diff_table</c>，与
        /// <see cref="LoadHitTables"/>/<see cref="LoadResistCurvesBySchool"/> 不同——本表是可选表
        /// （<see cref="CombatOptions.LevelDiffTableId"/> 缺省 <c>null</c> 即不接入），表未加载（未
        /// 注册 schema、或已注册但数据源里没有对应文件，见 <see cref="HasTable"/> 判断记录）时返回
        /// 空字典，不抛异常——单机/测试夹具/T-N1-8 之前的既有装配（不装配该表）都应继续正常工作。
        /// </summary>
        public static IReadOnlyDictionary<Id, LevelDiffTable> LoadLevelDiffTables(IDataRegistryView registry)
        {
            var result = new Dictionary<Id, LevelDiffTable>();
            if (!HasTable(registry, "combat.level_diff_table"))
            {
                return result;
            }

            foreach (var record in registry.GetAll("combat.level_diff_table"))
            {
                var table = new LevelDiffTable(record);
                result[table.Id] = table;
            }

            return result;
        }

        private static void RequireTable(IDataRegistryView registry, string table)
        {
            if (HasTable(registry, table))
            {
                return;
            }

            throw new InvalidOperationException(
                $"CombatHost 需要 \"{table}\" 表（见 06 第 4.7 节数据表），但数据注册表中未加载该表");
        }

        /// <summary>
        /// 判断记录：<see cref="IDataRegistryView.Tables"/> 只反映"实际从数据源加载了记录的表"，与
        /// "是否曾经 <c>RegisterSchema</c>"是两回事（<c>DataRegistry._tables</c> 由
        /// <c>LoadAllCore</c> 按数据源里实际存在的表文件填充，注册了 schema 但数据源没有对应文件的
        /// 表不会出现在这里）——这正是 <see cref="LoadLevelDiffTables"/> 需要的语义："该表本次是否
        /// 真的接了数据"，而不是"代码里是否声明过这张表的形状"。
        /// </summary>
        private static bool HasTable(IDataRegistryView registry, string table)
        {
            var tables = registry.Tables;
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] == table)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
