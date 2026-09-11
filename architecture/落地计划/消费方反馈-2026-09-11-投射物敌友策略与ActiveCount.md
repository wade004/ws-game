# 消费方反馈回复：投射物碰撞敌友关系策略与 ActiveCount 清理一致性（2026-09-11）

- 反馈来源：消费方游戏项目"框架反馈"提交的 `M-C10_投射物与技能位移通用能力反馈.md`
  （`D:\workespace\ws-game-wow\docs\框架反馈\M-C10_投射物与技能位移通用能力反馈.md`，提交
  `3427bc2`）第 3、4 两节；依据冻结基线 `ws-game 1.22.0`（commit
  `84a63b77e5b2d88615606538db3b24cfa2fc429f`），已核对通用源码路径
  `core/rules/common/contracts/ISkillHost.cs`/`core/rules/skill/core/EffectDispatcher.cs`/
  `core/carriers/projectile/core/ProjectileHost.cs`。本次落地基于 `main` 1.24.0（commit
  `9cd22f6`），隔离工作树分支 `waq/projectile-policy`。
- 反馈第 1、2 两节（连续技能位移、动态地面坐标施法请求）不在本次改动范围，按反馈原文"能力建议"
  分类留待独立评估；本回复只覆盖第 3 节（敌友关系策略）与第 4 节（`ActiveCount` P3 可观测性项）。
- 提交：见文末"六工程与门禁结果"下方提交哈希清单（ADR + 契约/schema、实现 + 测试、文档 + 本回复
  三笔）。

## 观察（消费方反馈原文摘要）

**第 3 节**：投射物已有沿途候选查询（`HitQueryTags`）、命中行为、射程/到期与命中效果回灌；施法
选中目标校验已有。冻结源码没有逐发敌友关系策略；诊断复现场景（施法者 `(0,0)`、目标 `(6,0)`、
友方候选 `(2,0)`）里友方候选被命中，实际伤害为 10。消费方明确这不是目标校验缺陷，请求提供可选
敌友关系策略：明确施法者排除、候选标签、友/敌/中立关系、目标锁定、穿透与命中效果回灌的优先级；
默认策略需保持兼容；相同场景切换默认与显式策略需逐事件核对。

**第 4 节（P3）**：`WorldSim.ClearAll` 后实体数量立即为 0，`ProjectileHost.ActiveCount` 在下一次
正 dt 原生更新前暂为 1，随后归零；未观察到幽灵伤害或泄漏。请求明确 `ActiveCount` 与"当前存活数量"
的契约，或提供统一 reset 完成查询，使 `ClearAll` 后的观察时点可区分。

## 契约决定

按消费方"建议合同"给出的方向拍板，详见 [ADR-0028](../adr/0028-投射物碰撞的敌友关系策略.md)（完整
裁决优先级、备选方案取舍见该文档）：

1. **敌友关系策略**：`projectile` 效果新增两个可选参数——`relation_policy`
   （`default`/`hostile_only`/`friendly_only`/`locked_target_only`，缺省 `default`）与
   `pierce_order`（`nearest`/`hostile_first`，缺省 `nearest`，仅 `pierce` 命中行为下多候选时有
   意义）。裁决优先级：**施法者排除 → 目标锁定 → 关系筛选 → 标签筛选 → 穿透计数 → 命中效果
   回灌**（前三步同时满足才算候选的逻辑与门；标签筛选在候选查询阶段——`ISpatialQuery.QueryLine`/
   `QueryRadius`——已经完成，属集合层面的性能优化，不改变最终纳入结果，见 ADR-0028"裁决优先级"
   一节）。施法者排除始终无条件生效，不受 `relation_policy` 取值影响，不提供关闭开关（ADR-0028
   "备选方案"一节说明理由：没有任何消费方提出关闭这条规则的需求，允许关闭反而是明显的误伤自己
   风险点）。阵营判定复用既有 `Core.Numbers.Faction.IFactionMatrix`（与
   `core/rules/skill` `SkillHost.FindUnits` 对 `UnitFilter.Relation` 的判定同一口径，两处独立
   实现）；`ProjectileHost` 新增可写属性 `IFactionMatrix? Factions { get; set; }`，`Core.Carriers.
   Assembly.CarriersAssembly` 装配完成后自动回填为与 `Rules.Factions` 同一实例（不新增构造参数，
   ABI 只增不改，见 ADR-0028"阵营矩阵注入"一节循环依赖处理手法）。未注入阵营矩阵时，
   `hostile_only`/`friendly_only`/`hostile_first` 在生成当下（不是每 tick）退化为
   `default`/`nearest` 并记一次诊断警告——**目标校验与候选筛选是两个不同阶段，互不替代**
   （施法时的合法性校验 vs. 飞行/到期时沿途候选谁被记为命中），见 ADR-0028、06 第 3.2 节同日
   勘误对这一点的专门说明。
2. **`ActiveCount` 契约**：明确为"当前存活（未到期/未命中终结/未被清除）的投射物数"。新增只读
   属性 `IsQuiescent`（`ActiveCount == 0` 的别名，语义"无存活投射物、且无待处理命中"）与公开方法
   `ClearAll()`（立即清空运行期簿记，不触碰 `IWorldSim`）。`Core.Gameplay.Assembly.
   GameplayAssembly.LeaveMap`（既有"出图"收尾入口，覆盖"地图切换"这一 `IWorldSim.ClearAll` 最主要
   的既有触发点）已接上 `Carriers.Projectiles.ClearAll()`；脱离场景路由、直接调用
   `world.ClearAll()` 的调用方（例如独立测试）若需要 `ActiveCount` 立即归零，需自行显式一并调用
   `ProjectileHost.ClearAll()`——不调用也不会产生幽灵伤害（既有行为不变，`Advance` 对已从世界移除
   的投射物直接跳过、不回灌效果），只是 `ActiveCount` 会在下一次正 dt 之前保持"清空前"的数值，
   与消费方诊断的既有观察完全一致（本次收口不改变这段既有语义，只是把它明确写成契约并提供显式
   对齐手段）。

## 新增公开成员签名

| 类型/成员 | 签名 | 说明 |
|---|---|---|
| `Core.Carriers.Projectile.ProjectileHost.Factions` | `public IFactionMatrix? Factions { get; set; }` | 可写属性，缺省 `null` |
| `Core.Carriers.Projectile.ProjectileHost.IsQuiescent` | `public bool IsQuiescent { get; }` | `ActiveCount == 0` 的别名 |
| `Core.Carriers.Projectile.ProjectileHost.ClearAll` | `public void ClearAll()` | 立即清空运行期簿记 |
| `projectile` 效果参数 | `relation_policy`:Enum（`SkillSchemas.ProjectileRelationPolicyValues`）、`pierce_order`:Enum（`SkillSchemas.ProjectilePierceOrderValues`） | 均可选，缺省 `default`/`nearest` |

`ProjectileHost` 既有构造函数签名（六个参数）未改动；`ActiveCount` 既有签名未改动（只补充契约
文档）。`Core.Carriers.Assembly.CarriersAssembly`/`Core.Gameplay.Assembly.GameplayAssembly` 均无
公开签名变化（只在内部装配/收尾流程里补一行接线）。

## 最小场景事件对照（默认策略 vs. 显式策略）

复现消费方反馈原文"最小观察例"：施法者 `(0,0)`、（选中）目标即敌方候选 `(6,0)`、友方候选
`(2,0)`，`impact_on_first` 命中行为、`on_hit_effects=[{school_damage, base_value:10}]`（见
`core/carriers/projectile/tests/ProjectileHostTests.cs`
`RelationPolicy_Default_HitsNearerFriendlyCandidate_CompatibleWithPreAdrBehavior`/
`RelationPolicy_HostileOnly_SkipsFriendlyCandidate_HitsHostileBehindIt` 两例逐字节复现下表）：

| 策略 | 候选查询（标签过滤后） | 施法者排除 | 关系筛选 | 命中结果 | `on_hit_effects` 回灌 |
|---|---|---|---|---|---|
| `default`（缺省，兼容既有行为） | 友方候选（距起点 2）、敌方候选（距起点 6） | 施法者本不在候选内 | 不做过滤 | 命中友方候选（更近，`impact_on_first` 取第一个） | 1 次，`TargetId=` 友方候选、`base_value=10`（与消费方诊断"友方命中伤害 10"完全一致） |
| `hostile_only`（显式） | 同上 | 同上 | 友方候选判不通过（Reaction≠Hostile）、敌方候选判通过 | 命中敌方候选 | 1 次，`TargetId=` 敌方候选、`base_value=10` |

同一最小场景另覆盖：`friendly_only`（只命中友方候选，敌方候选判不通过）、`locked_target_only`
+ `pierce`（只命中施法时锁定的目标，路径上的友方候选即使在射程内也被跳过）、
`pierce_order=hostile_first`（`pierce` 命中行为下，距起点更远的敌方候选先于更近的友方候选被
处理）、未注入 `IFactionMatrix` 时 `hostile_only`/`hostile_first` 退化为 `default`/`nearest`
并记诊断警告、`hostile_only` 不绕过既有地形阻挡判定（墙挡住仍然是墙挡住）、施法者自身即使落在
到期爆炸半径内也不会被算作候选——全部逐条落为独立测试用例，见下"测试清单"。

## `ActiveCount` 四窗口结果

复现消费方反馈原文"最小场景"（创建一个活动投射物，执行 `ClearAll`，检查零 dt、正 dt、暂停、新
世界四个窗口）：

| 窗口 | 操作 | `ActiveCount` | `IsQuiescent` | `Sink.Applied`（伤害/效果） |
|---|---|---|---|---|
| 暂停（`ClearAll` 后不驱动任何 `Advance`） | `world.ClearAll(); host.ClearAll();` | 立即 `0` | `true` | 空 |
| 零 dt | 上述之后 `host.Advance(0.0)` | `0` | `true` | 空 |
| 正 dt | 上述之后 `host.Advance(1.0)` | `0` | `true` | 空 |
| 新世界（`ClearAll` 后继续正常使用同一 `ProjectileHost`） | 生成新单位并 `Spawn` 一枚新投射物、`Advance(1.0)` | `1 → 0`（正常命中生命周期） | 命中后 `true` | 1 次，命中新目标，无旧状态干扰 |

回归契约本身（只调用 `world.ClearAll()`、不调用 `host.ClearAll()`）：`ActiveCount` 保持清空前的
数值（`1`）、`IsQuiescent=false`，但 `Sink.Applied` 全程为空（无幽灵伤害，与消费方诊断结论一致）；
下一次 `host.Advance(1.0)`（正 dt）后自愈归零——见
`WorldClearAll_WithoutHostClearAll_LeavesActiveCountStale_UntilNextPositiveDtAdvance_ThenSelfHealsWithoutGhostDamage`。

## 新增测试清单

- `core/carriers/projectile/tests/ProjectileHostTests.cs`"9. ADR-0028：敌友关系策略""10.
  ADR-0028：ActiveCount/IsQuiescent/ClearAll 契约"两节，共 15 例：
  `RelationPolicy_Default_HitsNearerFriendlyCandidate_CompatibleWithPreAdrBehavior`、
  `RelationPolicy_HostileOnly_SkipsFriendlyCandidate_HitsHostileBehindIt`、
  `RelationPolicy_FriendlyOnly_HitsOnlyFriendlyCandidate_SkipsHostile`、
  `RelationPolicy_LockedTargetOnly_Pierce_HitsOnlyLockedTarget_SkipsOthersInPath`、
  `RelationPolicy_LockedTargetOnly_WithoutTarget_HitsNothing`、
  `PierceOrder_HostileFirst_ProcessesFartherHostileBeforeNearerFriendly`、
  `RelationPolicy_HostileOnly_WithoutFactionsInjected_DegradesToDefault_AndWarns`、
  `PierceOrder_HostileFirst_WithoutFactionsInjected_DegradesToNearest_AndWarns`、
  `RelationPolicy_HostileOnly_StillBlockedByWall_DoesNotBypassTerrainBlocking`、
  `ExcludeCaster_ImpactOnExpiry_SourceNeverHit_EvenWithinImpactRadius`、
  `ClearAll_ImmediatelyZeroesActiveCount_NoAdvanceCall`、
  `ClearAll_ThenAdvanceZeroDt_StaysZero_NoGhostDamage`、
  `ClearAll_ThenAdvancePositiveDt_StaysZero_NoGhostDamage`、
  `ClearAll_ThenSpawnNewProjectileInSameHost_WorksNormally_NoStaleStateInterference`、
  `WorldClearAll_WithoutHostClearAll_LeavesActiveCountStale_UntilNextPositiveDtAdvance_ThenSelfHealsWithoutGhostDamage`
  （真实 `WorldSim`/`WorldUnitAccess`，`IFactionMatrix` 桩 `StubFactionMatrix`，见
  `ProjectileTestSupport.cs`）。
- `core/carriers/assembly/tests/ADR0028_ProjectileFactionsBackfillTests.cs`（2 例，真实
  `CarriersAssembly` 装配，非桩）：`Construct_BackfillsProjectilesFactions_SameInstanceAsRulesFactions`
  （验证回填的是与 `Rules.Factions` 同一实例，不是另造一份）、
  `Construct_WithFactionData_ProjectilesFactionsReflectsRealReactionMatrix`（用真实
  `fac.faction`/`fac.reaction_matrix` 数据 + 运行期 `SetReaction` 覆盖验证回填进去的确实是一个
  正常工作的 `IFactionMatrix`，不只是非空引用）。
- `core/rules/skill/tests/C10c_ProjectileRelationPolicyParamsTests.cs`（2 例，验证"技能数据 →
  `EffectContext`"这一段的参数透传契约，分层边界同 `EffectPrimitiveDispatchTests.cs`"projectile"
  一节"真实生成路径由 core/carriers/projectile 模块测试覆盖，不在本模块依赖范围内"——本文件不
  重复覆盖 `ProjectileHost` 自身的关系裁决逻辑）：
  `RelationPolicyAndPierceOrder_PassThroughUnchangedToEffectContextParams`、
  `RelationPolicyAndPierceOrder_OmittedInSkillDef_AbsentFromEffectContextParams`。

既有投射物测试（`ProjectileHostTests.cs` 此前 18 例）、既有六工程全部测试均未删改，逐字节保留，
本次改动后全部继续通过。

## 六工程与门禁结果

- `dotnet build Core.sln -c Release --artifacts-path <scratchpad>\build_waq_check`：0 警告 0 错误。
- `dotnet test Core.sln -c Release --artifacts-path <scratchpad>\build_waq_check`：六工程全绿——
  Tests.Foundation 1026、Tests.Numbers 142、Tests.Carriers 436（含本次新增 17 例：
  ProjectileHostTests 15 例 + ADR0028_ProjectileFactionsBackfillTests 2 例）、Tests.Rules 534
  （含本次新增 2 例 C10c）、Tests.PresentationCommon 615、Tests.Gameplay 685。
- `python -m pytest toolchain/tests -q`：220 passed, 4 skipped。
- `python toolchain/validate_data.py --strict`：tables 63, records 308, errors 0, warnings 0。
- `dotnet run --project toolchain/validator -- --schema-audit --allowlist toolchain/schema_audit_allowlist.json`：
  tables 63, fields 808, errors 0, warnings 0。
- `powershell -File toolchain\abi_probe.ps1 -BaselineZip D:\workespace\ws-game\dist\ws-game-1.12.0.zip -ArtifactsPath <scratchpad>\build_waq_check -SkipIfBaselineMissing:$false`：
  旧编译 consumer（针对 1.12.0 基线）换上当前工作树 Release DLL、不重新编译，运行正常；
  `abi_surface compare`：`breaks=0 allowed=0 additions=391`（`RESULT=OK`；新增行含
  `ProjectileHost.Factions`/`IsQuiescent`/`ClearAll` 三个新公开成员，其余为基线 1.12.0 至今累计的
  历次既有 ADR 新增，均为新增不计入破坏）。
- `powershell -File check.ps1 -SkipUnity -ArtifactsPath <scratchpad>\build_waq_check -LogFile <scratchpad>\waq_check.log`：
  全部 24 步 PASS（Unity 相关 6 项按 `-SkipUnity` 跳过；`check.ps1` 自身内置的 ABI 探针步骤因本
  工作树 `dist/`（`.gitignore` 排除的本机构建缓存目录）下没有本地基线 zip，按既定
  `SkipIfBaselineMissing` 语义判定 SKIP，不是 PASS 也不是失败——本文档上一条已用主树
  `dist/ws-game-1.12.0.zip` 单独跑过一次完整 ABI 探针并确认 `breaks=0`），总用时约 92s。

提交（隔离工作树分支 `waq/projectile-policy`，主树 `D:\workespace\ws-game` 全程未被触碰）：

| 提交 | 内容 |
|---|---|
| ①ADR + 契约/schema | `architecture/adr/0028-投射物碰撞的敌友关系策略.md`、`architecture/adr/README.md`、`core/rules/skill/schema/SkillSchemas.cs`、`core/rules/skill/schema/README.md` |
| ②实现 + 测试（含 ActiveCount） | `core/carriers/projectile/core/ProjectileHost.cs`、`core/carriers/assembly/CarriersAssembly.cs`、`core/gameplay/assembly/GameplayAssembly.cs`、`core/carriers/projectile/tests/ProjectileHostTests.cs`、`core/carriers/projectile/tests/ProjectileTestSupport.cs`、`core/carriers/assembly/tests/ADR0028_ProjectileFactionsBackfillTests.cs`、`core/rules/skill/tests/SkillTestSupport.cs`、`core/rules/skill/tests/C10c_ProjectileRelationPolicyParamsTests.cs` |
| ③文档 + 回复 | `architecture/05_对象模型与世界.md`、`architecture/06_规则层_属性技能战斗AI.md`、`core/carriers/projectile/README.md`、本文档 |

（三笔提交的具体哈希见 `git log --oneline -3` 分支 `waq/projectile-policy`。）

## 对消费方的提示

1. **`relation_policy`/`pierce_order` 均为可选字段，缺省行为逐字节不变**：不改动任何
   `skill.def` 的既有游戏无需做任何改动；诊断复现的"友方候选被命中"在缺省策略下仍会发生（这是
   既有行为，不是本次要修的 bug），需要避免误伤时显式声明 `relation_policy: "hostile_only"`。
2. **`hostile_only`/`friendly_only`/`hostile_first` 依赖装配根注入 `IFactionMatrix`**：本仓库
   `Core.Carriers.Assembly.CarriersAssembly` 已自动完成注入（与 `SkillHost.FindUnits` 共用同一份
   阵营矩阵，运行期 `SetReaction` 覆盖两侧同步可见）；若消费方自行手工构造 `ProjectileHost`（不
   经 `CarriersAssembly`），需要自行设置 `ProjectileHost.Factions` 属性，否则这三项取值会静默
   退化为兼容默认行为并记一条诊断警告（不是报错，也不是悄悄按"全部判不通过"处理）。
3. **`ActiveCount` 立即归零需要显式调用 `ProjectileHost.ClearAll()`**：经本仓库
   `Core.Gameplay.Assembly.GameplayAssembly.LeaveMap`（地图切换既有收尾入口）触发的
   `world.ClearAll()` 已自动接线；若消费方在框架"出图"入口之外的路径直接调用
   `IWorldSim.ClearAll()`（例如脱离场景路由的手工重置/测试），需要自行一并调用
   `ProjectileHost.ClearAll()` 才能让 `ActiveCount`/`IsQuiescent` 立即反映"已清空"，否则会保持
   清空前的数值直到下一次正 dt 的 `Advance`（不产生幽灵伤害，纯粹是这段窗口期内 `ActiveCount`
   本身的可观测性滞后，与消费方诊断的既有现象完全一致）。
4. **目标校验与候选筛选是两个不同概念**：`CastSkill` 对选中目标的合法性校验通过，不代表投射物
   飞行/到期时一定会命中该目标（可能被墙挡住、可能配置了 `locked_target_only` 但目标已移出射
   线）；候选筛选（含新增的敌友关系策略）的结论也不能反过来推翻已经做出的目标校验结果。

## 异常与未完成项

无遗留异常。反馈原文第 3 节"验证条件"（相同场景切换默认与显式策略、逐事件核对候选/命中/伤害/
清理）与第 4 节"验证条件"（零 dt/正 dt/暂停/新世界四个窗口）均已逐条覆盖，见上"最小场景事件对照"
"`ActiveCount` 四窗口结果"两节与对应测试清单。反馈第 1、2 两节（连续技能位移、动态地面坐标施法
请求）明确不在本次范围，留待独立评估；反馈原文"以下文案可直接转发……只描述通用原语边界"的免责
声明一节本身不需要框架侧动作。
