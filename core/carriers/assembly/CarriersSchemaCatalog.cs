using Core.Carriers.Creature;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Assembly;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// L0～L3 全部 <see cref="TableSchema"/>/<see cref="IValidationRule"/> 的统一注册清单（阶段 3
    /// 整理"事项四"，落地方案 T2-11 <see cref="RulesSchemaCatalog"/> 在 L3 层的延续）：调用方
    /// （游戏层引导代码、集成测试）在构造 <see cref="DataRegistry"/> 之后、
    /// <see cref="IDataRegistry.LoadAll"/> 之前调用 <see cref="RegisterAll"/> 一次即可注册齐全，不
    /// 需要自己在 L2 <see cref="RulesSchemaCatalog"/> 与 L3 三个模块的 <c>*Schemas</c> 类之间逐一
    /// 枚举。
    /// <para>
    /// 分层判断记录：本类<b>不</b>注册 <c>core/gameplay/world_state</c> 的 <c>world.flag_schema</c>
    /// ——那是 L4 玩法层的表（见 01_分层与依赖.md L4 模块表 <c>world_state</c> 行），01 第 3 节依赖
    /// 矩阵"L3 允许依赖 L0～L2，禁止依赖 L4"同样约束"L3 的统一注册清单不得反向登记 L4 的表"，否则
    /// 本类（L3）就产生了对 L4 程序集/表清单的隐式认知耦合。L4 数据装配根需要
    /// <c>world.flag_schema</c> 时应在 <see cref="RegisterAll"/> 之后自行追加
    /// <c>registry.RegisterSchema(WorldStateSchemas.FlagSchema)</c>，本类不代劳。
    /// </para>
    /// </summary>
    public static class CarriersSchemaCatalog
    {
        /// <summary><c>item.budget_curve</c> 的默认 id，同 <see cref="ItemOptions.BudgetCurveId"/>
        /// 默认值——<see cref="RegisterAll"/> 未显式传入 <paramref name="itemBudgetCurveId"/> 时，
        /// <see cref="ItemBudgetValidationRule"/> 用这份默认值构造，保证"注册期用哪个曲线校验"与
        /// "运行期 <see cref="ItemOptions"/> 默认值指向哪个曲线"一致（避免两处各自维护一份默认值
        /// 而漂移）。</summary>
        public static readonly Id DefaultItemBudgetCurveId = new Id("item.budget.default");

        /// <summary>T-N2-3（ADR-0032 决策 10"预算利用率低于阈值（默认七成）警告……可配置"）：<see
        /// cref="ItemBudgetValidationRule"/> 新增构造重载用的默认阈值，同 <see
        /// cref="ItemBudgetValidationRule.DefaultUtilizationWarningThreshold"/>。</summary>
        public static readonly double DefaultItemBudgetUtilizationWarningThreshold =
            ItemBudgetValidationRule.DefaultUtilizationWarningThreshold;

        /// <summary><c>item.weapon_dps_curve</c> 的默认 id，同 <see cref="ItemOptions.WeaponDpsCurveId"/>
        /// 默认值——惯例同 <see cref="DefaultItemBudgetCurveId"/>。</summary>
        public static readonly Id DefaultWeaponDpsCurveId = new Id("item.weapon_dps.default");

        /// <summary>T-N2-6（拍板 6）：<see cref="ItemWeaponDamageDeviatesDpsCurveRule"/> 新增构造重载
        /// 用的默认偏离阈值，同 <see cref="ItemWeaponDamageDeviatesDpsCurveRule.DefaultDeviationThreshold"/>。
        /// </summary>
        public static readonly double DefaultItemWeaponDamageDeviationThreshold =
            ItemWeaponDamageDeviatesDpsCurveRule.DefaultDeviationThreshold;

        /// <summary>
        /// 注册 L0 内置 + L1 五模块 + L2 四模块（经 <see cref="RulesSchemaCatalog.RegisterAll"/>）+
        /// L3 三模块（item 六张表、creature 两张表、gobj 两张表）的全部 <see cref="TableSchema"/> 与
        /// <see cref="IValidationRule"/>。不调用 <see cref="IDataRegistry.LoadAll"/>——加载时机由
        /// 调用方决定（通常紧跟在本方法之后）。
        /// </summary>
        /// <param name="registry">目标注册表。</param>
        /// <param name="itemBudgetCurveId"><see cref="ItemBudgetValidationRule"/> 构造用的曲线 id，
        /// 缺省 <see cref="DefaultItemBudgetCurveId"/>（与 <see cref="ItemOptions.BudgetCurveId"/>
        /// 默认值一致）。游戏层若用非默认的 <see cref="ItemOptions.BudgetCurveId"/>，需要把同一个值
        /// 传给本参数，否则会出现"运行期实际用曲线 A、注册期却用默认曲线 B 校验"的不一致（同
        /// <see cref="RulesSchemaCatalog.RegisterAll"/> 里 <c>MaxEffectsPerSkillRule</c> 一节判断
        /// 记录的同类风险）。</param>
        public static void RegisterAll(IDataRegistry registry, Id? itemBudgetCurveId = null)
        {
            RegisterAll(registry, itemBudgetCurveId, itemBudgetUtilizationWarningThreshold: null);
        }

        /// <summary>T-N2-3 新增重载（硬性规则 5：ABI 只允许新增，<see cref="RegisterAll(IDataRegistry,
        /// Id?)"/> 既有两参数签名不得改，见上一重载）：额外接受 <see
        /// cref="ItemBudgetValidationRule"/> 的预算利用率警告阈值。</summary>
        /// <param name="registry">目标注册表。</param>
        /// <param name="itemBudgetCurveId">同上一重载。</param>
        /// <param name="itemBudgetUtilizationWarningThreshold"><see cref="ItemBudgetValidationRule"/>
        /// 预算利用率警告阈值，缺省（<c>null</c>）取 <see
        /// cref="DefaultItemBudgetUtilizationWarningThreshold"/>（ADR-0032 决策 10"默认七成，可
        /// 配置"——本参数即"可配置"落地位置之一，见 <see cref="ItemBudgetValidationRule"/> 类型判断
        /// 记录"设计层裁定（2026-09-15）：采纳"）。</param>
        public static void RegisterAll(IDataRegistry registry, Id? itemBudgetCurveId,
            double? itemBudgetUtilizationWarningThreshold)
        {
            RegisterAll(registry, itemBudgetCurveId, itemBudgetUtilizationWarningThreshold,
                weaponDpsCurveId: null, weaponDamageDeviationThreshold: null);
        }

        /// <summary>T-N2-6 新增重载（硬性规则 5：ABI 只允许新增，前两个重载既有签名不得改，见上两个
        /// 重载）：额外接受 <see cref="ItemWeaponDamageDeviatesDpsCurveRule"/> 的曲线 id 与偏离阈值，
        /// 同 <paramref name="itemBudgetCurveId"/>/<paramref name="itemBudgetUtilizationWarningThreshold"/>
        /// 一贯惯例。</summary>
        /// <param name="registry">目标注册表。</param>
        /// <param name="itemBudgetCurveId">同两参数重载。</param>
        /// <param name="itemBudgetUtilizationWarningThreshold">同三参数重载。</param>
        /// <param name="weaponDpsCurveId"><see cref="ItemWeaponDamageDeviatesDpsCurveRule"/> 构造用的
        /// 武器秒伤曲线 id，缺省 <see cref="DefaultWeaponDpsCurveId"/>（与 <see
        /// cref="ItemOptions.WeaponDpsCurveId"/> 默认值一致，同 <paramref name="itemBudgetCurveId"/>
        /// 判断记录同一处理口径——运行期实际用曲线与注册期校验曲线需保持一致）。</param>
        /// <param name="weaponDamageDeviationThreshold"><see
        /// cref="ItemWeaponDamageDeviatesDpsCurveRule"/> 偏离阈值，缺省（<c>null</c>）取 <see
        /// cref="DefaultItemWeaponDamageDeviationThreshold"/>（拍板 6"新增偏离秒伤曲线警告"——阈值
        /// 本身设计层裁定（2026-09-15）：采纳，见该规则类型判断记录）。</param>
        public static void RegisterAll(IDataRegistry registry, Id? itemBudgetCurveId,
            double? itemBudgetUtilizationWarningThreshold, Id? weaponDpsCurveId,
            double? weaponDamageDeviationThreshold)
        {
            RulesSchemaCatalog.RegisterAll(registry);

            RegisterItemSchemas(registry, itemBudgetCurveId ?? DefaultItemBudgetCurveId,
                itemBudgetUtilizationWarningThreshold ?? DefaultItemBudgetUtilizationWarningThreshold,
                weaponDpsCurveId ?? DefaultWeaponDpsCurveId,
                weaponDamageDeviationThreshold ?? DefaultItemWeaponDamageDeviationThreshold);
            RegisterCreatureSchemas(registry);
            RegisterGobjSchemas(registry);
        }

        private static void RegisterItemSchemas(IDataRegistry registry, Id budgetCurveId,
            double utilizationWarningThreshold, Id weaponDpsCurveId, double weaponDamageDeviationThreshold)
        {
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.BudgetCurve);
            // T-N2-1（ADR-0032 决策 4/5）：护甲/武器秒伤/需求等级三条新曲线表，登记与注册同 BudgetCurve。
            registry.RegisterSchema(ItemSchemas.ArmorCurve);
            registry.RegisterSchema(ItemSchemas.WeaponDpsCurve);
            registry.RegisterSchema(ItemSchemas.ReqLevelCurve);
            registry.RegisterSchema(ItemSchemas.Set);
            registry.RegisterSchema(ItemSchemas.Affix);

            registry.RegisterValidationRule(new ItemBudgetValidationRule(budgetCurveId, utilizationWarningThreshold));
            registry.RegisterValidationRule(new ItemWeaponProfileRule());
            registry.RegisterValidationRule(new ItemSetMembershipRule());
            registry.RegisterValidationRule(new ItemStackSizeRule());
            // 相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907，WA 报告"需要说明的取舍"第 3 条）：
            // grants.auras 同一物品内重复登记同一个 aura_def，见 ItemGrantsAurasDuplicateRule 判断记录。
            registry.RegisterValidationRule(new ItemGrantsAurasDuplicateRule());
            // T-N2-1（ADR-0032 决策 2；04 第 5 节"品质倍率顺序"阻断校验）：budget_multiplier/
            // price_multiplier 大小顺序须与 sort_weight 一致，见 ItemQualityMultiplierOrderRule 判断记录。
            registry.RegisterValidationRule(new ItemQualityMultiplierOrderRule());
            // T-N2-2（ADR-0032 决策 7；04 第 5 节"词缀份额之和"阻断校验）：单条 item.affix.stat_mix
            // 内部 ratio 之和不超过一，见 ItemAffixStatMixRatioSumRule 判断记录。
            registry.RegisterValidationRule(new ItemAffixStatMixRatioSumRule());
            // T-N2-6（ADR-0032 决策 4；拍板 6"damage_min/max 保留手填，新增偏离秒伤曲线警告"）：
            // 手填 weapon_profile.damage_min/damage_max 均值偏离"秒伤 × speed"超阈值报警告，见
            // ItemWeaponDamageDeviatesDpsCurveRule 判断记录。
            registry.RegisterValidationRule(
                new ItemWeaponDamageDeviatesDpsCurveRule(weaponDpsCurveId, weaponDamageDeviationThreshold));
            // T-N2-11（ADR-0032 决策 7/10；04 第 5 节"模板加词缀最大份额超预算"阻断校验）：模板自身
            // 消耗 + 可抽词缀池最大份额 × 预算不得超过预算上限，见 ItemTemplateAffixShareExceedsBudgetRule
            // 判断记录；与 ItemBudgetValidationRule 核算同一条预算曲线，复用同一个 budgetCurveId，不
            // 新增独立参数/不新增 RegisterAll 重载。
            registry.RegisterValidationRule(new ItemTemplateAffixShareExceedsBudgetRule(budgetCurveId));

            // item.template.slot/quality/set_id 三个字段已在 ItemSchemas.Template 声明为
            // FieldKind.Reference，data_registry 内置 reference_integrity 校验自动生效，不需要本类
            // 额外调用 DeclareReference（同 RulesSchemaCatalog.DeclareKnownReferences 判断记录里
            // "只有 Reference 字段才享受内置检查"的惯例，本模块三个字段恰好都已经是 Reference）。
        }

        private static void RegisterCreatureSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(CreatureSchemas.Template);
            registry.RegisterSchema(CreatureSchemas.TierDefinition);

            registry.RegisterValidationRule(new CreatureContentValidationRule());

            // creature.template.tier/stat_growth_ref 同样已声明为 FieldKind.Reference（见
            // CreatureSchemas 判断记录），ai_rotation_ref/ai_behavior_ref/loot_table_ref/display_ref
            // 故意不声明为 Reference（目标表不在本任务数据集范围内，见 creature/README.md），本类
            // 不越权替它们补 DeclareReference。
        }

        private static void RegisterGobjSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(GobjSchemas.Template);
            registry.RegisterSchema(GobjSchemas.Lock);

            registry.RegisterValidationRule(new GobjTypeDataFieldGroupRule());
            // ADR-0019 F1c 退役：GobjOnUseKindRule/GobjLockRequirementFieldGroupRule 的全部检查项
            // 已被 GobjSchemas.OnUseSchema/RequirementSchema 的 Variants 登记（variant_discriminator/
            // required_field/field_type 内建校验）完全覆盖，整条删除，见 gobj/schema/README.md
            // "退役规则"一节。GobjTypeDataFieldGroupRule 不退役——type_data 判别字段 kind 与
            // type_data 本身不同级，Variants 不适用，见 GobjSchemas.TypeDataSchema 判断记录。
            // P2-03 根治：退役时遗漏 world_flag.expected 的必填/形状校验（Variants 登记表达不了
            // 联合类型），补一条窄职责规则，见 GobjLockWorldFlagExpectedRule 判断记录。
            registry.RegisterValidationRule(new GobjLockWorldFlagExpectedRule());
        }
    }
}
