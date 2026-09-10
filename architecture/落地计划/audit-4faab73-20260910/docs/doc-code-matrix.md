# ws-game 1.16.2 文档—代码—接线—证据矩阵

## 审计边界与证据口径

- 冻结根：`D:\workespace\ws-game-artifacts\audit-4faab73-frozen`，detached HEAD `4faab73e7081f2984e7addb88fee051b6c0d3d02`，tag `v1.16.2`，`VERSION=1.16.2`。本轮只读冻结根，报告写入 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\docs`。
- 覆盖计数：architecture `00`～`14` 共 15 个 Markdown、5076 行；`architecture/adr/0001`～`0023` 共 23 个 ADR、1046 行，另读 `adr/README.md` 索引；core 73、presentation 12、adapters 5、games 2、editor 1、toolchain 5 个目录下 README，共 98 个 README、13589 行。已读根 `README.md`、`CHANGELOG.md`、能力索引/落地计划，以及上述模块 README、schema README、适配器/模板/工具链文档。
- 关联代码按真实生产装配根、公开合同、核心实现和已有测试定点核对；未声称逐行全仓审计。未覆盖范围：非目标模块的全部实现细节、构建缓存/Unity `Library`/二进制内部、外部消费方仓库源码；外部消费方证据只按当前 ADR/归档报告中的可复核指针标注。
- 三种证据分开记录：`API/实现` 是当前源码静态事实；`默认接线` 是生产组合根静态事实；`Runtime` 只认保存的 XML/log/消费方报告。本轮交付验证记录在 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\output\raw\check.log`：六个 .NET 测试程序集合计 3045 passed，toolchain 为 176 passed/4 skipped，`-SkipUnity` 门禁为 17 PASS/7 SKIP；正式 `D:\workespace\ws-game\dist\ws-game-1.16.2.zip/.lock` 与 ABI/indexer 记录以 `delivery/run5/output/raw/formal-and-indexer.raw.txt` 和 `delivery/run5-console.log` 为准。Unity 专项证据由主报告另行汇总；本矩阵不把专项重跑边界写成产品缺陷。
- 1.16.2 目录化发布快照 `dist/1.16.2/MANIFEST.txt` 记录 `git_commit: 4faab73`、六个核心程序集、`Adapters.Stub.dll`、`Validator.dll` 哈希；正式 `D:\workespace\ws-game\dist\ws-game-1.16.2.zip/.lock` 已由 `delivery/run5/output/raw/formal-and-indexer.raw.txt` 核验，不把目录化 MANIFEST 单独当成正式 zip/lock 验证。

## 三个生产入口的定位

| 入口 | 当前代码证据 | 数据根与内容 | 定位/责任结论 |
|---|---|---|---|
| `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs` | `:2-14` 说明灰盒引导根；`:90-103`；`:114-137` | `data/_framework` + `data/_sample`；`world.sample_field`、`unit.sample_player`、`skill.sample_strike` 等 sample id | 灰盒工作台 `GreyBox.unity` 的示例/验证入口。sample id 是中性样例内容，不是 Core 反向依赖，也不是新游戏必须复用的通用入口。 |
| `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs` + `ShellRoot.cs` | `FrameworkResidentHost.cs:79-103,270-302`；`ShellRoot.cs:29-60,68-98` | 常驻框架宿主仍加载 `data/_framework` + `data/_sample`；`ShellRoot.Awake` 调 `FrameworkResidentHost.Ensure()` | Unity 工作台 Shell 的常驻框架部分与 UI 流程入口。sample ids/`data/_sample` 属工作台示例闭环；`ShellRoot` 负责菜单/存档槽/新游戏 UI，不应被误写成通用 Framework API。 |
| `games/_template/Runtime/GameBootstrap.cs` | 文件头 `:2-27` 明确“游戏层组合根”；`:47-61`；`:167-193` | `data/_framework` + 可改名的 `data/game`；id 与策略由 `GameOptions`/Inspector 提供 | 新游戏复制模板后的游戏侧组合根。它复用框架程序集，但不复用工作台 sample 数据根；由游戏决定模块装配、起始状态、口味和内容。 |

## Architecture 00～14

| 文档 | 当前代码/合同锚点（存在性） | 默认接线与 Runtime 证据 | 结论与责任分类 |
|---|---|---|---|
| `architecture/00_架构总则.md`（v3，217 行） | 强制约束 `:70-91`：解耦、适配层、固定步、数据驱动、Expr、存档、游戏口味；实现分布于 `core/*` 合同与五个程序集 | API/实现静态对齐；Runtime 不由本章单独证明 | 架构总则/非具体游戏设计；能力验证由各层与入口承担。 |
| `architecture/01_分层与依赖.md`（v3，255 行） | 七层和模块表 `:25-104`；依赖/游戏层边界 `:110-198`；真实程序集为 `Core.Foundation/Numbers/Rules/Carriers/Gameplay` 与 `Presentation.Common` | `RulesAssembly.cs:156-364`、`CarriersAssembly.cs:135-237`、`GameplayAssembly.cs:234-410`、`PresentationAssembly.cs:267-599` 静态显示组合方向；当前 delivery `output/raw/check.log:64-107` 提供 .NET 层交付证据 | 分层规则属框架合同；具体游戏组装和策略是游戏责任。 |
| `architecture/02_引擎适配层.md`（v3，351 行） | L-1 合同 `:26-27`、接口清单 `:28-238`；实现接口位于 `core/foundation/engine_adapter/contracts`，Unity/Stub/Headless 实现在 `adapters/*` | 本轮 delivery 的 .NET/Python 门禁与正式包哈希通过；Unity adapter Runtime 专项由主报告另列 | 框架定义窄接口；引擎实现与性能承担者是适配器。`findPath` 跨帧预算、完整空间索引化已在 `:168-169` 收窄为实现方边界，不是框架缺陷。 |
| `architecture/03_运行时骨架.md`（v2，327 行） | 状态机/固定步/tick/场景/输入合同；`core/foundation/app_lifecycle`、`core/foundation/sim_loop/core/WorldSim.cs`、`core/foundation/scene_router/core/SceneRouter.cs`、`core/foundation/input_map/core/InputMapHost.cs`；Unity 注册见 `GameFoundationBootstrap.cs:16-26` 与 `GameBootstrap.cs:121-124` | 本轮 delivery `check.log:64-107` 有六工程 .NET 测试与门禁汇总；`-SkipUnity` 明确跳过 Unity/独立版/消费方，Unity 专项证据另行汇总 | Runtime 骨架机制已实现；ATB 是 `ADR-0013` 明确预留延期项，不列非目标或能力缺陷。输入动作内容和游戏策略由数据/游戏决定。 |
| `architecture/04_数据与内容管线.md`（v6，606 行） | `DataRegistry.LoadAll` `core/foundation/data_registry/core/DataRegistry.cs:177-240`；多根覆盖 `:623-685`；schema/validator `presentation/assembly/ContentValidationAssembly.cs:98-207`、`toolchain/validator/Program.cs` | 本轮 delivery `check.log:149-153,260-262` 合并根/框架根校验与 schema-audit 均 PASS，并记录 1 条 override；具体记录与覆盖规则以当前代码为准 | 表/引用/字段范围/复合结构/元数据由框架提供；具体业务规则由模块 `IValidationRule` 注册。孤儿检查等在文档中是建议或未实现，不能误判为已承诺门禁。 |
| `architecture/05_对象模型与世界.md`（v2，424 行） | `core/foundation/sim_loop/contracts/Entity.cs`、`WorldSim.cs`；`core/carriers/*`；移动/导航 `core/carriers/unit/core/MovementHost.cs`、`engine_adapter/contracts/INavigation2D.cs`；空间同步 `CarriersAssembly.cs:125-180` | `RulesAssembly`/`CarriersAssembly` 静态注入；本轮 delivery 六工程 .NET 测试通过，Unity/Standalone 专项由主报告另列 | 逻辑对象不依赖引擎，世界/移动/刷新由框架机制承载；具体地图、阻挡集合、轨迹碰撞策略由游戏/适配器负责。默认空间同步不登记 `gobj`（`CarriersAssembly.cs:89-103`），宿主自定义索引时自行过滤。 |
| `architecture/06_规则层_属性技能战斗AI.md`（v2，517 行） | `RulesAssembly.cs:331-364` 构造 Combat/Skill；`SkillHost.cs`、`AuraHost.cs`、`CombatHost.cs`、`TargetHost.cs`、`AiHost.cs`；`SkillHost.FindUnits` 已实现 `:274-320` | 本轮 delivery 六工程 .NET 测试通过；默认 `RulesAssembly` 把 `Spatial`/`Factions` 传入 Skill；ADR-0023 测试见 `core/rules/skill/tests/ADR0023_StackCategoryStaticGroupingTests.cs` | 规则机制已提供；控制递减、技能槽数量、目标链等是口味/策略。`stack_category` 已明确只是静态分组，跨定义运行时共享槽位是“未提供”，不是 AuraHost 缺陷。 |
| `architecture/07_载体层_物品生物物件.md`（v1，315 行） | `core/carriers` contracts/core：`EquipmentHost.cs`、`InventoryHost.cs`、`GameObjectHost.cs`、`SummonHost.cs`、`ProjectileHost.cs`、`CarriersAssembly.cs` | Carriers tests 与 `EntitySpatialSyncHost` 静态证据；已有归档 Unity 装备/模型证据不属于本轮新 Runtime | 载体通用状态/交互/装备/召唤机制属框架；天赋完整点数管理、具体内容、召唤跟随策略取值由游戏接入。`door/chest/gather_node` 内置行为按 ADR-0001 勘误可由载体直接结算。 |
| `architecture/08_玩法层_掉落任务对话关卡.md`（v2，407 行） | `core/gameplay/assembly/GameplayAssembly.cs:234-410,1055-1180`；Quest/Loot/Dialog/Encounter/Difficulty/Achievement/Economy/Spawn 各 Host 与 `GameplayAssembly` | 本轮 delivery 六工程 .NET 测试通过；三处生产根可透传 owner/day/vendor，默认 null/0；Unity/Standalone 专项由主报告另列 | 框架提供玩法 Host、状态机和扩展点；剧情、escort 自动路线、日循环、商店/归属回调和内容是游戏/宿主责任。`Quest.Update` 存在但自动驱动是否接入由游戏决定。 |
| `architecture/09_表现层.md`（v4，476 行） | `presentation/assembly/PresentationAssembly.cs:157-599`、`presentation/render`、`feedback_binder`、`vfx_sfx`、`ui`；Unity `UnityViewFactory`/`UnityModelView`/`UnitySpriteView` | 三入口传 `renderer3D`/`hitFrameSource`/`RenderOptions` 的静态接线见 `GameFoundationBootstrap.cs:454-517`、`FrameworkResidentHost.cs` 同构段、`GameBootstrap.cs`；本轮 `-SkipUnity` 门禁没有覆盖 Unity，专项 XML/log 由主报告另行汇总 | 表现只读逻辑并消费事件；资源/动画/VFX/UI 机制已提供，具体美术资产、主题和开关由游戏/适配器负责。 |
| `architecture/10_存档与持久化.md`（v1，234 行） | `core/foundation/save_system/core/SaveSystem.cs`、`ReplayPlayer.cs`、`ISaveSystem`/Persistable 合同；Gameplay/Carriers Persistable 实现 | 本轮 delivery 六工程 .NET 测试通过；跨进程/Unity 载入专项是否通过由主报告另列 | 存档版本/迁移/确定性回放机制属框架；存档槽、初始状态、自动存档口味由游戏/宿主配置。ReplayPlayer 存在但无生产 UI/命令行入口属“已实现未默认接线”。 |
| `architecture/11_工程规范与测试.md`（v2，233 行） | `check.ps1`、`build.ps1`、`.github/workflows`、`toolchain/tests`、六个 .NET 工程；ABI 工具 `toolchain/abi_surface` | 本轮 delivery `check.log:252-280` 明确 24 步、17 PASS/7 SKIP，含六工程 3045 passed、Python 176/4；`delivery/run5/output/raw/formal-and-indexer.raw.txt` 与 `delivery/run5-console.log` 核对 1.16.2 zip/lock、DLL/package/manifest 哈希与旧 consumer/indexer oracle。`toolchain/README.md:555-559` 对 ABI surface 的“全部公开 API”声明与 `SurfaceDumper` 未编码 indexer 参数共同构成 P2 ABI-1162-01 的文档关联更新（不另计 P3），详见 docs-findings；Unity/Standalone 专项是否通过由主报告另列 | 工程/测试分层和 release 流程属框架交付；`-SkipUnity` 是受限证据，tracked 源码未被门禁改写，但 ignored `bin/`/`dist/` 会产出中间物。 |
| `architecture/12_扩展与变更流程.md`（v1，132 行） | ADR 模板/新增原语流程；`architecture/adr/README.md` 当前列 0001～0023 共 23 条 | 静态索引核对；ADR-0020～0023 文件存在且被代码/工具/文档引用 | 新原语需 ADR、登记、代码、校验和测试；细节勘误/内容级更新按流程。 |
| `architecture/13_新游戏接入指南.md`（v4，183 行） | `games/_template/Runtime/GameBootstrap.cs:47-193`、`GameOptions.cs`、`GameSceneBuilder.cs`、`validate.ps1`；步骤与检查表 `:42-183` | 模板代码/数据静态闭环；本轮 delivery 的 Python/门禁/正式 zip-lock 证据覆盖版本与数据工具链，模板 Unity Runtime 由主报告专项证据另列 | 新游戏先消费版本快照/锁，再选适配器、组装、填数据、配表现和验收；模板是游戏层起点，不能把 sample 灰盒根当成通用框架入口。 |
| `architecture/14_资产规格书模板.md`（v1，399 行） | 资产/命名/导入契约；`toolchain/import_assets.py`、`asset_import/*`、`adapters/unity/Assets/Editor` 生成器 | 本轮 delivery `check.log:263-267` 的资产检查/格式检查/Python 测试均 PASS；Unity 资产导入专项由主报告另列 | 规格与导入工具是框架交付物，真资产内容/美术规格填值由游戏负责。 |

## ADR 0001～0023

下表把每条 ADR 的拍板内容和实际入口分开；“Runtime”一栏没有文件化运行证据时写明静态/.NET边界，不把测试源码存在误写为 Runtime 通过。

| ADR | 当前代码/实现锚点 | 默认接线 / Runtime 证据 | 分类结论 |
|---|---|---|---|
| 0001 万物皆法术 | `core/rules/skill/core/EffectDispatcher.cs`、`AuraHost.cs`、`ProcHost.cs`；`core/carriers/gobj/core/GameObjectHost.cs` | 本轮 delivery 六工程 .NET 测试通过；Unity/消费方专项由主报告另列 | 框架原语合同；固定 kind 行为例外按 ADR 勘误处理。 |
| 0002 逻辑对象不依赖引擎对象 | `core/foundation/sim_loop/contracts/Entity.cs`、`core/carriers/*/contracts`；Unity View 位于 adapter/presentation | .NET WorldSim/载体测试；无引擎依赖静态证明 | 框架分层合同；View 由表现/适配器实现。 |
| 0003 固定步长模拟 | `SimClockHost.cs`、`WorldSim.cs`、`ISimClockHost.cs` | 本轮 delivery 六工程 .NET 测试通过；Unity/Standalone 专项由主报告另列 | 框架机制；跨平台只承诺逻辑等价，非逐位一致。 |
| 0004 数据即内容 | `DataRegistry.cs:177-240`、各 schema catalog、`toolchain/validate_data.py` | 本轮 delivery 合并根/框架根 validator PASS（`check.log:149-153,260-262`）；正式 zip/lock 另有哈希核验 | 框架数据合同；具体游戏数据由消费方提供。 |
| 0005 一个条件语言 | `core/foundation/expr/core/ExprLexer.cs`、`ExprParser.cs`、`ExprValidator.cs`；Rules Expr host | 本轮 delivery 六工程 .NET 测试通过；消费方/Unity 专项由主报告另列 | 框架语言合同；各模块只登记宿主 key。 |
| 0006 逻辑 id 到 DisplayInfo | `DisplayInfoRegistry.cs`、`DisplayInfo.cs`、`presentation/render` | DisplayInfo/.NET tests；Unity 视图证据仅有旧归档 | 框架映射机制；资源和映射内容由游戏/美术提供。 |
| 0007 窄契约加事件总线 | `core/foundation/event_bus/core/EventBus.cs`、`IEventBus.cs`、各模块 common contracts | EventBus tests；生产组装静态使用 bus | 框架通信合同；禁止跨模块内部耦合。 |
| 0008 世界状态标志替代 Phasing | `core/gameplay/world_state/core/WorldState.cs`、`schema/WorldStateSchemas.cs`；`GameplayAssembly.cs:328` 构造并在 `:1224` 注册持久化 | 本轮六工程 .NET 测试通过；`SceneRouter` 只负责路由，代码注释明确由 L4 依据 Spawn 与当前 WorldState 构建场景初始状态，不把“读取 WorldState”归给 SceneRouter | 框架世界状态机制；具体状态内容/地图分支由游戏提供。 |
| 0009 存档是唯一持久化 | `SaveSystem.cs`、`ISaveSystem.cs`、各 `IPersistable`/Persistable | SaveSystem migration/replay tests；无跨进程新证据 | 框架持久化合同；槽位策略和存档内容属游戏。 |
| 0010 新增原语走审批 | `architecture/12_扩展与变更流程.md:14-58`、schema/event 注册接口、ADR 索引 | 文档/代码静态核对 | 流程约束，不是运行时 API。 |
| 0011 引擎适配层隔离技术选型 | `core/foundation/engine_adapter/contracts/*`、`adapters/stub`、`adapters/headless`、Unity 包 | 本轮 delivery .NET/Python 门禁与正式包哈希通过；Unity/Standalone 专项由主报告另列 | L-1 合同；具体引擎实现和选型由游戏/适配器负责。 |
| 0012 双外形类型加固定镜头 | `DisplayInfo`、`IRenderer2D`/`IRenderer3D`、`CameraHost`、Unity Sprite/Model View | 本轮 delivery .NET 测试与正式包哈希通过；Unity model/视图专项由主报告另列 | 框架表现机制；游戏决定 sprite/model 路线和资源。 |
| 0013 可替换即时与回合制时间模型 | `core/foundation/sim_loop/core/TurnScheduler.cs`、`core/gameplay/assembly/TimeModelSwitch.cs`、`GameplayAssembly.Advance` | Discrete/Replay/.NET tests；三入口静态传 `clockHost`; ATB 取值合法但 `TurnScheduler.Configure` 明确 `NotSupported`，ADR-0013 拍板本版不展开 | 框架连续/离散机制；ATB 是预留延期项；日循环属于玩法/游戏策略边界，不与 ATB 合并成同一非目标。 |
| 0014 资产契约/导入工具/UI 套件是交付物 | `toolchain/import_assets.py`、`asset_import/*`、presentation UI contracts、包 README | 本轮 delivery toolchain 资产检查与 Python tests PASS；UI/Unity 专项由主报告另列 | 框架交付面；编辑器产品项目随游戏走，见 ADR-0018。 |
| 0015 Expr 点分标识符以引用登记表消歧 | `ExprLexer.cs`/`ExprParser.cs`、`ExprSchema`/登记表；`ExprAdr0015Tests.cs` | 本轮 delivery 六工程 .NET 测试通过；消费方/Unity 专项由主报告另列 | 框架解析/引用合同。 |
| 0016 引擎适配层契约阶段 4 联调补齐 | `IClock` subscription、`INavigation2D` blocking、`ISpatialQuery` register/update/unregister、Unity adapter | Stub/Unity adapter tests；归档 Unity/灰盒证据来自 1.16.1 | 框架接口与接线机制；实际地图/索引数据由宿主/游戏负责。 |
| 0017 模型型外形默认路线与命中帧同步 | `presentation/render/core/ModelCharacterRig.cs`、`presentation/render/core/AnimStateMachine.cs`、`presentation/feedback_binder/core/CharacterRigHitFrameSource.cs`；Unity `UnityViewFactory.cs`、`AnimClipResolver.cs` | 三入口静态接线；既有 Unity EditMode/PlayMode XML 与定向报告只作为主报告单列的专项证据，不把 `-SkipUnity` delivery 当成 Unity Runtime 证明 | A/B 机制已实现且开关可接线；默认 `HitFrameSync=false`，具体资源/口味由游戏负责。 |
| 0018 编辑器随游戏走与框架交付物 | `ContentValidationAssembly.cs:98-207`、`SchemaAudit`、`games/_template/Runtime/DataHotReload.cs`、`adapters/headless` | 本轮 delivery validator/schema-audit 与 Python tests PASS；编辑器基础套件不在本仓库，Unity 热重载专项由主报告另列 | 框架承诺无头适配层/校验入口/热重载标准实现；编辑器产品与模板由具体游戏项目维护，当前用户拍板“暂不落地”。 |
| 0019 复合字段子结构机器可读 schema | `FieldSchema.cs`、`VariantSchema.cs`、`DataRegistry.ValidateFieldValue`；各模块 `schema/*` | 本轮 delivery validator/schema-audit PASS 与六工程 .NET 测试通过；Unity/消费方专项由主报告另列 | 框架数据校验/元数据；未知子字段默认关闭是明确取舍。 |
| 0020 表达式词法器纳入公开契约 | `core/foundation/expr/core/ExprLexer.cs`、`ExprToken.cs`、`ExprTokenKind.cs` 为 public；`ExprAdr0015Tests` 邻接测试 | 本轮 delivery 六工程 .NET 测试通过；formal zip 旧编译 consumer 结果与其他消费方专项证据按主报告分列 | 已提供公开词法 API；消费方应复用 `Tokenize`，不是自行正则切词。 |
| 0021 字段登记表数值范围约束 | `FieldRange.cs`、`FieldSchema.Range`、`DataRegistry` field range 分支 | `FieldRangeValidationTests`/DataRegistry tests；1.16.2 源码静态已含 `field_finite` 与有限端点防线 | 框架 schema/加载合同；数据内容越界由 validator 阻断。 |
| 0022 登记表导航与编辑元数据 | `TableSchema`/`FieldSchema` 元数据；`DataRegistry` IdList 引用；`toolchain/validator --list-tables --json` | `SchemaMetadataTests`、`SchemaAuditTests`；归档 direct validator JSON 已有 `tables_list/field_meta/field_ranges`，wrapper 输出边界有记录 | 已实现元数据合同；编辑器读取 direct validator 清单，不能把 `validate_data.py --json` 当同一输出。 |
| 0023 光环叠加类别为静态校验分组 | `SkillValidationRules.cs:63-119` 的 `StackCategoryConflictRule`；`AuraHost.cs:76-77` 槽位 `(target, defId, sourceKey)`；`SkillOptions.cs` | `ADR0023_StackCategoryStaticGroupingTests.cs` 5 条用例静态/.NET；ADR 引用的消费方 Runtime 报告显示同类别不同定义各自激活，但该外部报告不在冻结根；本轮 delivery .NET 3045 tests 已通过，专项消费方 Runtime 仍由主报告按原始报告汇总 | `stack_category` 仅加载期静态分组；跨定义共享槽位是“未提供”，不是运行时算法缺陷。默认 `AllowMultiSourceTiming=false` 时 `sourceKey=null`。 |

## 当前已对齐项与证据边界

- 旧审计 DOC-116-01～04、TOOL-116-01 在当前源码中已按 `architecture/README.md`、根 `README.md`、`core/rules/skill/README.md`、`core/foundation/data_registry/README.md`、ADR-0022 和 toolchain README 的现状修正；当前未再把 `FindUnits` 写成恒空，也未把 1.16.2 计为 19 条 ADR。
- ADR-0023 的 06、04、skill README、`SkillValidationRules`、`AuraHost`、测试已统一为静态分组/按定义槽位；未发现仍把同类别不同 `aura_def` 写成运行时互斥的当前正式文档。编辑器产品文档的“同类别语义冲突”应理解为静态叠加形态冲突，不能扩大成运行时共享槽位承诺。
- 本轮 delivery 证据来自 1.16.2 目标快照：六工程 .NET 合计 3045 passed、toolchain 176 passed/4 skipped、`-SkipUnity` 为 17 PASS/7 SKIP；正式 zip/lock、四个包与八类 DLL/manifest 哈希另有 formal-and-indexer 原始输出。既有 1.16.1 Unity XML 不改变本轮 Unity 覆盖边界。相对 1.16.1 基线，本冻结提交前的审计增量包含 35 个非落地计划路径变化，含核心修复，不能把 1.16.2 视为纯版本号提交。
- `check.ps1 -SkipUnity` 对 tracked 源码保持不改，但会写 `.gitignore` 覆盖的 `bin/`、`dist/` 构建/打包中间物；这属于交付验证过程边界，不改变冻结根源码只读口径。

## 未提供、未默认接线、延期和条件支持清单

下列项目逐项按当前能力索引、模块 README 与生产装配核对；它们不能因为某个游戏尚未接入而被记成框架缺陷。

| 分类 | 项目 | 当前证据与责任边界 |
|---|---|---|
| 未提供 | 编辑器产品 | ADR-0018 与 13 说明框架交付 validator/无头入口，具体编辑器随游戏走；框架不承诺仓库内通用编辑器产品。 |
| 游戏责任 | talent 完整 allocator | 当前没有既定通用分配器契约；技能树/点数分配策略由具体游戏实现。 |
| 条件支持/上层消费 | TargetPoint | 已提供字段，cast 意图可携带落点；`SkillHost.CastSkill` 不消费，游戏/AI/辅助施法适配层按自身语义转换为目标。与 `world.map.teleport_points` 是不同功能。 |
| 条件支持/上层消费 | `world.map.teleport_points` | 元素结构已登记；命名点由 `TeleportTargetResolver` 解析；引用目标完整性不在该 schema 保证内。 |
| 条件支持 | 离散召唤/loot | `SummonTickHandler` 在 Discrete 直接跳过、`duration` 不推进；`LootExpiryTickHandler` 跳过清理但底层绝对时钟仍推进。扩大行为需另立 ADR，不是仅补注入即可自动支持。 |
| 未默认接线 | weaponVfx | Resolver 已构造并暴露，`ResolveSwingVfx`/`ResolveImpactVfxOverride` 无默认生产调用；调用方负责接线。 |
| 条件支持/调用方责任 | gatherClock | `GobjOptions.SimTime` 默认恒 0，需调用方注入模拟时钟；刷新机制已存在。 |
| 未默认接线 | Replay、`Quest.Update`/owner/day/vendor | `ReplayPlayer`、`QuestHost.Update` 和 `GameplayAssembly` 回调已有 API/实现；三入口只透传可选参数，默认 `null`/不驱动，需游戏接入。 |
| 未默认接线 | 三个可选校验规则 | `DisplayMapCoverageRule`、`FeedbackRuleValidator`、`SpawnSummonOnlyCreatureRule` 本体存在，但分别需要显式登记/回调/查询注入；delivery validator 输出也记录 optional rules disabled。 |
| 延期 | ATB | `TimeModelSchema` 接受 `atb` 枚举，`TurnScheduler.Configure` 对其抛 `NotSupportedException`；ADR-0013 明确本版不展开，是预留延期项。 |
| 建议项/未承诺为本版门禁 | 孤儿记录检测 | 04 明确标注“建议”而非门禁契约，当前无对应规则；后续需另立决策/排期，不能作为本轮框架缺陷。 |
| 未提供 | 跨 aura 定义共享槽位 | ADR-0023、`AuraHost` 和 `StackCategoryConflictRule` 一致说明 `stack_category` 仅静态分组，运行时按 `(target, defId, sourceKey)` 独立；需新需求时另立 ADR。 |
| 条件支持/实现方边界 | nav 跨帧预算、空间完整索引化 | 02 已将性能要求收窄为实现方边界；`UnityNavigation2D`/`UnitySpatialQuery` 的具体实现不代表 Core 契约承诺。 |
| 条件支持/游戏责任 | escort 自动路线、轨迹碰撞、完整新局重置 | 框架提供窄 API/最终落点/可替换 `NewGameStarter`，自动路线、沿途物理和初始状态清理由游戏实现。 |

`prog.xp_source.condition` 是已登记的 Expr 字段，但 Progression README 与 schema README 明确本任务只登记类型、不求值；授予前判断由宿主/游戏完成，或未来另行扩展，不列为缺陷。
