# ws-game 1.14.0 文档—代码对照矩阵

基线为冻结 detached worktree `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`，`VERSION=1.14.0`。源代码只读位置：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`；本文件只引用该冻结树、当前正式 1.14.0 发布包和本轮新产生的门禁/ABI证据。`followup-2026-09-10` 与旧 audit 目录只用于理解变更背景，不作为本轮独立运行证明。

分类：**框架通用契约**=架构明确由框架提供且可由独立 consumer/模块验证；**可选模块契约**=模块启用并完成装配后适用；**已实现未默认接线**=代码和窄接口存在，接线由宿主选择；**游戏/宿主/内容责任**=框架给出机制或扩展点，具体策略、内容和回调由消费方决定；**未提供能力/待另行契约**=没有既定通用承诺，不能自动生成框架缺陷；**明确非目标**=架构当前明确不承诺；**暂不落地**=用户已决定暂缓；**已发现缺口**=存在明确通用契约而当前实现不满足。

证据等级：A=正式 ZIP 流 hash 或独立 consumer 的真实运行；B=冻结源码与模块测试/静态装配互相对照；C=架构/README 文字边界；D=历史记录，仅说明演进，不证明当前 Runtime。

| 范围/契约 | owner 与启用条件 | 当前实现与实际 | 预期/分类 | 源码/文档证据 | 重现与证据等级 |
|---|---|---|---|---|---|
| 00 总则、01 分层 | 框架仓库定义层边界；游戏层只在接入时组装 | `core`、`presentation`、`adapters`、`games` 目录分层存在；本轮扫描未发现 core/presentation 对具体游戏程序集的生产引用（测试 fixture/注释不计） | 通用分层契约；静态通过，不代表所有游戏语义已证明 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\00_架构总则.md:16`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\01_分层与依赖.md:45` | 源码扫描；B/C |
| 02 引擎适配接口 | 适配层实现 `IClock`、`IFileSystem`、`INavigation2D`、`ISpatialQuery` 等；具体引擎自行实现 | Unity 适配器和 Stub 均实现窄接口；跨帧寻路预算、完整空间索引化是性能边界/实现选择，当前没有独立性能量化证明 | 可选适配器契约；不把具体实现策略自动升级为框架缺陷 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\02_引擎适配层.md:149`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\engine_adapter\contracts\ISpatialQuery.cs:1` | 源码/文档；B/C |
| 03 运行时骨架、时间模型、SceneRouter | 框架提供状态机、tick 顺序、场景路由；游戏提供口味配置与宿主回调 | `GameBootstrap`/`FrameworkResidentHost` 使用组合根；连续/离散模式与路由接口存在。真实 Unity/consumer 运行是否覆盖由对应验证报告单独声明 | 通用基础设施 + 宿主装配责任；不以静态存在等价 Runtime 认证 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\03_运行时骨架.md:24`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\app_lifecycle\core\AppStateMachineConfig.cs:33` | 源码；B/C |
| 04 DataRegistry/schema | 框架 registry、schema、validator；可选 validator 由装配方提供来源 | 本轮 `schema-audit` 63 tables/783 fields/0 errors/0 warnings；合并数据 0 errors/3 warnings，framework 根 0 errors/1 warning。WorldMap `PointItemSchema` 展开 `{id?,position?}`，新增规则只对 `spawn_points[0].position` 作用途契约检查；命名点跨表/跨图完整引用仍未被当前规则承诺 | 通用 schema/报告门禁已通过；WorldMap 其余点是否填 id/position取决于用途，不要求所有内容补全 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\data_registry\core\DataRegistry.cs:415`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\scene_router\core\WorldMapSchema.cs:67`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\scene_router\core\WorldMapSchema.cs:112`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\04_数据与内容管线.md:207` | check 日志、源码；A/B |
| ADR-0019 复合 schema | 框架登记 `Fields`/`Item`/`Variants`；模块 coverage 测试负责运行时集合对账 | `SchemaAudit` 只检查登记自洽，不比较 runtime 注册集合；Skill/Gobj/Quest/Dialog/AreaTrigger 等 coverage tests 做集合一致性。两者是不同载体 | 通用契约已分层实现；文档应继续明确命令行结构审计与模块测试的边界 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\adr\0019-复合字段子结构登记为机器可读schema.md:17`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\assembly\SchemaAudit.cs:12`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\tests\SkillSchemaCoverageTests.cs:13` | 源码/测试；B/C |
| 05 世界模型、导航、空间查询 | 框架提供 Entity/WorldSim/Shape；适配层提供导航/空间算法 | `Shape`、`ISpatialQuery.QueryShape`、WorldMap parser 与传送解析存在；导航跨帧预算、全方法索引化、位移轨迹碰撞没有本版统一强制实现证明 | 通用几何/窄接口 + 适配器/游戏策略边界；没有性能或轨迹新 Runtime finding | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\05_对象模型与世界.md:177`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\engine_adapter\contracts\ISpatialQuery.cs:1`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\core\EffectDispatcher.cs:337` | 源码/架构；B/C |
| 06 Skill/Combat/Targeting | `SkillHost` 是框架公开组合根；`ISpatialQuery`/`IFactionMatrix` 仅在装配需要时注入 | 当前 `SkillHost` 公开构造器有 18 个物理参数，最后新增可选 `IFactionMatrix? factions`；`FindUnits` 委托 `QueryShape` 并按 UnitFilter 过滤，RulesAssembly 默认传入 Spatial/Factions | `FindUnits` 通用机制已实现；**当前 1.13→1.14 旧二进制兼容不满足**（见 P2-ABI） | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\core\SkillHost.cs:90`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\core\SkillHost.cs:222`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\assembly\RulesAssembly.cs:359` | formal consumer old exit 0/new exit 11；A |
| 07 Item/Creature/Gobj/Summon/Talent | 框架提供物品、装备、召唤跟随/owner/联动及天赋查询/光环复用机制；具体游戏提供内容与策略 | Inventory/Equipment/Archetype 等 host 有 `DataLoadCompletedEvent` 刷新；连续召唤跟随存在，Discrete duration 跳过是时间模型支持边界；IArchetypeRegistry 没有 allocate/spend/refund API；完整 talent 流程未形成既定通用契约 | 已实现可选模块 + 宿主内容/策略责任；talent 完整管理归“未提供能力/待另行契约”，不自动列框架缺陷 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\07_载体层_物品生物物件.md:227`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\numbers\archetype\contracts\IArchetypeRegistry.cs:14`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\carriers\summon\core\SummonTickHandler.cs:53` | 源码/架构；B/C |
| Gobj lock/world flag | 框架 Gobj validation 在启用模块后应在报告阶段拒绝错误字段 | `GobjLockWorldFlagExpectedRule` 已登记并装入验证；加载期错误不应依赖消费时异常 | 通用 schema/业务 validator；以 `ValidationReport` blocking error 为验收 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\carriers\gobj\schema\GobjValidationRules.cs:1`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\carriers\gobj\assembly\GobjSchemaCatalog.cs:1` | 源码/模块测试；B |
| 08 Quest/Dialog/Loot/Economy | GameplayAssembly 组装各 host；owner/day/vendor、自动驱动由宿主按需提供 | Quest/Dialog/Loot/Economy host 均订阅数据完成事件并刷新定义；`Quest.UpdateProgress`/`Fail` 是窄 API，escort 自动路线无 generic route provider；day 缺省恒定默认值，owner/vendor 缺省无回调效果 | 可选模块契约 + 已实现未默认接线；escort 路线、具体内容属游戏/宿主；不把默认值当模块关闭 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\08_玩法层_掉落任务对话关卡.md:323`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\assembly\GameplayAssembly.cs:722`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\quest\core\QuestHost.cs:226`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\quest\core\QuestHost.cs:362` | 源码/测试；B/C |
| 09 Presentation/View | Presentation 只读逻辑状态；可选装备外观与 VFX 能力按适配器声明 | `ViewBinder` 在 save.loaded 对实现 `IEquipmentVisualResettable` 的既有 View 清旧外观再应用快照；`VfxPlayer` 探测可选 `IParticleRepositioner`，Unity Renderer2D 提供实现，未实现的适配器保持生成时定位降级 | 通用表现契约 + 可选适配能力；具体游戏是否选装备/VFX 口味不构成框架缺陷 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\09_表现层.md:72`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\view_binding\core\ViewBinder.cs:247`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\vfx_sfx\core\VfxPlayer.cs:92`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\vfx_sfx\contracts\IParticleRepositioner.cs:21` | 源码；B/C |
| 10 Save/Persistence | 框架持久化段和迁移链；游戏定义新局策略与内容 | Persistable 接口、读档事件抑制/恢复链存在；本轮 docs 子任务未重跑 Unity/consumer 语义 | 通用持久化契约；运行时结论仅取对应报告，不由 check SkipUnity 推断 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\10_存档与持久化.md:30`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\save_system\contracts\IPersistable.cs:1` | 源码；B/C |
| 11 工程规范与门禁 | `check.ps1` 定义完整门禁；完整门禁需不跳过 Unity 且含 consumer smoke | 本轮一次 `check.ps1 -SkipUnity`：22 steps，16 PASS/6 SKIP/0 FAIL；dotnet 2,840 passed/0 skipped，pytest 100 passed/2 skipped；Unity 六步（含 consumer）全部 SKIP | 当前命令是有限门禁；不能称完整发布认证。内置 ABI 行显示 PASS 但实际因冻结树无 baseline ZIP 被 `SkipIfBaselineMissing=true` 静默跳过 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\11_工程规范与测试.md:181`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\check.ps1:473`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\toolchain\abi_probe.ps1:68`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\check.ps1:486`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\check\check-1.14.0-skipunity.log:109` | check transcript；A/B |
| 12 扩展流程 | 框架 ADR/版本流程；变更作者维护同步文档 | 当前 1.14 变更记录列出 ABI façade、schema、reload、VFX 等；本矩阵对新增公开签名另做 ABI 复核 | 文档同步与版本判据是框架交付责任 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\12_扩展与变更流程.md:99`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\CHANGELOG.md:42` | 源码/文档；B/C |
| 13 新游戏接入与热重载 | 模板提供开发期 watcher；仅 Editor/Development 有效；宿主按模板/内容根接入 | `GameBootstrap` 从 `UnityFileSystem.GetContentRootDir()` 构造 StreamingAssets 下 framework/game 数据根；`DataHotReload` 监听 Changed/Created/Deleted/Renamed，去抖后主线程 Reload；框架源 `data/_framework` 改动须先同步到副本 | 模板开发工具契约；不是发布构建通用运行时保证，也不是作者源目录直接监听 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\games\_template\Runtime\GameBootstrap.cs:202`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\games\_template\Runtime\DataHotReload.cs:112`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\games\_template\README.md:159` | 源码/README；B/C |
| 14 资产规格/导入 | 框架定义资源引用与导入检查；游戏提供美术文件和规格值 | `gen_placeholder_assets.py --check` 92/92；`import_assets.py check --dataset _sample` 0 issues；实际资产是否满足某个游戏视觉目标由游戏负责 | 通用管线通过；具体内容/美术责任不移入框架 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\14_资产规格书模板.md:21`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\toolchain\import_assets.py:1` | check transcript；A/B |
| ADR-0018 headless + validation assembly | 框架交付 Adapters.Stub 与 ContentValidationAssembly；编辑器是外部 consumer | 正式 ZIP 有 headless package、四 manifest 精确 1.14.0；`SchemaAudit`/`ContentValidationAssembly` 为框架 API | 通用交付契约；编辑器 UI 本身是外部项目/用户暂缓项 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\adr\0018-编辑器随游戏走与框架为此提供的交付物.md:19`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\assembly\ContentValidationAssembly.cs:1`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\logs\release-formal-1.14.0.log` | 正式 ZIP；A/B/C |

## 当前开放能力逐项归类

| 条目 | 当前分类与责任 | 当前事实/最小验收 |
|---|---|---|
| 内容编辑器 | 暂不落地（用户决定）；editor 是独立消费方 | ADR-0018 只要求 headless/validation 契约，编辑器实现不属于本仓库 Runtime |
| Talent 完整分配/激活/撤销/持久化 | 未提供能力，责任需另行契约决策 | 已有树查询、奖励注入位和被动光环复用；没有既定 allocator API，不能把具体游戏需求排成框架待办 |
| `TargetPoint` 地面点选消费 | 已实现未默认接线，游戏/上层 AI 消费 | 字段/意图可携带落点；解析为具体目标由上层策略决定 |
| 连续召唤跟随/owner/联动 | 可选模块通用机制，生产装配可用 | 连续步 `TryFollow` 存在；follow distance/策略取值由宿主内容提供 |
| Discrete 召唤 duration/掉落清理 | 当前时间模型的条件支持边界 | `SummonTickHandler` 跳过 Discrete；`LootExpiryTickHandler` 跳过清理但绝对时间继续累加，若要扩展需另行契约 |
| Quest.Update、owner/day/vendor、采集 SimTime | 已实现未默认接线 | 宿主按固定步/触发协议注入；默认回调/固定值不表示整个模块关闭 |
| escort 自动路线、完整新局 reset | 游戏/宿主责任或未提供通用策略 | `IQuestHost.UpdateProgress/Fail`、`NewGameStarter` 窄契约已存在；路线 provider、完整状态初始化由消费方决定 |
| Replay、Swing/Impact、反馈消费者 | 已实现未默认接线 | 类型/解析/测试存在；生产入口由游戏按需求接入 |
| 导航跨帧预算、空间查询完整索引化 | 本版性能边界/实现选择，非本轮强制缺陷 | 需特定规模契约与基准后再决定，不能只由同步实现静态推导 Runtime 失败 |
| 位移轨迹碰撞 | 无既定通用承诺；游戏物理策略 | `move` 保证逻辑终点语义，沿途碰撞需另行通用契约 |
| `day_cycle`、ATB | 明确非目标 | 本版只保留预留/现有时间模型能力 |
| VFX anchor/socket 跟随、装备外观 SaveLoaded | 通用可选表现契约 | 通过可选接口/`IEquipmentVisualResettable`；未选能力时按降级语义运行 |
| WorldMap 点元素与引用 | 元素有部分结构；用途契约按消费方 | `PointItemSchema` 已展开；首个 spawn 必须有 position；命名点/跨图引用是否 required 需按 parser/规则决定 |


## 当前运行时 findings（core 独立 probe）

以下四项是当前 1.14 独立 .NET probe 的真实负例；它们属于可选模块启用后定义 reload/validation 契约的框架实现问题，不是具体游戏内容缺失。

| ID | 契约、owner、条件 | 实际与预期 | 源码/重现/等级 |
|---|---|---|---|
| CORE114-01 | QuestHost reload；框架 owner；活跃任务定义从一条目标变为两条后继续更新 | `ObjectiveCounts` 仍按旧长度，更新新 index 抛 `IndexOutOfRangeException`；应迁移/调整进度数组或原子拒绝不兼容 reload | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\quest\core\QuestHost.cs:134`、`:226`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log`；A |
| CORE114-02 | EconomyHost none→timer；框架 owner；既有 vendor/item 状态 reload 新 timer 定义 | 既有库存为 0 且 timer null，Update 后仍 0；fresh host 可补回 2。应初始化/重采样计时器并保持已消费库存语义 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\economy\core\EconomyHost.cs:103`、`:555`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log`；A |
| CORE114-03 | StatHost reload；框架 owner；已注册 unit 只读派生值后 reload default_base | A 查询缓存保持 0，B 得 77，二者 base 都 77；应让未显式覆盖的派生缓存按新定义一致得到 77 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\numbers\stat_block\core\StatHost.cs:73`、`:264`；同上 current-v5；A |
| CORE114-04 | ExprValueJson/Quest validation；框架 validator owner；正式 ContentValidationAssembly 输入 `{"$id":"BAD"}` | `IsValid`/Quest 正式校验冒泡 `ArgumentException`；应输出可定位 blocking report，合法 Bool/Number/String/Id 仍通过 | `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\common\contracts\ExprValueJson.cs:16`；`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\QuestWorldFlagValueBoundaryProbe.current-v3.log`；A |

Gobj `world_flag.expected`、Quest 合法 `true` 和 SkillHost cache hot reload 的当前正确行为均见 core 代理报告；它们不能因历史 finding 名称相似而重新计为失败。Equipment set cache 本轮只有静态候选，没有独立负例，不列 confirmed P2。


## 需要更新的文档/元数据清单

这里区分“必须更新”（当前文字已与本轮事实冲突）和“已对齐/可选澄清”（不应再制造重复 finding）。所有更新都只同步契约和证据边界，不为具体游戏补功能。

### 必须更新

| 文件与行 | 当前问题 | 建议改成什么 | 是否需先补代码 |
|---|---|---|---|
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\CHANGELOG.md:101`、`:118` | 1.14 同时声称旧 binary 无需重编译即可运行、无公开签名删改；本轮 SkillHost 17→18 consumer 为 `MissingMethodException` | 先选择 A：恢复 17 参数转发并以旧 binary 实跑；或 B：按 MAJOR/迁移契约接受破坏并改版本/迁移说明。不能只改 changelog | A 需代码；B 需版本与迁移决策 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\11_工程规范与测试.md:29`、`:181`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\check.ps1:473`、`:492` | ABI 覆盖范围和缺基线行为易被当完整证明；check 的 ABI PASS 实际为 skip | 明列基线缺失为可见 SKIP/strict failure，保存子进程 exit/输出，并覆盖本版新增公开签名 | 需先修门禁判定 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\toolchain\abi_probe.ps1:104`、`:116` | 缺基线 exit 0；显式已有 OutDir 直接递归删除 | 缺基线不写 PASS；默认新建唯一输出；显式已有目录拒绝或验证在专用临时根内；补 sentinel | 需先修工具 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\CHANGELOG.md:109`、`:112`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\quest\core\QuestHost.cs:134` | CHANGELOG 的“下一次访问见新定义/状态不变”没有把合法结构变化、目标数组迁移、库存 timer 转换、派生缓存和 validator 异常安全边界写成可验收条件（README:184 是关联失败语义说明） | 关联 CORE114-01..04：说明只适用于定义可兼容 reload；不兼容结构必须迁移或原子拒绝；resident cache/状态转换/validation 的最小 oracle 分别见 findings | 需先补四项代码，再同步承诺 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\gameplay\common\contracts\RewardSchemaFields.cs:9-13` | 注释仍称 FieldSchema 不能表达嵌套，和当前 Object/复合 schema 登记能力冲突 | 改为描述当前登记范围、Object/union 边界和未登记字段；不能用注释掩盖 validator 运行问题 | 仅文档 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\落地计划\落地方案与分阶段计划.md:1281`、`:1294`、`:1296` | 历史四类/三类记录与当前六类索引容易被连续阅读为现行分类；“未实现”未说明责任 | 在当前索引首段标出历史记录，使用通用契约/可选启用/宿主游戏/未提供/非目标/暂缓六类；不为开放条目自动排期 | 仅文档 |

### 已对齐或仅可选澄清

| 文件与行 | 当前核验 | 处理 |
|---|---|---|
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\games\_template\README.md:157`、`:166`、`:172` | 已明确 StreamingAssets 内容根、`-SyncContent` 和 Editor/Development 条件 | 不列缺陷；可在总入口再加链接 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\adr\0019-复合字段子结构登记为机器可读schema.md:17`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\presentation\assembly\SchemaAudit.cs:12` | 已区分 SchemaAudit 结构审计与模块 coverage runtime 集合对账 | 不改实现；只在索引说明证据载体 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\rules\skill\schema\SkillSchemas.cs:335`、`:340`；`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\04_数据与内容管线.md:246` | `Periodic` 与非周期 `scaling_stat` 当前均为可选 `Reference(stat.definition)`，文案与结算消费一致 | 不列 metadata finding |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\core\foundation\scene_router\core\WorldMapSchema.cs:67`、`:112` | PointItem 部分结构、首个 spawn 用途规则已在当前实现；不要求所有点填满字段 | 不列 P2；按用途契约解释 |
| `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen\architecture\13_新游戏接入指南.md:28`、`:103` | 模板热重载默认挂载、开发期生效、删除回落描述已同步 | 不把“未默认接线”或源目录监听误读写回 |




