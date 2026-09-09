using System;
using System.Collections.Generic;
using Core.Carriers.Assembly;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.Achievement;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Dialog;
using Core.Gameplay.Difficulty;
using Core.Gameplay.Economy;
using Core.Gameplay.Encounter;
using Core.Gameplay.Loot;
using Core.Gameplay.Quest;
using Core.Gameplay.Spawn;
using Core.Gameplay.WorldState;
using Core.Rules.ExprHost;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// L0～L4 全部 <see cref="TableSchema"/>/<see cref="IValidationRule"/> 的统一注册清单（阶段 3
    /// 集成收尾"事项一"，<see cref="CarriersSchemaCatalog"/> 在 L4 层的延续——同一惯例：调用方
    /// （游戏层引导代码、集成测试、<c>toolchain/validator</c>）在构造 <see cref="DataRegistry"/> 之后、
    /// <see cref="IDataRegistry.LoadAll"/> 之前调用 <see cref="RegisterAll"/> 一次即可注册齐全全部十张
    /// L4 表 + 沿用的 L0～L3 全部表，不需要自己在各模块的 <c>*Schemas</c> 类之间逐一枚举。
    /// <para>
    /// 判断记录（登记顺序）：先 <see cref="CarriersSchemaCatalog.RegisterAll"/>（L0～L3），再逐个登记
    /// L4 十张表——<c>world.flag_schema</c>（<see cref="CarriersSchemaCatalog"/> 顶部判断记录明确点名
    /// "L4 数据装配根应在 RegisterAll 之后自行追加"，本类正是那个"L4 数据装配根"）、
    /// <c>loot.table</c>、<c>econ.currency</c>/<c>econ.vendor</c>、<c>quest.def</c>、
    /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c>、<c>encounter.def</c>/<c>encounter.level</c>、
    /// <c>diff.tier</c>、<c>achv.def</c>、<c>area.trigger_def</c>、<c>spawn.table</c>。
    /// <c>world.map</c> 已由 <c>core/foundation/scene_router</c>（经
    /// <see cref="Core.Rules.Assembly.RulesSchemaCatalog.RegisterL0Schemas"/>）登记，本类不重复登记
    /// （见该类型判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="IValidationRule"/> 构造参数里用到的 <see cref="IExprSchema"/>）：多个模块
    /// 的校验规则/解析器需要一份能识别 <c>quest</c>/<c>player</c>/<c>world</c>/<c>event</c> 分组的
    /// 登记表（<see cref="QuestExprSchemaEntries.BuildParsingSchema"/>——已经合并了
    /// <c>WorldExprSchemaEntries</c> 且对 <c>event</c> 分组宽松放行，本类不再自建一份平行的
    /// "完整组合" schema）；<see cref="EncounterContentValidationRule"/> 的非空 <c>exprSchema</c>
    /// 参数同样传入该登记表。<see cref="CreateOptions"/> 把
    /// <see cref="DataRegistryOptions.ExprSchema"/> 设为同一份，保证"内容校验期"与"运行期
    /// <see cref="GameplayAssembly"/> 装配用的 <see cref="RulesAssembly.ExprSchema"/>"三者一致
    /// （ADR-0015 决策 3）。
    /// </para>
    /// </summary>
    public static class GameplaySchemaCatalog
    {
        /// <summary>
        /// 供内容校验期/运行期共用的完整组合 <see cref="IExprSchema"/>：
        /// <see cref="QuestExprSchemaEntries.BuildParsingSchema"/>（quest/player/world/event 四分组）
        /// 与 <see cref="RulesExprSchema.Base"/>（self/target/combat/enemies/time 五分组）经
        /// <see cref="CompositeExprSchema"/> 合并——九个分组悉数覆盖，供 <see cref="CreateOptions"/>、
        /// <see cref="RegisterAll"/> 内各模块校验规则、<see cref="GameplayAssembly"/> 装配运行期
        /// <c>ExprHostFactory</c> 三处共用同一份签名登记（同 <c>core/rules/assembly.RulesSchemaCatalog</c>
        /// "校验期与运行期须用同一份登记表"判断记录）。
        /// </summary>
        public static IExprSchema FullExprSchema { get; } =
            new CompositeExprSchema(QuestExprSchemaEntries.BuildParsingSchema(), RulesExprSchema.Base);

        /// <summary>
        /// 判断记录：<see cref="DataRegistryOptions.ExprSchema"/> 不由 <see cref="RegisterAll"/> 设置
        /// （惯例同 <see cref="Core.Rules.Assembly.RulesSchemaCatalog.CreateOptions"/> 判断记录——
        /// <see cref="IDataRegistry"/> 契约不暴露构造期传入的 <see cref="DataRegistryOptions"/> 实例）。
        /// 调用方用本方法构造 <see cref="DataRegistryOptions"/>，把 <c>ExprSchema</c> 设为
        /// <see cref="FullExprSchema"/>——阶段 3 落地计划要求"<c>CreateOptions()</c> 把 ExprSchema 设为
        /// 完整组合 schema"，与 L2/L3 只覆盖五分组的 <see cref="Core.Rules.Assembly.RulesSchemaCatalog.CreateOptions"/>
        /// 不同，本方法覆盖全部九个分组。
        /// </summary>
        public static DataRegistryOptions CreateOptions()
        {
            return new DataRegistryOptions { ExprSchema = FullExprSchema };
        }

        /// <summary>
        /// 注册 L0～L3（经 <see cref="CarriersSchemaCatalog.RegisterAll"/>）+ L4 十张表的全部
        /// <see cref="TableSchema"/> 与 <see cref="IValidationRule"/>。不调用
        /// <see cref="IDataRegistry.LoadAll"/>——加载时机由调用方决定。
        /// </summary>
        /// <param name="registry">目标注册表。</param>
        /// <param name="itemBudgetCurveId">透传给 <see cref="CarriersSchemaCatalog.RegisterAll"/>，见该方法同名参数。</param>
        /// <param name="creatureTemplateQuery">
        /// <see cref="SpawnSummonOnlyCreatureRule"/> 构造用（校验 <c>spawn.table.content_ref</c> 指向
        /// <c>creature.template</c> 时该模板不是"仅供召唤"的生物）；未提供时该规则退化为不做此项检查
        /// （见该规则构造参数可空的判断记记录，本方法不强制调用方在注册期就已经有一个真正接入数据的
        /// <see cref="ICreatureTemplateQuery"/> 实例——注册通常发生在 <see cref="GameplayAssembly"/>
        /// 构造之前，此时 <c>CreatureFactory</c> 尚不存在）。
        /// </param>
        public static void RegisterAll(
            IDataRegistry registry, Id? itemBudgetCurveId = null, ICreatureTemplateQuery? creatureTemplateQuery = null)
        {
            CarriersSchemaCatalog.RegisterAll(registry, itemBudgetCurveId);

            var exprSchema = FullExprSchema;

            RegisterWorldStateSchemas(registry);
            RegisterLootSchemas(registry);
            RegisterEconomySchemas(registry);
            RegisterQuestSchemas(registry, exprSchema);
            RegisterDialogSchemas(registry, exprSchema);
            RegisterEncounterSchemas(registry, exprSchema);
            RegisterDifficultySchemas(registry);
            RegisterAchievementSchemas(registry);
            RegisterAreaTriggerSchemas(registry);
            RegisterSpawnSchemas(registry, creatureTemplateQuery);
            RegisterTimeFieldConsistencyRule(registry);
        }

        private static void RegisterWorldStateSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(WorldStateSchemas.FlagSchema);
            // world.flag_schema 只文档化，不注册 IValidationRule（见该表 schema 类型注释判断记录）。
        }

        private static void RegisterLootSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());
        }

        private static void RegisterEconomySchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
        }

        private static void RegisterQuestSchemas(IDataRegistry registry, Core.Foundation.Expr.IExprSchema exprSchema)
        {
            registry.RegisterSchema(QuestSchemas.Def);
            registry.RegisterValidationRule(new QuestContentValidationRule(exprSchema));
        }

        private static void RegisterDialogSchemas(IDataRegistry registry, Core.Foundation.Expr.IExprSchema exprSchema)
        {
            registry.RegisterSchema(DialogSchemas.GossipMenu);
            registry.RegisterSchema(DialogSchemas.StoryTree);
            registry.RegisterValidationRule(new DialogContentValidationRule(exprSchema));
        }

        private static void RegisterEncounterSchemas(IDataRegistry registry, Core.Foundation.Expr.IExprSchema exprSchema)
        {
            registry.RegisterSchema(EncounterSchemas.Def);
            registry.RegisterSchema(EncounterSchemas.Level);
            registry.RegisterValidationRule(new EncounterContentValidationRule(exprSchema));
        }

        private static void RegisterDifficultySchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(DifficultySchemas.Tier);
            // diff.tier 无模块专属 IValidationRule（见 DifficultySchemas 类型注释）。
        }

        private static void RegisterAchievementSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(AchievementSchemas.Def);
            registry.RegisterValidationRule(new AchievementContentValidationRule());
        }

        private static void RegisterAreaTriggerSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(AreaTriggerSchemas.TriggerDef);
            // ADR-0019 / F1b：AreaTriggerShapeKindRule（shape.kind 合法性检查）整条退役，由
            // AreaTriggerSchemas.ShapeSchema 登记的 Variants 经 DataRegistry 的
            // variant_discriminator 检查完全覆盖（见 core/gameplay/area_trigger/schema/README.md
            // "退役规则"一节）。
            registry.RegisterValidationRule(new AreaTriggerParamsFieldGroupRule());
        }

        private static void RegisterSpawnSchemas(IDataRegistry registry, ICreatureTemplateQuery? creatureTemplateQuery)
        {
            registry.RegisterSchema(SpawnSchemas.Table);
            registry.RegisterValidationRule(new SpawnRespawnPolicyFieldGroupRule());
            registry.RegisterValidationRule(new SpawnContentRefRule());

            // 判断记录：SpawnSummonOnlyCreatureRule 在 creatureTemplateQuery 为 null 时每次
            // Validate() 都会产出一条 Warning（"未注入 ICreatureTemplateQuery，跳过 summon_only
            // 生物检查"，见该规则类型注释）。RegisterAll 的典型调用时机（本方法、toolchain/validator）
            // 早于 GameplayAssembly/CreatureFactory 构造完成，此时没有真正的查询实现可用；与其注册
            // 一条注定产生 Warning 的规则（污染"0 错误 0 警告"验收标准，见落地计划第 13 节验收 4），
            // 不如干脆不注册——效果与"注册但每次都警告跳过"完全一致（都不做该项检查），只是不产生
            // 噪音诊断。真正能提供该查询的调用方（<see cref="GameplayAssembly"/> 装配完成后，若需要
            // 重新校验数据，可自行用 <see cref="Core.Carriers.Creature.CreatureFactory"/> 再注册一条）
            // 应显式传入非空的 creatureTemplateQuery。
            if (creatureTemplateQuery != null)
            {
                registry.RegisterValidationRule(new SpawnSummonOnlyCreatureRule(creatureTemplateQuery));
            }
        }

        /// <summary>
        /// 04 第 3.1/5 节"时间字段与时间模型一致"，L4 部分：<c>spawn.table.respawn_timer</c>
        /// （<c>respawn_policy: timer</c> 时才有值，见 <c>SpawnRespawnPolicyFieldGroupRule</c>）——
        /// 按 <c>exploration</c> 作用域校验（刷新点计时属于世界探索机制，见
        /// <c>Core.Rules.Assembly.RulesSchemaCatalog</c> 同名方法判断记录"作用域归属拍板"同一惯例）。
        /// L0～L2 范围内的时间字段（<c>skill.*</c>/<c>arch.power_type</c>）由
        /// <see cref="Core.Rules.Assembly.RulesSchemaCatalog"/> 另行登记，两条规则互不重叠、各自
        /// 只认识自己所在层能看到的表名。
        /// </summary>
        private static void RegisterTimeFieldConsistencyRule(IDataRegistry registry)
        {
            var declarations = new[]
            {
                new Core.Foundation.SimLoop.TimeFieldDeclaration(
                    "spawn.table", "respawn_timer", "exploration",
                    r => r.TryGetNumber("respawn_timer", out var v) ? v : (double?)null),
            };

            registry.RegisterValidationRule(new Core.Foundation.SimLoop.TimeFieldConsistencyRule(declarations));
        }
    }
}
