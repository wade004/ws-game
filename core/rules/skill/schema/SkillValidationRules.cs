using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 效果数上限校验（见 04 第 5 节"效果数上限……策略配置项，默认建议值见 06"、
    /// <see cref="SkillOptions.MaxEffectsPerSkill"/>）：<c>skill.def.effects</c> 与
    /// <c>skill.aura_def.effects</c> 两张表的效果列表长度均不得超过上限。
    /// </summary>
    public sealed class MaxEffectsPerSkillRule : IValidationRule
    {
        private readonly int _maxEffects;

        public MaxEffectsPerSkillRule(int maxEffects)
        {
            _maxEffects = maxEffects;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var issue in CheckTable(view, "skill.def"))
            {
                yield return issue;
            }

            foreach (var issue in CheckTable(view, "skill.aura_def"))
            {
                yield return issue;
            }
        }

        private IEnumerable<ValidationIssue> CheckTable(IDataRegistryView view, string table)
        {
            if (!view.Tables.Contains(table))
            {
                yield break;
            }

            foreach (var record in view.GetAll(table))
            {
                if (!record.TryGetArray("effects", out var effects))
                {
                    continue;
                }

                if (effects.Count > _maxEffects)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, table, "max_effects_per_skill",
                        $"效果列表长度 {effects.Count} 超过上限 {_maxEffects}", recordKey: record.Key, field: "effects");
                }
            }
        }
    }

    /// <summary>
    /// 叠加类别冲突校验（见 04 第 5 节"叠加类别冲突：同一 <c>stack_category</c> 静态分组下不得
    /// 同时登记可叠加与不可叠加两种叠加形态的定义"）。判断记录：06 第 3.8 节未给出"互斥的叠加
    /// 语义"的精确定义，本模块取最小可判定解释——同一 <c>stack_category</c> 下，若同时存在
    /// <c>max_stacks > 1</c>（声明"可叠加"）与 <c>max_stacks == 1</c>（声明"不可叠加，仅可刷新"）
    /// 两种记录，视为叠加形态冲突。
    /// <para>
    /// 范围说明（架构 ADR-0023"光环叠加类别为静态校验分组"）：<c>stack_category</c> 与本规则均只
    /// 作用于加载期内容校验，不是运行时叠加槽位维度——<see cref="AuraHost"/> 的运行时槽位键是
    /// <c>(target, aura_def, sourceKey)</c>，不包含 <c>stack_category</c>；本规则通过（同类别下
    /// 叠加形态一致）不代表这些定义在运行时共享同一份叠加计数或互相触发溢出策略，不同 <c>aura_def</c>
    /// 即使类别相同、且都通过本规则，运行时仍各自独立叠加、互不影响，这是既定范围而非本规则的
    /// 检查缺口。
    /// </para>
    /// </summary>
    public sealed class StackCategoryConflictRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.aura_def"))
            {
                yield break;
            }

            var stackableCategories = new Dictionary<string, string>();
            var nonStackableCategories = new Dictionary<string, string>();

            foreach (var record in view.GetAll("skill.aura_def"))
            {
                if (!record.TryGetId("stack_category", out var category))
                {
                    continue;
                }

                var maxStacks = 1L;
                record.TryGetInt("max_stacks", out maxStacks);
                if (maxStacks <= 0)
                {
                    maxStacks = 1;
                }

                if (maxStacks > 1)
                {
                    stackableCategories[category.Value] = record.Key;
                }
                else
                {
                    nonStackableCategories[category.Value] = record.Key;
                }
            }

            foreach (var pair in stackableCategories)
            {
                if (nonStackableCategories.TryGetValue(pair.Key, out var otherKey))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.aura_def", "stack_category_conflict",
                        $"静态叠加校验分组 \"{pair.Key}\" 下同时存在叠加形态互斥的两条定义：max_stacks>1（{pair.Value}）与 max_stacks==1（{otherKey}），加载期内容校验不通过；本项不影响运行时叠加行为，运行时叠加仍按各定义自身的 (target, aura_def, sourceKey) 槽位独立处理（见 ADR-0023）",
                        recordKey: pair.Value, field: "stack_category");
                }
            }
        }
    }

    /// <summary>
    /// <c>cast_time</c>/<c>channel_time</c> 互斥校验（见 06 第 3.1 节
    /// "channel_time……与 cast_time 互斥语义"）：不得同时非零。
    /// </summary>
    public sealed class CastTimeChannelTimeExclusiveRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                var castTime = record.TryGetNumber("cast_time", out var ct) ? ct : 0;
                var channelTime = record.TryGetNumber("channel_time", out var cht) ? cht : 0;

                if (castTime != 0 && channelTime != 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "cast_time_channel_time_exclusive",
                        "cast_time 与 channel_time 不得同时非零", recordKey: record.Key);
                }
            }
        }
    }

    /// <summary>被动技能不得声明 <c>cast_time</c>（见 06 第 3.1 节 <c>kind</c> 字段说明"被动技能……
    /// 不可主动施放"）。</summary>
    public sealed class PassiveSkillNoCastTimeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetString("kind", out var kind) || kind != "passive")
                {
                    continue;
                }

                var castTime = record.TryGetNumber("cast_time", out var ct) ? ct : 0;
                if (castTime != 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "passive_skill_no_cast_time",
                        "kind: passive 的技能不得声明非零 cast_time", recordKey: record.Key, field: "cast_time");
                }
            }
        }
    }

    /// <summary>
    /// ADR-0019 / F1a 判断记录（退役说明）：本文件此前的 <c>EffectKindRegisteredRule</c>
    /// （<c>effect_entry_not_object</c>/<c>effect_kind_missing</c>/<c>effect_kind_not_string</c>/
    /// <c>unknown_effect_kind</c>/<c>unknown_aura_effect_kind</c>）、<c>CostEntryShapeRule</c>
    /// （<c>cost_entry_not_object</c>/<c>cost_power_type_invalid</c>/<c>cost_amount_invalid</c>）
    /// 两条纯结构手写规则已整条删除，<c>ChargesShapeRule</c> 的结构部分
    /// （<c>charges_max_missing</c>/<c>charges_recharge_time_missing</c>/
    /// <c>charges_recharge_time_not_number</c>）同样删除，唯一的业务判断（<c>charges.max</c> 必须
    /// &gt;= 1，FieldSchema 无法表达的数值范围约束）收窄保留为下方 <see cref="ChargesMaxAtLeastOneRule"/>：
    /// <c>SkillSchemas.Def</c>/<c>AuraDef</c> 现把 <c>effects</c> 登记为按 <c>kind</c> 分派的
    /// <see cref="Core.Foundation.DataRegistry.VariantSchema"/>、<c>charges</c>/<c>cost[]</c> 登记为
    /// 带 <c>Fields</c> 的 <see cref="Core.Foundation.DataRegistry.FieldSchema"/>，退役规则要检查的
    /// 结构性坏形状（条目不是对象/缺 kind/kind 非字符串/未登记的 kind 取值、charges 子字段缺失或
    /// 类型错、cost 条目缺失或类型错）全部由 <c>DataRegistry</c> 的递归结构校验以
    /// <c>required_field</c>/<c>field_type</c>/<c>variant_discriminator</c> 三个既有/新增检查名覆盖，
    /// 不再需要平行的手写规则（避免同一缺陷双报，也避免登记与手写规则各自维护一份"合法 kind 集合"
    /// 造成的漂移）。原测试已改写为直接对 <c>SkillSchemas</c> 断言（见
    /// <c>SkillValidationNestedGapTests.cs</c>、<c>SkillSchemaCoverageTests.cs</c>）。
    /// </summary>
    /// <summary>
    /// R06 收边补齐（外部审计 5e779c6，P2；见 <c>Core.Rules.Skill.CooldownTracker.StartCooldown</c>
    /// 判断记录）：<c>skill.def.charges.recharge_time</c> 显式登记为 <c>&lt;= 0</c> 时告警——引擎层
    /// 把 <c>recharge_time &lt;= 0</c> 统一解读为"即时恢复"（充能耗尽的下一刻立即原地补满，见
    /// <c>CooldownTracker</c> 判断记录），不是字面上可能被误读的"永久不恢复"；若数据作者本意确实是
    /// "用一次就永久没了"，<c>charges</c> 不是表达这个意图的正确字段（<c>charges.max</c> 恒定为槽位
    /// 上限，没有"永久耗尽"的语义），需要改用其它机制（如学会即用掉的消耗品/一次性任务奖励）。只是
    /// 提醒复核，不阻断——0 在"即时恢复"这个引擎语义下是合法值，不是数据错误，见
    /// <see cref="ValidationSeverity.Warning"/> 语义（04 第 5 节 Warning 是否阻断取决于
    /// <c>DataRegistryOptions.Strictness</c>）。
    /// </summary>
    /// <summary>
    /// ADR-0019 / F1a 判断记录（<c>ChargesShapeRule</c> 退役后的收窄版，见本文件顶部退役说明）：
    /// <c>charges.max</c> 是充能槽位上限，语义上必须 &gt;= 1（0 或负数没有"最多同时持有 N 次充能"
    /// 的意义）——这条数值范围约束不是 <see cref="Core.Foundation.DataRegistry.FieldSchema.Fields"/>
    /// 能表达的"存在/类型/枚举"结构（登记层只保证 <c>max</c> 是 <see cref="FieldKind.Int"/>，0 与
    /// 负数同样是合法 Int），保留为一条纯粹的业务判断，呼应 04 第 3.2 节"各模块原有的手写结构校验
    /// 规则只保留登记表达不了的业务判断"。<c>max</c> 缺失或类型不对时结构层已经报过
    /// <c>required_field</c>/<c>field_type</c>，本规则跳过、不重复报告。
    /// </summary>
    public sealed class ChargesMaxAtLeastOneRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("charges", out var charges))
                {
                    continue;
                }

                if (!charges.TryGetValue("max", out var maxVal) || !(maxVal is JsonNumber maxNum))
                {
                    continue;
                }

                if (maxNum.Value < 1 || maxNum.Value != System.Math.Floor(maxNum.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_invalid",
                        "charges.max 必须是 >= 1 的整数", recordKey: record.Key, field: "charges.max");
                }
            }
        }
    }

    public sealed class ChargesRechargeTimeZeroWarningRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("charges", out var charges))
                {
                    continue;
                }

                if (!charges.TryGetValue("recharge_time", out var rechargeRaw) || !(rechargeRaw is JsonNumber rechargeNum))
                {
                    continue;
                }

                if (rechargeNum.Value <= 0)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "skill.def", "charges_recharge_time_zero",
                        "charges.recharge_time <= 0 会被引擎解读为\"即时恢复\"（充能不会真正耗尽），" +
                        "若本意是\"永久不再恢复\"请改用其它机制表达，不是把这里填 0",
                        recordKey: record.Key, field: "charges.recharge_time");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // P2-01 ABI/API 兼容 façade（外部审计 audit-c9ff301-20260909）：ADR-0019 / F1a 把本文件顶部
    // "退役说明"里的 EffectKindRegisteredRule/CostEntryShapeRule/ChargesShapeRule 三个 public 类型
    // 整条删除，而 1.13.0 CHANGELOG 宣称该版本源码/二进制兼容——旧源码（或不重编译只替换 DLL 的旧
    // 编译产物）里显式 `registry.RegisterValidationRule(new EffectKindRegisteredRule())` 一类调用会
    // 编译失败/MissingMethodException（见 architecture/落地计划/audit-c9ff301-20260909/docs-project/
    // project-findings.md P2-01）。下面三个类型原样恢复删除前的完整逻辑（不是转发到某个等价新类型
    // ——新实现是 DataRegistry 内建的 required_field/field_type/variant_discriminator 递归结构
    // 校验，不是可供转发调用的具体类型），标 [Obsolete] 提醒新代码改用 SkillSchemas.Def/AuraDef 的
    // 声明式登记；显式注册这三个 façade 仍然可用、行为与 1.12 完全一致，只是会与内建结构校验对同一
    // 处坏形状重复报告（两份 check 名不同，不会互相掩盖，只是冗余），这是选择兼容旧调用方式的已知
    // 代价，不是新缺陷。
    // -----------------------------------------------------------------------

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>EffectKindRegisteredRule</c>——
    /// <c>skill.def.effects[].kind</c>/<c>skill.aura_def.effects[].kind</c> 必须是已登记的
    /// snake_case 名字。见本文件"P2-01 ABI/API 兼容 façade"分节判断记录：ADR-0019/F1a 已用
    /// <c>SkillSchemas.Def</c>/<c>AuraDef</c> 的 <c>VariantSchema</c> 覆盖同一批坏形状，显式注册本
    /// façade 会重复报告（check 名不同：本类用 <c>unknown_effect_kind</c>/<c>unknown_aura_effect_kind</c>
    /// 等，内建用 <c>variant_discriminator</c>），不是新缺陷。
    /// </summary>
    [Obsolete("已被 SkillSchemas.Def/AuraDef 的 VariantSchema 声明式登记覆盖；仅为 1.12 源码/二进制兼容保留，显式注册会与内建结构校验对同一坏形状重复报告。")]
    public sealed class EffectKindRegisteredRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (view.Tables.Contains("skill.def"))
            {
                foreach (var record in view.GetAll("skill.def"))
                {
                    if (!record.TryGetArray("effects", out var effects))
                    {
                        continue;
                    }

                    for (var i = 0; i < effects.Count; i++)
                    {
                        foreach (var issue in CheckEntry(
                            "skill.def", record.Key, i, effects[i],
                            s => EffectKindNames.TryParse(s, out _), "unknown_effect_kind", "EffectKind"))
                        {
                            yield return issue;
                        }
                    }
                }
            }

            if (view.Tables.Contains("skill.aura_def"))
            {
                foreach (var record in view.GetAll("skill.aura_def"))
                {
                    if (!record.TryGetArray("effects", out var effects))
                    {
                        continue;
                    }

                    for (var i = 0; i < effects.Count; i++)
                    {
                        foreach (var issue in CheckEntry(
                            "skill.aura_def", record.Key, i, effects[i],
                            s => AuraEffectKindNames.TryParse(s, out _), "unknown_aura_effect_kind", "AuraEffectKind"))
                        {
                            yield return issue;
                        }
                    }
                }
            }
        }

        private static IEnumerable<ValidationIssue> CheckEntry(
            string table, string recordKey, int index, JsonValue entry,
            Func<string, bool> isKnownKind, string unknownKindCheck, string kindTypeName)
        {
            if (!(entry is JsonObject obj))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_entry_not_object",
                    $"effects[{index}] 不是 JSON 对象（{kindTypeName} 条目必须是 {{kind, params}} 形状）",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!obj.TryGetValue("kind", out var kindVal))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_kind_missing",
                    $"effects[{index}] 缺少必填字段 \"kind\"",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!(kindVal is JsonString kindStr))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, "effect_kind_not_string",
                    $"effects[{index}].kind 必须是字符串",
                    recordKey: recordKey, field: "effects");
                yield break;
            }

            if (!isKnownKind(kindStr.Value))
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, table, unknownKindCheck,
                    $"effects[{index}].kind \"{kindStr.Value}\" 不是已登记的 {kindTypeName}",
                    recordKey: recordKey, field: "effects");
            }
        }
    }

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>ChargesShapeRule</c>——
    /// <c>skill.def.charges</c> 已声明时必须是 <c>{max: Int, recharge_time: Number}</c> 形状。见
    /// <see cref="EffectKindRegisteredRule"/> 同一分节判断记录；本类覆盖的结构性坏形状已被
    /// <c>SkillSchemas.Def</c> 的 <c>Fields</c> 登记覆盖，数值范围判断（<c>max &gt;= 1</c>）另见
    /// <see cref="ChargesMaxAtLeastOneRule"/>（未退役，两者不冲突，可以同时注册）。
    /// </summary>
    [Obsolete("结构性检查已被 SkillSchemas.Def 的 Fields 声明式登记覆盖，数值范围判断见 ChargesMaxAtLeastOneRule；仅为 1.12 源码/二进制兼容保留。")]
    public sealed class ChargesShapeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetObject("charges", out var charges))
                {
                    continue;
                }

                if (!charges.TryGetValue("max", out var maxVal))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_missing",
                        "charges 已声明但缺少必填子字段 \"max\"", recordKey: record.Key, field: "charges.max");
                }
                else if (!(maxVal is JsonNumber maxNum) || maxNum.Value < 1 || maxNum.Value != Math.Floor(maxNum.Value))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_max_invalid",
                        "charges.max 必须是 >= 1 的整数", recordKey: record.Key, field: "charges.max");
                }

                if (!charges.TryGetValue("recharge_time", out var rechargeVal))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_recharge_time_missing",
                        "charges 已声明但缺少必填子字段 \"recharge_time\"", recordKey: record.Key, field: "charges.recharge_time");
                }
                else if (!(rechargeVal is JsonNumber))
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "skill.def", "charges_recharge_time_not_number",
                        "charges.recharge_time 必须是数字", recordKey: record.Key, field: "charges.recharge_time");
                }
            }
        }
    }

    /// <summary>
    /// ABI/API 兼容 façade（P2-01 根治）：1.12 的 <c>CostEntryShapeRule</c>——
    /// <c>skill.def.cost[]</c> 每一项要求 <c>{power_type: Id, amount: Number}</c>。见
    /// <see cref="EffectKindRegisteredRule"/> 同一分节判断记录。
    /// </summary>
    [Obsolete("已被 SkillSchemas.Def 的 cost[] Fields 声明式登记覆盖；仅为 1.12 源码/二进制兼容保留，显式注册会与内建结构校验对同一坏形状重复报告。")]
    public sealed class CostEntryShapeRule : IValidationRule
    {
        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            if (!view.Tables.Contains("skill.def"))
            {
                yield break;
            }

            foreach (var record in view.GetAll("skill.def"))
            {
                if (!record.TryGetArray("cost", out var cost))
                {
                    continue;
                }

                for (var i = 0; i < cost.Count; i++)
                {
                    if (!(cost[i] is JsonObject entry))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_entry_not_object",
                            $"cost[{i}] 不是 JSON 对象（必须是 {{power_type, amount}} 形状）",
                            recordKey: record.Key, field: "cost");
                        continue;
                    }

                    if (!entry.TryGetValue("power_type", out var powerTypeVal) || !(powerTypeVal is JsonString powerTypeStr)
                        || !Id.TryParse(powerTypeStr.Value, out _))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_power_type_invalid",
                            $"cost[{i}].power_type 缺失或不是合法 Id 字符串",
                            recordKey: record.Key, field: "cost");
                    }

                    if (!entry.TryGetValue("amount", out var amountVal) || !(amountVal is JsonNumber))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, "skill.def", "cost_amount_invalid",
                            $"cost[{i}].amount 缺失或不是数字",
                            recordKey: record.Key, field: "cost");
                    }
                }
            }
        }
    }
}
