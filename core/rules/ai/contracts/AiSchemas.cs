using Core.Foundation.DataRegistry;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <c>ai.behavior_profile</c> / <c>ai.rotation</c> / <c>ai.patrol_path</c> 的
    /// <see cref="TableSchema"/>（见 04_数据与内容管线.md 第 1.1 节表清单、
    /// 06_规则层_属性技能战斗AI.md 第 6.1～6.3 节字段列表）。
    /// <para>
    /// 判断记录：04/06 均未给出这三张表的完整字段表（04 只给出表用途一句话描述，06 §6.1 只列出
    /// <c>ai.behavior_profile</c> 字段名，未给类型/必填性；<c>ai.rotation</c>/<c>ai.patrol_path</c>
    /// 字段结构只在 06 §6.2/6.3 以伪代码/散文形式出现）。字段为本任务按任务书拍板给出的最小字段集
    /// 补录，取舍与每个字段的详细说明见 <c>schema/README.md</c>。
    /// </para>
    /// </summary>
    public static class AiSchemas
    {
        private static readonly string[] CombatReturnPolicyValues = { "return_to_spawn", "stay", "patrol" };
        private static readonly string[] PatrolModeValues = { "loop", "pingpong" };

        /// <summary>行为外壳参数：感知半径、巡逻路径引用、脱战规则、优先级表引用（见 06 第 6.1 节）。</summary>
        public static readonly TableSchema BehaviorProfile = new TableSchema(
            name: "ai.behavior_profile",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "ai.profile.<name>"),
                new FieldSchema("perception_radius", FieldKind.Number, required: true,
                    description: "感知半径（idle/patrol -> chase 判定用）"),
                new FieldSchema("patrol_path_ref", FieldKind.Reference, required: false, referenceTable: "ai.patrol_path",
                    description: "巡逻路径引用；缺省表示该单位无巡逻路径，默认态为 idle"),
                new FieldSchema("leash_range", FieldKind.Number, required: true,
                    description: "脱战追击上限距离（chase/flee -> return 判定用）"),
                new FieldSchema("flee_hp_pct_threshold", FieldKind.Number, required: false,
                    description: "血量低于该比例（0~1）且策略允许时 combat -> flee；缺省表示不启用 flee"),
                new FieldSchema("combat_return_policy", FieldKind.Enum, required: true, enumValues: CombatReturnPolicyValues,
                    description: "return 状态到达后的去向：return_to_spawn -> idle；patrol -> patrol；stay -> 脱战瞬间直接 idle，不经过 return 行进"),
                new FieldSchema("rotation_ref", FieldKind.Reference, required: true, referenceTable: "ai.rotation",
                    description: "combat 态执行的优先级表；可经 IAiHost.SetRotation 运行期替换（Boss 阶段换表）"),
                new FieldSchema("decision_interval", FieldKind.Number, required: false,
                    description: "combat 态求值优先级表的节奏（模拟时间单位），缺省 0.5"),
                new FieldSchema("transitions", FieldKind.Object, required: false,
                    description: "Map<转移名, Expr 文本>（键动态，ADR-0019 通用规则 5 不登记子结构）：" +
                        "覆盖默认转移条件（如 idle_to_chase）；未列出的转移名沿用代码内置默认判定，见本模块 README 对照表"),
            });

        /// <summary>优先级表：条件到候选技能的有序列表（见 06 第 6.2 节 <c>RotationEntry</c>）。</summary>
        public static readonly TableSchema Rotation = new TableSchema(
            name: "ai.rotation",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "ai.rotation.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: new FieldSchema("<rotation_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        // AiHost.LoadRotations 对三个字段均直接强转（JsonNumber/JsonString 索引器），
                        // 缺失或类型不符会抛异常，三者均必填（判断记录：偏离方案"priority?"示例，以
                        // 运行时为准）。
                        new FieldSchema("priority", FieldKind.Int, required: true,
                            description: "优先级数值，越小越优先；同表内不得重复，见 AiContentValidationRule"),
                        new FieldSchema("condition", FieldKind.Expr, required: true,
                            description: "Expr 文本，self/target/combat/enemies 分组，见 06 第 6.2 节"),
                        new FieldSchema("skill_id", FieldKind.Reference, required: true, referenceTable: "skill.def",
                            description: "core/rules/ai 与 core/rules/skill 同属 Core.Rules 程序集（同层），登记为 Reference"),
                    },
                        description: "RotationEntry：一条候选技能规则 {priority, condition, skill_id}"),
                    description: "有序 RotationEntry 列表：[{priority: Int, condition: Expr, skill_id: Reference(skill.def)}]；priority 在同一张表内不得重复，见 AiContentValidationRule"),
            });

        /// <summary>巡逻路径：有序 Vec2 列表 + <c>loop</c>|<c>pingpong</c> 模式（见 06 第 6.3 节）。</summary>
        public static readonly TableSchema PatrolPath = new TableSchema(
            name: "ai.patrol_path",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "ai.path.<name>"),
                new FieldSchema("points", FieldKind.Array, required: true,
                    item: new FieldSchema("<point>", FieldKind.Vec2, required: true,
                        description: "路径点坐标 {x, y}"),
                    description: "有序路径点列表：[{x: Number, y: Number}, ...]，至少 2 个点，见 AiContentValidationRule"),
                new FieldSchema("mode", FieldKind.Enum, required: true, enumValues: PatrolModeValues,
                    description: "loop：到终点跳回起点；pingpong：到端点折返方向"),
            });
    }
}
