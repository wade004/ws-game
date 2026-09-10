# 文档—代码对照与职责边界

对象：ws-game 1.18.0，提交 `d6fda65cd6b00ede5c5f6f606f724d2ab0f60603`。源码根：`D:/workespace/ws-game-audit-d6fda65-20260911`。本表是当前源码的审核索引；运行证据以总报告和各域日志为准，不把类/API 存在解释为生产运行通过。

## 按文档章节核对

| 文档 | 代码与交付对应 | 当前判断 / 要更新的内容 |
|---|---|---|
| 根 README、architecture/README | VERSION、六个类库、adapter/template manifests、ADR 目录 | 1.18.0 版本源存在。两处目录说明仍写 23 篇 ADR，实际已有 0024；`adr/README.md` 本身已更新。根 README 能力摘要中的 ATB、teleport 子结构描述落后于能力索引。 |
| 00 架构总则 | 六个 .csproj、Directory.Build.props、Options/策略接口、模板与样例分离 | Core/L5 不引用 Unity；从 Foundation→Numbers→Rules→Carriers→Gameplay→Presentation 单向依赖。没有发现核心运行时引用具体游戏仓库/职业内容的证据。单机 RPG 领域定位不等于绑定某款游戏。 |
| 01 分层与依赖 | 各层 assembly、ICreatureTemplateQuery/IProjectileSpawner 等窄接口 | 累加式装配根与跨层依赖倒置符合文档。程序集粒度为层、模块粒度为目录；“模块可单独抽出”仍需带其契约依赖，不能理解为每模块独立二进制包。 |
| 02 引擎适配层 | core/foundation/engine_adapter、adapters/stub、Unity 包、conformance | 契约和参考实现分离；导航跨帧预算/所有形状索引化不是现行强制保证。具体 Unity/SFX/镜头装配问题见表现审核，不能由桩适配测试通过代替。 |
| 03 运行时骨架 | SimClockHost、WorldSim、TurnScheduler、Rules/Carriers/GameplayAssembly、三处 Bootstrap | 连续/离散机制存在。格子吸附没有消费 grid_snap 的运行实现，需与 ADR-0013 及 13 的“可选开启”表同步说明。各装配根的逐帧维护调用必须分别核对。 |
| 04 数据与内容管线 | DataRegistry、FieldSchema/MapSchema、SchemaAudit、ContentValidationAssembly、validate_data.py | ADR-0024 的 Map 键/值递归校验已实现；白名单剩 7 条有不同原因，不等于 7 个都应补齐的框架缺陷。CLI 顶层 map 元数据不提供完整递归 schema；不能宣称 CLI 能完全生成任意嵌套编辑表单。 |
| 05 对象模型与世界 | Unit/Player/Creature/Gobj/Projectile、移动、SceneRouter、Spawn/AreaTrigger | 逻辑仍是二维位置/可选高度。移动/空间性能取舍与具体游戏碰撞策略要分开。保存后的视图恢复和镜头进图行为见表现审核。 |
| 06 属性技能战斗 AI | StatHost/PowerHost、CastPipeline、Aura/Proc、CombatHost、AI/Targeting | 1.18 读条完成新冷却时序与实例 ID 已落地，但死亡兜底删除施法状态漏发 terminal event，独立探针复现。不能把“提供 CastInstanceId”直接写成“每个请求全路径终结事件完整”。 |
| 07 载体层 | Inventory/Equipment/Creature/Gobj/Summon/Projectile | 物品、装备与召唤通用机制存在。item.affix 具体效果是明确扩展占位；talent 完整分配器不是本框架当前承诺。离散召唤过期与 trap.trigger_shape 的未消费状态需显式列边界。 |
| 08 玩法层 | Loot/Quest/Dialog/Encounter/Economy/WorldState/Achievement/Difficulty/Spawn/Death | 任务热重载已实现，但删除活跃任务、恢复同 ID 新目标形状仍可异常。Quest.Update、日/owner/vendor 回调需宿主注入；不因此判整个任务或商人能力未实现。 |
| 09 表现层 | ViewBinder/CameraHost、FeedbackBinder、Vfx/SfxPlayer、UI/Shell、PresentationAssembly | API/生产装配/运行证据分开。SFX 帧维护、进图镜头与静止读档视图需按入口复核；已实现的 model/装备外观/命中帧不应仍列成“未实现”。 |
| 10 存档与持久化 | SaveSystem/迁移链、各模块 Persistable、ReplayRecorder/Player | 逻辑状态持久化与表现快照刷新是两个验收面。Replay 类型存在但无默认游戏回放入口；新局状态策略由游戏自己的 NewGameStarter 决定。旧 Int64 精度问题有修复代码，当前状态以本轮验证记录为准。 |
| 11 工程规范与测试 | check.ps1、build.ps1、ABI probe/surface、CI/release、Python 测试 | .NET、数据、元数据、包清单门禁均有执行入口。ABI 属性 static 改变漏检已实证；release 修复缺附件路径需要与正常 build.ps1 的 lock 字段同步。Quick/SkipUnity 不是完整交付验收。 |
| 12 扩展与变更 | ADR-0019/0021/0022/0024、schema/validator/tests 同源 | 流程文本完整。新增 ADR 后应同步总导读/根清单。不能以修改文档收窄已有承诺替代修复；若改变格子吸附等已拍板结论，应走设计决策。 |
| 13 新游戏接入指南 | games/_template GameOptions/GameBootstrap/DataHotReload、get_framework/consumer_smoke | 模板是框架交付的可替换起点；真实游戏数据/剧情/职业应留外部仓库。grid_snap “可开”与无运行消费不符。CameraResetFollowOnSceneLoadFinished 需核对实际透传，不能只列配置字段。 |
| 14 资产规格模板 | import_assets.py、gen_placeholder_assets、model/anim 素材、Display/Feedback schema | 数据化导入与通用占位资源存在；合并根的素材校验是本轮自动化范围。真实游戏素材的观感、美术质量、具体布局不属框架缺陷，也不在本次验收内。 |
| ADR、模块 README | ADR 0001–0024、各模块 contracts/core/schema/tests、能力索引 | 当前 ADR 与过去审计结论分开读取。优先级：已确认架构契约→当前代码→可重复行为；历史“已修复”文字不能覆盖本轮复现。 |
| editor 产品说明 | editor/docs/编辑器产品文档.md/.html、editor/README | Markdown 已 v2.5；HTML 仍 v2.2；README 宣称两者同步 v2.1。Range/field_meta/Map 等新契约在离线 HTML 缺失。这里需更新文档，不把外部编辑器软件开发列为框架待办。 |
| toolchain/registry 与正式包 | get_framework、sync_package_content、四包 manifests、ABI 工具、正式 zip/lock | 检查分发一致性与通用消费路径，不触发发布、不启动/修改用户私服；静态 manifest 和本地正式包核对不等于在线 registry 可用性证明。 |

## 文档更新清单

| ID | 优先级 | 文件/锚点 | 应更新的事实 | 验收方式 |
|---|---|---|---|---|
| DOC-118-01 | P3 | README.md:16；architecture/README.md:93–119 | 23 条 ADR 改为 24，并补 0024 到总文件清单；adr/README 已有，无需重复修改正确处。 | 文件列表与实际 ADR 文件集合一致。 |
| DOC-118-02 | P3 | README.md:9；architecture/落地计划/落地方案与分阶段计划.md:1366/1371 | 根摘要仍把 ATB 当非目标、teleport nested 元素结构当未实现。实际 ATB 为延期预留；teleport 元素结构已登记，目标引用完整性另属未提供校验。 | README 与能力索引分类、代码说明一致；只更新当前摘要，不篡改历史记录。 |
| DOC-118-03 | P3 | editor/README.md:7；editor/docs/编辑器产品文档.md:3/19/265；同名 .html:246/664 | 三处版本/状态不同步，HTML 缺新增 Range/field_meta/Map 契约。 | 从当前 Markdown 生成 HTML，检查关键契约段；README 写真实版本和审批状态。 |
| DOC-118-04 | P3 + 能力决策 | 04:188；13:99；ADR-0013:16；sim_loop/README:276–282；能力索引 | grid_snap 是已声明、尚无运行消费的可选机制；补进能力索引。item.affix 的占位边界也应让新消费方能在入口发现。 | 单独登记未实现/预留，不自动改为游戏责任；若保留承诺则补通用实现及测试。 |
| DOC-118-05 | P3 | QuestHost.cs:201–228 的判断记录及模块 README；施法生命周期说明 | Quest “移除定义后不会被其他路径索引”“恢复时旧定义必存在”的注释与实际路径矛盾。施法兜底依赖之后事件补发的假设不成立。 | 先按总报告修复行为，再用故障用例更新模块说明，避免仅改注释消除问题。 |
| DOC-118-06 | P3 | 11 工程规范 ABI 描述、toolchain README、release.yml 注释 | ABI surface 仍有属性调用形态盲点；release repair 的字段集合需反映当前交付物。 | 修复工具后用旧编译消费程序及正常/修复 lock 对照验证；不得仅把漏检结果继续写成兼容通过。 |
| DOC-118-07 | P3 | 14_资产规格书模板.md:389；ADR-0014 决策 2；toolchain/import_assets.py | 14 责任表仍把导入工具具体实现列给游戏层，实际通用导入工具是框架交付物。 | 将通用工具实现归框架，具体阈值/规格/资源及游戏专属扩展归游戏，避免迫使每款游戏重做工具链。 |
| DOC-118-08 | P3 | 13_新游戏接入指南.md:137；三处 Bootstrap 的 EquipmentWeaponStyleSource / view factory 参数 | “命中帧同步与武器风格均默认关闭/未接线”过度合并：武器风格来源已传进默认视图工厂，命中帧开关与 Swing/Impact VFX 消费又是不同状态。 | 拆成开关、数据来源接线、VFX 调用三项，与各入口实现逐项对照。 |
| DOC-118-09 | P3 / 策略语义核对 | core/rules/combat/README.md:61–65；core/rules/combat/core/Resolver.cs:325–337 | README 声称偏斜/格挡必跳过暴击；实际继续判暴击并应用倍率。06 未规定互斥，该算法自初始实现已如此，不能擅自按某游戏习惯改动结算。 | 先确认框架应支持的组合/可替换策略，修正文档或按决策实现；补组合分支测试。详见 core-review 补充。 |

## 未实现、未接线、游戏责任：逐项分开

| 分类 | 能力 | 本轮裁定 |
|---|---|---|
| 文档声明但运行未实现 | 格子吸附 grid_snap/cell_size、范围按格子中心采样 | 有 ADR/接入指南声明；schema/字段存在，代码不消费。是需处理的框架能力差异。 |
| 当前无离散执行 | Summon 离散持续时间过期；Loot 离散自动清理 | 不可视为完整离散覆盖；维持当前边界，是否扩展依已批准时间语义决定。注意 Loot 总时钟与“不清理”不同。 |
| 缺默认目标存在性检查 | teleport_target_ref 指向命名落点 | 元素结构校验已存在；目标地图/命名点引用完整性不由该 schema 保证，不重复算成元素未登记。 |
| 明确扩展位 | item.affix.effects | 本版只登记，不实现随机词缀/附魔等具体规则；不强制把任何游戏的装备成长系统加进框架。 |
| 透传、尚无运行消费 | gobj trap.trigger_shape | 当前 schema 白名单承认没有下游解析；不要从字段存在推导自动区域陷阱能力。需要与游戏层交互/AreaTrigger 组装责任再定接口，不能直接补具体陷阱玩法。 |
| 已实现未默认调用 | WeaponStyleResolver 的 Swing/Impact、ReplayPlayer | 类型/解析方法存在，当前默认生产流程未消费，调用方明确接线后才生效。 |
| 需注入策略/时钟 | Gobj SimTime、Quest.Update、owner/day/vendor | 默认恒值/null 有明确边界；整个采集/任务/商人机制并未关闭。 |
| 可选校验 | DisplayMapCoverageRule、FeedbackRuleValidator、SpawnSummonOnlyCreatureRule | 分别需要内容表范围、显式调用、creature 查询依赖；不是“校验器不存在”。 |
| 上层输入解析 | TargetPoint | 框架携带意图；转成目标集合由 AI/玩家辅助施法层负责，非通用技能管线缺陷。 |
| 游戏责任 | talent 完整分配/撤销/存档、新局重置策略、escort 自动路线、位移轨迹碰撞策略 | 无现行通用实现承诺，不进入框架修复单。 |
| 延期预留 | ATB | schema 接受但 TurnScheduler 不支持；不能声称可运行，也不能写成永久非目标。 |
| 建议而非门禁承诺 | 孤儿记录检测 | 尚未实现；只作为改进建议，不抬高为本轮 P2 缺陷。 |
| 明确边界 | day_cycle、跨 aura 定义互斥槽位、导航跨帧预算、完整空间索引化 | 按现行 ADR/契约处理，不从某个消费游戏的需求倒推为框架必须提供。 |
| 仓库外职责 | 编辑器可执行程序/模板/游戏实例 | 框架提供稳定数据契约、验证器与无头适配；外部编辑器开发不是本仓库缺失实现。 |

## 审核范围的限制

按 00–14 和模块清单建立覆盖，并深读装配根、变化集、数据校验/时序/持久化/发布高风险路径；不宣称逐行证明整个仓库所有代码正确。自动化通过只证明对应输入与环境。游戏内容、美术品质、具体游戏玩法、在线注册表、全部引擎/平台都不属于本次运行证明。
