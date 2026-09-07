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
            RulesSchemaCatalog.RegisterAll(registry);

            RegisterItemSchemas(registry, itemBudgetCurveId ?? DefaultItemBudgetCurveId);
            RegisterCreatureSchemas(registry);
            RegisterGobjSchemas(registry);
        }

        private static void RegisterItemSchemas(IDataRegistry registry, Id budgetCurveId)
        {
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.BudgetCurve);
            registry.RegisterSchema(ItemSchemas.Set);
            registry.RegisterSchema(ItemSchemas.Affix);

            registry.RegisterValidationRule(new ItemBudgetValidationRule(budgetCurveId));
            registry.RegisterValidationRule(new ItemWeaponProfileRule());
            registry.RegisterValidationRule(new ItemSetMembershipRule());
            registry.RegisterValidationRule(new ItemStackSizeRule());
            // 相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907，WA 报告"需要说明的取舍"第 3 条）：
            // grants.auras 同一物品内重复登记同一个 aura_def，见 ItemGrantsAurasDuplicateRule 判断记录。
            registry.RegisterValidationRule(new ItemGrantsAurasDuplicateRule());

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
            registry.RegisterValidationRule(new GobjOnUseKindRule());
            registry.RegisterValidationRule(new GobjLockRequirementFieldGroupRule());
        }
    }
}
