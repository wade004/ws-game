using Core.Foundation.DataRegistry;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <c>prog.level_curve</c> / <c>prog.xp_source</c> / <c>prog.xp_base_curve</c> 的
    /// <see cref="TableSchema"/>（见 01_分层与依赖.md L1 模块表 <c>progression</c> 行、
    /// 04_数据与内容管线.md 第 1.1 节表清单"等级到所需经验、到属性成长系数的曲线表" /
    /// "经验来源 id 到经验值与限制规则"；分阶段落地计划 T-N4-1、[ADR-0033]
    /// (../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 2/3、
    /// 06_规则层_属性技能战斗AI.md 第 2.5 节）。
    /// <para>
    /// 判断记录：04 未给出 <c>prog.level_curve</c>/<c>prog.xp_source</c> 的完整字段表（只有一句话
    /// 描述），字段为实现期按任务书 T2-3 给出的最小字段集补录；T-N4-1 在此基础上补齐 ADR-0033 决策
    /// 2/3 要求的新字段。详细取舍与判断见 <c>schema/README.md</c>。
    /// </para>
    /// <para>
    /// 契约疑点上报（<c>prog.xp_base_curve</c> 在 04 第 1.1 节表清单里的登记行）：04 当前表清单
    /// 尚未出现 <c>prog.xp_base_curve</c> 这一行——落地改动点清单第 10 节拍板 5、分阶段落地计划
    /// 拍板表第 5 条已经拍定表名与归属（<c>prog.xp_base_curve</c>，归 L1 progression），但截至本
    /// 任务执行时 04 正文尚未补这一行（对照 N3 阶段先例：T-N3-2 新增 <c>skill.base_curve</c> 时
    /// 04 表清单已经同一批一起补了行，本次 04 表清单落后于拍板结论一步）。本任务禁止改动
    /// architecture 文档（任务硬性规则 2），按已拍板结论（表名、归属层）登记本表，04 表清单该行
    /// 的补齐留给阶段收尾的"文档回填"批次（既有先例：commit 30a8448 "阶段 N3 合入后的文档回填"）。
    /// </para>
    /// </summary>
    public static class ProgSchemas
    {
        /// <summary><c>prog.xp_source.kind</c> 的合法取值（ADR-0033 决策 3：三种当量来源）。</summary>
        public static readonly string[] XpSourceKindValues = { "kill", "quest", "discovery" };

        public static readonly TableSchema LevelCurve = new TableSchema(
            name: "prog.level_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "等级曲线 id，格式 prog.curve.<name>"),
                new FieldSchema("max_level", FieldKind.Int, required: true,
                    description: "曲线的最大等级，必须等于 entries 的元素个数"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: new FieldSchema("<level_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("level", FieldKind.Int, required: true,
                            description: "等级，从 1 连续到 max_level"),
                        new FieldSchema("xp_to_next", FieldKind.Int, required: true,
                            description: "升到下一级所需经验值"),
                        // growth 是 Map<stat_id, Number>（键为 stat.definition 的 id，动态键）：
                        // ADR-0024 第二批登记（04 第 3.3 节"映射登记"），与 ProgressionHost.ApplyGrowth
                        // 逐键 JsonNumber 解析的形状核对一致。
                        new FieldSchema("growth", FieldKind.Object, required: false,
                            description: "Map<stat_id, Number>，ProgressionHost.ApplyGrowth 逐键读取")
                            .WithMap(MapSchema.ReferenceKeyTable("stat.definition",
                                new FieldSchema("value", FieldKind.Number, required: true,
                                    description: "该等级该属性的成长增量"))),
                        // T-N4-1（ADR-0033 决策 2；06 第 2.5 节"prog.level_curve（每级 xp_to_next、
                        // growth、talent_points）"）：新增可选字段，缺省 0，不需要迁移、不升 schema
                        // 版本（纯新增可选字段）。ProgressionHost 对本字段的消费（写入天赋点余额）
                        // 留 T-N4-2，本任务只登记字段形状。
                        //
                        // 判断记录（2026-09-16 深度复审 D-S1：截至 1.36.0 仍未消费，正文补一句提示）：
                        // 设计层裁定（2026-09-16：采纳）"talent_points 的实际消费（天赋系统接入）留待
                        // 后续任务"——框架侧（ProgressionHost/IProgressionHost）从未读取过本字段，
                        // 运行期填了任何数值都不产生任何效果；此前只有 README.md 判断记录写明这一点，
                        // 正文（本字段描述）没有同步提示，接入方容易误以为框架已经处理了发放，见本
                        // 复审报告 D-S1。
                        new FieldSchema("talent_points", FieldKind.Int, required: false,
                            description: "该级获得的天赋点数，缺省 0（ADR-0033 决策 2）；框架当前只" +
                                "登记与累计该字段本身，运行期不消费——天赋点余额/发放/存档由游戏层" +
                                "自行订阅 progression.level_up 累加并实现（2026-09-16 深度复审 D-S1）")
                            .WithRange(FieldRange.Range(min: 0)),
                    }, description: "单级成长条目"),
                    description: "Array<{level:Int, xp_to_next:Int, growth:Object<stat_id,Number>, " +
                        "talent_points?:Int}>，level 从 1 连续到 max_level（连续性/数量一致性业务判断" +
                        "留在 ProgLevelCurveValidationRule，登记层只表达无条件必填/类型）"),
            }).WithOwnership(SchemaLayer.Numbers, "progression");

        /// <summary>
        /// <c>prog.xp_base_curve</c>：击杀基数曲线——怪物/任务/区域等级到"一只同级普通怪的经验"的
        /// 曲线（分阶段落地计划 T-N4-1，落地改动点清单第 10 节拍板 5"击杀基数曲线表名
        /// <c>prog.xp_base_curve</c>，归 L1 progression"；ADR-0033 决策 3"<c>base_curve_ref</c>
        /// （击杀基数曲线：怪物/任务/区域等级 → 一只同级普通怪的经验）"；06 第 2.5 节"三种来源全部
        /// 以'同级怪当量'计"）。
        /// <para>
        /// 判断记录（表结构）：04 第 3.6 节通用断点表形态，横轴取 <see cref="CurveAxis.Level"/>
        /// （怪物/任务/区域等级，整数横轴，沿用 <c>skill.base_curve</c>/<c>item.armor_curve</c>
        /// 等新曲线表同一惯例：新表，直接按通用断点表形态登记，不需要迁移），纵轴 <c>y</c> 为该
        /// 等级一只普通怪的基础经验值
        /// （<see cref="FieldRange.Range"/> 约束 <c>&gt;= 0</c>，经验值不能为负）。单调有限阻断校验
        /// （<c>curve_monotonic_finite</c>）由 04 第 3.6 节对全部登记为断点表形态的字段统一生效，
        /// 本表登记时自动受约束，不需要专属校验规则（同 T-N2-1 对三条新曲线的判断记录）。
        /// </para>
        /// <para>
        /// 消费实现（<c>ProgressionHost.grantXp</c> 读取本表折算三种来源当量）留 T-N4-2，本任务只
        /// 登记数据形状与 schema 覆盖测试。
        /// </para>
        /// </summary>
        public static readonly TableSchema XpBaseCurve = new TableSchema(
            name: "prog.xp_base_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "击杀基数曲线 id，格式 prog.xp_base_curve.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true,
                    description: "断点表 [{x: 怪物/任务/区域等级(Int), y: 一只同级普通怪的基础经验值(Number)}]，" +
                        "按 x 线性插值、越界夹取到端点（04 第 3.6 节通用曲线形态；ADR-0033 决策 3）",
                    xDescription: "采样点对应的怪物/任务/区域等级",
                    yDescription: "该等级一只同级普通怪的基础经验值，供 prog.xp_source.base_curve_ref 引用",
                    yRange: FieldRange.Range(min: 0)),
            }).WithOwnership(SchemaLayer.Numbers, "progression");

        /// <summary><c>base_curve_ref</c> 字段（拆出为静态字段，供 <see cref="XpSource"/> 与本类
        /// 判断记录互相引用；写法惯例同 <c>SkillSchemas.BaseCurveRefField</c>）。</summary>
        private static readonly FieldSchema XpSourceBaseCurveRefField = new FieldSchema(
            "base_curve_ref", FieldKind.Reference, required: false, referenceTable: "prog.xp_base_curve",
            description: "击杀基数曲线引用（ADR-0033 决策 3）：存在时优先于 base_xp/weight 折算当量" +
                "（消费实现留 T-N4-2，本任务只登记字段与引用完整性）");

        /// <summary><c>level_diff_ref</c> 字段：等级差表引用（ADR-0033 决策 3/5；ADR-0030 决策 6
        /// <c>combat.level_diff_table</c> 的经验系数列）。</summary>
        private static readonly FieldSchema XpSourceLevelDiffRefField = new FieldSchema(
            "level_diff_ref", FieldKind.Reference, required: false, referenceTable: "combat.level_diff_table",
            description: "等级差规则表引用，取其经验系数列（Δ = 目标/来源有效等级 − 攻击者/领取者" +
                "有效等级）；可选——不填时不做等级差折算（消费实现留 T-N4-2）");

        public static readonly TableSchema XpSource = new TableSchema(
            name: "prog.xp_source",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "经验来源 id，格式 prog.xp.<name>"),
                // T-N4-1（ADR-0033 决策 3）：来源分类，三值可选枚举。
                // 判断记录（required: false，未随 base_curve_ref/level_diff_ref/once_key 一起要求
                // 必填）：本任务验收标准显式要求"旧字段兼容 1 组——只有 base_xp/weight 的来源加载
                // 0 error"；ADR-0033/06 第 2.5 节原文把 kind 写在字段表里但未明确"是否对存量数据
                // 必填"，若登记为 required: true 会让现存只有 base_xp/weight 的样例/游戏数据在
                // 未补 kind 前直接加载失败（required_field 报错），与验收标准的"0 error"要求冲突。
                // 设计层待确认：本任务先按 required: false 落地以满足显式验收标准；kind 是否应在
                // 下一个版本周期（拍板 4"保留一个版本周期"到期、base_xp/weight 真正删除时）连带
                // 收紧为必填，留给该阶段任务重新判断。
                new FieldSchema("kind", FieldKind.Enum, required: false, enumValues: XpSourceKindValues,
                    description: "来源分类：kill|quest|discovery（ADR-0033 决策 3）；" +
                        "可选——见本字段上方判断记录（旧数据兼容）"),
                new FieldSchema("base_xp", FieldKind.Int, required: true,
                    description: "该来源单次授予的基础经验值" +
                        "（废弃，base_curve_ref 存在时优先，保留一个版本周期，拍板 4）"),
                new FieldSchema("weight", FieldKind.Number, required: false,
                    description: "省略时按 1 处理（见 IProgressionHost.GrantFromSource）" +
                        "（废弃，base_curve_ref 存在时优先，保留一个版本周期，拍板 4）"),
                XpSourceBaseCurveRefField,
                XpSourceLevelDiffRefField,
                // T-N4-1（ADR-0033 决策 3）：探索类一次性标志键前缀，kind=discovery 时使用
                // （消费实现——世界状态标志判定——留 T-N4-3）。
                new FieldSchema("once_key", FieldKind.String, required: false,
                    description: "一次性标志键前缀，kind=discovery 时使用（ADR-0033 决策 3；" +
                        "消费实现留 T-N4-3）"),
                new FieldSchema("condition", FieldKind.Expr, required: false,
                    description: "本任务只登记字段类型（供未来内容校验 expr_parsable 使用），" +
                        "IProgressionHost 本身不对该字段求值"),
            }).WithOwnership(SchemaLayer.Numbers, "progression");
    }
}
