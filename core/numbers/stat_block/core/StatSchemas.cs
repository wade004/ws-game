using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <c>stat.definition</c>/<c>stat.rating_conversion</c> 的 <see cref="TableSchema"/>
    /// 声明（见 04_数据与内容管线.md 第 1.1 节表清单、schema/README.md 字段表）。调用方在
    /// 构造 <see cref="Core.Foundation.DataRegistry.IDataRegistry"/> 后需要
    /// <c>RegisterSchema(StatSchemas.Definition)</c>/<c>RegisterSchema(StatSchemas.RatingConversion)</c>
    /// 才能加载对应数据文件（本模块不自动注册，注册时机由宿主统一掌控，见 11 第 2 节模块范式）。
    /// </summary>
    public static class StatSchemas
    {
        /// <summary>属性分组枚举合法取值（06 第 1 节、04 第 1.1 节）。<c>resistance</c> 分组是
        /// 00 第 3 节"可选属性维度"的落地历史记法；T-N1-2 起 <see cref="StatHost"/> 的抗性维度判定
        /// 已改用 <c>category == "defense"</c>（见 <see cref="StatHostOptions.EnableResistanceGroup"/>
        /// 判断记录），本字段的取值不再驱动任何运行期行为，只作内容格式约束。
        /// <para>
        /// 分阶段落地计划 T-N1-1（ADR-0030 决策 1，拍板 1）：本字段自 schema 版本 2 起标废弃，由
        /// <see cref="CategoryValues"/> 取代，<b>保留一个版本周期不删</b>。
        /// </para>
        /// <para>
        /// 分阶段落地计划 T-N1-2：<c>StatHost.LoadDefinitions</c> 已改为无条件消费 <c>category</c>，
        /// 不再读取本字段（<c>DataRecord.GetString("group")</c> 调用已删除）——T-N1-1 遗留的
        /// "保持 required:true，等 T-N1-2 放宽"判断记录到此兑现：本字段自本版本起放宽为
        /// <c>required: false</c>，允许"新写只填 category、不填 group"的纯 v2 数据直接通过加载，
        /// 不再需要为兼容旧运行期读取路径而强制内容作者同时维护两套字段。已有的 v1→v2 迁移链
        /// （<c>MigrateDefinitionV1ToV2</c>）与既有 v1 数据（仍提供 <c>group</c>）不受影响——
        /// 放宽必填只影响"是否可以不填"，不影响"填了就必须是合法枚举值"这条既有校验。
        /// </para>
        /// </summary>
        public static readonly string[] GroupValues = { "primary", "secondary", "derived", "resistance" };

        /// <summary>属性类别枚举合法取值（ADR-0030 决策 1；06 第 1.3 节字段表）：决定聚合轮次
        /// （<c>derived</c> 走第二轮）与是否经换算层（<c>percent</c>）。</summary>
        public static readonly string[] CategoryValues = { "primary", "derived", "percent", "defense", "misc" };

        /// <summary>属性作用域枚举合法取值（ADR-0030 决策 5；06 第 1.3 节字段表）：结算管线"目标
        /// 乘区"步骤按来源类别只读取匹配作用域的减免/被暴击减免属性。</summary>
        public static readonly string[] ScopeValues = { "any", "from_player", "from_creature" };

        /// <summary><c>stat.definition.derived_from</c> 数组元素结构（ADR-0030 决策 1；06 第 1.3 节
        /// 字段表 <c>List&lt;{stat: Id, coefficient: Number}&gt;</c>）：<c>stat</c> 字段名取自该字段表
        /// 原文（不是任务派发提示词里的非正式命名 "source"——以契约原文为准，见本类型顶部判断记录）。
        /// <c>coefficient</c> 按拍板 11 只登记有限性（<see cref="FieldKind.Number"/> 字段自动经
        /// <c>field_finite</c> 检查，见 <c>DataRegistry.ValidateFieldValue</c>），不登记 <c>WithRange</c>
        /// 下界——允许负数（反向派生是合法设计）。</summary>
        private static readonly FieldSchema DerivedFromEntrySchema = new FieldSchema(
            "<derived_from_entry>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                    description: "派生来源属性，指向 stat.definition（取该属性本轮聚合后的最终值参与计算，两轮拓扑序聚合见 06 第 1.1 节）"),
                new FieldSchema("coefficient", FieldKind.Number, required: true,
                    description: "派生系数：本属性基础值 = Σ(来源属性最终值 × coefficient)；允许负数（拍板 11），" +
                        "只登记有限性（field_finite 自动检查），不登记下界"),
            },
            description: "{stat:Reference(stat.definition), coefficient:Number}，一条派生来源与系数（ADR-0030 决策 1）");

        /// <summary><c>stat.definition.clamp</c> 结构（ADR-0030 决策 1，拍板 2）：取代原平级
        /// <c>min</c>/<c>max</c>，夹取发生在两轮聚合中该属性自己完成三段式聚合之后
        /// （T-N1-2 落地：<c>StatHost.ComputeFinal</c> 已读取本字段并在聚合末尾夹取，见该方法
        /// 判断记录）。</summary>
        private static readonly FieldSchema ClampSchema = new FieldSchema(
            "clamp", FieldKind.Object, required: false, fields: new[]
            {
                new FieldSchema("min", FieldKind.Number, required: false,
                    description: "最终值下限（可空），聚合结束后夹取（拍板 2）"),
                new FieldSchema("max", FieldKind.Number, required: false,
                    description: "最终值上限（可空），聚合结束后夹取（拍板 2）"),
            },
            description: "{min?:Number, max?:Number}，最终值取值区间（06 第 1.3 节字段表；v1 平级 " +
                "min/max 经 1→2 迁移嵌套进本字段，拍板 2）");

        public static TableSchema Definition { get; } = new TableSchema(
            name: "stat.definition",
            primaryKey: "id",
            currentSchemaVersion: 2,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "属性 id，stat.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键"),
                new FieldSchema("category", FieldKind.Enum, required: true, enumValues: CategoryValues,
                    description: "类别：primary/derived/percent/defense/misc，决定聚合轮次（derived 第二轮）" +
                        "与是否经换算层（percent）（06 第 1.3 节字段表，ADR-0030 决策 1）；v1 数据经 1→2 迁移" +
                        "由 group 映射而来（resistance→defense 为拍板 1 明文规定，primary/derived 恒等，" +
                        "secondary 按 is_rating 取值分裂为 percent/misc，见 MigrateDefinitionV1ToV2 判断记录）"),
                new FieldSchema("derived_from", FieldKind.Array, required: false, item: DerivedFromEntrySchema,
                    description: "Array<{stat:Reference(stat.definition), coefficient:Number}>，仅 " +
                        "category=derived 时登记，派生来源与系数（ADR-0030 决策 1；派生无环校验见 " +
                        "StatDefinitionDerivationCycleValidationRule，T-N1-2 新增；非 derived 类别登记本字段报 " +
                        "Error，见 StatDefinitionValidationRule）"),
                ClampSchema,
                new FieldSchema("conversion_ref", FieldKind.Reference, required: false,
                    referenceTable: "stat.rating_conversion",
                    description: "换算曲线引用，仅 category=percent 时有意义，缺省恒等曲线（06 第 1.3 节字段表，" +
                        "ADR-0030 决策 1/3）；v1 数据经 1→2 迁移由 is_rating=true 且带 rating_conversion_ref 时" +
                        "映射而来；非 percent 类别登记本字段报 Error，见 StatDefinitionValidationRule"),
                new FieldSchema("scope", FieldKind.Enum, required: false, enumValues: ScopeValues,
                    description: "作用域：any/from_player/from_creature，缺省 any（06 第 1.3 节字段表，ADR-0030 决策 5）"),
                new FieldSchema("group", FieldKind.Enum, required: false, enumValues: GroupValues,
                    description: "（废弃，v2 起由 category 取代，迁移链自动转换；保留一个版本周期不删，拍板 1；" +
                        "T-N1-2 起放宽为可选——StatHost.LoadDefinitions 已不再读取本字段，见 GroupValues 判断记录）" +
                        "聚合分组：primary/secondary/derived/resistance"),
                new FieldSchema("default_base", FieldKind.Number, required: false,
                    description: "未显式 SetBase 时的基础值，缺省 0"),
                new FieldSchema("min", FieldKind.Number, required: false,
                    description: "（废弃，v2 起由嵌套 clamp.min 取代，迁移链自动转换；保留一个版本周期不删，拍板 2）" +
                        "最终值下限（可空）"),
                new FieldSchema("max", FieldKind.Number, required: false,
                    description: "（废弃，v2 起由嵌套 clamp.max 取代，迁移链自动转换；保留一个版本周期不删，拍板 2）" +
                        "最终值上限（可空）"),
                new FieldSchema("is_rating", FieldKind.Bool, required: false,
                    description: "（废弃，v2 起由 category=percent 取代，迁移链自动转换；保留一个版本周期不删，" +
                        "ADR-0030 决策 1/3）评级换算启用时，本属性是否先过曲线，缺省 false"),
                new FieldSchema("rating_conversion_ref", FieldKind.Reference, required: false,
                    referenceTable: "stat.rating_conversion",
                    description: "（废弃，v2 起由 conversion_ref 取代，迁移链自动转换；保留一个版本周期不删，" +
                        "ADR-0030 决策 1）指向 stat.rating_conversion 的曲线引用，仅 is_rating=true 时有意义"),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "属性说明文本，供编辑器/文档展示，可为空"),
            },
            migrations: new[]
            {
                new TableMigration(1, 2, MigrateDefinitionV1ToV2),
            }).WithOwnership(SchemaLayer.Numbers, "stats");

        /// <summary>
        /// 分阶段落地计划 T-N1-1（ADR-0030 决策 1；落地清单拍板 1/2/11）：<c>stat.definition</c> 1→2
        /// 迁移——纯结构转换，不做业务判断，且<b>不删除旧字段</b>（拍板 1/2"保留、不删"）：旧字段原样
        /// 透传，仅在其存在时补算对应的新字段。三条独立映射：
        /// <list type="bullet">
        /// <item><description><c>group→category</c>：<c>resistance→defense</c> 是拍板 1 明文规定；
        /// <c>primary</c>/<c>derived</c> 与新枚举同名，恒等映射；<c>secondary</c> 契约未给出显式映射——
        /// 旧 <c>group</c> 与 <c>is_rating</c> 在 v1 是两个互相独立的字段，"secondary" 语义上横跨新分类里
        /// 的 <c>percent</c>（评级换算候选，如 crit_rating/dodge_rating）与 <c>misc</c>（如
        /// move_speed）两种，本迁移按 <c>is_rating</c> 取值二选一分裂（true→percent，否则→misc），
        /// 已在任务汇报标注"待设计层确认"，不视为契约条文。</description></item>
        /// <item><description><c>min</c>/<c>max→clamp{min,max}</c>：拍板 2，仅当至少一个存在时才生成
        /// <c>clamp</c> 对象（两者都缺失时不产生空 <c>clamp</c>）。</description></item>
        /// <item><description><c>is_rating=true</c> 且带 <c>rating_conversion_ref</c> 时
        /// →<c>conversion_ref</c>：与 <c>StatDefinitionValidationRule.CheckConversionRefRequiresPercentCategory</c>
        /// 组合使用时，要求内容作者保证"<c>is_rating=true</c> 的属性"与"<c>group</c> 映射后落在
        /// <c>percent</c> 类别"两者一致（即 <c>group</c> 应为 <c>secondary</c>）——这正是"评级换算候选
        /// 属性理应归入 secondary 分组"这条既有内容惯例（见 data/_sample 样例 crit_rating/dodge_rating）
        /// 在新模型下的必然要求，不是本迁移新增的约束。</description></item>
        /// </list>
        /// </summary>
        private static JsonObject MigrateDefinitionV1ToV2(JsonObject row)
        {
            var builder = new JsonObjectBuilder();
            foreach (var entry in row)
            {
                builder.Add(entry.Key, entry.Value);
            }

            if (row.TryGetValue("group", out var groupRaw) && groupRaw is JsonString groupStr)
            {
                string category;
                switch (groupStr.Value)
                {
                    case "resistance":
                        category = "defense";
                        break;
                    case "primary":
                        category = "primary";
                        break;
                    case "derived":
                        category = "derived";
                        break;
                    case "secondary":
                        var isRatingCandidate = row.TryGetValue("is_rating", out var candidateRaw)
                            && candidateRaw is JsonBool candidateBool && candidateBool.Value;
                        category = isRatingCandidate ? "percent" : "misc";
                        break;
                    default:
                        // 不识别的旧 group 取值（理论上不可能——v1 GroupValues 只有以上四种，加载期
                        // 已经过 field_type/枚举校验）兜底为 misc，不阻断迁移本身，让字段级校验按
                        // CategoryValues 正常报告（迁移函数不做业务判断，异常输入交给后续校验处理）。
                        category = "misc";
                        break;
                }
                builder.Add("category", new JsonString(category));
            }

            var hasMin = row.TryGetValue("min", out var minRaw) && minRaw is JsonNumber;
            var hasMax = row.TryGetValue("max", out var maxRaw) && maxRaw is JsonNumber;
            if (hasMin || hasMax)
            {
                var clampBuilder = new JsonObjectBuilder();
                if (hasMin) clampBuilder.Add("min", minRaw);
                if (hasMax) clampBuilder.Add("max", maxRaw);
                builder.Add("clamp", clampBuilder.Build());
            }

            if (row.TryGetValue("is_rating", out var isRatingRaw) && isRatingRaw is JsonBool isRatingBool && isRatingBool.Value
                && row.TryGetValue("rating_conversion_ref", out var refRaw) && refRaw is JsonString)
            {
                builder.Add("conversion_ref", refRaw);
            }

            return builder.Build();
        }

        /// <summary><c>stat.rating_conversion</c>：点数到百分比的换算曲线。分阶段落地计划 T-N0-4
        /// （落地清单 2.1 C4）：<c>entries</c> 迁移到 04 第 3.6 节通用断点表形态 <c>{x, y}</c>
        /// （横轴语义 <see cref="CurveAxis.Level"/>，<c>x</c> = 单位等级，<c>y</c> = 该等级下每 1% 效果
        /// 所需点数），schema 版本 1→2，迁移环节把 v1 的 <c>{level, points_per_percent}</c> 逐元素改名。
        /// <c>y &gt; 0</c> 的范围登记沿用 v1 对 <c>points_per_percent</c> 的判断记录（ADR-0021：
        /// <c>StatHost.DivideByPointsPerPercent</c> 对 0 特判返回 0 是"内容错误被静默降级"的同款模式，
        /// 负值没有合理含义，提前到加载期拦下）。通用规则 <c>curve_monotonic_finite</c> 自动覆盖本表
        /// （数值总纲第 3 节原则 1：除数随等级不递减）。
        /// <para>
        /// 判断记录（T-N1-3，ADR-0030 决策 3；04 第 3.6 节；数值总纲 01 第 5 节"三种曲线形态"）：新增
        /// 可选 <c>saturation</c> 字段（<see cref="CurveSchema.SaturationField"/>，形态
        /// <see cref="CurveShape.Saturation"/>，子字段 <c>k</c>/<c>cap</c>），与 <c>entries</c> 二选一——
        /// 一条 <c>stat.rating_conversion</c> 记录用哪种形态由内容作者选择（"标准版：等级索引除数"用
        /// <c>entries</c>，"变态版：饱和曲线，除数随等级增长"用 <c>saturation</c>，见
        /// <c>StatRatingConversionValidationRule</c>"恰好二选一"校验）。<c>entries</c> 因此从
        /// <c>required: true</c> 放宽为 <c>required: false</c>——纯放宽必填不改变已合法数据的判定结论
        /// （既有只填 <c>entries</c> 的记录仍然合法），<b>不升级 schema 版本</b>（同
        /// <c>StatSchemas.GroupValues</c> 判断记录"T-N1-2 起放宽为 required:false"先例：放宽必填不是
        /// 破坏性变更，不需要迁移函数）。"恰好二选一"（而不是"两者都可选、都缺省时按恒等"）是因为
        /// 恒等形态由 <c>stat.definition.conversion_ref</c> 缺省本身表达（不引用任何
        /// <c>stat.rating_conversion</c> 记录），已存在的 <c>stat.rating_conversion</c> 记录理应
        /// 明确选择一种非恒等形态，否则该记录本身没有存在意义。
        /// </para></summary>
        public static TableSchema RatingConversion { get; } = new TableSchema(
            name: "stat.rating_conversion",
            primaryKey: "id",
            currentSchemaVersion: 2,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "stat.rating.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: false,
                    description: "断点表 [{x: 单位等级(Int), y: 每 1% 效果所需点数(Number, > 0)}]，按 x 线性插值、" +
                        "越界夹取到端点（04 第 3.6 节通用曲线形态；v1 字段名 level/points_per_percent 经 1→2 迁移改名）；" +
                        "\"标准版：等级索引除数\"形态（数值设计 01 第 5 节），与 saturation 二选一（T-N1-3）",
                    xDescription: "单位等级（StatHostOptions.LevelLookup 查到的等级，不是评级原始值）",
                    yDescription: "该等级下每 1% 效果所需的评级点数，> 0",
                    yRange: FieldRange.Range(min: 0, minExclusive: true)),
                CurveSchema.SaturationField("saturation", required: false,
                    description: "二元饱和形态 {k: Number(>0), cap?: Number(>0, 缺省 1)}：输出 = " +
                        "rawValue / (rawValue + k × 单位等级)，以 cap 封顶（04 第 3.6 节；\"变态版：饱和曲线，" +
                        "除数随等级增长\"形态，数值设计 01 第 5 节；与护甲/抗性减免曲线同形态，ADR-0030 决策 3）；" +
                        "与 entries 二选一（T-N1-3，见本表类型顶部判断记录）"),
            },
            migrations: new[]
            {
                new TableMigration(1, 2, row => CurveSchema.MigrateBreakpointsFieldNames(row, "entries", "level", "points_per_percent")),
            }).WithOwnership(SchemaLayer.Numbers, "stats");
    }
}
