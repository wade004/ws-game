using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

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
            "floating_text", "play_vfx", "play_sfx", "freeze", "shake_camera", "flash",
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
                }, description: "play_sfx 动作参数：{sfx_id?, from_display?}"),
                ["freeze"] = ParamsCase(new[]
                {
                    new FieldSchema("duration_ms", FieldKind.Number, required: true, description: "须 >= 0，见 FreezeAction 构造函数"),
                }, description: "freeze 动作参数：{duration_ms}，单位毫秒"),
                ["shake_camera"] = ParamsCase(new[]
                {
                    // camera_profile 与本模块同属 L5，但分属两个不同的 Presentation 子目录；判断记录
                    // 同 vfx_id/sfx_id：本模块不预设跨子模块表已加载，退回 Id（不做引用完整性检查）。
                    new FieldSchema("profile_id", FieldKind.Id, required: true, description: "指向 camera_profile（消费方反馈第 30 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "camera_profile"),
                }, description: "shake_camera 动作参数：{profile_id}"),
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

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Binding, FloatingTextStyle };
    }
}
