using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 预算超标校验（见 04 第 5 节检查项清单"预算超标"、07 第 1.2 节预算公式契约）：一件
    /// <c>item.template</c> 的 <c>Σ|stats.value|</c>（<c>pct</c>/<c>mult</c> ×100 折算，见 <see
    /// cref="ItemBudgetCurve.SumConsumed"/>）不得超过 <c>item.budget_curve(item_level) ×
    /// quality.budget_multiplier</c>。曲线 id 由构造参数 <paramref name="budgetCurveId"/> 指定（同
    /// <c>MaxEffectsPerSkillRule(int)</c> 惯例：策略配置项经构造参数传入，不在规则内部读取任何
    /// 全局配置）。调用方需要 <c>registry.RegisterValidationRule(new
    /// ItemBudgetValidationRule(options.BudgetCurveId))</c> 才会生效。
    /// </summary>
    public sealed class ItemBudgetValidationRule : IValidationRule
    {
        public const string Check = "item_budget_exceeded";

        private readonly Id _budgetCurveId;

        public ItemBudgetValidationRule(Id budgetCurveId)
        {
            _budgetCurveId = budgetCurveId;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var templates = view.GetAll("item.template");

            // 加固J3 容错（见 data/README.md 曾记录的已知现象）：数据集完全没有 item 域
            // （item.template 一行都没有——如 data/_framework 单独校验，该数据根只登记
            // found.event_catalog/found.input_action 两张框架级纯登记表）时，本规则设计上假定
            // "跑校验的是一份完整游戏/示例数据"这一前提不成立，不应该因为"预算曲线也不存在"而报错——
            // 没有物品就没有什么预算可超标。一旦数据集确实登记了至少一条 item.template（哪怕只有一条），
            // 就视为"有 item 域"，下面 curveRecord == null 的分支仍然按原样报错（"有 item 表但缺
            // item.budget.default"不属于可容错的场景，说明该数据集本该配一条默认预算曲线却没配）。
            if (templates.Count == 0)
            {
                yield break;
            }

            var curveRecord = view.Get("item.budget_curve", _budgetCurveId);
            if (curveRecord == null)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, "item.template", Check,
                    $"预算曲线 \"{_budgetCurveId}\" 不存在，无法校验预算超标");
                yield break;
            }

            // T-N0-4：解析一次为 PiecewiseCurve，逐模板插值不再重建曲线。
            var curve = ItemBudgetCurve.ParseCurve(curveRecord);

            var qualityMultipliers = new Dictionary<string, double>();
            foreach (var q in view.GetAll("item.quality_definition"))
            {
                qualityMultipliers[q.Key] = q.TryGetNumber("budget_multiplier", out var m) ? m : 1.0;
            }

            foreach (var record in templates)
            {
                if (!record.TryGetArray("stats", out var stats) || stats.Count == 0)
                {
                    continue;
                }

                if (!record.TryGetInt("item_level", out var itemLevel) || !record.TryGetString("quality", out var quality))
                {
                    continue;
                }

                var multiplier = qualityMultipliers.TryGetValue(quality, out var m2) ? m2 : 1.0;
                var budget = ItemBudgetCurve.Interpolate(curve, (int)itemLevel) * multiplier;
                var consumed = ItemBudgetCurve.SumConsumed(stats);

                if (consumed > budget)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", Check,
                        $"stats 消耗预算 {consumed:0.###} 超过上限 {budget:0.###}" +
                        $"（item_level={itemLevel}, quality={quality}）",
                        recordKey: record.Key, field: "stats");
                }
            }
        }
    }

    /// <summary>
    /// 武器槽必须携带 <c>weapon_profile</c>、非武器槽不得携带（见 07 第 1.1 节"weapon_profile……
    /// 视 slot 而定……视 slot_definition.is_weapon"、任务书"武器槽缺 weapon_profile"反例）。
    /// </summary>
    public sealed class ItemWeaponProfileRule : IValidationRule
    {
        public const string CheckMissing = "item_weapon_profile_missing";
        public const string CheckUnexpected = "item_weapon_profile_unexpected";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var weaponSlots = new HashSet<string>();
            foreach (var slotDef in view.GetAll("item.slot_definition"))
            {
                if (slotDef.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlots.Add(slotDef.Key);
                }
            }

            foreach (var record in view.GetAll("item.template"))
            {
                if (!record.TryGetString("slot", out var slot))
                {
                    continue;
                }

                var isWeaponSlot = weaponSlots.Contains(slot);
                var hasProfile = record.Has("weapon_profile");

                if (isWeaponSlot && !hasProfile)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", CheckMissing,
                        $"槽位 \"{slot}\" 是武器槽（is_weapon=true）但缺少 weapon_profile",
                        recordKey: record.Key, field: "weapon_profile");
                }
                else if (!isWeaponSlot && hasProfile)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", CheckUnexpected,
                        $"槽位 \"{slot}\" 不是武器槽（is_weapon 非 true）但携带了 weapon_profile",
                        recordKey: record.Key, field: "weapon_profile");
                }
            }
        }
    }

    /// <summary>
    /// <c>item.set.pieces</c> 与 <c>item.template.set_id</c> 双向一致性（见 07 第 1.5 节 <c>ItemSet</c>
    /// 对照"保留件数门槛→额外效果的结构"、任务书"套装 set_id 的 pieces 包含该物品"）。
    /// </summary>
    public sealed class ItemSetMembershipRule : IValidationRule
    {
        public const string Check = "item_set_membership_mismatch";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("item.template"))
            {
                if (!record.TryGetString("set_id", out var setId))
                {
                    continue;
                }

                var setRecord = view.Get("item.set", setId);
                if (setRecord == null)
                {
                    // 引用完整性已由 item.template.set_id 的 FieldKind.Reference 检查报告，这里不重复报错。
                    continue;
                }

                var pieces = setRecord.GetIdList("pieces");
                var found = false;
                foreach (var piece in pieces)
                {
                    if (piece.Value == record.Key)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", Check,
                        $"item.template \"{record.Key}\" 声明 set_id={setId}，但 {setId}.pieces 未包含本物品",
                        recordKey: record.Key, field: "set_id");
                }
            }
        }
    }

    /// <summary>
    /// <c>stack_size</c> 下限与装备类唯一堆叠约束（见任务书"stack_size ≥ 1，装备类……stack_size ==
    /// 1"）。
    /// <para>
    /// 阶段 3 整理判断记录：原"装备类"拍板是"凡 <c>slot</c> 指向已登记 <c>item.slot_definition</c>
    /// 的即装备类"——不再区分 <c>is_weapon</c>，武器只是装备类的一种。这条拍板把"分类桶"（如消耗品/
    /// 材料，同样需要一个 <c>item.slot_definition</c> 记录才能通过 <c>item.template.slot</c> 的
    /// <c>Reference</c> 校验）也误判成装备类，强迫这类物品 <c>stack_size</c> 只能为 1，与"消耗品应可
    /// 堆叠"的常识冲突。改为读取 <c>item.slot_definition.is_equipment</c>（缺省 true）：只有
    /// <c>is_equipment</c> 不为 false 的槽位才是本规则约束的"装备类"，<c>is_equipment: false</c> 的
    /// 分类桶不受"stack_size 必须为 1"约束。
    /// </para>
    /// </summary>
    public sealed class ItemStackSizeRule : IValidationRule
    {
        public const string CheckMin = "item_stack_size_min";
        public const string CheckEquipmentUnique = "item_stack_size_equipment_not_one";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var equipmentSlots = new HashSet<string>();
            foreach (var slotDef in view.GetAll("item.slot_definition"))
            {
                var isEquipment = !slotDef.TryGetBool("is_equipment", out var value) || value;
                if (isEquipment)
                {
                    equipmentSlots.Add(slotDef.Key);
                }
            }

            foreach (var record in view.GetAll("item.template"))
            {
                if (!record.TryGetInt("stack_size", out var stackSize))
                {
                    continue;
                }

                if (stackSize < 1)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", CheckMin,
                        $"stack_size ({stackSize}) 必须 >= 1", recordKey: record.Key, field: "stack_size");
                    continue;
                }

                if (record.TryGetString("slot", out var slot) && equipmentSlots.Contains(slot) && stackSize != 1)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", CheckEquipmentUnique,
                        $"槽位 \"{slot}\" 是已登记的装备槽，stack_size 必须为 1（实际 {stackSize}）",
                        recordKey: record.Key, field: "stack_size");
                }
            }
        }
    }

    /// <summary>
    /// 相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907 AUDIT_REPORT.md，WA 报告"需要说明的
    /// 取舍"第 3 条）：<c>item.template.grants.auras</c> 同一件物品内重复登记同一个 <c>aura_def</c>
    /// 引用 2 次及以上。<see cref="Core.Carriers.Item.EquipmentHost.ApplyGrants"/> 判断记录（R03
    /// 收边补齐）已经保证这种数据运行期不会再产生残留句柄/计数不一致（第二次施加触发
    /// <c>StackOverflowPolicy.Replace</c> 时能正确迁移句柄），但重复引用本身对数据作者而言几乎总是
    /// 误操作——同一件装备只需要登记一次就能授予同一份光环，重复项不会带来任何额外效果（不是"叠加
    /// 两次"，见 <c>AuraHost.ApplyAura</c> 同来源合并语义），多半是复制粘贴遗留或误增的冗余数据。
    /// <para>
    /// 判断记录（Warning 而非 Error）：不同于 <see cref="ItemWeaponProfileRule"/>/
    /// <see cref="ItemStackSizeRule"/> 这类"必定是数据错误"的 Error 级规则，重复 <c>aura_def</c>
    /// 引用运行期已确认安全（不会导致状态残留/引用计数错乱），拦截阻断会让本就合法可加载的数据集
    /// 突然无法通过校验；提醒级别足以让内容作者注意到这处冗余并自行判断是否要清理，同
    /// <c>SkillValidationRules.ChargesRechargeTimeZeroWarningRule</c>"可能是误操作、也可能是有意为之，
    /// 交给作者复核"同一判断记录惯例。
    /// </para>
    /// </summary>
    public sealed class ItemGrantsAurasDuplicateRule : IValidationRule
    {
        public const string Check = "item_grants_auras_duplicate";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("item.template"))
            {
                if (!record.TryGetObject("grants", out var grants))
                {
                    continue;
                }

                if (!grants.TryGetValue("auras", out var aurasRaw) || !(aurasRaw is JsonArray aurasArr))
                {
                    continue;
                }

                var seen = new HashSet<string>();
                var duplicates = new List<string>();
                foreach (var a in aurasArr)
                {
                    if (a is JsonString s && !seen.Add(s.Value) && !duplicates.Contains(s.Value))
                    {
                        duplicates.Add(s.Value);
                    }
                }

                foreach (var dup in duplicates)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "item.template", Check,
                        $"grants.auras 重复登记了同一个 aura_def \"{dup}\"（同一份光环引用出现 2 次及以上）：" +
                        "运行期不会因此产生残留状态，但重复项不会带来任何额外效果，多半是数据作者误操作，" +
                        "建议只保留一份",
                        recordKey: record.Key, field: "grants.auras");
                }
            }
        }
    }

    /// <summary>
    /// 品质倍率顺序校验（分阶段落地计划 T-N2-1；ADR-0032 决策 2"品质倍率的大小顺序必须与排序权重
    /// 一致（阻断校验）"；04 第 5 节数值类校验项分级表"品质倍率顺序"行："item.quality_definition
    /// 的预算倍率与价格倍率大小顺序与排序权重一致"）。
    /// <para>
    /// 判断记录（检查名）：04 第 5 节该行未像同表其余阻断项那样给出具体检查名（对照"曲线单调有限"→
    /// <c>curve_monotonic_finite</c>、"派生无环"→<c>stat_definition_derivation_cycle</c> 等均括号
    /// 注明检查名，本行没有）。按任务书"检查名按 04 §5 或 item_quality_* 前缀，标注待确认"取
    /// <c>item_quality_multiplier_order</c>，上报待设计层确认，见本任务汇报"契约疑点"一节。
    /// </para>
    /// <para>
    /// 判断记录（"顺序一致"的操作化定义、并列取值的处理）：契约原文"大小顺序须与排序权重一致"未展开
    /// 到"严格递增"还是"不递减"、"sort_weight 相等时如何处理"两个细节。本规则按 <c>sort_weight</c>
    /// 升序分组（同 <see cref="ItemQualityMultiplierOrderRule"/> 命名同义），组内 <c>sort_weight</c>
    /// 相同的记录彼此不比较（并列品质的相对顺序未定义，不应被本规则强行约束），但整组的取值必须
    /// >= 所有更低 <c>sort_weight</c> 分组已出现过的最大值——即"不递减"而非"严格递增"，与 04 第 3.6
    /// 节曲线单调校验"纵轴不递减（允许平台段）"同一处理口径，允许两档品质倍率相同（如两档都是
    /// <c>budget_multiplier: 1</c>）。<c>budget_multiplier</c>/<c>price_multiplier</c> 缺省值分别为
    /// 1（既有/本次新增字段缺省，见 <see cref="ItemSchemas.QualityDefinition"/>），未显式填写的记录
    /// 按缺省值参与比较。
    /// </para>
    /// </summary>
    public sealed class ItemQualityMultiplierOrderRule : IValidationRule
    {
        public const string Check = "item_quality_multiplier_order";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = new List<DataRecord>(view.GetAll("item.quality_definition"));
            if (records.Count == 0)
            {
                yield break;
            }

            // 按 sort_weight 升序、同权重按 Key 稳定排序，保证多次运行结果确定。
            records.Sort((a, b) =>
            {
                var wa = a.TryGetInt("sort_weight", out var sa) ? sa : 0;
                var wb = b.TryGetInt("sort_weight", out var sb) ? sb : 0;
                var cmp = wa.CompareTo(wb);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Key, b.Key);
            });

            foreach (var issue in CheckField(records, "budget_multiplier"))
            {
                yield return issue;
            }
            foreach (var issue in CheckField(records, "price_multiplier"))
            {
                yield return issue;
            }
        }

        private static IEnumerable<ValidationIssue> CheckField(List<DataRecord> sortedRecords, string field)
        {
            var groupMax = double.NegativeInfinity;
            var previousWeight = long.MinValue;
            var runningMaxBeforeGroup = double.NegativeInfinity;
            var hasPrevious = false;

            foreach (var record in sortedRecords)
            {
                var weight = record.TryGetInt("sort_weight", out var w) ? w : 0;
                var value = record.TryGetNumber(field, out var v) ? v : 1.0;

                if (hasPrevious && weight != previousWeight)
                {
                    // 分组切换：把上一分组的最大值并入"更低 sort_weight 已出现过的最大值"基线。
                    runningMaxBeforeGroup = groupMax;
                    groupMax = double.NegativeInfinity;
                }

                if (value < runningMaxBeforeGroup)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.quality_definition", Check,
                        $"{field} ({value:0.###}) 低于更低 sort_weight 分档已出现的最大值 " +
                        $"({runningMaxBeforeGroup:0.###})：品质倍率大小顺序须与排序权重一致（ADR-0032 决策 2）",
                        recordKey: record.Key, field: field);
                }

                if (value > groupMax)
                {
                    groupMax = value;
                }

                previousWeight = weight;
                hasPrevious = true;
            }
        }
    }
}
