using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Presentation.VfxSfx.Contracts;

namespace Presentation.FeedbackBinder.Schema
{
    /// <summary>
    /// 本模块拥有的两张表的 <see cref="TableSchema"/> 登记（见 09_表现层.md 第 6.1、6.2 节）：
    /// <c>feedback.binding</c>、<c>feedback.floating_text_style</c>（同
    /// <c>Presentation.VfxSfx.Schema.VfxSfxSchemas</c> 惯例：只登记 schema，不接入
    /// <c>data/_sample/</c>）。
    /// </summary>
    public static class FeedbackSchemas
    {
        public static readonly string[] ActionKindValues =
        {
            "floating_text", "play_vfx", "play_sfx", "freeze", "shake_camera", "flash", "stop_vfx", "stop_sfx", "play_impact",
        };

        public static readonly string[] FeedbackAttachTargetValues = { "source", "target", "world" };
        public static readonly string[] FromDisplaySourceValues = { "source", "target", "skill" };

        /// <summary><c>feedback.binding.actions[]</c>：判别字段 <c>kind</c> 与 <c>params</c> 同处一个
        /// 对象内，Variants 适用（惯例同 <c>SkillSchemas.EffectsItemSchema</c> 的
        /// "kind + params"记法）。参数表以 <see cref="Presentation.FeedbackBinder.Contracts.FeedbackRule.ParseAction"/>
        /// 为唯一依据。<c>play_vfx</c> 的 <c>vfx_id</c>/<c>from_display</c> 二选一、<c>play_sfx</c>
        /// 同理、<c>flash.target</c> 不得为 <c>world</c>——这些"二选一"/"取值子集"业务判断登记层
        /// 表达不了（<see cref="FieldSchema"/> 没有"至少一个""排除某个枚举取值"的记法），继续由
        /// 各 <see cref="Presentation.FeedbackBinder.Contracts.FeedbackAction"/> 子类构造函数（抛
        /// 异常）承担——不同于 <c>DataRegistry.LoadAll</c> 的优雅收集，<c>FeedbackRule.FromRecord</c>
        /// 本身在遇到非法数据时构造期直接抛异常，本次登记是在它之前新增一道更早、更友好的
        /// <c>ValidationIssue</c> 报告（必填/类型/枚举/引用），不影响也不重复它的行为。</summary>
        public static readonly FieldSchema ActionsItemSchema = new FieldSchema(
            "<action>", FieldKind.Object, required: true, variants: BuildActionVariants(),
            description: "{kind, params}，见 09 第 6.1 节六种动作");

        private static VariantSchema BuildActionVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                ["floating_text"] = ParamsCase(new[]
                {
                    new FieldSchema("style_id", FieldKind.Reference, required: true, referenceTable: "feedback.floating_text_style",
                        description: "指向 feedback.floating_text_style，决定飘字颜色/字号/运动曲线"),
                    new FieldSchema("text_source", FieldKind.String, required: true,
                        description: "field:<name>|literal:<text_key>|amount，见 TextSource.Parse"),
                }, description: "floating_text 动作参数：{style_id, text_source}"),
                ["play_vfx"] = ParamsCase(new[]
                {
                    new FieldSchema("vfx_id", FieldKind.Id, required: false,
                        description: "与 from_display 二选一，至少一个非空（构造期校验）；vfx.def 本任务未定义，退回 Id"),
                    new FieldSchema("from_display", FieldKind.Enum, required: false, enumValues: FromDisplaySourceValues,
                        description: "与 vfx_id 二选一，取 source/target/skill 显示信息里配置的特效"),
                    new FieldSchema("attach", FieldKind.Enum, required: true, enumValues: FeedbackAttachTargetValues,
                        description: "特效挂载目标（source/target/world）"),
                    new FieldSchema("anchor_id", FieldKind.Id, required: false, description: "缺省退化为世界位置播放"),
                }, description: "play_vfx 动作参数：{vfx_id?, from_display?, attach, anchor_id?}"),
                ["play_sfx"] = ParamsCase(new[]
                {
                    new FieldSchema("sfx_id", FieldKind.Id, required: false,
                        description: "与 from_display 二选一，至少一个非空（构造期校验）；sfx.def 本任务未定义，退回 Id"),
                    new FieldSchema("from_display", FieldKind.Enum, required: false, enumValues: FromDisplaySourceValues,
                        description: "与 sfx_id 二选一，取 source/target/skill 显示信息里配置的音效"),
                    new FieldSchema("attach", FieldKind.Enum, required: false, enumValues: FeedbackAttachTargetValues,
                        description: "ADR-0089：音效挂载目标（source/target/world），缺省 world——与本字段新增前的既有行为一致（不跟踪、不建键）；循环音效（sfx.def.loop=true）挂到 source/target 后才能被 stop_sfx 按 (sfx_id, 实体) 定位停止"),
                }, description: "play_sfx 动作参数：{sfx_id?, from_display?, attach?}"),
                ["stop_vfx"] = ParamsCase(new[]
                {
                    new FieldSchema("vfx_id", FieldKind.Id, required: false,
                        description: "与 from_display 二选一，至少一个非空（构造期校验）；按 (vfx_id, attach 解析出的附着实体) 定位由 play_vfx 播放的在播实例并停止，见 ADR-0075"),
                    new FieldSchema("from_display", FieldKind.Enum, required: false, enumValues: FromDisplaySourceValues,
                        description: "与 vfx_id 二选一，取 source/target/skill 显示信息里配置的特效"),
                    new FieldSchema("attach", FieldKind.Enum, required: true, enumValues: FeedbackAttachTargetValues,
                        description: "特效附着的实体（source/target），不得为 world——按 (vfx_id, 实体) 定位需要具体实体，登记层不表达取值子集，运行期由 StopVfxAction 构造函数校验，见 ADR-0075"),
                }, description: "stop_vfx 动作参数：{vfx_id?, from_display?, attach}"),
                ["stop_sfx"] = ParamsCase(new[]
                {
                    new FieldSchema("sfx_id", FieldKind.Id, required: false,
                        description: "与 from_display 二选一，至少一个非空（构造期校验）；按 (sfx_id, attach 解析出的附着实体) 定位由 play_sfx 播放的在播循环实例并停止，见 ADR-0089"),
                    new FieldSchema("from_display", FieldKind.Enum, required: false, enumValues: FromDisplaySourceValues,
                        description: "与 sfx_id 二选一，取 source/target/skill 显示信息里配置的音效"),
                    new FieldSchema("attach", FieldKind.Enum, required: true, enumValues: FeedbackAttachTargetValues,
                        description: "音效附着的实体（source/target），不得为 world——按 (sfx_id, 实体) 定位需要具体实体，登记层不表达取值子集，运行期由 StopSfxAction 构造函数校验，见 ADR-0089"),
                }, description: "stop_sfx 动作参数：{sfx_id?, from_display?, attach}"),
                ["freeze"] = ParamsCase(new[]
                {
                    new FieldSchema("duration_ms", FieldKind.Number, required: true, description: "须 >= 0，见 FreezeAction 构造函数"),
                }, description: "freeze 动作参数：{duration_ms}，单位毫秒"),
                ["shake_camera"] = ParamsCase(new[]
                {
                    // camera_profile 与本模块同属 L5，但分属两个不同的 Presentation 子目录；判断记录
                    // 同 vfx_id/sfx_id：本模块不预设跨子模块表已加载，退回 Id（不做引用完整性检查）。
                    new FieldSchema("profile_id", FieldKind.Id, required: true, description: "震屏 preset id：语义 = 当前相机档（camera_profile）shake_presets 里的 preset id，字段名沿用不改（ADR-0121 第 4 条、ADR-0039）；运行期当前档没有该 preset 时记一条诊断并跳过本次震屏，不抛异常（消费方反馈第 30 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "camera_profile"),
                }, description: "shake_camera 动作参数：{profile_id}"),
                ["play_impact"] = ParamsCase(new[]
                {
                    new FieldSchema("profile_id", FieldKind.Reference, required: false, referenceTable: "feedback.impact_profile",
                        description: "显式反馈包；缺省即 from_feel——取攻击方（缺则受击方）手感表特效组的 impact_profile_ref，再按事件的 impactClass/hitResult/isCrit/isKill 选变体（手感设计/07 第 1 节）"),
                }, description: "play_impact 动作参数：{profile_id?}"),
                ["flash"] = ParamsCase(new[]
                {
                    new FieldSchema("profile_id", FieldKind.Id, required: true, description: "指向 camera_profile 中的震屏/闪光档位（消费方反馈第 30 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "camera_profile"),
                    new FieldSchema("target", FieldKind.Enum, required: true, enumValues: FeedbackAttachTargetValues,
                        description: "只能是 source|target，不得为 world（FlashAction 构造函数校验，登记层不表达取值子集）"),
                }, description: "flash 动作参数：{profile_id, target}"),
            };
            return new VariantSchema("kind", cases);
        }

        private static IReadOnlyList<FieldSchema> ParamsCase(IReadOnlyList<FieldSchema> paramFields, string description) => new[]
        {
            new FieldSchema("params", FieldKind.Object, required: true, fields: paramFields, description: description),
        };

        /// <summary><c>feedback.binding</c>（09 第 6.1 节）。<c>actions</c> 的嵌套结构见
        /// <see cref="ActionsItemSchema"/>。</summary>
        public static readonly TableSchema Binding = new TableSchema(
            name: "feedback.binding",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "feedback.<name>"),
                new FieldSchema("event", FieldKind.Id, required: true, description: "订阅的事件 key（如 combat.damage_dealt），指向 found.event_catalog，见判断记录（跨 domain 登记表不适合用 FieldKind.Reference，本模块的 FeedbackRuleValidator 另行按 EventKeys.All 校验；消费方反馈第 30 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "found.event_catalog"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "Expr 条件文本，见 04 第 6 节；宿主分组含 event（触发事件字段）"),
                new FieldSchema("actions", FieldKind.Array, required: true, item: ActionsItemSchema,
                    description: "有序 FeedbackAction 列表：[{kind: floating_text|play_vfx|play_sfx|freeze|shake_camera|flash, params: {...}}]，见 09 第 6.1 节"),
                new FieldSchema("sync", FieldKind.Enum, required: false, enumValues: new[] { "hit_frame" }, description: "ADR-0017 决策 d：命中帧同步声明，未提供时按 event 是否为 combat.damage_dealt 决定默认值（见 FeedbackRule.Sync 判断记录）"),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "feedback");

        /// <summary><c>feedback.floating_text_style</c>（09 第 6.2 节）。</summary>
        public static readonly TableSchema FloatingTextStyle = new TableSchema(
            name: "feedback.floating_text_style",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "feedback.floating_text_style.<name>"),
                new FieldSchema("color_ref", FieldKind.Id, required: true, description: "颜色标识（正常伤害/暴击/治疗/闪避文案各自配色），由表现层适配代码按约定字符串解析，不对应任何已登记内容表（消费方反馈第 30 条核实，不登记 SoftReferenceTable）"),
                new FieldSchema("size_scale", FieldKind.Number, required: false, description: "相对基础字号的缩放"),
                new FieldSchema("motion_profile", FieldKind.Id, required: false, description: "飘字运动曲线标识（上浮/抖动/聚合等），由表现层适配代码按约定字符串解析，不对应任何已登记内容表（消费方反馈第 30 条核实，不登记 SoftReferenceTable）"),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "feedback");

        private static readonly string[] ImpactClassValues = { "light", "medium", "heavy", "massive" };
        private static readonly string[] ImpactOutcomeValues = { "hit", "crit", "kill", "avoided", "whiff" };

        private static FieldSchema ImpactObject(string name, string description, params FieldSchema[] fields) =>
            new FieldSchema(name, FieldKind.Object, required: false, fields: fields, description: description);

        /// <summary><c>feedback.impact_profile</c>（手感设计/07 第 1 节，打击反馈包）。判断记录：设计里的
        /// <c>variants: Map&lt;(impactClass, outcome), ImpactVariant&gt;</c> 登记为数组，每个元素用 <c>class</c>/<c>outcome</c> 两个字段
        /// 当复合键（键里不出现判定型字段名 <c>impact_class</c>，避免触发呈现型表的"判定型字段名"校验）；
        /// <c>(class, outcome)</c> 不得重复由 <see cref="Presentation.FeedbackBinder.Contracts.ImpactProfile.FromRecord"/> 校验
        /// （登记层表达不了复合键唯一）。<c>outcome</c> 比设计多一个 <c>whiff</c>（挥空）。</summary>
        public static readonly TableSchema ImpactProfile = new TableSchema(
            name: "feedback.impact_profile",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "feedback.impact_profile.<name>"),
                new FieldSchema("variants", FieldKind.Array, required: true,
                    item: new FieldSchema("<variant>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("class", FieldKind.Enum, required: true, enumValues: ImpactClassValues, description: "冲击等级（light/medium/heavy/massive）；事件里的 impactClass 缺项时回落 medium"),
                        new FieldSchema("outcome", FieldKind.Enum, required: true, enumValues: ImpactOutcomeValues, description: "命中结局；crit/kill 缺项回落 hit，avoided/whiff 不回落（回避类不播成功命中的反馈）"),
                        ImpactObject("flash", "闪白（09 Flash 原语）",
                            new FieldSchema("profile_id", FieldKind.Id, required: true, description: "闪白档位 id"),
                            new FieldSchema("target", FieldKind.Enum, required: true, enumValues: new[] { "target", "source" }, description: "闪白对象"),
                            new FieldSchema("sync", FieldKind.Enum, required: false, enumValues: new[] { "immediate", "impact_marker" }, description: "同步方式：immediate（缺省，命中批出时立即闪）或 impact_marker（推迟到闪白对象当前剪辑的 impact 标记，超时没有标记则照常闪）")),
                        ImpactObject("vfx", "命中特效",
                            new FieldSchema("vfx_id", FieldKind.Id, required: true, description: "特效 id（vfx.def）"),
                            new FieldSchema("attach", FieldKind.Enum, required: true, enumValues: new[] { "contact", "target", "source" }, description: "挂接位置；contact 缺接触点时退回目标实体"),
                            new FieldSchema("orient", FieldKind.Enum, required: false, enumValues: new[] { "none", "contact_normal", "world_direction" }, description: "朝向来源，缺省 none"),
                            CurveSchema.BreakpointsField("scale_by_ratio", CurveAxis.Value, required: false,
                                description: "按 amountRatio 缩放特效大小的曲线（x=amountRatio，y=倍率）；缺省按 intensity.ratio_curve")),
                        new FieldSchema("sfx", FieldKind.Array, required: false,
                            item: new FieldSchema("<sfx_layer>", FieldKind.Object, required: true, fields: new[]
                            {
                                new FieldSchema("layer", FieldKind.Enum, required: true, enumValues: SfxFeelLayers.Names, description: "手感音效层"),
                                new FieldSchema("tier", FieldKind.Int, required: false, description: "强度档（1 起）；缺省取攻击方手感表对应的 sfx_*_tier"),
                            }, description: "{layer, tier?}，映射到 sfx.def 行见 sfx.def 的 feel_layer/feel_tier/feel_material"),
                            description: "音效层列表（只引用层与档，不引用资源）"),
                        ImpactObject("camera", "镜头：冲击与震屏",
                            new FieldSchema("impulse_gain", FieldKind.Number, required: false, description: "冲击增益乘数（无量纲，缺省 1）；最终幅度 = 手感表 camera_impulse_gain × 本乘数 × 强度缩放"),
                            new FieldSchema("shake_profile", FieldKind.Id, required: false, description: "可选震屏档（当前相机档 shake_presets 的 id）"),
                            new FieldSchema("decay_ms", FieldKind.Number, required: false, description: "冲击衰减时长（毫秒），缺省 120"),
                            new FieldSchema("zoom_punch", FieldKind.Number, required: false, description: "缩放脉冲峰值（比例，缺省 0 = 不做）：命中瞬间可视范围收窄这么多再在 decay_ms 内回落；需镜头适配层的缩放脉冲能力（ICameraZoomPunch），不支持时忽略")),
                        ImpactObject("rumble", "手柄震动（需 IRumble 能力，不支持或没有设备时静默忽略）",
                            new FieldSchema("strength", FieldKind.Number, required: false, description: "震动强度 0..1，缺省 1"),
                            new FieldSchema("duration_ms", FieldKind.Number, required: false, description: "震动时长（毫秒），缺省 120")),
                        ImpactObject("floating_text", "飘字",
                            new FieldSchema("style_id", FieldKind.Reference, required: true, referenceTable: "feedback.floating_text_style", description: "飘字样式")),
                        ImpactObject("trail", "拖尾作者意图（仅承载；运行期拖尾由剪辑 trail_start/trail_end 标记与手感字段 trail_enabled/trail_ref 驱动）",
                            new FieldSchema("start", FieldKind.Enum, required: true, enumValues: new[] { "active_start", "hit" }, description: "拖尾起点"),
                            new FieldSchema("end", FieldKind.Enum, required: true, enumValues: new[] { "active_end" }, description: "拖尾终点")),
                        ImpactObject("freeze_layers", "顿帧期间冻结的表现层（骨骼/序列帧恒冻，不用声明）",
                            new FieldSchema("particles", FieldKind.Bool, required: false, description: "粒子是否冻结，缺省 false"),
                            new FieldSchema("trail", FieldKind.Bool, required: false, description: "拖尾是否冻结，缺省 false")),
                        ImpactObject("intensity", "幅度类字段缩放",
                            CurveSchema.BreakpointsField("ratio_curve", CurveAxis.Value, required: false,
                                description: "按 amountRatio 缩放幅度的曲线（x=amountRatio，y=倍率）；缺省不缩放"),
                            new FieldSchema("crit_multiplier", FieldKind.Number, required: false, description: "暴击倍率，缺省 1"),
                            new FieldSchema("kill_multiplier", FieldKind.Number, required: false, description: "击杀倍率，缺省 1")),
                    }, description: "一个 (class, outcome) 变体"),
                    description: "变体列表；(class, outcome) 不得重复"),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "feedback");

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Binding, FloatingTextStyle, ImpactProfile };
    }
}
