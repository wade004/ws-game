# sim_loop 拥有的数据表：`found.time_model`

本模块拥有 `found.time_model` 表的字段定义（见
[01_分层与依赖.md](../../../../architecture/01_分层与依赖.md) L0 模块表 `sim_loop` 行
"主要数据表"列）。本文件只照抄字段说明备查，权威定义见
[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 3.1 节"时间字段语义"。

**离散时间模型已落地（ADR-0013），本表现已实际加载并解释**（2026-09-07 改写，此前一版文字
写于 T1-5 阶段"本项目当前只启用连续模式，不加载、不解释这张表"，已过时）：表结构由
`schema/TimeModelSchema.cs`（本模块）登记，`core/rules/assembly.RulesSchemaCatalog` 在装配期
调用 `registry.RegisterSchema(TimeModelSchema.Table)` 注册进 `DataRegistry`；`SimClockHost`/
`WorldSim` 本身仍不读取这张表（只提供连续/离散两种机制点，见本目录 `README.md`"不负责什么"
一节）——真正按记录内容做"探索/战斗该用哪种时间模型、先攻策略、移动预算规则"这些解释与连续
⇄ 离散模式切换的，是 `core/gameplay/assembly.TimeModelSwitch`（L4，构造期经
`IDataRegistryView.GetAll("found.time_model")` 分别加载 `exploration`/`combat` 两条 scope 记录，
见该类型判断记录）。

**数据行归属游戏数据，不属于框架默认数据集**（H3b 收口）：`found.time_model` 的具体数据行
（游戏实际用连续还是离散、先攻策略选哪种）是每个游戏自己的口味配置，不是框架该替游戏预先决定
的默认值，因此不放在 `data/_framework`/`data/_sample` 里，而是随游戏数据登记在
`games/<game>/data/game/found/found.time_model.json`（模板范例见
`games/_template/data/game/found/found.time_model.json`）；测试/灰盒等场景各自的示例数据放在
调用方自己的测试数据目录（如 `adapters/unity/.../Tests/Runtime/TestData/found/`、
`core/gameplay/tests/Discrete/` 的测试数据）。这条"数据行放哪"的约定与上一段"schema 由谁登记、
谁解释"是两件事：本文件登记的是字段结构，不是任何一份具体数据行的存放位置。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `found.time_model.<name>`，探索与战斗各自登记一条（对应 `exploration_time_model`、`combat_time_model`） |
| `scope` | `exploration\|combat` | 是 | 声明本条作用于探索还是战斗 |
| `mode` | `continuous\|discrete` | 是 | 时间模型：连续（固定步长，时间单位为秒）或离散（回合，时间单位为回合） |
| `seconds_per_turn` | Number | `mode: discrete` 时必填 | 连续/离散切换时刻的时间单位换算系数，默认 6 |
| `initiative_policy` | `initiative_stat\|action_points\|fixed_order\|atb` | `mode: discrete` 时必填 | 先攻策略；`atb` 为预留扩展位，本版不展开 |
| `initiative_stat` | Optional\<Id\> | `initiative_policy: initiative_stat` 时必填 | 指向 `stat.definition` 的先攻属性 id |
| `movement_budget_rule` | `distance\|action_points` | `mode: discrete` 时必填 | 离散模式下每回合移动预算的计算方式 |
| `grid_snap` | Optional\<{cell_size: Number}\> | 否 | 若启用格子吸附，声明格子尺寸；范围形状按格子中心采样 |

全部与时间相关的数据字段（`cast_time`、`channel_time`、`cooldown_duration`、
`charges.recharge_time`、光环 `duration`/`interval`、资源 `regen`/`decay`、
`respawn_timer`、脱战判定时长、`internal_cooldown` 等）一律"以数据集声明的时间单位计"：
`discrete` 作用域下必须是整数（以回合计），`continuous` 作用域下按秒计、允许小数。
本模块的 `ISimTimers` 只机械地按传入的 dt 推进剩余时长，不关心当前处于哪种时间模型，
时间单位的解释完全由调用方（数据集声明 + 未来的离散模式实现）负责。
