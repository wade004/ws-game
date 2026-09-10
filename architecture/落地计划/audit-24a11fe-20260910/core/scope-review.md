# Core scope and responsibility review — 1.16.1

冻结点：HEAD `24a11fe28f9647cd532c41f56f7ab18c00fb8516` / VERSION `1.16.1`。本子审计只审 framework core 的公开契约、resident reload、跨模块存档/状态和 73cb55e 效果免疫；不把游戏内容、Unity 接线或发布环境缺失推成 framework bug。

| 范围 | 框架责任 | 合法触发与前提 | 本版证据 | 结论 |
|---|---|---|---|---|
| Quest reload | `QuestHost` 定义索引与运行进度迁移 | 活跃任务、同 id 定义变化、后续新索引更新 | 14 CORE114-01 tests | 当前通过；历史 P2 已修 |
| Economy reload | `EconomyHost` vendor/item 运行库存与策略对账 | registry validation 通过、resident host、策略/上限转换后 `Update` | 5 CORE114-02 tests | 当前通过；历史 P2 已修 |
| Stat reload | `StatHost` derived cache 重算，保留 explicit base | 先查旧 cache，reload `stat.definition`，发布完成事件 | 4 CORE114-03 tests | 当前通过；历史 P2 已修 |
| ExprValue validation | shared parser + Quest/Dialog formal rules | 正式 `ContentValidationAssembly.Run`、非法 Id/合法形状 | 7 CORE114-04 tests | 当前通过；历史 P2 已修 |
| Equipment set | `EquipmentHost` applied threshold ledger | 合法 item.set/template 引用，reload 后装备变化触发重算 | 2 candidate tests | 条件性通过；reload-time sweep 是已知边界 |
| Effect immunity | `EffectDispatcher` entry gate / combat Resolver | 动态 aura 或静态 provider，原语分别施放 | 13 EffectImmunityGate tests | 当前通过；不覆盖 ApplyAura 本身免疫 |
| FindUnits/Gobj | `CarriersAssembly.DefaultSpatialSyncKinds` + `EntitySpatialSyncHost` + `SkillHost.FindUnits` | 默认 spatial config 与 Gobj 共区时才有触发；显式 config 需消费方过滤 | `EntitySpatialSyncHostTests`、`ISkillHost_FindUnitsTests` | 默认不登记 Gobj，候选不成立；显式接入是调用方责任 |
| Quest/save | QuestPersistable + SaveSystem integration | snapshot replace/rollback/idempotence/跨 unit | 6 selected tests | 当前通过；未作 Unity runtime 声明 |
| Resource state | PowerHost/Progression persistable | current/max、level/xp/growth、reload/rebuild | 6 selected tests | 当前通过 |
| Item/unit state | Inventory/Equipment/Unit persistables | 快照替换、装备 linkage、位置与身份 | 25 selected tests | 当前通过 |

“当前通过”表示选定回归测试和独立 probe 在冻结源码中通过；不等于完整 solution、Unity 或 IL2CPP release gate。旧 audit raw logs 只用于形成候选，不作为本版结果解释。

## 解耦边界

Core owns definitions, runtime state, validation, persistence contracts and effect settlement. A game consumer owns content rows, input, assets and Unity bootstrapping. The selected tests use in-memory sources, framework assemblies and stubs; they prove framework behavior under those explicit preconditions. No consumer configuration or missing Unity wiring was upgraded into a core finding.

## Reproduction safety

The archive entry `repro/prepare-and-run.ps1` accepts `-FrozenRoot` and `-OutputRoot`, checks the exact frozen HEAD/VERSION, copies only `core`, `presentation`, `adapters/stub` and `Directory.Build.props` into `build/core/source`, copies the repro files when needed, and passes `FrameworkRoot` to the independent probe project. All `bin`/`obj` and console captures stay below `build/core`. It invokes the two bounded runners and never runs the full solution or Unity. It refuses an existing build image or raw log and never recursively deletes. The frozen source status was clean before the copy; file-by-file SHA256 parity is recorded in `evidence/source-provenance.sha256`.
