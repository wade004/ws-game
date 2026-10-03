using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// <c>found.input_action</c> 表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md
    /// 第 1.1 节、01_分层与依赖.md L0 模块表 <c>input_map</c> 行、本模块
    /// <c>schema/found.input_action.md</c>）。
    /// <para>
    /// 判断记录：表名 <c>found.input_action</c> 的 domain 前缀是 <c>found</c>，但记录本身的
    /// id 域名是 <c>input</c>（见 04 第 2.2 节域名清单 <c>input</c> 行"输入动作"、示例
    /// <c>input.action.primary_attack</c>）——与 <c>found.event_catalog</c> 同理，属于
    /// "found 表登记 input 域的记录"。任务书拍板：按登记表处理，主键字段名为 <c>key</c>
    /// （而不是一般内容表的 <c>id</c>），<see cref="TableSchema.IsRegistryTable"/> = true，
    /// 与 <c>data/README.md</c>"记录主键"一节、<c>core/foundation/data_registry/README.md</c>
    /// "主键规则"一节的登记表约定完全一致。
    /// </para>
    /// </summary>
    public static class InputActionSchema
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "found.input_action",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.Id, required: true,
                    description: "动作 id，如 input.action.move；登记表主键字段名为 key（见类型级判断记录）"),
                new FieldSchema("kind", FieldKind.Enum, required: true,
                    enumValues: new[] { "button", "axis1d", "axis2d" },
                    description: "button：按下/抬起；axis1d：一维轴；axis2d：二维轴（如摇杆/方向输入）"),
                new FieldSchema("default_bindings", FieldKind.Array, required: true,
                    item: new FieldSchema("<binding>", FieldKind.String, required: true,
                        description: "单条默认绑定字符串，语法见 core/foundation/input_map/README.md"),
                    description: "默认绑定字符串数组（ActionDefinition.FromRecord 对非字符串元素抛异常），" +
                        "语法见 core/foundation/input_map/README.md；至少一项，见 WithItemCount（消费方反馈第 60 条根治：此前" +
                        "登记层表达不了非空数组，只能留给 ActionDefinition 构造函数兜底抛异常，现已改用 WithItemCount 登记，" +
                        "由 DataRegistry 通用字段校验的 field_item_count 检查项在加载期报告；构造函数的防御性检查予以保留，" +
                        "覆盖绕过 DataRegistry 校验直接构造的调用路径，不做静默降级）")
                    .WithItemCount(1),
                new FieldSchema("rebind_group", FieldKind.String, required: false,
                    description: "重绑分组，缺省视为 \"default\""),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "该输入动作的说明文本，供编辑器/文档展示，可为空"),
                // 手感设计/01 第 2.1 节（手感落地第 1 波 S1）：以下七个字段全部可选、纯加法；既有数据一行不改仍然合法，
                // 且缺省值保证既有输入行为逐位不变（未声明 class 的动作不经输入缓冲，见 ActionDefinition.Class 判断记录）。
                new FieldSchema("class", FieldKind.Enum, required: false,
                    enumValues: new[] { "move", "attack", "skill", "dodge", "interact", "item", "menu", "jump" },
                    description: "动作类别（取消窗口、优先级、连招按类别工作）；缺省表示不经输入缓冲（既有行为）；move 为轴类，不入缓冲；" +
                        "jump 为跳跃（接受时是对竖直轴能力包的起跳请求而不是施法，装配了竖直轴才生效，优先级介于 dodge 与 attack 之间）"),
                new FieldSchema("buffer_ms", FieldKind.Number, required: false,
                    description: "缓冲窗口（毫秒）；缺省取手感档案输入组 buffer_ms（无手感档案时为 0）；0 表示不缓冲，只在按下当 tick 有效")
                    .WithRange(FieldRange.Range(min: 0, max: 1000)),
                new FieldSchema("priority", FieldKind.Int, required: false,
                    description: "同 tick 多条待消费意图的排序与槽满替换依据（越大越优先）；缺省按类别（dodge > attack = skill > item > interact > menu）"),
                new FieldSchema("hold_threshold_ms", FieldKind.Number, required: false,
                    description: "按住阈值（毫秒）：声明后区分点按与按住（抬起早于阈值为点按，否则为按住，蓄力类动作用）；缺省不声明")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true, max: 5000)),
                new FieldSchema("repeat_policy", FieldKind.Enum, required: false,
                    enumValues: new[] { "refresh", "ignore" },
                    description: "同一动作在缓冲未过期时再次按下：refresh 刷新过期时刻（缺省），ignore 忽略本次按下"),
                new FieldSchema("face_on_accept", FieldKind.Bool, required: false,
                    description: "被接受时是否把行动者朝向对齐到按下瞬间的移动轴方向（无轴输入则保持当前朝向）；缺省按类别（attack/skill/dodge 为真）"),
                new FieldSchema("grace_conditions", FieldKind.IdList, required: false, referenceTable: "found.grace_condition",
                    description: "宽限窗口适用的条件名（found.grace_condition 的 key）：条件刚失效后的 grace_ms 内仍视为满足；缺省为空"),
                // 手感落地 S10（生产装配接线）：输入动作 -> 技能的映射。设计文档没有给出这一层，数据里此前也没有，
                // 缓冲被接受之后"该发哪个技能"没处可查，因此加一个可选字段指向技能绑定槽位名（见 SkillBindingHost）。
                new FieldSchema("skill_slot", FieldKind.String, required: false,
                    description: "缓冲接受后要施放的技能所在的技能绑定槽位名（与 SkillBindingHost 的 slot 同名，如 slot_0）；" +
                        "缺省表示该动作不映射技能（装配层的缓冲出口对它不发 cast 意图）"),
                // 控制空间（相机相对输入，第三人称/俯角镜头）：可选加法字段，缺省 world = 轴值原样当世界方向（既有行为逐位不变）。
                new FieldSchema("control_space", FieldKind.Enum, required: false,
                    enumValues: new[] { "world", "camera_relative" },
                    description: "轴动作的控制空间（仅 axis2d 有意义）：world 轴值原样当世界方向（缺省）；camera_relative 轴值按当前相机偏航换算成世界方向，" +
                        "要求宿主给输入映射配相机朝向查询（ICameraOrientation）"),
                // ADR-0143（手感落地 M5-S1）：以下字段全部可选、纯加法，缺省行为逐位不变。
                new FieldSchema("dead_zone", FieldKind.Number, required: false,
                    description: "模拟轴死区（仅 axis 动作，仅手柄绑定）：摇杆向量长度不超过它时输出零，超过部分重标度到 0～1；缺省无死区（原始值直通）。" +
                        "归设备/玩家设置：这里是默认值，玩家设置可覆盖（InputMapHost.SetAxisProcessing，随设置文件持久化）")
                    .WithRange(FieldRange.Range(min: 0, max: 1, maxExclusive: true)),
                new FieldSchema("response_curve", FieldKind.String, required: false,
                    description: "模拟轴响应曲线（仅 axis 动作）：linear（缺省）| expo（幅值平方）| custom:<curve_id>（宿主提供的分段线性曲线，输入输出为 0～1 幅值）"),
                new FieldSchema("smoothing_ms", FieldKind.Number, required: false,
                    description: "模拟轴平滑（仅 axis 动作）：幅值下降时满幅回落到 0 的毫秒数；只作用于幅值的下降沿，上升与方向变化即时生效（一次按下至少一个 tick 满幅）；缺省不平滑")
                    .WithRange(FieldRange.Range(min: 0, max: 5000)),
                new FieldSchema("hold_skill_slot", FieldKind.String, required: false,
                    description: "点按/按住变体：按住释放（holdState 为 hold_released）时施放的技能所在的技能绑定槽位名；点按走 skill_slot；缺省两者同一技能。" +
                        "点按与按住的分界只有一个：hold_threshold_ms（attack/skill 类缺省取档案按住阈值）"),
                new FieldSchema("jump_cut_ratio", FieldKind.Number, required: false,
                    description: "可变跳高（仅 jump 类）：起跳后松键（或起跳时已松键）若仍在上升，上升速度乘以该比例；缺省不裁切")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true, max: 1, maxExclusive: true)),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true)
            .WithOwnership(SchemaLayer.Foundation, "foundation");

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记
        /// （本模块目前只有一张表，保留该属性与其它模块的 <c>*Schemas.All</c> 惯例一致）。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Table };
    }
}
