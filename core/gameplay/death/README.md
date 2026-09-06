# L4 玩法层 · death（死亡复活）

职责：落地 06_规则层_属性技能战斗AI.md 第 4.6 节死亡与复活三策略（`respawn_point`/`reload_save`/
`permadeath`）的**执行主体**。`core/rules/combat.CombatOptions.DeathPolicy` 只是一个孤立的配置项
（06 §4.6 只声明策略取值，不声明谁来执行），本模块订阅 `unit.died`（仅玩家单位，AI/生物死亡不
触发），按 `DeathPolicyHost.EffectivePolicy`（`DeathPolicyOptions.Policy` 覆盖，或回退
`CombatOptions.DeathPolicy`）执行对应策略——这是 DECISIONS（2026 深夜设计层拍板）拍板 3 的落地：
"死亡复活三策略执行主体应落在 L4 新模块 `core/gameplay/death`"。

依赖：L0（`event_bus`/`app_lifecycle`/`save_system`/`sim_loop`）、L2（`core/rules/common.RespawnPolicy`/
`RulesEventKeys`/`UnitDiedEvent`/`UnitRespawnedEvent`）。不直接依赖 `core/carriers/unit`
（L3 具体类型 `WorldUnitAccess`）——经 `DeathPolicyOptions.ReviveUnit` 窄契约委托接线（见判断记录 1），
也不直接依赖同层的 `core/gameplay/assembly`（经 `DeathPolicyOptions.ResolveDefaultSpawn` 窄契约委托
接线，同一份判断记录）。

## 目录

```
death/
  README.md
  contracts/
    IDeathPolicyHost.cs       契约接口（只暴露只读 EffectivePolicy，无触发方法——事件驱动）
    DeathPolicyOptions.cs     策略覆盖/复活生命值比例/延迟 tick 数/存档槽 id/ReviveUnit/
                              ResolveDefaultSpawn 两个 L4↔L3、L4↔L4 边界委托
    IDeathPolicyDiagnostics.cs 最小诊断出口
  core/
    DeathPolicyHost.cs        IDeathPolicyHost + ITickPhaseHandler 唯一实现
    InMemoryDeathPolicyDiagnostics.cs
  tests/
    TestSupport.cs
    DeathPolicyHostTests.cs   三策略各 ≥2 条 + AI 死亡不触发 + EffectivePolicy 覆盖
```

## 判断记录

1. **`ReviveUnit`/`ResolveDefaultSpawn` 两个窄契约委托，而不是直接引用 `WorldUnitAccess`/
   `TeleportTargetResolver`**：`WorldUnitAccess.Revive`（L3 `core/carriers/unit`）是本模块
   `respawn_point` 策略需要的"复活单位"能力，但该方法不在 `IUnitAccess` 契约上（只是具体类型的
   窄契约，见其判断记录），本模块不为此新增一条对 `core/carriers/unit` 具体类型的编译期依赖；
   `TeleportTargetResolver`（L4 `core/gameplay/assembly`）能把一个地图 id 解析成该地图默认出生点，
   但 `assembly` 是组装根，同层 L4 模块不应反向依赖组装根（会造成"部件依赖装配它的容器"这种
   倒置）。两者都改为 `GameplayAssembly` 装配期用 `is` 模式/直接持有的方式接线的委托——
   `ReviveUnit` 只在 `Carriers.Units` 运行期确实是 `WorldUnitAccess` 时才接线（防御性判断，惯例
   同 `GameplayAssembly` 第 10.5 步 `world is WorldSim` 的一贯做法），`ResolveDefaultSpawn` 直接
   接同一个 `teleportTargetResolver.Resolve` 方法组（传入地图 id 本身即命中其"两段式地图 id"解析
   路径，见该类型判断记录）。任一委托未接线（如测试直接构造 `DeathPolicyHost` 不提供）时
   `respawn_point` 策略退化为"只记诊断、不复活"，不抛异常（同本仓库一贯的未接线退化惯例）。

2. **`RespawnDelayTicks` 默认 1，且不接受非正数**：死亡结算（`unit.died` 发布）发生在
   `WorldSim.Tick` 的 `EventDispatch` 阶段（第 7 步），晚于本模块 tick 处理器挂载的
   `TriggerEvaluation` 阶段（第 6 步）——因此"死亡当次 tick 立即复活"在时序上不可行，最早只能是
   下一次 tick 的 `TriggerEvaluation`。`DeathPolicyHost` 构造函数对 `RespawnDelayTicks < 1` 直接
   抛 `ArgumentException`（构造期快速失败，不是运行期悄悄降级为 1）。

3. **`respawn_point` 的延迟队列不区分 `Continuous`/`Discrete` 步**：两种时间模型下死亡都可能
   发生，复活延迟按"tick 数"（`ITickPhaseHandler.Execute` 被调用的次数）计，不按秒数（`Discrete`
   步没有秒数概念，`SimStep.Dt` 恒为 0）——同一份倒计时逻辑天然对两种模式都成立，不需要为离散
   模式单独分支。

4. **`permadeath` 删除的"当前槽"默认等于 `AutosaveSlotId`**：06 第 4.6 节"死亡即结束当前存档
   周期"没有定义"当前存档周期"对应哪个槽 id 这一框架层概念（多存档槽 vs 单存档槽是游戏层决策）。
   `DeathPolicyOptions.CurrentSlotIdProvider` 为 `null`（默认）时退化为删除
   `AutosaveSlotId`——单存档槽的游戏这一默认值已经足够；多存档槽的游戏（有独立于自动存档槽的
   "当前正在进行的存档周期"概念）应显式提供该委托。

5. **`reload_save` 不做任何"复活单位"的专门处理**：直接调用 `ISaveSystem.Load(AutosaveSlotId)`——
   读档本身会按 10 号文档固定顺序恢复全部已注册段（含玩家位置、生命值经各自 `IPersistable`），
   不需要本模块额外调用 `ReviveUnit` 或做任何单位状态改写，"回退到最近一次存档"这句话本身就是
   "整体读档"的准确技术含义。读档失败（如自动存档槽从未写入过）只记一条诊断错误，不抛异常、
   不做任何回退处理（保留死亡后的世界状态原样，同 `ISaveSystem.Load` 契约本身"失败不改变当前
   运行期状态"的既有语义）。

6. **`EffectivePolicy` 在构造期一次性解析，运行期不重新读取 `CombatOptions.DeathPolicy`**：
   `CombatOptions.DeathPolicy` 是构造期口味配置（同 `SkillOptions`/`CombatOptions` 其余字段），
   本仓库没有任何"运行时热切换战斗口味配置"的先例，`DeathPolicyHost` 与其它 L4 宿主一样，在
   构造函数里把最终生效值算好存成只读属性，不在每次死亡事件到来时重新计算。

## 不负责什么

- 不解析/查找具体的复活点坐标算法本身（如"取最近出生点"而非固定 `spawn_points[0]`的距离比较）
  ——这属于 `ResolveDefaultSpawn` 委托实现方（`GameplayAssembly` 装配的
  `TeleportTargetResolver.Resolve`）的职责，本模块只负责"死亡后调用这个委托、延迟 N tick 后调用
  复活委托"这条编排逻辑。
- 不做尸体清理、掉落物生成、复活无敌帧一类周边表现/规则——06/08 文档未把它们划给死亡复活策略，
  分别属于既有的 `core/gameplay/loot`（死亡掉落）/表现层（复活特效）职责。
- 不持久化"待复活队列"：`respawn_point` 策略的延迟复活队列是纯运行期瞬时状态，不实现
  `IPersistable`——如果死亡后、复活前的极短窗口内恰好触发存档并读档，待复活的记录会丢失（复活
  永远不会发生）。这是一处已知的边界情况，未在 06/10 文档中找到明确要求覆盖，判定为可接受的
  简化（该窗口通常只有 1 个 tick，实际游戏中触发概率极低）。
- 不管理 AI/生物单位的死亡后续（复活、移除、刷新计时）——那是 `core/gameplay/spawn`
  （`respawn_policy`）与 `core/carriers/creature`（`Despawn`/`creature.despawned`）既有职责，
  本模块对非玩家单位的 `unit.died` 直接忽略（见判断记录、08 归属说明惯例）。
