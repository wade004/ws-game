using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>
    /// 手感实验室（<c>architecture/手感设计/06_手感实验室与验收.md</c> 第 1.1、2 节，ADR-0120/0122）的三张数据表
    /// 声明：<c>lab.scenario</c>（场景矩阵的一个格子）、<c>lab.arena</c>（竞技场地形）、<c>lab.dummy_set</c>
    /// （靶子集）。
    /// <para>
    /// 判断记录（为何 schema 声明放在 <c>core/sim</c> 而不是实验室内核模块）：这三张表与 <c>sim.anchor</c>/
    /// <c>sim.scenario</c> 同属"仅无头宿主与内容工具读取、运行期宿主不读"的工具表，已有现成的两个接入点——
    /// <see cref="HeadlessWorldBuilder"/>（装配根装载前调用 <see cref="SimSchemaCatalog.RegisterAll"/>）与
    /// <c>toolchain/validator</c>（经 <c>ContentValidationOptions.ExtraSchemaRegistration</c> 调同一入口）。
    /// 声明放这里，校验器与独立发行包不需要新增对实验室内核程序集的引用（否则 <c>Validator.csproj</c> 与
    /// <c>build.ps1</c> 的 lib 补齐都要跟着改），实验室内核只做类型化读取（<c>lab/core/LabCatalog.cs</c>）。
    /// <c>core/sim</c> 因此只知道表形状，不引用实验室内核，满足"不得被任何运行时核心程序集反向引用"。
    /// </para>
    /// <para>
    /// 判断记录（<c>SchemaLayer.Sim</c> 复用）：与 <c>sim.*</c> 同为"框架工具"层，不新增枚举成员。
    /// </para>
    /// <para>
    /// 判断记录（相对 06 第 1.1 节 <c>LabScenario</c> 结构的扩展字段）：<c>arena</c>（指向地形）、
    /// <c>settlement</c>（<c>targeted|action</c>，格子 id 之外的显式取值，供宿主与测试不靠解析 id 字符串）、
    /// <c>skill_bindings</c>（输入动作 id → 技能 id，等价引擎宿主引导代码里"普攻/技能 1 绑定哪个技能"的选项）。
    /// 06 的结构是格子的最小集合，这三项是无头宿主跑起来必需的，不改变既有字段语义。
    /// </para>
    /// </summary>
    public static class LabSchemas
    {
        private static readonly string[] SpaceValues = { "plane", "volume", "side_2d" };
        private static readonly string[] FormValues = { "sprite", "model" };
        private static readonly string[] CameraModeValues = { "ortho_top", "fixed_pitch", "third_person" };
        private static readonly string[] ControlSpaceValues = { "world", "camera_relative" };
        private static readonly string[] FacingValues = { "flip", "quantized", "continuous" };
        private static readonly string[] HitShapeValues = { "shape_2d", "volume" };
        private static readonly string[] SettlementValues = { "targeted", "action" };
        private static readonly string[] BlockKindValues = { "wall", "pillar" };
        private static readonly string[] DummyKindValues = { "stake", "mob", "elite", "swarm", "patrol", "breakable" };

        public static readonly TableSchema Scenario = new TableSchema(
            name: "lab.scenario",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "lab.scenario.<组合>_<模式>，如 lab.scenario.2d_targeted"),
                new FieldSchema("space", FieldKind.Enum, required: true, enumValues: SpaceValues,
                    description: "空间模型：plane=平面世界（当前唯一可运行）；volume=体积空间能力包（reserved，需导航与空间查询契约扩展）；side_2d=横版二维（侧视+重力轴，reserved）"),
                new FieldSchema("form", FieldKind.Enum, required: true, enumValues: FormValues,
                    description: "外形：sprite=精灵；model=模型。只影响呈现，无头宿主读取但不据此改变判定"),
                new FieldSchema("camera_mode", FieldKind.Enum, required: true, enumValues: CameraModeValues,
                    description: "镜头模式：ortho_top=正交俯视；fixed_pitch=固定俯角透视；third_person=第三人称跟随（需适配层能力 supportsFreeYaw）"),
                new FieldSchema("control_space", FieldKind.Enum, required: true, enumValues: ControlSpaceValues,
                    description: "控制空间：world=世界相对；camera_relative=镜头相对（轴到世界方向的换算是被测对象，当前无头宿主不支持）"),
                new FieldSchema("facing", FieldKind.Enum, required: true, enumValues: FacingValues,
                    description: "朝向表达：flip=翻转；quantized=4/8 向量化；continuous=连续。只影响呈现"),
                new FieldSchema("hit_shape", FieldKind.Enum, required: true, enumValues: HitShapeValues,
                    description: "命中形状：shape_2d=二维 Shape；volume=体积扫掠（需适配层能力 supportsVolumeSweep）"),
                new FieldSchema("default_preset", FieldKind.Id, required: true,
                    description: "缺省手感预设 id（feel.preset.*）。手感数据域落地前仅作标签，无头宿主不解析")
                    .WithFreeIds("feel.preset 所属数据域由手感解析器切片登记；本表落地时该域尚不存在，只作标签字符串携带，不做存在性校验"),
                new FieldSchema("default_weapons", FieldKind.IdList, required: false,
                    description: "缺省武器 id 清单（feel.weapon.*）。手感数据域落地前仅作标签")
                    .WithFreeIds("feel.weapon 所属数据域由手感解析器切片登记；本表落地时该域尚不存在，只作标签字符串携带，不做存在性校验"),
                new FieldSchema("dummy_set", FieldKind.Reference, required: true, referenceTable: "lab.dummy_set",
                    description: "靶子集"),
                new FieldSchema("script_subset", FieldKind.IdList, required: false,
                    description: "标准脚本集里适用于本格子的脚本 id（脚本文件名，不含扩展名）；空表示适用全部")
                    .WithFreeIds("脚本 id 是夹具文件名而非数据表主键，不属于任何已登记表或 domain"),
                new FieldSchema("required_capabilities", FieldKind.Array, required: false,
                    item: new FieldSchema("<capability>", FieldKind.String, required: true,
                        description: "适配层能力名，如 supportsFreeYaw、supportsVolumeSweep"),
                    description: "缺失则该格子在此适配层上标'不可运行'，不静默跳过"),
                new FieldSchema("arena", FieldKind.Reference, required: true, referenceTable: "lab.arena",
                    description: "（扩展字段，见类型注释）竞技场地形"),
                new FieldSchema("settlement", FieldKind.Enum, required: true, enumValues: SettlementValues,
                    description: "（扩展字段，见类型注释）结算模式：targeted=目标选择式；action=动作式。动作式在时间线机制落地前与目标选择式行为一致"),
                new FieldSchema("skill_bindings", FieldKind.Object, required: true,
                    description: "（扩展字段，见类型注释）输入动作 id → 技能 id；宿主在动作按下沿提交 cast 意图")
                    .WithMap(MapSchema.FreeKeyed(
                        "键是输入动作 id（found.input_action 的 key，登记表主键字段名与内容表不同，不走引用校验）",
                        new FieldSchema("<skill>", FieldKind.Reference, required: true, referenceTable: "skill.def",
                            description: "动作按下时施放的技能"))),
                new FieldSchema("note", FieldKind.String, required: false,
                    description: "内容作者说明原文，可选"),
            }).WithOwnership(SchemaLayer.Sim, "lab");

        private static readonly FieldSchema ArenaBlock = new FieldSchema(
            "<block>", FieldKind.Object, required: true,
            description: "一块轴对齐阻挡矩形（登记进导航阻挡，无头宿主用桩导航承载）",
            fields: new[]
            {
                new FieldSchema("name", FieldKind.String, required: true,
                    description: "块名，同一张地形内唯一，供脚本说明与诊断引用"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: BlockKindValues,
                    description: "wall=墙（含走廊两侧）；pillar=柱子"),
                new FieldSchema("min", FieldKind.Vec2, required: true, description: "矩形左下角（含）"),
                new FieldSchema("max", FieldKind.Vec2, required: true, description: "矩形右上角（含）"),
            });

        public static readonly TableSchema Arena = new TableSchema(
            name: "lab.arena",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "lab.arena.<name>"),
                new FieldSchema("map_ref", FieldKind.Reference, required: true, referenceTable: "world.map",
                    description: "承载本地形的地图（玩家、靶子都出生在这张图上）"),
                new FieldSchema("blocks", FieldKind.Array, required: true,
                    item: ArenaBlock,
                    description: "阻挡矩形清单：空地之外的直墙、走廊、柱子"),
                new FieldSchema("note", FieldKind.String, required: false,
                    description: "内容作者说明原文，可选"),
            }).WithOwnership(SchemaLayer.Sim, "lab");

        private static readonly FieldSchema DummyEntry = new FieldSchema(
            "<dummy>", FieldKind.Object, required: true,
            description: "一个靶子（swarm 展开为 count 个）",
            fields: new[]
            {
                new FieldSchema("name", FieldKind.String, required: true,
                    description: "靶子名，同一靶子集内唯一"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: DummyKindValues,
                    description: "stake=不死木桩（可选 poise 韧性）；mob=普通怪；elite=精英；swarm=群体簇（一键刷 count 个）；patrol=巡逻靶；breakable=可破坏障碍（动态阻挡，见 block_half_extent）"),
                new FieldSchema("creature_ref", FieldKind.Reference, required: true, referenceTable: "creature.template",
                    description: "靶子的生物模板"),
                new FieldSchema("position", FieldKind.Vec2, required: true,
                    description: "出生位置（swarm 为簇中心）"),
                new FieldSchema("group", FieldKind.String, required: true,
                    description: "分组标签；输入脚本按组选择本次出场的靶子"),
                new FieldSchema("count", FieldKind.Int, required: false,
                    description: "swarm 的个数，缺省 1")
                    .WithRange(FieldRange.Range(min: 1)),
                new FieldSchema("spacing", FieldKind.Number, required: false,
                    description: "swarm 簇内相邻两个的间距，缺省 1.0")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("ai", FieldKind.Bool, required: false,
                    description: "是否保留模板自带 AI 注册，缺省 false（实验室默认关闭 AI，保证指纹可复现）"),
                new FieldSchema("block_half_extent", FieldKind.Number, required: false,
                    description: "（可破坏障碍）声明该靶子同时是动态阻挡：出生时在地形阻挡之外追加一块以出生点为中心、半边长为该值的轴对齐矩形，"
                        + "被打死时经导航接口批量替换去掉（阻挡版本号递增）。kind = breakable 不声明时缺省 0.5；其它 kind 声明了就同样生效，不声明就不挡路")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                new FieldSchema("poise", FieldKind.Number, required: false,
                    description: "（韧性）靶子的韧性值，开手感的场景里出场后写进该单位的韧性属性（受击裁决：攻击的硬直强度不高于它时只播受击动画不打断）；"
                        + "缺省不声明，等价韧性为 0（所有命中按冲击等级映射反应）")
                    .WithRange(FieldRange.Range(min: 0)),
            });

        public static readonly TableSchema DummySet = new TableSchema(
            name: "lab.dummy_set",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "lab.dummy_set.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: DummyEntry,
                    description: "靶子清单；输入脚本按 group 选择出场"),
                new FieldSchema("note", FieldKind.String, required: false,
                    description: "内容作者说明原文，可选"),
            }).WithOwnership(SchemaLayer.Sim, "lab");
    }
}
