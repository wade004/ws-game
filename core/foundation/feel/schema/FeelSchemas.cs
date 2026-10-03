using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// <c>feel</c> 数据域的八张表登记（手感设计/05 第 2 节；域名由 ADR-0118 审批）及其共享子结构。
    /// <para>
    /// 判断记录（写入条目的形状）：五张分层表用<b>列表形状</b>的 <c>writes</c> 承载覆盖，每条
    /// <c>{field, op, value}</c>，按判别字段 <c>field</c> 分派到该字段的值类型（Variants，case 由字段登记
    /// <see cref="FeelFields.Default"/> 逐字段生成，新增手感字段只改登记表）。不用"字段名→值"的 JSON 对象，
    /// 因为对象无法表达"同层同字段写两次"这一需要被校验拒绝的错误（05 第 3.3 节），也无法表达操作类型。
    /// 写入值子字段不登记范围（倍数/增量的语义与字段取值范围不同），按操作区分的范围检查由
    /// <see cref="FeelProfileChecker"/> 负责（检查名 <see cref="FeelChecks.ValueOutOfRange"/>）。
    /// </para>
    /// <para>
    /// 判断记录（预设取值形状）：<c>feel.preset.values</c> 是 Object，每个手感字段一个可选子字段，类型、范围与
    /// 元数据（half/group/ops/composition/unit/offhand_stackable）来自字段登记；"预设必须全字段"不是 schema 的
    /// <c>required</c>（预设可 <c>extends</c> 父预设，需沿链合并后判断），由 <see cref="FeelChecks.PresetMissingField"/> 检查。
    /// </para>
    /// </summary>
    public static class FeelSchemas
    {
        private static readonly IReadOnlyList<string> MaturityValues = new[] { "experimental", "validated" };
        private static readonly IReadOnlyList<string> OpValues = new[] { "set", "multiply", "add", "remove" };

        /// <summary>写入条目（<c>writes</c>/<c>offhand_writes</c>/<c>feel_modifiers</c> 数组元素）的结构（框架默认登记）。</summary>
        public static FieldSchema WriteItem { get; } = BuildWriteItem(FeelFields.Default);

        private static FieldSchema BuildWriteItem(FeelFieldSet fields)
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal);
            for (var i = 0; i < fields.Count; i++)
            {
                cases[fields[i].Name] = new[] { fields[i].ToWriteValueSchema() };
            }
            var common = new[]
            {
                new FieldSchema("op", FieldKind.Enum, required: true, enumValues: OpValues,
                    description: "覆盖操作：set 替换、multiply 乘、add 加；remove 只用于列表字段。每个字段允许的操作集合见字段登记"),
            };
            return new FieldSchema("<feel_write>", FieldKind.Object, required: true,
                variants: new VariantSchema("field", cases, common),
                description: "{field: 手感字段名, op: 覆盖操作, value: 该字段类型的值}");
        }

        /// <summary>构造一个写入条目数组字段（每次新建，调用方可以再挂 <c>WithGroup</c> 等元数据）。</summary>
        public static FieldSchema WriteArray(string name, string description, bool required = false) =>
            WriteArray(name, description, WriteItem, required);

        private static FieldSchema WriteArray(string name, string description, FieldSchema item, bool required = false) =>
            new FieldSchema(name, FieldKind.Array, required, item: item, description: description);

        private static FieldSchema Id(string description) =>
            new FieldSchema("id", FieldKind.Id, required: true, description: description);

        private static FieldSchema Description(string text) =>
            new FieldSchema("description", FieldKind.String, required: false, description: text);

        private static FieldSchema Maturity() =>
            new FieldSchema("maturity", FieldKind.Enum, required: false, enumValues: MaturityValues,
                description: "成熟度：未经试玩一律 experimental；validated 需要实验室验收记录（手感设计/05 第 8 节）");

        private static FieldSchema ProfileVersion() =>
            new FieldSchema("profile_version", FieldKind.Int, required: false,
                description: "档案行版本号（迁移旧字段必须有显式转换，沿 ADR-0029/0039）")
                .WithRange(FieldRange.Range(min: 1));

        private static TableSchema Own(TableSchema table) => table.WithOwnership(SchemaLayer.Foundation, "feel");

        private static TableSchema BuildPreset(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Preset,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: BuildPresetFields(fields)));

        private static IReadOnlyList<FieldSchema> BuildPresetFields(FeelFieldSet fields)
        {
            var valueFields = new List<FieldSchema>();
            for (var i = 0; i < fields.Count; i++) valueFields.Add(fields[i].ToFieldSchema());
            return new[]
            {
                Id("预设行 id，如 feel.preset.arpg_responsive"),
                Description("预设说明"),
                Maturity(),
                ProfileVersion(),
                new FieldSchema("extends", FieldKind.Reference, required: false, referenceTable: FeelTables.Preset,
                    description: "父预设（沿 extends 链合并，子覆盖父；必须无环）。预设不允许 extends 以外的引用"),
                new FieldSchema("values", FieldKind.Object, required: true, fields: valueFields,
                    description: "字段取值（只有 set 语义）；预设沿 extends 链合并后必须覆盖全部非可选字段"),
            };
        }

        private static TableSchema BuildArchetype(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Archetype,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("体型原型行 id，如 feel.archetype.heavy"),
                Description("原型说明"),
                Maturity(),
                ProfileVersion(),
                new FieldSchema("body_mass", FieldKind.Enum, required: false, enumValues: new[] { "light", "medium", "heavy" },
                    description: "体量轴（描述性，供工具筛选；数值差异写在 writes 里）"),
                new FieldSchema("stride", FieldKind.Enum, required: false, enumValues: new[] { "short", "medium", "long" },
                    description: "步幅轴（描述性）"),
                new FieldSchema("agility", FieldKind.Enum, required: false, enumValues: new[] { "low", "medium", "high" },
                    description: "敏捷轴（描述性）"),
                new FieldSchema("attack_weight", FieldKind.Enum, required: false, enumValues: new[] { "light", "medium", "heavy" },
                    description: "攻击重量轴（描述性）"),
                WriteArray("writes", "与基础预设的差异写入；武器为主字段只允许 multiply", writeItem),
            }));

        private static TableSchema BuildWeapon(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Weapon,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("武器原型行 id，与 display.weapon_style 同 id 分表（规则层只读本表）"),
                Description("武器说明"),
                Maturity(),
                ProfileVersion(),
                new FieldSchema("family", FieldKind.String, required: true,
                    description: "武器族（姿势集回落链的维度，手感设计/04 第 2 节），如 sword_1h、greatsword"),
                new FieldSchema("auto_attack_timeline_ref", FieldKind.Id, required: false,
                    description: "普通攻击的时间线技能引用（手感设计/03 第 1 节），缺省保持既有挥击计时")
                    .WithSoftReference(table: "skill.def"),
                WriteArray("writes", "主手写入：角色为主字段不得写；攻击期间临时覆盖字段只在动作中生效", writeItem),
                WriteArray("offhand_writes", "作为副手装备时的写入：只允许 offhand_stackable 字段的 add/multiply", writeItem),
                new FieldSchema("timeline_reference", FieldKind.Object, required: false,
                    description: "试调起点参考数值（手感设计/05 第 9 节），不分层、不参与解析，供动作时间线作者起步与实验室对照",
                    fields: new[]
                    {
                        new FieldSchema("startup_ms", FieldKind.Number, required: false, description: "前摇参考时长（毫秒）")
                            .WithRange(FieldRange.Range(min: 0)),
                        new FieldSchema("active_ms", FieldKind.Number, required: false, description: "判定相参考时长（毫秒）")
                            .WithRange(FieldRange.Range(min: 0)),
                        new FieldSchema("recovery_ms", FieldKind.Number, required: false, description: "后摇参考时长（毫秒）")
                            .WithRange(FieldRange.Range(min: 0)),
                        new FieldSchema("lunge_body_heights", FieldKind.Number, required: false, description: "突进距离参考（身高倍数）")
                            .WithRange(FieldRange.Range(min: 0)),
                        new FieldSchema("dodge_cancel_open_progress", FieldKind.Number, required: false,
                            description: "闪避取消窗口从动作进度的哪个比例开始（0～1）")
                            .WithRange(FieldRange.Range(min: 0, max: 1)),
                        new FieldSchema("inflicted_hit_stun_ms", FieldKind.Number, required: false,
                            description: "该武器造成的硬直参考时长（毫秒）；硬直字段 hit_stun_ms 是受击方体型为主，不在武器层分层")
                            .WithRange(FieldRange.Range(min: 0)),
                    }),
            }));

        private static TableSchema BuildCharacter(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Character,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("角色/怪物手感行 id（供单位模板的 feel_ref 字段使用）"),
                Description("说明"),
                Maturity(),
                ProfileVersion(),
                WriteArray("writes", "只写与前几层的差异", writeItem),
            }));

        private static TableSchema BuildAction(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Action,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("动作层手感行 id（供 skill.def.timeline.feel_ref 字段使用）"),
                Description("说明"),
                Maturity(),
                ProfileVersion(),
                WriteArray("writes", "进行中动作的覆盖，动作结束即撤", writeItem),
            }));

        private static TableSchema BuildCalibration(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.Calibration,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("标定行 id（每款游戏一行）"),
                Description("说明"),
                new FieldSchema("base_preset", FieldKind.Reference, required: true, referenceTable: FeelTables.Preset,
                    description: "这款游戏的基础预设（覆盖顺序第 1 层）"),
                Positive("reference_height", "参考身高（世界单位）：身高倍数单位的换算依据"),
                Positive("base_speed", "参考基础移速（世界单位/秒）：速度倍数字段在解析结果绝对值视图里的换算依据，只是参考值；移动的目标速度 = 倍数 × 单位的移动速度属性，不读本字段"),
                Positive("animation_fps", "动画帧率（帧/秒）"),
                Positive("reference_camera_height", "参考镜头高度（世界单位，画面纵向可见范围）：画面高度比例的换算依据"),
                Positive("reference_zoom", "参考镜头缩放（倍率）"),
                Positive("pixels_per_unit", "像素密度（像素/世界单位）"),
                new FieldSchema("marker_tolerance_ms", FieldKind.Number, required: true,
                    description: "标记容差（毫秒）：数据标记与剪辑标记比对的容差")
                    .WithRange(FieldRange.Range(min: 0)),
            }));

        private static FieldSchema Positive(string name, string description) =>
            new FieldSchema(name, FieldKind.Number, required: true, description: description)
                .WithRange(FieldRange.Range(min: 0.0001));

        private static TableSchema BuildMotionModeRules(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.MotionModeRules,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("运动模式规则行 id"),
                Description("说明"),
                new FieldSchema("mode", FieldKind.Enum, required: true, enumValues: FeelMotionModes.Modes,
                    description: "运动模式（手感设计/02 第 3.1 节）"),
                new FieldSchema("accepts_input_displacement", FieldKind.Enum, required: true, enumValues: FeelMotionModes.AcceptsInputValues,
                    description: "接受输入位移：yes/no/by_profile（按手感档案字段，如 action 模式按 action_move_speed_ratio）"),
                new FieldSchema("allows_turn", FieldKind.Enum, required: true, enumValues: FeelMotionModes.AllowsTurnValues,
                    description: "允许转向：yes/no/by_profile（如 action 模式按 action_turn_lock）"),
                new FieldSchema("keeps_momentum_on_exit", FieldKind.Enum, required: true, enumValues: FeelMotionModes.KeepsMomentumValues,
                    description: "退出时保留动量：none 不适用、yes/no、by_profile、restore_previous_mode（顿帧叠加态恢复原模式）"),
            }));

        private static TableSchema BuildTagMap(FeelFieldSet fields, FieldSchema writeItem) => Own(new TableSchema(
            name: FeelTables.TagMap,
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                Id("标签映射行 id（游戏层内容，框架给结构）"),
                Description("说明"),
                new FieldSchema("tag", FieldKind.String, required: true,
                    description: "标签，形如 gender:female、race:orc；单位持有该标签时本行在第 3 层生效"),
                new FieldSchema("archetype_ref", FieldKind.Reference, required: false, referenceTable: FeelTables.Archetype,
                    description: "映射到的体型原型（其 writes 在第 3 层应用）"),
                new FieldSchema("priority", FieldKind.Int, required: false,
                    description: "同层多个标签的应用顺序：(priority, id) 升序，后应用者的 set 胜出，缺省 0"),
                WriteArray("writes", "标签直接携带的覆盖（与 archetype_ref 的 writes 同处第 3 层，不得重复写同一字段）", writeItem),
            }));

        // ---------- 成熟度验证记录表（手感设计/05 第 8 节、06 第 6 节；ADR-0146）----------

        private static readonly string[] ScoreDimensions = { "move", "turn", "hit_weight", "combo" };

        private static TableSchema BuildValidation(FeelFieldSet fields, FieldSchema writeItem)
        {
            var scoreFields = new List<FieldSchema>();
            var labels = new[] { "移动", "转向", "命中重量", "连招衔接" };
            for (var i = 0; i < ScoreDimensions.Length; i++)
            {
                scoreFields.Add(new FieldSchema(ScoreDimensions[i], FieldKind.Number, required: true,
                    description: labels[i] + "维度的评分汇总（1～5）；升级门槛是各项不低于 " + FeelMaturity.ScoreThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .WithRange(FieldRange.Range(min: 1, max: 5)));
            }

            return Own(new TableSchema(
                name: FeelTables.Validation,
                primaryKey: "id",
                currentSchemaVersion: 1,
                fields: new[]
                {
                    Id("验证记录行 id（游戏自己的数据，框架不提供任何行）"),
                    Description("说明"),
                    new FieldSchema("profile_ref", FieldKind.Id, required: true,
                        description: "被验证的档案行 id（feel.preset / feel.archetype / feel.weapon / feel.character / feel.action 之一的行，前缀决定表）"),
                    new FieldSchema("profile_version", FieldKind.Int, required: true,
                        description: "验证时该档案行的 profile_version（行缺省为 1）；档案行之后改了版本，这条记录即过期、不再覆盖该行")
                        .WithRange(FieldRange.Range(min: 1)),
                    new FieldSchema("cell", FieldKind.String, required: true,
                        description: "格子 id：实验室场景矩阵的格子（手感设计/06 第 1.1 节，如 2_5d_action）；一个档案行可以只在部分格子验证过"),
                    new FieldSchema("data_root_version", FieldKind.String, required: true,
                        description: "验证时的数据根版本（版本号或内容哈希）"),
                    new FieldSchema("pose_set_version", FieldKind.String, required: true,
                        description: "验证时的姿势集版本"),
                    new FieldSchema("device", FieldKind.String, required: true,
                        description: "设备条件：输入设备与设备类别的自由文本（如 pc_keyboard_mouse、pad、touch），不做枚举"),
                    new FieldSchema("scores", FieldKind.Object, required: true, fields: scoreFields,
                        description: "评分摘要：手感设计/06 第 6 节四个维度各自的汇总分；评分明细只留本地，不入库"),
                    new FieldSchema("date", FieldKind.String, required: true,
                        description: "验证日期，yyyy-mm-dd"),
                    new FieldSchema("note", FieldKind.String, required: false,
                        description: "备注（试玩者人数、已知遗留问题等）"),
                }));
        }

        private sealed class TableSet
        {
            public readonly TableSchema[] Tables;

            public TableSet(FeelFieldSet fields, FieldSchema writeItem)
            {
                Tables = new[]
                {
                    BuildPreset(fields, writeItem), BuildArchetype(fields, writeItem), BuildWeapon(fields, writeItem),
                    BuildCharacter(fields, writeItem), BuildAction(fields, writeItem), BuildCalibration(fields, writeItem),
                    BuildMotionModeRules(fields, writeItem), BuildTagMap(fields, writeItem), BuildValidation(fields, writeItem),
                };
            }
        }

        private static readonly TableSet DefaultTables = new TableSet(FeelFields.Default, WriteItem);

        public static readonly TableSchema Preset = DefaultTables.Tables[0];
        public static readonly TableSchema Archetype = DefaultTables.Tables[1];
        public static readonly TableSchema Weapon = DefaultTables.Tables[2];
        public static readonly TableSchema Character = DefaultTables.Tables[3];
        public static readonly TableSchema Action = DefaultTables.Tables[4];
        public static readonly TableSchema Calibration = DefaultTables.Tables[5];
        public static readonly TableSchema MotionModeRules = DefaultTables.Tables[6];
        public static readonly TableSchema TagMap = DefaultTables.Tables[7];

        /// <summary>成熟度验证记录表 <c>feel.validation</c>（游戏自己的数据；框架不提供任何行）。</summary>
        public static readonly TableSchema Validation = DefaultTables.Tables[8];

        /// <summary>九张表（登记顺序同 <see cref="FeelTables.All"/>），按框架默认字段登记生成。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = DefaultTables.Tables;

        /// <summary>
        /// 按指定字段登记生成九张表（手感设计/05 第 4 节"游戏自有字段"的 schema 扩展位）：预设的 <c>values</c> 子字段与五张分层表的
        /// <c>writes</c> 变体分支都来自 <paramref name="fields"/>，所以游戏登记了自有字段（<see cref="FeelFields.Extend"/>）后，
        /// 数据里就能合法地写它。传框架默认登记得到与 <see cref="All"/> 等价的表。
        /// </summary>
        public static IReadOnlyList<TableSchema> BuildAll(FeelFieldSet fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            return ReferenceEquals(fields, FeelFields.Default) ? All : new TableSet(fields, BuildWriteItem(fields)).Tables;
        }

        /// <summary>
        /// 注册九张表与四条校验规则（<see cref="FeelProfileValidationRule"/>、<see cref="FeelHalfIsolationRule"/>、
        /// <see cref="FeelCalibrationRule"/>、<see cref="FeelMaturityRule"/>）。没有任何 <c>feel.*</c> 行时规则全部静默，既有数据校验结果不变。
        /// <para>
        /// 判断记录（游戏自有字段的注册顺序无关，ADR-0146）：传入游戏扩展后的登记（非框架默认）时，表按该登记生成并登记为这份注册表的字段集，
        /// 之后规则按"这份注册表的字段集"取字段（规则本身按规则 id 去重、先注册者胜出，所以字段集必须在校验时才取）；传框架默认登记
        /// （或缺省）而这份注册表已有扩展登记时，保留扩展登记的表，不被框架目录随后的默认注册覆盖。
        /// </para>
        /// </summary>
        public static void RegisterAll(IDataRegistry registry, FeelFieldSet? fields = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var set = fields ?? FeelFields.Default;
            var custom = !ReferenceEquals(set, FeelFields.Default);
            if (custom) FeelFieldExtensions.Set(registry, set);
            if (custom || !FeelFieldExtensions.Has(registry))
            {
                var tables = BuildAll(set);
                for (var i = 0; i < tables.Count; i++) registry.RegisterSchema(tables[i]);
            }

            registry.RegisterValidationRule(new FeelProfileValidationRule(FeelFieldExtensions.Provider(FeelFields.Default)));
            registry.RegisterValidationRule(new FeelHalfIsolationRule(FeelFieldExtensions.Provider(FeelFields.Default)));
            registry.RegisterValidationRule(new FeelCalibrationRule());
            registry.RegisterValidationRule(new FeelMaturityRule());
        }
    }
}
