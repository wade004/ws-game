# L3 载体层 · projectile（投射物）

职责：落地 [05_对象模型与世界.md](../../../architecture/05_对象模型与世界.md) 第 1.4 节
`Projectile`（飞行物运行期实体）与
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 3.2 节
`projectile` 效果原语（"飞行方式、命中行为、命中后效果列表……生成一个 `Projectile` 实体"）。
对外契约 `Core.Rules.Common.IProjectileSpawner`（`Spawn`）已在 `core/rules/common` 声明，本模块
提供其唯一实现 `ProjectileHost`。

依赖：`Core.Rules.csproj`（含其 `Core.Numbers`/`Core.Foundation` 传递引用）。不引用
`Core.Gameplay`、不引用 `Core.Carriers` 内其它模块（`unit`/`item`/`creature`/`summon`/`gobj`），
不使用 `UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、`System.Reflection`。

## 目录

```
projectile/
  README.md
  contracts/
    ProjectileEntity.cs        Projectile 运行期实体（05 第 1.4 节五字段 + HeightOffset，见下）
    ProjectileOptions.cs        口味配置 + 参数缺省值
    IProjectileDiagnostics.cs   最小诊断出口
  core/
    ProjectileHost.cs           IProjectileSpawner 实现：生成 + 逐 tick 推进 + 命中判定 + 效果回灌
    ProjectileTickHandler.cs    挂 TickPhase.MovementAndNavigation（紧随 MovementTickHandler）
    InMemoryProjectileDiagnostics.cs
  tests/
    ...
```

**没有 `schema/` 目录**：04/06 均未定义 `projectile.def`（或类似）数据表——06 第 3.2 节把
`projectile` 描述为技能 `effects[]` 里的一项（`{kind: "projectile", params: {...}}`），飞行/命中/
命中后效果参数全部内联在 `skill.def` 该效果项的 `params` 里，不新造表（判断记录：与 04 总索引
逐表核对，未发现遗漏的表定义）。

## `projectile` 效果的 `params` 字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `travel_mode` | `straight\|homing\|arc` | 否，缺省 `straight` | 飞行方式（见 05 第 1.4 节） |
| `hit_behavior` | `impact_on_first\|pierce\|impact_on_expiry` | 否，缺省 `impact_on_first` | 命中行为（见 05 第 1.4 节） |
| `speed` | Number | 否，缺省 `ProjectileOptions.DefaultSpeed`（10） | 飞行速度（单位距离/秒） |
| `max_range` | Number | 否，缺省 `ProjectileOptions.DefaultMaxRange`（30） | 最大射程，超出仍未命中/到期视为未命中并销毁 |
| `arc_height` | Number | 否，缺省 `ProjectileOptions.DefaultArcHeight`（2） | `arc` 飞行方式的抛物线峰值高度（纯表现参数，见 `ProjectileEntity.HeightOffset`） |
| `impact_radius` | Number | 否，缺省 `ProjectileOptions.DefaultImpactRadius`（1） | `impact_on_expiry` 到期时的命中判定半径 |
| `max_pierce_count` | Number | 否，缺省不限 | `pierce` 命中行为最多穿透命中的单位数 |
| `display_ref` | Id | 否 | 指向 `display.map`（`logical_id` 匹配），写入 `ProjectileEntity.TemplateId` 供表现层解析外形；未提供时 `TemplateId` 为空，presentation 按"未知种类降级"处理（本任务不改表现层） |
| `on_hit_effects` | List\<{kind, params}\> | 否，缺省空列表 | 命中后交回 L2 效果管线的效果列表，形状同 `skill.def.effects[]`（见 06 第 3.1 节 `EffectRef`），未知 `kind` 的项记诊断警告并跳过 |

## 命中判定

- **单位命中**（`impact_on_first`/`pierce`）：每 tick 用本次位移线段
  `ISpatialQuery.QueryLine(prevPos, newPos, filter)` 查询候选（`filter` 要求
  `ProjectileOptions.HitQueryTags`，默认 `["unit"]`，与
  `Core.Carriers.Assembly.CarriersAssembly.DefaultSpatialSyncKinds` 给 `creature`/`player`
  两类 Unit 打的标签一致），按到线段起点的距离升序处理，跳过发射者自身与已命中过的单位。
- **地形阻挡**：`INavigation2D.Raycast(mapId, prevPos, newPos)` 非空即视为撞墙，销毁投射物、不
  产生任何命中后效果（撞墙不是"命中了某个单位"）。
- **到期判定**（`impact_on_expiry`）：飞行途中不做单位命中检测，只在
  `DistanceTraveled >= max_range` 时按 `impact_radius` 做一次 `QueryRadius`，对范围内全部符合
  标签过滤的单位（排除发射者）逐个回灌命中后效果。

## 命中行为

| `hit_behavior` | 语义 |
|---|---|
| `impact_on_first` | 命中线段上按距离最近的第一个候选后立即回灌效果并销毁 |
| `pierce` | 命中即回灌效果但不销毁，继续飞行，直到 `max_pierce_count`（若给出）耗尽或超出 `max_range` |
| `impact_on_expiry` | 飞行途中忽略单位命中，只在到期时按 `impact_radius` 做一次范围判定并回灌 |

## 未命中处理

`impact_on_first`/`pierce` 达到 `max_range` 仍未产生（更多）命中时，直接销毁、不回灌任何效果
（"未命中"）；`impact_on_expiry` 到期即触发范围判定，范围内若无任何符合条件的单位，同样销毁、
不回灌任何效果（"落点空砸"）。

## 设计要点与判断记录

1. **运行期簿记不放进 `ProjectileEntity`**：瞄准目标、已飞行距离、已命中单位集合、命中后效果
   列表、效果回灌出口等字段只在 `ProjectileHost` 内部字典维护，不是 05 第 1.4 节定义的对象模型
   字段——惯例同 `core/rules/combat` `ThreatTable`/`core/rules/skill` `AuraHost`。
2. **不注册进 `ISpatialQuery` 空间索引**：`CarriersAssembly.DefaultSpatialSyncKinds` 默认只登记
   `creature`/`player` 两类 Unit（"避免'最近敌人'捞到物件"），本模块同一顾虑延伸到投射物——若
   投射物也登记进同一空间索引，任何不显式按标签过滤的目标解析/AI 感知查询都可能把飞行中的箭矢
   当成候选目标。`ProjectileHost` 因此从不调用 `ISpatialQuery.Register`/`UpdatePosition` 登记
   投射物自身，只把 `ISpatialQuery` 当"查询别的单位"的只读工具使用。
3. **不新增专属事件**：06 第 8 节事件词汇表未给投射物登记 `projectile.spawned`/
   `projectile.expired` 一类专属事件；本模块复用 `IWorldSim.AddEntity`/`MarkForDestruction`
   自身触发的 `entity.created`/`entity.destroyed` 通用事件覆盖"生成"/"销毁"两个时机，命中后
   效果本身产生的事件（如 `combat.damage_dealt`）经 `IEffectSink.ApplyEffect` 走既有管线正常
   发出。
4. **`homing` 目标丢失后继续沿最后方向直线飞行，不立即销毁**：05/06 均未规定"追踪目标丢失"时
   的行为；任务拍板按"飞完这一下"处理（目标恰好在命中前死亡是正常游戏场景），不是"目标一死
   投射物就凭空消失"。
5. **`ProjectileTickHandler` 挂载阶段**：03 第 4.2 节八步固定顺序没有专门的"投射物飞行"阶段，
   任务拍板"移动步之后、战斗结算步之前"——本模块把它挂到 `TickPhase.MovementAndNavigation`
   （不新建阶段，那属于"新增原语"级别的改动），且在该阶段内排在 `MovementTickHandler` 之后
   （同一阶段多个处理器按注册顺序执行）：这样投射物用的是"本 tick 全部单位已完成移动之后"的
   最新位置做命中判定，且整个阶段本身天然排在 `TickPhase.CombatResolution` 之前，同时满足两个
   顺序要求。
6. **`IProjectileSpawner` 不经 `IEffectExtension` 六合一扩展点**：`projectile` 原本也在
   `IEffectExtension` 覆盖的原语之列，收边任务给它单开了专用依赖倒置接口——投射物命中是"跨多个
   tick 才发生"的异步结果（不像 `summon`/`create_item` 等在效果落地的同一次调用内就能完成），
   `IProjectileSpawner.Spawn` 需要额外接收一个 `IEffectSink` 回调供命中时反向调用，这个签名形状
   超出了 `IEffectExtension.TryHandle`"同步处理并返回 `ResolveResult`"的设计。
7. **确定性**：飞行推进（速度 × dt）、命中判定（按距离排序取最近候选）均不引入任何非确定性
   随机源；`ProjectileHost.Advance` 按 `Id` 序数遍历全部存活投射物（惯例同
   `Core.Rules.Combat.CombatHost.Update`），保证同种子同输入同结果。

## 契约缺口 / 未覆盖内容

- **`arc` 飞行方式的水平轨迹与 `straight` 完全一致**：05 第 3.3 节"高度偏移……不参与平面距离与
  碰撞计算"——本模块把 `arc` 实现为"水平直线飞行 + `HeightOffset` 按抛物线曲线变化"两部分完全
  解耦，水平方向的路径/命中判定与 `straight` 无差异，只有 `HeightOffset`（纯表现参数）随飞行
  进度变化。
- **不做多目标同 tick 命中的伤害分摊/减免**：`pierce` 命中多个单位时，每个单位各自独立走一遍
  `on_hit_effects`（含各自的命中表/护甲抗性结算），06 文档未提及穿透命中需要联动衰减。

## 不负责什么

- 不实现 `core/carriers` 其它并行模块（`unit`/`item`/`creature`/`summon`/`gobj`）的任何逻辑。
- 不实现表现层外形解析——`display_ref` 只是原样写入 `ProjectileEntity.TemplateId`，presentation
  视图工厂如何按 `TemplateId`/`display.map` 解析外形不属于本模块职责（也不在本任务改动范围）。
- 不持久化：见 05 第 1.4 节"进存档：否，飞行物是瞬时对象，不跨存档周期存在"——本模块不提供任何
  `IPersistable` 实现，`GameplayAssembly.RegisterPersistables` 不会、也不应该登记投射物。
