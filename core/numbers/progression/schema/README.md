# progression 数据表字段说明

对应 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单：
`prog.level_curve`"等级到所需经验、到属性成长系数的曲线表"、`prog.xp_source`"经验来源 id 到
经验值与限制规则"。**判断记录：04 未给出这两张表的字段表**，以下字段为实现期按任务书 T2-3
给出的最小字段集补录，待 04 正式登记时以 04 为准同步本文件。

**T-N4-1 补记（[ADR-0033](../../../../architecture/adr/0033-等级经验模块正文与当量来源.md)
决策 2/3；[06 第 2.5 节](../../../../architecture/06_规则层_属性技能战斗AI.md)）**：本次新增
`prog.level_curve.talent_points`、`prog.xp_source` 的 `kind`/`base_curve_ref`/`level_diff_ref`/
`once_key` 四个字段，以及新表 `prog.xp_base_curve`；`base_xp`/`weight` 标废弃（拍板 4）。全部
新增字段均为纯新增可选字段，两张既有表 `currentSchemaVersion` 均不递增。**契约疑点**：
`prog.xp_base_curve` 在 04 第 1.1 节表清单里尚未有登记行（落地改动点清单第 10 节拍板 5、分阶段
落地计划拍板表第 5 条已拍定表名与归属，但 04 正文暂未补，详见 `ProgSchemas.cs` 类型注释"契约
疑点上报"），本任务不改架构文档，留阶段收尾的文档回填批次同步。

## `prog.level_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `prog.curve.<name>` |
| `max_level` | Int | 是 | 曲线最大等级，必须等于 `entries` 的元素个数（见下方校验规则） |
| `entries` | Array | 是 | `Array<{level:Int, xp_to_next:Int, growth:Object<stat_id,Number>, talent_points?:Int}>`，`level` 从 1 连续到 `max_level`；最后一级的 `xp_to_next` 按约定为 0（无下一级）；`talent_points`（T-N4-1，ADR-0033 决策 2）该级获得的天赋点数，缺省 0，`>= 0`。**契约疑点上报（T-N4-2 留待复核）**：T-N4-1 落地时本行标注"消费实现（写入天赋点余额）留 T-N4-2"，但分阶段落地计划正式的 T-N4-2 任务行（第 14 节"阶段 N4 任务级拆分"）与本任务实际收到的派发任务书只列 `grantXp(XpContext)`/`GetXpToNext` 满级归零/`XpContext` 结构三项，均未提及天赋点消费，也没有给出"天赋点余额"应挂在哪个契约面（`IProgressionHost` 新增查询方法？新事件？由游戏层自行订阅 `progression.level_up` 累加？）的字面结论——T-N4-2 不擅自新增这类未声明的契约面，本字段的消费实现继续留待设计层在后续任务重新拆分派发 |

`entries` 声明为 `FieldKind.Array`（04 第 5 节"结构未知/由上层模块自行解释的 JSON 数组，本模块
只做存在且是数组检查"），元素内部结构由 `Core.Numbers.Progression.ProgressionHost` 与
`ProgLevelCurveValidationRule` 自行解析——04 的 `DataRecord`/`TableSchema` 没有"数组元素的
嵌套 schema"机制，这与 `arch.talent_tree.nodes`（见 archetype 模块 schema）是同一类处理方式。

`growth` 的 key 是属性 id（`stat.*`，供 `StatModifierWriter` 使用），value 是该级相对上一级的
成长增量；1 级（`level=1`）的 `growth` 即便存在也不参与累计（任务书原文"1 级无成长"）。

**校验规则**（`ProgLevelCurveValidationRule`，check 名 `level_curve_entries`/
`level_curve_continuity`/`level_curve_xp_monotonic`）：`entries.Count == max_level`；`entries[i].level == i+1`（0 基下标）；
`xp_to_next` 沿等级不递减（允许相等；末级条目按约定为 0，不参与比较）——分阶段落地计划 T-N0-5、拍板 3：本表保持
逐级密集枚举、不迁移到 04 第 3.6 节断点表形态，只接入单调校验（数值总纲原则 1、ADR-0033）。

## `prog.xp_source`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `prog.xp.<name>` |
| `kind` | Enum | 否（见判断记录） | `kill`\|`quest`\|`discovery`（ADR-0033 决策 3）；T-N4-1 新增 |
| `base_xp` | Int | 是 | 基础经验值（**废弃**，`base_curve_ref` 存在时优先，保留一个版本周期，拍板 4） |
| `weight` | Number | 否 | 省略时按 1 处理（**废弃**，同上，拍板 4） |
| `base_curve_ref` | Reference → `prog.xp_base_curve` | 否 | 击杀基数曲线引用（ADR-0033 决策 3）；T-N4-1 新增，存在时优先于 `base_xp`/`weight`；消费实现见 T-N4-2（`ProgressionHost.GrantXp`/`GrantFromSource` 均已接入，见模块 README"T-N4-2"一节） |
| `level_diff_ref` | Reference → `combat.level_diff_table` | 否 | 等级差规则表引用，取其经验系数列（ADR-0033 决策 3/5）；T-N4-1 新增；消费实现见 T-N4-2（`kind=kill`/`quest` 生效，`kind=discovery` 按 ADR 原文公式刻意不查本列，即便登记了也不生效） |
| `once_key` | String | 否 | 一次性标志键前缀，`kind=discovery` 时使用（ADR-0033 决策 3）；T-N4-1 新增；消费实现留 T-N4-3 |
| `condition` | Expr | 否 | 触发条件；本任务只登记字段类型（供未来 `expr_parsable` 校验使用），`IProgressionHost` 不对其求值 |

**判断记录（`kind` 登记为可选而非必填）**：ADR-0033/06 第 2.5 节把 `kind` 写进字段表但未明确
"是否对存量数据强制必填"；本任务 T-N4-1 的验收标准显式要求"旧字段兼容 1 组——只有
`base_xp`/`weight` 的来源加载 0 error"，若 `kind` 登记为必填会让现存只有 `base_xp`/`weight` 的
样例/游戏数据在未补 `kind` 前直接加载失败，与该验收标准冲突。**待设计层确认**：`kind` 是否应
在下一个版本周期（拍板 4"保留一个版本周期"到期、`base_xp`/`weight` 真正删除时）连带收紧为
必填，本任务先按 `required: false` 落地满足显式验收标准，留给该阶段任务重新判断。

## `prog.xp_base_curve`（T-N4-1 新增）

击杀基数曲线：怪物/任务/区域等级到"一只同级普通怪的基础经验值"的曲线（ADR-0033 决策 3；落地
改动点清单第 10 节拍板 5"表名 `prog.xp_base_curve`，归 L1 progression"）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `prog.xp_base_curve.<name>` |
| `entries` | Array（04 第 3.6 节断点表形态，横轴 `Level`） | 是 | `[{x: 怪物/任务/区域等级(Int), y: 基础经验值(Number, >= 0)}]`，按 `x` 线性插值、越界夹取到端点；单调有限阻断校验（`curve_monotonic_finite`）对全部断点表形态字段统一生效，不需要本表专属校验规则 |

消费实现（`ProgressionHost.GrantXp`/`GrantFromSource` 读取本表折算三种来源当量）见 T-N4-2；
本节（原 T-N4-1）只登记数据形状与 schema 覆盖测试（`ProgSchemaCoverageTests.cs`）。

## T-N4-2 补记（`grantXp`/`GrantFromSource` 消费实现）

[ADR-0033](../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 1/3/9；
[06 第 2.5 节](../../../../architecture/06_规则层_属性技能战斗AI.md)。`ProgressionHost` 新增
`GrantXp(unitId, sourceId, context: XpContext)` 折算三种来源（公式与判断记录见
`contracts/IProgressionHost.cs`/`core/ProgressionHost.cs` 的 `GrantXp`/`ComputeCurveBasedRawAmount`
判断记录，不在本文重复），要点摘录：

- `base_curve_ref` 未填时，`GrantXp`/`GrantFromSource` 都走拍板 4 的旧算法
  `base_xp × weight ×（GrantXp 隐式 1 / GrantFromSource 显式 multiplier）`，逐位保留 T-N4-2 之前
  的既有实现，不引入任何计算差异。
- `base_curve_ref` 存在时以曲线为准（拍板 4"存在时优先"，覆盖旧字段而不是与之混算）。**待设计层
  确认**：`kind` 未登记时按 `kill` 处理（该分支公式最贴近曲线本身的语义，见判断记录），若日后
  `kind` 收紧为必填（见上方判断记录）本条兜底可以移除。
- `combat.level_diff_table.xp_factor` 只对 `kind=kill`/`quest` 生效，`kind=discovery` 按 ADR 原文
  公式刻意不查（即便该来源同时登记了 `level_diff_ref`）。本模块读取该表时**不引用**
  `Core.Rules.Combat.LevelDiffTable` 类型（那会让 L1 反向依赖 L2、倒置分层，见 `ProgressionHost`
  字段判断记录），改用 `Core.Foundation.DataRegistry.CurveSchema.ReadBreakpoints` 直接从原始
  `DataRecord` 读取同名字段；该表与 `prog.xp_base_curve` 一样是可选表——数据源没有对应文件时
  （单机/测试夹具不装配 combat 模块）留空字典，等级差系数退化为恒 1，不抛异常。
- `ProgressionOptions.MaxLevel` 从本任务起真正被消费（此前 T-N4-1 落地时是纯登记壳），语义与
  默认值同时调整为"0=不设全局上限（等价于从曲线推导），正值=收紧上限但不超过曲线自身
  `max_level`"，详见 `ProgressionOptions.cs` 该字段"变更记录"判断记录。
- `ProgressionHost` 新增接受 `ProgressionOptions?` 的构造重载（旧构造函数转发 `null`，行为不变，
  ABI 只新增不改动既有签名）。

## 判断记录

- **未把 `primary_stat`/`power_types` 一类跨模块引用声明为 `FieldKind.Reference`**：本表
  两张表都不涉及跨模块引用，此条不适用（对照 archetype 模块 schema/README.md 的同名判断记录，
  以免误读为遗漏）。
- **`condition` 只登记类型不求值**：见模块 README 判断记录 2；本模块不依赖 `Core.Foundation.Expr`
  的求值 API，只用 `FieldKind.Expr` 让 04 的 `field_type`/`expr_parsable` 校验项能识别该字段。
