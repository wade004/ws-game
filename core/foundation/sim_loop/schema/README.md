# sim_loop 拥有的数据表：`found.time_model`

本模块拥有 `found.time_model` 表的字段定义（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `sim_loop` 行
"主要数据表"列），但**本项目当前只启用连续模式，不加载、不解释这张表**——`SimClockHost`/
`WorldSim` 的连续模式实现完全不读取 `found.time_model`。本文件只照抄字段说明备查，
真正的加载与解释属于未来启用离散模式时的工作，本阶段（T1-5）不实现。

来源：[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 3.1 节
"时间字段语义"。

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
