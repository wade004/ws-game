# 1.14.0 责任与证据边界

本文件是本轮“框架—具体游戏彻底解耦”复核的权威解释。冻结对象是 `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen` 的 `76d16a54e54f0f11204d97d7460563c8a0dc8cd8` / `1.14.0`。这里的“正确行为判定（预期，非已通过）”只描述 oracle，不表示测试已经通过。

## 分类规则

| 分类 | 使用条件 | 责任含义 |
|---|---|---|
| 框架通用契约 | 架构或公开 API 明确承诺，独立 consumer 可验证 | 框架必须实现并在发布门禁中证明 |
| 可选模块契约 | 模块被装配且其依赖注入/适配器能力已启用 | 框架负责模块内正确性，宿主负责选择和提供依赖 |
| 已实现未默认接线 | 通用窄 API 或实现存在，生产入口由宿主选择 | 不是缺陷；需在具体接入中接线 |
| 游戏/宿主/内容责任 | 内容、策略、回调、路线、完整新局政策属于消费方 | 不因某个游戏没有接线而扩大框架契约 |
| 未提供能力/待另行契约 | 没有既定通用 API 或行为承诺 | 先定义 owner、输入、状态和验收，再决定是否扩展框架 |
| 明确非目标 | ADR/本版架构明确排除 | 不列发布缺陷 |
| 暂不落地 | 用户明确暂缓的外部消费方或工具 | 不混入框架 Runtime 风险 |
| 已发现缺口 | 有明确通用契约且现实现不满足 | 形成带复现和最小修复验收的 finding |

## 权威责任/契约矩阵

| 域 | 通用/可选契约与 owner | 启用条件 | 当前实际 | 正确行为判定（预期，非已通过） | 证据用途与限制 |
|---|---|---|---|---|---|
| 分层与组合根 | 框架提供 core/presentation/adapters 的依赖边界；游戏在组合根装配 | 消费方组装模块 | 冻结树扫描未发现 core/presentation 对具体游戏程序集的生产引用（fixture/注释不算） | 独立 consumer 可引用框架而不依赖具体游戏程序集 | 源码/架构是静态证据，不证明每个游戏运行语义 |
| 引擎适配 | 框架定义 `IClock`、`IFileSystem`、`INavigation2D`、`ISpatialQuery` 窄接口；适配器 owner 自己实现 | 选择某个 Unity/Stub 或自定义适配器 | Unity 与 Stub 提供窄接口；跨帧预算和索引策略未成为本版统一性能门槛 | 已选适配器满足接口及其明确语义；性能需独立规模契约 | 源码/架构证据，不把具体游戏性能目标上移 |
| 运行时骨架/路由 | 框架提供状态机/tick/SceneRouter；宿主提供口味配置 | GameBootstrap 或等价组合根装配 | 状态机、tick、Loading→MainMenu 默认路由和路由接口存在 | 宿主接入后按架构顺序驱动；不要求框架替每个游戏决定全循环 | 源码静态证据；Unity运行由独立报告负责 |
| Schema/数据管线 | DataRegistry、结构 schema、已装配 validator 是框架契约；可选 validator 由装配方启用 | `ContentValidationAssembly`/validator 装配 | SkipUnity check 的 schema audit 63 tables/783 fields/0 errors/0 warnings；合并数据 0 errors/3 warnings/1 override | 已登记结构自洽；业务 validator 启用时输出 blocking `ValidationReport` | check 只证明本次输入和命令路径，不证明所有可选规则已开启 |
| ADR-0019 | `SchemaAudit` 负责登记结构自洽；模块 coverage tests 负责 runtime 注册集合 | 对应模块测试工程运行 | `SchemaAudit` 本身不对 runtime 集合比较；Skill/Gobj/Quest/Dialog/AreaTrigger coverage tests 有集合对账 | 文档应把两个载体写清，不能把命令行 audit 单独写成全 runtime 对账 | 源码/测试边界证据 |
| WorldMap/出生点 | 框架按 parser/规则定义用途契约；内容 owner 提供点数据 | 解析出生点或命名引用时 | `PointItemSchema` 有 `id?`/`position?`，首个 spawn 的 position 有规则检查；匿名点与可选字段不能一概拒绝 | 被用作首个出生点的记录满足 position；被命名/跨图引用的目标按相应引用规则验证 | 当前静态/门禁证据不应强迫所有点完整填充 |
| Skill/FindUnits | `SkillHost`/`ISpatialQuery` 是框架公开机制；宿主提供 spatial/faction 依赖 | `RulesAssembly` 或自定义 host 装配 | `FindUnits` 委托 `QueryShape` 并按过滤器处理；公开 SkillHost 当前新增 18 参数构造器 | 新旧承诺若声明兼容，旧 consumer 需按 ABI finding 通过；已选 spatial 需返回正确查询结果 | 当前 ABI 是独立 A 证据；不把游戏 targeting 策略当框架缺陷 |
| Item/Equipment/SaveLoaded | 框架提供可选装备表现 reset 接口；游戏决定是否选择该表现 | View 实现 `IEquipmentVisualResettable` | ViewBinder 可在 save.loaded 清旧后应用快照；未选接口的 View 不承担装备外观契约 | 已选 View 覆盖空/非空装备状态；不通过重复业务事件重放替代视觉刷新 | 源码/独立表现报告分开记录；非所有游戏必选 |
| VFX anchor/socket | `IParticleRepositioner` 是可选表现接口，适配器 owner 实现 | VFX 玩家探测到接口 | VfxPlayer 探测并在支持的适配器上更新；未实现适配器保留降级语义 | 选用接口后 anchor/socket 跟随源对象；未选时只要求既有生成语义 | 可选能力静态/表现证据，不是所有游戏视觉目标 |
| 召唤 | 框架提供连续时间模型的 owner/follow/联动；内容和策略由宿主提供 | 连续 tick 与相应依赖 | 连续跟随机制存在；`Discrete` 明确跳过该 tick 语义 | 连续模式按提供的距离/策略工作；离散模式只有另行契约后才要求 duration/清理 | 支持边界，不自动形成 P2 |
| Talent/archetype | 当前公开 registry 是查询/ApplyTo；被动光环复用机制存在 | 需要树查询或 aura | 没有既定 allocator/spend/refund API | 若要定义完整点数生命周期，先决定 framework/game owner、状态模型和迁移；不把游戏愿望直接排成框架任务 | 静态 API 证据，不能证明永远不扩展 |
| Quest/Dialog/Economy/Loot | GameplayAssembly 提供可选 host；owner/day/vendor/时间回调由宿主按需驱动 | 对应 host 和 callback 装配 | Reload 与窄 API 存在；Quest `UpdateProgress`/`Fail` 是手动接口；默认回调使用各自固定/空语义 | 启用模块后定义 reload 正确；宿主按其世界时钟/owner/vendor 语义调用；不把默认值称“模块关闭” | 源码/模块测试；具体任务策略属消费方 |
| Escort/新局 | 框架提供窄 quest API；游戏拥有路线与完整新局政策 | 游戏提供 route/新局实现 | 没有 generic route provider；模板 `NewGameStarter` 只重置最小位置/地图/模板项 | 宿主自行驱动 `UpdateProgress`/`Fail`，并实现自己的路线/全状态初始化 | 架构与模板示例说明责任，不是框架漏实现 |
| Replay/Swing/Impact/TargetPoint | 框架携带类型/解析与可选消费者；游戏决定生产入口 | 游戏选择反馈/战斗入口 | 相关类型和解析存在，入口可由宿主接线 | 选用入口后消费方验证事件/动画语义；未选不构成缺陷 | 静态边界，具体战斗 E2E不属于框架必要证明 |
| 导航/空间/位移 | `ISpatialQuery` 与最终逻辑落点是通用窄语义；性能/物理策略另行定义 | 需要具体适配器或游戏物理规则 | 跨帧 budget、全索引、轨迹碰撞没有本版统一强制承诺；move 保证最终逻辑落点 | 只有在另行定义规模/碰撞契约后才验收对应性能或轨迹行为 | 不将游戏“不得穿墙”要求自动上移 |
| 热重载 | 模板开发期 watcher 是框架提供的接入实现；模板 owner 维护实际内容根 | Editor/Development，必要时 `-SyncContent` | `DataHotReload` 监听 Changed/Created/Deleted/Renamed；GameBootstrap 监视 StreamingAssets 下 framework/game 副本 | 同步源文件后，模板开发链按事件刷新；发布构建不获得该开发期保证 | README/源码证据；不是源 authoring 目录直接监听 |
| Editor | ADR-0018 将编辑器作为独立 consumer；用户已暂缓 editor 实现 | 外部仓库自行消费 headless/validation | 正式包提供 Adapters.Stub 与 ContentValidationAssembly；本仓库不实现 editor UI | editor 项目按其自身版本契约验证 | 不列框架 Runtime 缺陷 |
| day_cycle/ATB | 本版明确非目标 | 无需启用 | 未提供本版完整 day_cycle/ATB 系统 | 不进入 1.14 发布缺陷清单 | ADR/架构边界 |

## 证据等级与限制

- **A（正式/运行）**：正式 ZIP entry 流 hash 与 lock 对齐，或独立 consumer 真实运行。A 只证明指定包/指定输入/指定路径。
- **B（源码/测试）**：冻结源码、模块测试和装配关系互相对照。B 能证明实现存在或静态差距，不能替代跨进程/Unity 运行。
- **C（契约文字）**：架构、ADR、README 对责任和边界的规定。C 约束期望，不证明 actual。
- **D（历史）**：旧 followup、旧归档或变更记录，仅说明演进，不证明当前 1.14 Runtime。

本轮 `check.ps1 -SkipUnity` 的 16 PASS/6 SKIP 是有限门禁。Unity、IL2CPP、独立版和 consumer smoke 的实际结论只来自各自报告；具体游戏内容 E2E 不是本框架审计的默认必需证明，但框架提供的独立 consumer smoke 契约仍需在不跳过完整门禁时单独验证。

## 旧结论映射

| 旧审计中的表述/分类 | 本轮可能造成的误读 | 1.14 当前解释 |
|---|---|---|
| “未实现”总表 | 会把框架缺口、宿主未接线、内容未提供混在一起 | 按上表拆为通用契约、可选契约、宿主责任、未提供能力和非目标 |
| Talent 点数管理 | 容易扩张为框架必须实现完整分配/激活/持久化 | 当前有查询/光环复用；没有既定 allocator 契约，新增能力需独立决策 |
| 召唤生命周期 | 容易把 Discrete 支持边界说成全召唤缺陷 | 连续机制已存在；Discrete 是当前时间模型边界 |
| FindUnits/TargetPoint | 容易把具体 targeting 接法与基础查询机制混写 | `FindUnits` 通用查询已存在；TargetPoint 消费由上层策略接线 |
| 新局/escort | 容易要求框架知道所有游戏状态和路线 | 模板是可替换最小示例；宿主负责完整新局策略与手动 quest 驱动 |
| WorldMap 点结构 | 容易要求每个点都同时有 id 和 position | PointItem 有部分结构；required 性取决于首个出生点/命名引用用途 |
| 导航预算/轨迹碰撞 | 容易将具体规模或物理目标写成框架默认契约 | 本版只承诺已声明窄语义；新增性能/碰撞契约需另行定义 |
| Editor/热重载 | 容易把 editor 或发布构建行为当本仓库默认 Runtime | editor 是外部、用户暂缓；热重载是模板 Editor/Development 接入，源文件先同步内容副本 |
| 1.14 ABI facade/兼容说明 | CHANGELOG 的 MINOR/旧 binary 兼容文字被当作已证明 | 当前 SkillHost 1.13→1.14 旧 consumer 复现为 MissingMethodException；见 `project-findings.md` 和 `validation.md`，需选兼容或正式破坏迁移路径 |

旧 ZIP 与旧 candidate 只作为历史输入管理；本轮 raw 证据不会被重写成当前 1.14 运行证明。
