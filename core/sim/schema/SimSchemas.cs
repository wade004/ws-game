using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// <c>sim.anchor</c> / <c>sim.scenario</c> 的 <see cref="TableSchema"/> 声明（T-N6-2a，ADR-0035
    /// 决策 4；04 第 1.1 节表清单第 145～146 行；数值总纲第 4.1 节"锚点"）。两表"结构归框架，数归
    /// 游戏"（ADR-0035 决策 4 原文）：本模块只定义结构，不预置任何非示例数据；仅无头仿真与内容工具
    /// （<c>toolchain/validator</c>）读取，运行期宿主（<see cref="Core.Gameplay.Assembly.GameplayAssembly"/>）
    /// 不读——因此不进 <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog"/>，见
    /// <see cref="SimSchemaCatalog"/> 类型注释判断记录。
    /// <para>
    /// 判断记录（<c>SchemaLayer.Sim</c>）：04 第 1.1 节表清单"层"列对这两张表的取值原文是"框架工具
    /// （无头仿真）"，不落在 01 文档 L-1～L5 任何一层——<see cref="SchemaLayer"/> 因此新增
    /// <see cref="SchemaLayer.Sim"/> 成员专供这两张表登记 <see cref="TableSchema.WithOwnership"/>，
    /// 满足 ADR-0022 <c>table_ownership</c> 门禁；不是把它们错记成 <see cref="SchemaLayer.Gameplay"/>
    /// 或新造一层业务含义。
    /// </para>
    /// <para>
    /// 判断记录（<c>sim.anchor</c> 主键为何仍是 <c>id</c> 而不是任务书字面提到的 <c>level</c>）：
    /// <see cref="TableSchema"/> 构造函数硬性要求 <c>primaryKey</c> 取值只能是 <c>"id"</c> 或
    /// <c>"key"</c>（且 <c>"id"</c> 语义上必须是 <c>Core.Foundation.Common.Id</c> 格式的字符串，见
    /// <c>DataRegistry.ParseTable</c> 对主键字段的处理），<c>level</c> 是纯数值、不满足这个格式约束，
    /// 无法直接充当框架意义上的主键字段。落地为：<c>id</c>（<c>sim.anchor.&lt;name&gt;</c>，如
    /// <c>sim.anchor.l1</c>）仍是记录的框架主键，<c>level</c> 是一个独立的必填 Int 字段，"每级一行、
    /// 全表 level 值从 1 起连续无缺口且不重复"这条任务书要求的约束改由 <see cref="SimAnchorValidationRule"/>
    /// 在加载期做表级校验（单条记录内的主键唯一性已经由框架内置的 <c>primary_key</c> 检查覆盖，但那
    /// 覆盖的是 <c>id</c> 不重复，不是 <c>level</c> 不重复——两条不同 <c>id</c> 的记录仍可能手滑填了
    /// 同一个 <c>level</c>，需要额外这条规则）。
    /// </para>
    /// <para>
    /// 判断记录（未登记 <c>FieldUnit.Time</c>）：<c>ttk_seconds</c>/<c>ttd_seconds</c>/
    /// <c>level_duration_seconds</c>/<c>kill_interval_seconds</c> 语义上是"时长"，但 04 第 3.4 节
    /// "时间字段单位与作用域"针对的是运行期真正参与 <c>found.time_model</c> 连续/离散换算、需要在
    /// 离散模式下被强制为整数 tick 的字段（如技能 <c>cast_time</c>/<c>cooldown_duration</c>）；本表
    /// 四个"秒数"字段是数值仿真的分析输入/期望值常量，从不流入 <see cref="Core.Foundation.SimLoop.SimClockHost"/>
    /// 的推进逻辑，不属于该检查覆盖的"时间字段"范畴，因此不登记 <c>FieldUnit.Time</c>、表也不需要
    /// <see cref="TableSchema.WithTimeScope"/>。
    /// </para>
    /// </summary>
    public static class SimSchemas
    {
        public static readonly TableSchema Anchor = new TableSchema(
            name: "sim.anchor",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "sim.anchor.<name>，惯例按等级命名，如 sim.anchor.l1"),
                new FieldSchema("level", FieldKind.Int, required: true,
                    description: "角色等级（数值总纲第 4.1 节记法 L）；全表须从 1 起连续、不重复，见 SimAnchorValidationRule")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("hp", FieldKind.Number, required: true,
                    description: "期望生命 HP(L)（数值总纲第 4.1/4.2 节：怪物血量=DPS(L)×TTK(L)×分档属性倍率、怪物伤害=HP(L)÷TTD(L)×分档属性倍率）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("dps", FieldKind.Number, required: true,
                    description: "期望秒伤 DPS(L)（数值总纲第 4.1/5 节自上而下秒伤，与自下而上算出的实际秒伤须在带宽内相等）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("ttk_seconds", FieldKind.Number, required: true,
                    description: "击杀同级普通怪时长 TTK(L)，单位秒")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("ttd_seconds", FieldKind.Number, required: true,
                    description: "被同级普通怪击杀时长 TTD(L)，单位秒")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("expected_item_level", FieldKind.Number, required: true,
                    description: "期望装备等级曲线 E(L)（07 第 1.2 节：该等级玩家身上装备的期望物品等级）；全表须沿 level 单调不减（警告级，见 SimAnchorValidationRule）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("level_duration_seconds", FieldKind.Number, required: true,
                    description: "目标每级时长 T(L)，单位秒（数值总纲第 4.7 节：每级怪当量=T(L)÷(TTK(L)+G(L))）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("kill_interval_seconds", FieldKind.Number, required: true,
                    description: "期望击杀间隔 G(L)，单位秒（含移动/拾取/赶路等非战斗耗时，数值总纲第 4.7 节）")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("quest_share", FieldKind.Number, required: true,
                    description: "任务与探索经验占比 Q(L)，取值 [0,1]（数值总纲第 4.7 节：升级所需(L)=击杀基数(L)×每级怪当量(L)×(1+Q(L))）")
                    .WithRange(FieldRange.Range(min: 0, max: 1)),
                new FieldSchema("note", FieldKind.String, required: false,
                    description: "内容作者说明原文，可选，随校验报告原样展示，不参与任何公式计算"),
            }).WithOwnership(SchemaLayer.Sim, "sim");

        private static readonly FieldSchema ScenarioPlayer = new FieldSchema(
            "player", FieldKind.Object, required: true,
            description: "标准玩家输入（ADR-0035 决策 2 标准玩家生成器的构造参数）",
            fields: new[]
            {
                new FieldSchema("class_id", FieldKind.Reference, required: true,
                    referenceTable: "arch.class",
                    description: "标准玩家职业"),
                new FieldSchema("level", FieldKind.Int, required: true,
                    description: "标准玩家基准等级；levels/level_from~level_to 未登记时场景按这单一等级运行")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("quality_id", FieldKind.Reference, required: false,
                    referenceTable: "item.quality_definition",
                    description: "标准玩家期望装备品质（07 第 1.2 节'预算反解'的品质输入），可选"),
                new FieldSchema("race_id", FieldKind.Reference, required: false,
                    referenceTable: "arch.race",
                    description: "标准玩家种族，可选"),
            });

        private static readonly FieldSchema ScenarioOpponent = new FieldSchema(
            "opponent", FieldKind.Object, required: true,
            description: "对手（战斗仿真的怪物来源）",
            fields: new[]
            {
                new FieldSchema("creature_id", FieldKind.Reference, required: true,
                    referenceTable: "creature.template",
                    description: "对手生物模板"),
                new FieldSchema("tier_id", FieldKind.Reference, required: false,
                    referenceTable: "creature.tier_definition",
                    description: "对手强度分档；不填时沿用 creature_id 记录自身登记的 tier，可选"),
                new FieldSchema("level", FieldKind.Int, required: false,
                    description: "对手出生等级；不填时默认等于 player.level（同级战斗）；与 level_offsets 至多指定一个，见 SimScenarioValidationRule")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("level_offsets", FieldKind.Array, required: false,
                    item: new FieldSchema("<offset>", FieldKind.Int, required: true,
                        description: "等级差（对手等级－玩家等级），可为负（对手比玩家低）"),
                    description: "越级矩阵仿真的等级差集合（ADR-0035 决策 3'越级矩阵输出胜率与时长热图'）；与 level 至多指定一个，见 SimScenarioValidationRule"),
            });

        private static readonly FieldSchema ScenarioBandwidthValue = new FieldSchema(
            "<bandwidth>", FieldKind.Number, required: true,
            description: "相对带宽，取值 [0,1]（数值总纲第 5 节对账等式：|自上而下-自下而上|÷自上而下≤带宽）")
            .WithRange(FieldRange.Range(min: 0, max: 1));

        public static readonly TableSchema Scenario = new TableSchema(
            name: "sim.scenario",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "sim.scenario.<name>，如 sim.scenario.arena_l10"),
                new FieldSchema("kind", FieldKind.Enum, required: true,
                    enumValues: new[] { "arena", "growth", "coverage" },
                    description: "仿真类型（ADR-0035 决策 3）：arena=战斗仿真；growth=成长仿真（一级到满级）；coverage=内容覆盖仿真"),
                ScenarioPlayer,
                ScenarioOpponent,
                new FieldSchema("levels", FieldKind.Array, required: false,
                    item: new FieldSchema("<level>", FieldKind.Int, required: true,
                        description: "玩家等级")
                        .WithRange(FieldRange.Range(min: 1)),
                    description: "本场景要覆盖的玩家等级集合；kind=arena/coverage 时必须非空，见 SimScenarioValidationRule"),
                new FieldSchema("level_from", FieldKind.Int, required: false,
                    description: "成长仿真起始等级；kind=growth 时必填，见 SimScenarioValidationRule")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("level_to", FieldKind.Int, required: false,
                    description: "成长仿真终止等级；kind=growth 时必填且须 ≥ level_from，见 SimScenarioValidationRule")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("runs", FieldKind.Int, required: true,
                    description: "每个采样格（等级×种子组合）独立运行的次数，用于按种子重复估计分布")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("base_seed", FieldKind.Int, required: true,
                    description: "基准随机种子（非负整数）；具体每次运行按本值与格索引派生实际种子，派生算法由仿真运行器实现，不在本表约定")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("max_ticks", FieldKind.Int, required: true,
                    description: "单场战斗的最大模拟 tick 上限，防止异常配置导致仿真死循环")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("bandwidths", FieldKind.Object, required: true,
                    description: "统计量名→相对带宽的映射（数值总纲第 5 节对账等式），键为统计量名（如 dps/hp/ttk/ttd/hit_rate/level_duration/item_level/gold），具体键集合由仿真运行器/报告工具约定，本表不枚举合法键")
                    .WithMap(MapSchema.FreeKeyed(
                        "带宽映射的键是统计量名（自由字符串词表，由仿真运行器/报告工具约定，不对应任何已登记表或 domain）",
                        ScenarioBandwidthValue)),
                new FieldSchema("anchor_ref", FieldKind.Id, required: false,
                    description: "预留字段：面向未来多套锚点数据场景，用于选择具体锚点集合的标识；本版本框架只登记 sim.anchor 一张表，本字段留空即表示使用该表，非空时的语义由后续任务定义（ADR-0035 决策 4）"),
                new FieldSchema("note", FieldKind.String, required: false,
                    description: "内容作者说明原文，可选，随校验报告原样展示"),
            }).WithOwnership(SchemaLayer.Sim, "sim");
    }
}
