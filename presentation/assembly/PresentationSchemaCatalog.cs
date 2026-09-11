using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.Expr;
using Core.Foundation.Localization;
using Core.Gameplay.Assembly;
using Core.Carriers.Creature;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Rules.Skill;
using Presentation.Camera.Schema;
using Presentation.FeedbackBinder.Schema;
using Presentation.VfxSfx.Schema;

namespace Presentation.Assembly
{
    /// <summary>
    /// L0～L5 全部 <see cref="TableSchema"/>/<see cref="IValidationRule"/> 的统一注册清单（阶段 4
    /// 收敛 B，<see cref="GameplaySchemaCatalog"/> 在 L5 表现层的延续——同一惯例：调用方（表现层
    /// 组装代码、集成测试、<c>toolchain/validator</c>）在构造 <see cref="DataRegistry"/> 之后、
    /// <see cref="IDataRegistry.LoadAll"/> 之前调用 <see cref="RegisterAll"/> 一次即可注册齐全全部
    /// L5 表现层表 + 沿用的 L0～L4 全部表，不需要自己在 <see cref="GameplaySchemaCatalog"/> 与各
    /// 表现层模块的 <c>*Schemas</c> 类之间逐一枚举。
    /// <para>
    /// 判断记录（登记顺序与表清单）：先 <see cref="GameplaySchemaCatalog.RegisterAll"/>（L0～L4），
    /// 再登记表现层表——<c>vfx.def</c>/<c>sfx.def</c>/<c>display.weapon_style</c>
    /// （<see cref="VfxSfxSchemas"/>）、<c>feedback.binding</c>/<c>feedback.floating_text_style</c>
    /// （<see cref="FeedbackSchemas"/>）、<c>camera_profile</c>（<see cref="CameraSchemas"/>）、
    /// <c>ui_layout_definition</c>（<see cref="Presentation.Ui.UiSchemas"/>）、
    /// <c>shell_menu_definition</c>（<see cref="Presentation.Shell.ShellSchemas"/>）。
    /// </para>
    /// <para>
    /// 判断记录（<c>display.map</c>/<c>display.anim_set</c>/<c>display.equip_visual</c> 一并登记，
    /// 尽管任务书枚举表现层表清单时未点名）：这三张表在 04 第 7.1、7.1.1、7.1.2 节定义、
    /// <c>core/foundation/display_info</c>（<see cref="DisplaySchemas"/>）已有 schema 类，但在
    /// <see cref="GameplaySchemaCatalog"/>/<c>CarriersSchemaCatalog</c> 里都未被登记过——本类型
    /// 是"L5 数据装配根"，<c>display.map</c> 是表现层解析 DisplayInfo 的唯一入口表（09 第 5.6、7
    /// 节），<c>data/_sample/</c> 也确实需要一份 <c>display.map</c> 示例数据（见任务书步骤三），
    /// 因此在本类型补registrer，不新造 schema、只补登记，符合"复用现有 schema、不发明新原语"的
    /// 取舍原则。<see cref="DisplayKindFieldGroupRule"/>（无构造依赖）一并注册；
    /// <see cref="DisplayMapCoverageRule"/> 需要调用方显式声明"哪些内容表参与外形域覆盖检查"
    /// （构造函数要求 <c>sources</c> 列表），本类型不知道调用方是否已经给全部内容表配齐
    /// <c>display.map</c> 行（不同调用方的接入进度不同），因此仍不在本方法内代为注册——留给
    /// <see cref="ContentValidationAssembly"/> 决定何时接线（见该类型消费方反馈第 34 条判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（消费方反馈第 34 条，<see cref="DefaultDisplayMapCoverageSources"/>）：此前
    /// <see cref="DisplayMapCoverageRule"/> 的 <c>sources</c> 清单要求每个调用方（<c>toolchain/validator</c>
    /// 命令行参数、编辑器基础套件）各自手写 <c>(table, idField)</c> 列表，容易遗漏/口径不一致。
    /// 本类型公开一份固定默认清单——04 第 1.1 节点名"外形映射存在"检查项覆盖"技能、光环、物品、
    /// 生物、物件"五类逻辑对象，对应 <c>skill.def</c>/<c>skill.aura_def</c>/<c>item.template</c>/
    /// <c>creature.template</c>/<c>gobj.template</c> 五张表；各表用于比对 <c>display.map.logical_id</c>
    /// 的字段统一是各自的主键 <c>id</c>（该字段本身就是这五类内容记录的逻辑 id，不是它们各自指向
    /// <c>display.map</c> 的 <c>display_ref</c> 反向字段——两者方向相反，见
    /// <see cref="DisplayMapCoverageRule"/> 类型注释"外形映射存在"检查项定义）。
    /// <see cref="ContentValidationAssembly"/> 未显式指定 <c>DisplayMapCoverageSources</c> 时默认
    /// 使用本清单（该规则从此默认启用，不再是"默认禁用、需要调用方自行接线"）。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="CreateOptions"/> 不新造一份表现层专属 <see cref="IExprSchema"/>）：
    /// <c>feedback.binding.condition</c>（09 第 6.1 节）读取事件携带数据，字段路径形如
    /// <c>event.is_crit</c>/<c>event.school</c>；<see cref="GameplaySchemaCatalog.FullExprSchema"/>
    /// 组合的 <c>QuestExprSchemaEntries.BuildParsingSchema()</c> 已经把 <c>event</c> 分组包装成
    /// "已登记 key 精确匹配，未登记 key 一律放行"（见该方法判断记录，避免 04 第 6.3 节的引用/字面量
    /// 消歧问题的同时不必穷举事件字段），足以覆盖 <c>FeedbackBinder</c> 的条件解析需求，不需要再
    /// 按任务书设想的"若 FeedbackBinder 用了额外分组，用 CompositeExprSchema 合并"——FeedbackBinder
    /// 运行期真正用来求值 <c>condition</c> 的 <see cref="Core.Foundation.Expr.IExprHostFactory"/>
    /// 也应使用同一份 <see cref="GameplaySchemaCatalog.FullExprSchema"/>（见
    /// <c>PresentationAssembly</c> 判断记录），保证"内容校验期"与"运行期"一致（ADR-0015 决策 3 的
    /// 延续）。
    /// </para>
    /// </summary>
    public static class PresentationSchemaCatalog
    {
        /// <summary>供内容校验期/运行期共用的完整组合 <see cref="IExprSchema"/>：直接复用
        /// <see cref="GameplaySchemaCatalog.FullExprSchema"/>（见类型注释判断记录，本层不新增
        /// 分组）。</summary>
        public static IExprSchema FullExprSchema => GameplaySchemaCatalog.FullExprSchema;

        /// <summary><see cref="DisplayMapCoverageRule"/> 的默认 <c>sources</c> 清单，见类型注释
        /// "消费方反馈第 34 条"判断记录。<see cref="ContentValidationAssembly"/> 未显式指定
        /// <see cref="ContentValidationOptions.DisplayMapCoverageSources"/> 时默认使用本清单。</summary>
        public static readonly IReadOnlyList<(string table, string idField)> DefaultDisplayMapCoverageSources = new[]
        {
            (SkillSchemas.Def.Name, "id"),
            (SkillSchemas.AuraDef.Name, "id"),
            (ItemSchemas.Template.Name, "id"),
            (CreatureSchemas.Template.Name, "id"),
            (GobjSchemas.Template.Name, "id"),
        };

        /// <summary>判断记录同 <see cref="GameplaySchemaCatalog.CreateOptions"/>：<see cref="IDataRegistry"/>
        /// 契约不暴露构造期传入的 <see cref="DataRegistryOptions"/> 实例，调用方用本方法构造。</summary>
        public static DataRegistryOptions CreateOptions()
        {
            return new DataRegistryOptions { ExprSchema = FullExprSchema };
        }

        /// <summary>
        /// 注册 L0～L4（经 <see cref="GameplaySchemaCatalog.RegisterAll"/>）+ L5 表现层全部表的
        /// <see cref="TableSchema"/> 与 <see cref="IValidationRule"/>。不调用
        /// <see cref="IDataRegistry.LoadAll"/>——加载时机由调用方决定。
        /// </summary>
        /// <param name="registry">目标注册表。</param>
        /// <param name="itemBudgetCurveId">透传给 <see cref="GameplaySchemaCatalog.RegisterAll"/>。</param>
        /// <param name="creatureTemplateQuery">透传给 <see cref="GameplaySchemaCatalog.RegisterAll"/>。</param>
        public static void RegisterAll(
            IDataRegistry registry, Id? itemBudgetCurveId = null, ICreatureTemplateQuery? creatureTemplateQuery = null)
        {
            GameplaySchemaCatalog.RegisterAll(registry, itemBudgetCurveId, creatureTemplateQuery);

            RegisterDisplayInfoSchemas(registry);
            RegisterLocalizationSchemas(registry);
            RegisterVfxSfxSchemas(registry);
            RegisterFeedbackSchemas(registry);
            RegisterCameraSchemas(registry);
            RegisterUiSchemas(registry);
            RegisterShellSchemas(registry);
        }

        /// <summary>判断记录：<c>l10n.locale</c>/<c>l10n.text</c>（04 第 7.2 节）已有 schema 类
        /// （<see cref="L10nSchemas"/>），但在 <see cref="GameplaySchemaCatalog"/>/
        /// <c>CarriersSchemaCatalog</c> 里都未被登记过——本任务书未点名这两张表，但
        /// <c>presentation/ui</c>/<c>presentation/shell</c> 依赖的 <c>Core.Foundation.Localization.L10nHost</c>
        /// 构造期强制要求 <c>l10n.locale</c> 恰好一条 <c>is_default: true</c> 记录（见该类型构造函数），
        /// <see cref="PresentationAssembly"/> 是第一个真正构造 <c>L10nHost</c> 的组装根，因此本类型
        /// 补上登记（复用既有 schema，不新造）。</summary>
        private static void RegisterLocalizationSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
        }

        private static void RegisterDisplayInfoSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(DisplaySchemas.Map);
            registry.RegisterSchema(DisplaySchemas.AnimSet);
            registry.RegisterSchema(DisplaySchemas.EquipVisual);
            registry.RegisterValidationRule(new DisplayKindFieldGroupRule());
            // ADR-0017 决策 c：display.anim_set.clips[*].events 形状校验（无构造依赖，同
            // DisplayKindFieldGroupRule 一并注册）。
            registry.RegisterValidationRule(new AnimSetEventsShapeRule());
            // DisplayMapCoverageRule 不在此注册，见类型注释判断记录。
        }

        private static void RegisterVfxSfxSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(VfxSfxSchemas.Vfx);
            registry.RegisterSchema(VfxSfxSchemas.Sfx);
            registry.RegisterSchema(VfxSfxSchemas.WeaponStyle);
            // 三张表均无模块专属 IValidationRule（见 presentation/vfx_sfx/README.md"不负责什么"）。
        }

        private static void RegisterFeedbackSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(FeedbackSchemas.Binding);
            registry.RegisterSchema(FeedbackSchemas.FloatingTextStyle);
            // 判断记录：Presentation.FeedbackBinder.Core.FeedbackRuleValidator 不是
            // IValidationRule——它的 Validate(IReadOnlyList<FeedbackRule>, IReadOnlyCollection<Id>)
            // 签名要求先把 feedback.binding 表解析成强类型 FeedbackRule 列表、并显式传入事件目录
            // （EventKeys.All，跨越多个 L2～L4 模块），与 IValidationRule.Validate(IDataRegistryView)
            // 的形状不同，本类型不为其代造一个适配包装（避免发明新原语），留给需要这项校验的调用方
            // （如 PresentationAssembly 或具体游戏的内容校验流程）自行在解析出 FeedbackRule 列表后
            // 调用，见 presentation/feedback_binder/README.md。
        }

        private static void RegisterCameraSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(CameraSchemas.Profile);
            // camera_profile 无模块专属 IValidationRule。
        }

        private static void RegisterUiSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(Presentation.Ui.UiSchemas.UiLayoutDefinition);
            // ui_layout_definition 无模块专属 IValidationRule。
        }

        private static void RegisterShellSchemas(IDataRegistry registry)
        {
            registry.RegisterSchema(Presentation.Shell.ShellSchemas.ShellMenuDefinitionTable);
            // shell_menu_definition 无模块专属 IValidationRule。
        }
    }
}
