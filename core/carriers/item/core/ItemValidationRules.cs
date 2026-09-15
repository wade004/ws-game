using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 预算超标 + 预算利用率过低校验（见 04 第 5 节检查项清单"装备预算超标（消耗侧补齐）"/"装备预算
    /// 利用率过低"、07 第 1.2 节预算公式契约、ADR-0032 决策 3/10）：一件 <c>item.template</c> 的加权
    /// 消耗 <c>(Σ(属性值×权重)^k)^(1/k)</c>（见 <see cref="ItemBudgetCurve.ComputeConsumed"/>）不得
    /// 超过 <c>item.budget_curve(item_level) × quality.budget_multiplier ×
    /// slot.budget_coefficient</c>；低于该上限的 <see cref="UtilizationWarningThreshold"/> 比例
    /// （默认七成）报警告。曲线 id 由构造参数 <paramref name="budgetCurveId"/> 指定（同
    /// <c>MaxEffectsPerSkillRule(int)</c> 惯例：策略配置项经构造参数传入，不在规则内部读取任何
    /// 全局配置）。调用方需要 <c>registry.RegisterValidationRule(new
    /// ItemBudgetValidationRule(options.BudgetCurveId))</c> 才会生效。
    /// <para>
    /// 判断记录（T-N2-3，"ItemBudgetValidationRule 改为能拿到 registry 视图（新增重载）"——
    /// **上报待设计层确认**）：任务书原文要求本规则"改签名拿 registry 视图"，但 <see
    /// cref="IValidationRule.Validate"/> 本就以 <see cref="IDataRegistryView"/> 为唯一参数——新公式
    /// 需要的 <c>stat.weight</c>/<c>stat.definition</c>/<c>stat.rating_conversion</c>/
    /// <c>item.slot_definition</c> 四张表均可经 <see cref="Validate"/> 既有的 <paramref
    /// name="view"/>（下方）直接查询，不需要在<b>构造期</b>额外注入一份 registry 视图（构造发生在
    /// <c>RegisterAll</c> 阶段，此时数据尚未 <c>LoadAll</c>，即便注入了也是空视图，见
    /// <see cref="Core.Carriers.Assembly.CarriersSchemaCatalog"/> 类型顶部判断记录"不调用
    /// <c>IDataRegistry.LoadAll</c>"）。本任务把"改签名"具体落实为新增一个可配置"预算利用率警告
    /// 阈值"的构造重载（<see cref="ItemBudgetValidationRule(Id, double)"/>）——落地 ADR-0032 决策 10
    /// "阈值默认七成，可配置"，旧的单参数构造函数保留并转发默认阈值（硬性规则 5：ABI 只允许新增，
    /// 既有构造签名不得改）。若"registry 视图"另有所指（如某种预先解析好的共享查询对象），需要设计
    /// 层进一步澄清后再补一个真正接受 <see cref="IDataRegistryView"/> 的构造重载。
    /// </para>
    /// </summary>
    public sealed class ItemBudgetValidationRule : IValidationRule
    {
        public const string Check = "item_budget_exceeded";

        /// <summary>预算利用率过低检查名（分阶段落地计划 T-N2-3；ADR-0032 决策 10；04 第 5 节"装备
        /// 预算利用率过低"一行未给出具体检查名，同 <see cref="ItemQualityMultiplierOrderRule"/>/
        /// <see cref="ItemAffixStatMixRatioSumRule"/> 同一处理口径，按 <c>item_budget_*</c> 前缀取
        /// <c>item_budget_utilization_low</c>——**上报待设计层确认**。</summary>
        public const string CheckUtilizationLow = "item_budget_utilization_low";

        /// <summary>预算利用率警告阈值缺省值（ADR-0032 决策 10"默认七成"）。</summary>
        public const double DefaultUtilizationWarningThreshold = 0.7;

        private readonly Id _budgetCurveId;
        private readonly double _utilizationWarningThreshold;

        /// <summary>沿用默认预算利用率警告阈值（<see cref="DefaultUtilizationWarningThreshold"/>）的
        /// 既有构造签名，硬性规则 5（ABI 只允许新增）不得改，转发到 <see cref="ItemBudgetValidationRule
        /// (Id, double)"/>。</summary>
        public ItemBudgetValidationRule(Id budgetCurveId)
            : this(budgetCurveId, DefaultUtilizationWarningThreshold)
        {
        }

        /// <summary>T-N2-3 新增重载：显式指定预算利用率警告阈值（ADR-0032 决策 10"默认七成，可
        /// 配置"），见类型判断记录。</summary>
        public ItemBudgetValidationRule(Id budgetCurveId, double utilizationWarningThreshold)
        {
            _budgetCurveId = budgetCurveId;
            _utilizationWarningThreshold = utilizationWarningThreshold;
        }

        /// <summary>04 第 5 节"警告级这一组登记为不可提升"（数值类校验警告"抓意图不抓手滑"，见
        /// <see cref="IValidationRule.NonEscalatable"/> 判断记录原文即以"装备预算利用率过低"为例）：
        /// 本规则同时产出 <see cref="Check"/>（Error）与 <see cref="CheckUtilizationLow"/>（Warning）
        /// 两种严重级别，<see cref="NonEscalatable"/> 只影响后者在 <c>WarningsBlock</c> 严格级别下
        /// 是否计入阻断（见 <see cref="ValidationReport"/> 聚合逻辑：Error 计数与 NonEscalatable
        /// 无关，恒阻断），对前者没有影响。</summary>
        public bool NonEscalatable => true;

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
            // T-N2-3（ADR-0032 决策 3）：消耗公式指数 k，登记在预算曲线记录自身的可选字段
            // exponent（见 ItemSchemas.BudgetCurve 判断记录），缺省 ItemBudgetCurve.DefaultExponent。
            var exponent = curveRecord.TryGetNumber("exponent", out var exponentValue)
                ? exponentValue
                : ItemBudgetCurve.DefaultExponent;

            var qualityMultipliers = new Dictionary<string, double>();
            foreach (var q in view.GetAll("item.quality_definition"))
            {
                qualityMultipliers[q.Key] = q.TryGetNumber("budget_multiplier", out var m) ? m : 1.0;
            }

            // T-N2-3（ADR-0032 决策 1/3；07 第 1.2 节修订段）：槽位预算系数，缺省 1（未登记视为全额
            // 槽位，见 ItemSchemas.SlotDefinition.budget_coefficient 判断记录）。
            var slotCoefficients = new Dictionary<string, double>();
            foreach (var s in view.GetAll("item.slot_definition"))
            {
                slotCoefficients[s.Key] = s.TryGetNumber("budget_coefficient", out var c) ? c : 1.0;
            }

            // T-N2-3：一次性构建属性权重/换算信息表，逐模板复用（见 ItemBudgetCurve.BuildStatBudgetInfo
            // 判断记录）。
            var statInfo = ItemBudgetCurve.BuildStatBudgetInfo(view);

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

                var qualityMultiplier = qualityMultipliers.TryGetValue(quality, out var m2) ? m2 : 1.0;
                var slotCoefficient = record.TryGetString("slot", out var slot) &&
                    slotCoefficients.TryGetValue(slot, out var sc) ? sc : 1.0;
                var budget = ItemBudgetCurve.Interpolate(curve, (int)itemLevel) * qualityMultiplier * slotCoefficient;
                var consumed = ItemBudgetCurve.ComputeConsumed(stats, statInfo, (int)itemLevel, exponent);

                if (consumed > budget)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", Check,
                        $"stats 消耗预算 {consumed:0.###} 超过上限 {budget:0.###}" +
                        $"（item_level={itemLevel}, quality={quality}, slot={slot}）",
                        recordKey: record.Key, field: "stats");
                    continue;
                }

                if (budget > 0 && consumed / budget < _utilizationWarningThreshold)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "item.template", CheckUtilizationLow,
                        $"预算利用率 {(consumed / budget):P1} 低于阈值 {_utilizationWarningThreshold:P0}" +
                        $"（消耗 {consumed:0.###} / 上限 {budget:0.###}，item_level={itemLevel}, quality={quality}, " +
                        $"slot={slot}）：抓漏填，可能是模板忘记补全属性词条",
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

    /// <summary>
    /// 词缀份额之和校验（分阶段落地计划 T-N2-2；ADR-0032 决策 7"单条词缀份额之和不超过一为阻断校验"；
    /// 07 第 1.6 节修订段"<c>stat_mix</c>（属性组合与内部分配比例，之和不超过一）"）：单条
    /// <c>item.affix.stat_mix</c> 内部 <c>ratio</c> 之和不超过一。
    /// <para>
    /// 判断记录（校验对象——"单条词缀内部"而非"同一品质池跨词缀"）：ADR-0032 决策 7 原文与 07 第 1.6
    /// 节修订段两处均把"之和不超过一"紧跟在 <c>stat_mix</c>（单个字段）后面描述，且决策 7 的完整句是
    /// "<c>stat_mix</c>（属性组合与内部分配比例）……单条词缀份额之和不超过一"——"单条词缀"与
    /// "<c>stat_mix</c> 内部分配"两处表述指向同一个对象：一条 <c>item.affix</c> 记录自己的
    /// <c>stat_mix[]</c> 数组。04 第 5 节数值类校验项分级表同一行说明"单条 <c>item.affix.stat_mix</c>
    /// 内部分配比例之和不超过一"，与本判断记录结论一致，非本任务实现期新解读。跨词缀/跨品质池的份额
    /// 关系（如"同一品质池全部词缀权重之和"）契约未提及任何约束，本规则不检查。
    /// </para>
    /// <para>
    /// 判断记录（检查名）：04 第 5 节"词缀份额之和"一行未像"曲线单调有限"等同表其余阻断项那样给出
    /// 具体检查名（括号注明检查名）。按任务书"检查名按 04 §5，没有就 item_affix_* 前缀标待确认"取
    /// <c>item_affix_stat_mix_ratio_sum</c>，命名同 <see cref="ItemQualityMultiplierOrderRule"/>
    /// 判断记录同一处理口径——**上报待设计层确认**，见本任务汇报"契约疑点"一节。
    /// </para>
    /// <para>
    /// 判断记录（浮点容差）：<c>ratio</c> 逐项相加存在浮点舍入误差（如三个 0.3333... 相加可能得到
    /// 1.0000000000000002），严格 <c>&gt; 1</c> 比较会把"策划填 1/3 三等分"这类合法数据误判为超标。
    /// 引入 <see cref="Epsilon"/>（1e-9，同任务书 G1 验收标准"误差 &lt; 1e-9"给出的量级）：
    /// <c>sum &gt; 1 + Epsilon</c> 才报错，边界值 1（含浮点误差范围内的 1）视为合法。
    /// </para>
    /// </summary>
    public sealed class ItemAffixStatMixRatioSumRule : IValidationRule
    {
        public const string Check = "item_affix_stat_mix_ratio_sum";

        private const double Epsilon = 1e-9;

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll("item.affix"))
            {
                if (!record.TryGetArray("stat_mix", out var statMix) || statMix.Count == 0)
                {
                    continue;
                }

                var sum = 0.0;
                foreach (var entry in statMix)
                {
                    if (entry is JsonObject o && o.TryGetValue("ratio", out var r) && r is JsonNumber n)
                    {
                        sum += n.Value;
                    }
                }

                if (sum > 1.0 + Epsilon)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.affix", Check,
                        $"stat_mix[].ratio 之和 {sum:0.#########} 超过一（ADR-0032 决策 7；" +
                        "单条词缀内部分配比例不得超过预算总量）",
                        recordKey: record.Key, field: "stat_mix");
                }
            }
        }
    }

    /// <summary>
    /// 分阶段落地计划 T-N2-6（ADR-0032 决策 4；拍板 6"damage_min/max 保留手填，新增偏离秒伤曲线警告"；
    /// 07 第 1.2 节修订段"伤害范围 = 武器秒伤 × weapon_profile.speed × (1 ± 浮动)"）：手填
    /// <c>weapon_profile.damage_min</c>/<c>damage_max</c> 的均值与理论期望值"武器秒伤 ×
    /// <c>weapon_profile.speed</c>"偏离超过阈值时报警告——"抓漏填/抓意图不抓手滑"，不阻断合入（同
    /// <see cref="ItemBudgetValidationRule.CheckUtilizationLow"/> 一贯处理口径）。
    /// <para>
    /// 判断记录（检查名——**上报待设计层确认**）：04 第 5 节"武器伤害范围手填还是推导"一行未给出
    /// 具体检查名，按任务书"没有就 item_weapon_* 前缀标待确认"取 <c>item_weapon_damage_deviates_dps_curve</c>，
    /// 与 <see cref="ItemQualityMultiplierOrderRule"/>/<see cref="ItemAffixStatMixRatioSumRule"/>/
    /// <see cref="ItemBudgetValidationRule.CheckUtilizationLow"/> 同一处理口径。
    /// </para>
    /// <para>
    /// 判断记录（阈值——**上报待设计层确认**）：契约同样未给出具体偏离阈值。落地改动点清单第 10 节
    /// 第 6 条候选写法是"新增手填偏离秒伤曲线警告"，未给数值；按任务书"没给阈值就选 ±20% 标待确认"
    /// 取 <see cref="DefaultDeviationThreshold"/>=0.2，构造重载可覆盖（同
    /// <see cref="ItemBudgetValidationRule(Id, double)"/> 惯例）。
    /// </para>
    /// <para>
    /// 判断记录（比较对象——均值 vs 单侧上下界）：本规则只比较
    /// <c>(damage_min+damage_max)/2</c> 与"秒伤 × speed"这一个理论期望均值的相对偏差，不展开到
    /// <c>item.weapon_dps_curve.variance</c>（(1±浮动) 的上下界）——该字段本任务只登记，尚无消费者
    /// （见 <see cref="ItemSchemas.WeaponDpsCurve"/> 判断记录），"均值偏离"已经足够覆盖任务书"手填
    /// 偏离秒伤曲线"的验收描述（正负例均按均值判断）。
    /// </para>
    /// <para>
    /// 判断记录（何时跳过——未填不报/曲线缺失不报/非武器槽不报）：<c>damage_min</c>/<c>damage_max</c>
    /// 任一字段在 JSON 里缺失（而不是"填了 0"）时跳过——两者都是可选字段、缺省 0（见 <see
    /// cref="ItemSchemas.WeaponProfileSchema"/>），"没填"与"填了 0"在数据层面无法通过默认值区分，
    /// 只能通过 <see cref="JsonObject.TryGetValue"/> 判断字段是否真的出现在 JSON 里；任务书"未填
    /// damage_min/max 不报"也是同一诉求（保留手填字段真正意义上的"可选"，不强迫作者为不关心的字段
    /// 填占位值）。<c>item.weapon_dps_curve</c> 曲线（id 取构造参数 <see cref="_weaponDpsCurveId"/>）
    /// 在已加载数据里找不到对应记录、或 <c>speed</c> 未填/为 0（无法建立理论期望值）时，本规则整体
    /// 不产出任何问题——不同于 <see cref="ItemBudgetValidationRule"/> 缺曲线报 Error：武器秒伤曲线不
    /// 是强制表，纯近战数值/不使用武器系统的游戏层可以完全不登记该表，见 <see
    /// cref="ItemSchemas.WeaponDpsCurve"/> 类型判断记录"本任务只登记 schema"。非武器槽
    /// （<c>item.slot_definition.is_weapon != true</c>）不会有 <c>weapon_profile</c>（<see
    /// cref="ItemWeaponProfileRule"/> 阻断校验保证），本规则天然只覆盖武器槽模板。
    /// </para>
    /// </summary>
    public sealed class ItemWeaponDamageDeviatesDpsCurveRule : IValidationRule
    {
        public const string Check = "item_weapon_damage_deviates_dps_curve";

        /// <summary>偏离阈值缺省值（见类型判断记录"阈值——上报待设计层确认"）。</summary>
        public const double DefaultDeviationThreshold = 0.2;

        /// <summary>04 第 5 节"数值类校验警告……抓意图不抓手滑"——本规则只产出 Warning，不影响任何
        /// Error 级别检查，见 <see cref="ItemBudgetValidationRule.NonEscalatable"/> 同一处理口径。</summary>
        public bool NonEscalatable => true;

        private readonly Id _weaponDpsCurveId;
        private readonly double _deviationThreshold;

        /// <summary>沿用缺省偏离阈值（<see cref="DefaultDeviationThreshold"/>）的构造签名。</summary>
        public ItemWeaponDamageDeviatesDpsCurveRule(Id weaponDpsCurveId)
            : this(weaponDpsCurveId, DefaultDeviationThreshold)
        {
        }

        /// <summary>显式指定偏离阈值的构造重载（见类型判断记录"阈值——上报待设计层确认"）。</summary>
        public ItemWeaponDamageDeviatesDpsCurveRule(Id weaponDpsCurveId, double deviationThreshold)
        {
            _weaponDpsCurveId = weaponDpsCurveId;
            _deviationThreshold = deviationThreshold;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var templates = view.GetAll("item.template");
            if (templates.Count == 0)
            {
                yield break;
            }

            var curveRecord = view.Get("item.weapon_dps_curve", _weaponDpsCurveId);
            if (curveRecord == null)
            {
                // 判断记录：武器秒伤曲线不是强制表，见类型判断记录"何时跳过"。
                yield break;
            }

            // item.weapon_dps_curve 是 T-N2-1 直接按 CurveSchema.BreakpointsField 登记的新表（无需
            // 迁移链，同 item.armor_curve/item.req_level_curve），用 CurveSchema.ReadBreakpoints 读取
            // ——同 EquipmentHost.ApplyArmorValue/GetWeaponDps 既有惯例，不用 ItemBudgetCurve.ParseCurve
            // （后者面向 item.budget_curve 自身，异常消息硬编码该表名，用于本表会产生误导性报错）。
            var curve = CurveSchema.ReadBreakpoints(curveRecord, "entries");

            var qualityMultipliers = new Dictionary<string, double>();
            foreach (var q in view.GetAll("item.quality_definition"))
            {
                qualityMultipliers[q.Key] = q.TryGetNumber("budget_multiplier", out var m) ? m : 1.0;
            }

            var weaponSlotCoefficients = new Dictionary<string, double>();
            foreach (var s in view.GetAll("item.slot_definition"))
            {
                if (s.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlotCoefficients[s.Key] = s.TryGetNumber("budget_coefficient", out var c) ? c : 1.0;
                }
            }

            foreach (var record in templates)
            {
                if (!record.TryGetString("slot", out var slot) || !weaponSlotCoefficients.TryGetValue(slot, out var slotCoefficient))
                {
                    continue;
                }

                if (!record.TryGetObject("weapon_profile", out var profile))
                {
                    continue;
                }

                // 判断记录："未填不报"——damage_min/damage_max 任一字段不在 JSON 里出现即跳过，
                // 不能用 GetNumber(…, fallback: 0) 之类默认值判断，见类型判断记录。
                if (!profile.TryGetValue("damage_min", out var minRaw) || !(minRaw is JsonNumber minNum) ||
                    !profile.TryGetValue("damage_max", out var maxRaw) || !(maxRaw is JsonNumber maxNum))
                {
                    continue;
                }

                var speed = profile.TryGetValue("speed", out var speedRaw) && speedRaw is JsonNumber speedNum
                    ? speedNum.Value
                    : 0.0;

                if (!record.TryGetInt("item_level", out var itemLevel) || !record.TryGetString("quality", out var quality))
                {
                    continue;
                }

                var qualityMultiplier = qualityMultipliers.TryGetValue(quality, out var qm) ? qm : 1.0;
                var dps = curve.Evaluate((int)itemLevel) * qualityMultiplier * slotCoefficient;
                var expectedMean = dps * speed;
                if (expectedMean <= 0)
                {
                    // speed 未填/为 0：无法建立理论期望值（除零防御），见类型判断记录。
                    continue;
                }

                var actualMean = (minNum.Value + maxNum.Value) / 2.0;
                var deviation = System.Math.Abs(actualMean - expectedMean) / expectedMean;

                if (deviation > _deviationThreshold)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, "item.template", Check,
                        $"weapon_profile.damage_min/damage_max 均值 {actualMean:0.###} 偏离武器秒伤曲线" +
                        $"期望值 {expectedMean:0.###}（秒伤 {dps:0.###} × speed {speed:0.###}）达 " +
                        $"{deviation:P1}，超过阈值 {_deviationThreshold:P0}" +
                        $"（item_level={itemLevel}, quality={quality}, slot={slot}）：抓漏填/抓意图不" +
                        "抓手滑，可能是伤害区间忘记跟随物品等级调整",
                        recordKey: record.Key, field: "weapon_profile");
                }
            }
        }
    }

    /// <summary>
    /// 模板加词缀最大份额超预算（分阶段落地计划 T-N2-11；ADR-0032 决策 7/10；04 第 5 节数值类校验项
    /// 分级表"模板加词缀最大份额超预算"行，检查名 <see cref="Check"/>：设计层裁定（2026-09-15）登记，
    /// 04 该行原文未给出具体检查名，按 <c>item_template_affix_share_exceeds_budget</c> 采纳，见 04
    /// 该行同一次改动的勘误记录）。
    /// <para>
    /// 语义（设计层裁定（2026-09-15）：采纳）：对每条 <c>item.template</c>——
    /// <c>consumed = ItemBudgetCurve.ComputeConsumed(stats, ...)</c>（同 <see
    /// cref="ItemBudgetValidationRule"/> 既有消耗侧公式，基础权重，不按职业覆盖）；
    /// <c>B</c> = 预算上限（曲线(item_level) × 品质预算倍率 × 槽位系数，同
    /// <see cref="ItemBudgetValidationRule"/> 既有算法）；候选词缀 = <c>item.affix</c> 中
    /// <c>quality_pool == 本模板 quality</c>，且模板 <c>affixes</c> 白名单非空时再与之取交集；
    /// <c>maxShare</c> = 候选按 <c>budget_share</c> 降序（同份额按 <c>Id</c> 升序稳定排序）取前
    /// <c>affix_count</c>（本模板品质在 <c>item.quality_definition.affix_count</c> 登记的数量；
    /// 该字段未登记时视为"不限"，取全部候选——与掉落三次掷骰（<c>core/gameplay/loot</c>
    /// <c>LootHost.RollAffixes</c>，T-N2-8）运行期"未登记按 0（不掷词缀骰）"处理不同：本层
    /// （<c>core/carriers/item</c>）不依赖 <c>core/gameplay</c>（L3 不反向依赖 L4），此处只作文字
    /// 说明不作代码引用；校验期核算的是"最坏情况下这个词缀池能把预算堆到多满"这一上界，词缀池将来
    /// 扩容、<c>affix_count</c> 补登记都不应该让已经通过校验的旧数据突然超标，取全部候选是保守
    /// 上界）之和；若
    /// <c>consumed + maxShare × B &gt; B</c>（1e-9 浮点容差）报 <see cref="ValidationSeverity.Error"/>，
    /// 消息点出 <c>consumed</c>/<c>B</c>/<c>maxShare</c> 三个量供内容作者定位。<c>B ≤ 0</c> 或预算
    /// 曲线记录不存在时跳过（<c>B ≤ 0</c> 同 <see cref="ItemBudgetValidationRule"/> 利用率警告的
    /// <c>budget &gt; 0</c> 门槛同一处理口径；曲线记录缺失时 <see cref="ItemBudgetValidationRule"/>
    /// 已经报出"预算曲线不存在"Error，本规则不重复报错，静默跳过整条规则）。
    /// </para>
    /// <para>
    /// 判断记录（候选词缀预排序、按品质分桶）：<c>item.affix</c> 全表按 <c>quality_pool</c> 分桶、
    /// 桶内按 <c>budget_share</c> 降序预排序一次，逐模板复用，避免每条模板都重新扫描并排序全表
    /// （同 <see cref="ItemBudgetValidationRule.Validate"/> 一次性构建 <c>qualityMultipliers</c>/
    /// <c>slotCoefficients</c>/<c>statInfo</c> 再逐模板复用的既有惯例）；模板白名单过滤在遍历某个
    /// 品质桶的预排序候选时逐个跳过非白名单项，不重新排序（保序过滤，不影响 <c>budget_share</c>
    /// 降序结果）。
    /// </para>
    /// </summary>
    public sealed class ItemTemplateAffixShareExceedsBudgetRule : IValidationRule
    {
        public const string Check = "item_template_affix_share_exceeds_budget";

        private const double Epsilon = 1e-9;

        private static readonly JsonArray EmptyStats = new JsonArray();
        private static readonly List<(string Key, double BudgetShare)> EmptyCandidates =
            new List<(string, double)>();

        private readonly Id _budgetCurveId;

        /// <summary>曲线 id 由构造参数指定，同 <see cref="ItemBudgetValidationRule(Id)"/> 惯例——
        /// 本规则与 <see cref="ItemBudgetValidationRule"/> 核算的是同一条预算曲线/同一个预算上限
        /// <c>B</c>，调用方需要传入同一个 <paramref name="budgetCurveId"/>（<see
        /// cref="Core.Carriers.Assembly.CarriersSchemaCatalog"/> 已按此惯例接线，两条规则共用
        /// 注册期传入的同一个 <c>budgetCurveId</c>，不新增独立参数）。</summary>
        public ItemTemplateAffixShareExceedsBudgetRule(Id budgetCurveId)
        {
            _budgetCurveId = budgetCurveId;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var templates = view.GetAll("item.template");
            if (templates.Count == 0)
            {
                yield break;
            }

            var curveRecord = view.Get("item.budget_curve", _budgetCurveId);
            if (curveRecord == null)
            {
                // 判断记录：ItemBudgetValidationRule 已经对"预算曲线不存在"报 Error，本规则不重复
                // 报错，静默跳过（见类型判断记录）。
                yield break;
            }

            var curve = ItemBudgetCurve.ParseCurve(curveRecord);
            var exponent = curveRecord.TryGetNumber("exponent", out var exponentValue)
                ? exponentValue
                : ItemBudgetCurve.DefaultExponent;

            var qualityMultipliers = new Dictionary<string, double>();
            var affixCounts = new Dictionary<string, int?>();
            foreach (var q in view.GetAll("item.quality_definition"))
            {
                qualityMultipliers[q.Key] = q.TryGetNumber("budget_multiplier", out var m) ? m : 1.0;
                // 判断记录：affix_count 未登记视为"不限"（null），不是 0——见类型判断记录"语义"一段。
                affixCounts[q.Key] = q.TryGetInt("affix_count", out var ac) ? (int?)ac : null;
            }

            var slotCoefficients = new Dictionary<string, double>();
            foreach (var s in view.GetAll("item.slot_definition"))
            {
                slotCoefficients[s.Key] = s.TryGetNumber("budget_coefficient", out var c) ? c : 1.0;
            }

            var statInfo = ItemBudgetCurve.BuildStatBudgetInfo(view);

            // 按品质池预构建候选词缀（budget_share 降序、同份额按 Key 升序稳定排序），见类型判断记录
            // "候选词缀预排序、按品质分桶"。
            var affixesByQuality = new Dictionary<string, List<(string Key, double BudgetShare)>>();
            foreach (var affix in view.GetAll("item.affix"))
            {
                if (!affix.TryGetId("quality_pool", out var pool))
                {
                    continue;
                }

                var share = affix.TryGetNumber("budget_share", out var bs) ? bs : 0.0;
                if (!affixesByQuality.TryGetValue(pool.Value, out var list))
                {
                    list = new List<(string, double)>();
                    affixesByQuality[pool.Value] = list;
                }

                list.Add((affix.Key, share));
            }

            foreach (var list in affixesByQuality.Values)
            {
                list.Sort((a, b) =>
                {
                    var cmp = b.BudgetShare.CompareTo(a.BudgetShare); // 降序
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Key, b.Key);
                });
            }

            foreach (var record in templates)
            {
                if (!record.TryGetInt("item_level", out var itemLevel) || !record.TryGetString("quality", out var quality))
                {
                    continue;
                }

                var qualityMultiplier = qualityMultipliers.TryGetValue(quality, out var m2) ? m2 : 1.0;
                var slotCoefficient = record.TryGetString("slot", out var slot) &&
                    slotCoefficients.TryGetValue(slot, out var sc) ? sc : 1.0;
                var budget = ItemBudgetCurve.Interpolate(curve, (int)itemLevel) * qualityMultiplier * slotCoefficient;

                if (budget <= 0)
                {
                    // 判断记录：B ≤ 0 跳过，同 ItemBudgetValidationRule 利用率警告 budget > 0 门槛口径
                    // 一致（见类型判断记录）。
                    continue;
                }

                var stats = record.TryGetArray("stats", out var statsArr) ? statsArr : EmptyStats;
                var consumed = ItemBudgetCurve.ComputeConsumed(stats, statInfo, (int)itemLevel, exponent);

                var hasWhitelist = record.TryGetIdList("affixes", out var whitelist) && whitelist.Count > 0;
                var candidates = affixesByQuality.TryGetValue(quality, out var poolCandidates)
                    ? poolCandidates
                    : EmptyCandidates;
                var affixCount = affixCounts.TryGetValue(quality, out var acForQuality) ? acForQuality : null;

                var maxShare = 0.0;
                var taken = 0;
                foreach (var candidate in candidates)
                {
                    if (hasWhitelist && !ContainsKey(whitelist, candidate.Key))
                    {
                        continue;
                    }

                    if (affixCount.HasValue && taken >= affixCount.Value)
                    {
                        break;
                    }

                    maxShare += candidate.BudgetShare;
                    taken++;
                }

                var total = consumed + maxShare * budget;
                if (total > budget + Epsilon)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, "item.template", Check,
                        $"模板自身消耗 {consumed:0.###} + 可抽词缀最大份额 {maxShare:0.###} × 预算上限 " +
                        $"{budget:0.###} = {total:0.###}，超过预算上限 {budget:0.###}" +
                        $"（item_level={itemLevel}, quality={quality}, slot={slot}）",
                        recordKey: record.Key, field: "affixes");
                }
            }
        }

        private static bool ContainsKey(IReadOnlyList<Id> list, string key)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Value == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
