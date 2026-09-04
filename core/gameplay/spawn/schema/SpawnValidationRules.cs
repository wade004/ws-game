using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// "<c>respawn_policy = timer</c> 时 <c>respawn_timer</c> 必填"检查项（见 05 第 5.1 节
    /// <c>respawn_timer</c> 行"视策略而定"、任务书拍板"策略/timer 一致"）。<c>respawn_policy</c>
    /// 缺失/取值非法属于 <c>required_field</c>/<c>field_type</c> 职责，本规则只处理合法时的一致性。
    /// </summary>
    public sealed class SpawnRespawnPolicyFieldGroupRule : IValidationRule
    {
        private const string CheckName = "spawn_respawn_policy_field_group";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(SpawnSchemas.Table.Name))
            {
                if (!record.TryGetString("respawn_policy", out var policyText)
                    || !RespawnPolicyNames.TryParse(policyText, out var policy))
                {
                    continue;
                }

                if (policy != RespawnPolicy.Timer)
                {
                    continue;
                }

                if (!record.TryGetNumber("respawn_timer", out var timer) || timer <= 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, SpawnSchemas.Table.Name, CheckName,
                        "respawn_policy=\"timer\" 要求 respawn_timer 必填且为正数",
                        recordKey: record.Key, field: "respawn_timer");
                }
            }
        }
    }

    /// <summary>
    /// "<c>content_ref</c> 域名合法且目标行存在"检查项（见 05 第 5.1 节 <c>content_ref</c> 行
    /// "指向 creature.template 或 gobj.template"、任务书拍板"content_ref 域名合法"）。判断记录：
    /// 目标表未加载/未注册时跳过存在性检查（不误报——见
    /// <c>Core.Gameplay.AreaTrigger.AreaTriggerSchemas.map_id</c> 同款判断记录，测试/内容管线未必
    /// 同时加载 creature.template/gobj.template 两张表）。
    /// </summary>
    public sealed class SpawnContentRefRule : IValidationRule
    {
        private const string CheckName = "spawn_content_ref";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(SpawnSchemas.Table.Name))
            {
                if (!record.TryGetId("content_ref", out var contentRef))
                {
                    // 缺失/格式不符属于 required_field/field_type 职责。
                    continue;
                }

                var domain = contentRef.Domain;
                if (domain != "creature" && domain != "gobj")
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, SpawnSchemas.Table.Name, CheckName,
                        $"content_ref \"{contentRef}\" 域名非法（只允许 creature.*|gobj.*）",
                        recordKey: record.Key, field: "content_ref");
                    continue;
                }

                var targetTable = domain == "creature" ? "creature.template" : "gobj.template";
                var tableLoaded = false;
                foreach (var t in view.Tables)
                {
                    if (t == targetTable)
                    {
                        tableLoaded = true;
                        break;
                    }
                }

                if (tableLoaded && view.Get(targetTable, contentRef) == null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, SpawnSchemas.Table.Name, CheckName,
                        $"content_ref \"{contentRef}\" 在表 \"{targetTable}\" 中不存在",
                        recordKey: record.Key, field: "content_ref");
                }
            }
        }
    }

    /// <summary>
    /// "<c>summon_only</c> 生物不得出现在 <c>spawn.table</c>"检查项（见 07 第 2.2 节
    /// <c>npc_flag.summon_only</c>、任务书拍板"用 ICreatureTemplateQuery.HasFlag——规则需要模板
    /// 查询，作为 IValidationRule 的构造参数注入；无查询时跳过并记警告"）。
    /// </summary>
    public sealed class SpawnSummonOnlyCreatureRule : IValidationRule
    {
        private const string CheckName = "spawn_summon_only_creature";

        private readonly ICreatureTemplateQuery? _templateQuery;

        public SpawnSummonOnlyCreatureRule(ICreatureTemplateQuery? templateQuery)
        {
            _templateQuery = templateQuery;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (_templateQuery == null)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Warning, SpawnSchemas.Table.Name, CheckName,
                    "未注入 ICreatureTemplateQuery，跳过 summon_only 生物检查");
                yield break;
            }

            foreach (var record in view.GetAll(SpawnSchemas.Table.Name))
            {
                if (!record.TryGetId("content_ref", out var contentRef) || contentRef.Domain != "creature")
                {
                    continue;
                }

                bool hasFlag;
                try
                {
                    hasFlag = _templateQuery.HasFlag(contentRef, NpcFlag.SummonOnly);
                }
                catch (System.ArgumentException)
                {
                    // 模板未登记：属 reference_integrity/SpawnContentRefRule 职责，本规则不重复报错。
                    continue;
                }

                if (hasFlag)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, SpawnSchemas.Table.Name, CheckName,
                        $"content_ref \"{contentRef}\" 是 summon_only 生物，不得出现在 spawn.table",
                        recordKey: record.Key, field: "content_ref");
                }
            }
        }
    }
}
