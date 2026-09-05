using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Numbers.Archetype;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Ai;
using Core.Rules.Combat;
using Core.Rules.ExprHost;
using Core.Rules.Skill;
using Core.Rules.Targeting;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// L0～L2 全部 <see cref="TableSchema"/>/<see cref="IValidationRule"/> 的统一注册清单（落地
    /// 方案 T2-11 集成任务"三、组装根"）。调用方（游戏层引导代码、集成测试）在构造
    /// <see cref="DataRegistry"/> 之后、<see cref="IDataRegistry.LoadAll"/> 之前调用
    /// <see cref="RegisterAll"/> 一次即可注册齐全，不需要自己在四个模块的 <c>Schemas</c> 类之间
    /// 逐一枚举——与 <c>core/numbers/tests/L1SampleDataTests.cs</c>
    /// <c>BuildWorld</c>（L1 五模块的等价手工清单）分工一致，本类把清单从"每个测试各自抄一遍"
    /// 收敛成"全架构一份"。
    /// </summary>
    public static class RulesSchemaCatalog
    {
        /// <summary>
        /// 判断记录：<see cref="DataRegistryOptions.ExprSchema"/> 不由本方法设置——
        /// <see cref="IDataRegistry"/> 契约（<c>RegisterSchema</c>/<c>DeclareReference</c>/
        /// <c>RegisterValidationRule</c>/<c>LoadAll</c>/<c>Validate</c>/<c>Reload</c>）不暴露
        /// 构造期传入的 <see cref="DataRegistryOptions"/> 实例，本方法拿到的只是
        /// <see cref="IDataRegistry"/> 接口，没有回写 Options 的入口。调用方必须在自己构造
        /// <see cref="DataRegistryOptions"/>、进而构造 <see cref="DataRegistry"/> 时就把
        /// <c>ExprSchema</c> 设为 <see cref="RulesExprSchema.Base"/>——本类提供
        /// <see cref="CreateOptions"/> 作为"推荐默认值"的便捷工厂方法，调用方可以直接用它，或者
        /// 自己 new 一份、只把 <c>ExprSchema</c> 字段抄过去。<see cref="RulesAssembly"/> 的 README
        /// 装配顺序图第 0 步即"调用方用 <see cref="CreateOptions"/> 构造 DataRegistry"。
        /// </summary>
        public static DataRegistryOptions CreateOptions()
        {
            return new DataRegistryOptions { ExprSchema = RulesExprSchema.Base };
        }

        /// <summary>
        /// 注册 L0 内置 + L1 五模块 + L2 四模块的全部 <see cref="TableSchema"/> 与
        /// <see cref="IValidationRule"/>，并 <see cref="IDataRegistry.DeclareReference"/> 已知的
        /// 外键（见本方法末尾"已知外键清单"）。不调用 <see cref="IDataRegistry.LoadAll"/>——加载
        /// 时机由调用方决定（通常紧跟在本方法之后）。
        /// </summary>
        public static void RegisterAll(IDataRegistry registry)
        {
            RegisterL0Schemas(registry);
            RegisterL1Schemas(registry);
            RegisterL2Schemas(registry);
            DeclareKnownReferences(registry);
        }

        private static void RegisterL0Schemas(IDataRegistry registry)
        {
            foreach (var schema in BuiltinSchemas.All)
            {
                registry.RegisterSchema(schema);
            }

            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
            registry.RegisterSchema(InputActionSchema.Table);
            registry.RegisterSchema(DisplaySchemas.Map);
            registry.RegisterSchema(DisplaySchemas.AnimSet);
            registry.RegisterSchema(DisplaySchemas.EquipVisual);
            registry.RegisterSchema(WorldMapSchema.Table);

            // ADR-0013 落地：found.time_model（见 core/foundation/sim_loop/schema/TimeModelSchema.cs）。
            registry.RegisterSchema(TimeModelSchema.Table);
            registry.RegisterValidationRule(new TimeModelValidationRule());
        }

        private static void RegisterL1Schemas(IDataRegistry registry)
        {
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            registry.RegisterSchema(PowerSchemas.PowerType);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            registry.RegisterSchema(ArchSchemas.Class);
            registry.RegisterSchema(ArchSchemas.Race);
            registry.RegisterSchema(ArchSchemas.TalentTree);
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);

            // power_set/faction 没有模块专属校验规则（见 L1SampleDataTests.cs 注释）。
            registry.RegisterValidationRule(new StatDefinitionValidationRule());
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            registry.RegisterValidationRule(new ArchTalentTreeCycleValidationRule());
        }

        private static void RegisterL2Schemas(IDataRegistry registry)
        {
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(CombatSchemas.HitTableConfig);
            registry.RegisterSchema(CombatSchemas.ResistCurve);
            registry.RegisterSchema(TargetSchemas.ChainDef);
            registry.RegisterSchema(AiSchemas.BehaviorProfile);
            registry.RegisterSchema(AiSchemas.Rotation);
            registry.RegisterSchema(AiSchemas.PatrolPath);

            // 8 是 SkillOptions.MaxEffectsPerSkill 的默认值（见该类型注释），MaxEffectsPerSkillRule
            // 的构造参数须与运行期实际生效的上限一致，否则数据校验期允许、运行期却又拒绝
            // （或反过来）。RulesAssembly 若使用非默认 SkillOptions.MaxEffectsPerSkill，调用方
            // 需要自己额外登记一条用同一上限构造的 MaxEffectsPerSkillRule（本方法登记的这一条
            // 仍会生效，两条规则同时跑不冲突，只是多余）。
            registry.RegisterValidationRule(new MaxEffectsPerSkillRule(8));
            registry.RegisterValidationRule(new StackCategoryConflictRule());
            registry.RegisterValidationRule(new EffectKindRegisteredRule());
            registry.RegisterValidationRule(new CastTimeChannelTimeExclusiveRule());
            registry.RegisterValidationRule(new PassiveSkillNoCastTimeRule());

            registry.RegisterValidationRule(new CombatHitTableValidationRule());
            registry.RegisterValidationRule(new CombatResistCurveValidationRule());

            // ChainDefValidationRule 需要"已知目标来源策略名清单"（见 targeting 模块 README 判断
            // 记录 9），这里只需要名字本身（BuiltinTargetStrategies 是静态常量清单），不需要一个
            // 真正接入 IUnitAccess/ISpatialQuery 的运行期 TargetStrategyRegistry/TargetHost 实例——
            // 数据校验与运行期装配（见 RulesAssembly.BuildTargetHost）各自独立构造一份
            // TargetStrategyRegistry，互不共享状态，只是登记的名字集合恰好相同。
            var knownSources = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(knownSources);
            registry.RegisterValidationRule(new ChainDefValidationRule(knownSources.Names));

            registry.RegisterValidationRule(new AiContentValidationRule());
        }

        /// <summary>
        /// 已知外键清单：<see cref="IDataRegistry.DeclareReference"/> 只支持"某表某个标量 Id
        /// 字段整体指向另一张表"（见 <c>L1SampleDataTests.BuildWorld</c> 判断记录——数组/嵌套对象
        /// 字段不可声明，静默跳过不产生校验效果），下面三条都满足这一形状要求：
        /// <list type="bullet">
        /// <item><c>arch.class.primary_stat</c> → <c>stat.definition</c>（L1SampleDataTests 已声明
        /// 过的同一条，本类为独立于 L1 测试的正式登记点重复声明一次，无副作用）。</item>
        /// <item><c>skill.def.target_shape_ref</c> → <c>target.chain_def</c>（06 第 3.1 节字段
        /// 说明"指向 target.chain_def 或直接指向 Shape 定义"两种可能之一，见 skill 模块 README
        /// "设计要点与判断记录"第 1 条；本类按"指向链"这一更常见用法声明引用完整性检查，
        /// 直接引用 Shape 定义的技能数据会被本检查误报，届时需要按字段实际语义调整）。</item>
        /// <item><c>skill.proc_def.trigger_skill</c> → <c>skill.def</c>（06 第 3.4 节
        /// <c>ProcDef.trigger_skill</c> 明确"触发后释放的技能"）。</item>
        /// </list>
        /// </summary>
        private static void DeclareKnownReferences(IDataRegistry registry)
        {
            registry.DeclareReference("arch.class", "primary_stat", "stat.definition");
            registry.DeclareReference("skill.def", "target_shape_ref", "target.chain_def");
            registry.DeclareReference("skill.proc_def", "trigger_skill", "skill.def");
        }
    }
}
