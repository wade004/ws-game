using System.Collections.Generic;
using Core.Foundation.Common;
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
            var curveRecord = view.Get("item.budget_curve", _budgetCurveId);
            if (curveRecord == null)
            {
                yield return new ValidationIssue(
                    ValidationSeverity.Error, "item.template", Check,
                    $"预算曲线 \"{_budgetCurveId}\" 不存在，无法校验预算超标");
                yield break;
            }

            var entries = ItemBudgetCurve.ParseEntries(curveRecord);

            var qualityMultipliers = new Dictionary<string, double>();
            foreach (var q in view.GetAll("item.quality_definition"))
            {
                qualityMultipliers[q.Key] = q.TryGetNumber("budget_multiplier", out var m) ? m : 1.0;
            }

            foreach (var record in view.GetAll("item.template"))
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
                var budget = ItemBudgetCurve.Interpolate(entries, (int)itemLevel) * multiplier;
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
    /// 1"；"装备类"拍板：凡 <c>slot</c> 指向已登记 <c>item.slot_definition</c> 的即装备类——不再
    /// 区分 <c>is_weapon</c>，武器只是装备类的一种）。
    /// </summary>
    public sealed class ItemStackSizeRule : IValidationRule
    {
        public const string CheckMin = "item_stack_size_min";
        public const string CheckEquipmentUnique = "item_stack_size_equipment_not_one";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var registeredSlots = new HashSet<string>();
            foreach (var slotDef in view.GetAll("item.slot_definition"))
            {
                registeredSlots.Add(slotDef.Key);
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

                if (record.TryGetString("slot", out var slot) && registeredSlots.Contains(slot) && stackSize != 1)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", CheckEquipmentUnique,
                        $"槽位 \"{slot}\" 是已登记的装备槽，stack_size 必须为 1（实际 {stackSize}）",
                        recordKey: record.Key, field: "stack_size");
                }
            }
        }
    }
}
