# 变更日志

本文件记录 ws-game（游戏技术基础架构框架仓库）各构建产物版本号之间的变更，格式遵循
[Keep a Changelog](https://keepachangelog.com/) 惯例；版本号遵循语义化版本
（[SemVer](https://semver.org/)）：`MAJOR.MINOR.PATCH`——MAJOR 表示不兼容变更（走 ADR 审批的
契约签名变化、存档格式不兼容、数据表字段删改）；MINOR 表示向后兼容的新增能力；PATCH 表示缺陷
修复与文档勘误。单一版本源见仓库根 `VERSION` 文件；版本号与发布流程见根 `README.md`"版本与发布"
一节。

## [Unreleased]

（尚未发布的变更累积在此，随下一次 `build.ps1 -Release` 归档为对应版本号的条目。）

## [1.1.0] - 2026-09-07

第六方深度审核（codex 第四轮，基线 `1.0.0`/`7e63d66`，报告见
`architecture/落地计划/audit-7e63d66-20260907/`）19 条发现（C01～C12 共 12 条代码问题、P01～P07
共 7 条项目/交付问题）全部核实成立并根治，详见
`architecture/落地计划/audit-7e63d66-20260907/followup-2026-09-07d.md`。本条目记录变更内容。

### 修复（概要，逐条详见 followup 文档）

- 存档读档：旧备份候选核对 `meta.slot_id` 归属，避免跨槽误读（C01）；候选筛选核对 meta 必填字段，
  避免语义损坏文件挡住健康备份（C10）。
- 规则/技能：施法来源被销毁后周期效果缩放属性降级为 0、进战判定静默跳过而非抛异常（C02）；吸收
  耗尽的连锁移除正确传递触发深度，纳入 `MaxTriggerDepth` 收敛预算（C03）；装备 Replace 换句柄后
  另一件装备同步迁移引用，不再误清光环（C08）；技能读档改为替换语义而非只增不减（C09）。
- 玩法：Encounter/Achievement 发奖失败后保留可重试状态，不提前提交终态（C04）；`RewardDispatcher`
  按实际落地量回滚，不假设"请求量=落地量"（C05）；任务扣除物品改为先核验总量、不够不碰库存的原子
  操作（C06）；刷新点存档倒计时在同图读档时优先于当下世界状态（C11）；`reload_save` 现在也发布
  复活事件，动画状态机不再卡在死亡态（C12）。
- 表现：VFX/SFX 资源冷加载超时也会完整走完播放完成信号链，`ISfxPlayer` 新增独立时钟入口接入
  Unity 生产帧循环（C07）。
- 交付：Release 工作流在打包前补一步默认构建，干净 checkout 也能出传统路径 DLL（P01）；发行 ZIP
  与 UPM 工具链包的 validator 自包含（编译好的 DLL 或源码引用二选一），不再依赖包内不存在的源码树
  （P02）；同步脚本改清单制，只清理框架自己上次写入的文件，不再误删消费者文件（P03）；
  `get_framework.ps1` 默认严格校验请求版本与本地归档版本一致，不一致需显式 `-AllowVersionMismatch`
  （P04）；发布只推当前分支与本次新建的单个标签，不再固定推 `main` 与全部标签（P05）；私服禁止
  自注册取得发布权限，发布/删包限定到显式发布账号（P06）；sprite/audio/vfx 三类资源的同步路径与
  Unity loader 实际查找路径统一到共享映射表 `toolchain/resource_layout_map.json`（P07）。

### 接口变更与迁移说明

非破坏性增补（C# 默认接口方法，未覆盖的既有实现自动获得历史行为，无需改动）：

- `Core.Carriers.Common.IInventoryHost` 新增 `bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount)`。
- `Core.Rules.Common.IAuraQuery` 新增带触发深度参数的移除重载，以及 `InstanceReplaced` 事件（默认空
  `add`/`remove`）。

破坏性增补（自定义实现方需补实现；仓库内既有实现均已补齐）：

- `Core.Gameplay.Achievement.IAchievementHost` 新增 `IReadOnlyList<Id> RetryPendingRewards(Id unitId)`
  ——仓库内唯一实现 `AchievementHost` 已补齐。
- `Presentation.VfxSfx.Contracts.ISfxPlayer` 新增 `void Update(double dt)`——仓库内 `SfxPlayer` 与
  测试用 `RecordingSfxPlayer`（`presentation/vfx_sfx/tests/AudioLayerVolumeHostTests.cs`）已补齐。

行为变更（签名不变，语义/时序变化，下游若按旧假设编写逻辑需要重新核对）：

- `Core.Gameplay.Death.RespawnPolicy.ReloadSave` 读档成功时现在也经 `IEventBus.Enqueue` 补发一次
  `Core.Rules.Common.UnitRespawnedEvent`（此前只有 `RespawnPoint` 策略发布该事件）。
- 存档段 `player.achievement_state` 每条记录新增可选字段 `pending_reward`（布尔，默认 `false`），
  向后兼容，旧存档缺省该字段按 `false` 处理。
- `Core.Rules.Skill.KnownSkillsPersistable.Load` 改为替换语义（快照未包含的永久技能会被撤销），不
  再是只增不减。
- `toolchain/get_framework.ps1` 新增 `-AllowVersionMismatch` 开关（默认关闭）；不带该开关时请求
  版本与本地归档版本不一致会直接 `throw`，不再仅 warning 后继续落地。
- `build.ps1 -Release`/`-Publish` 的 `git push` 改为推送当前所在分支 + 本次新建的单个标签，不再
  固定推送 `main` 分支与本机全部标签（`--tags`）；在 detached HEAD 下会报错拒绝执行。
- 私服 `toolchain/registry/config.yaml`：`auth.htpasswd.max_users` 由未设置（等价放开自注册）改为
  `-1`（禁止自注册）；`publish`/`unpublish` 权限从 `$authenticated`（任何已认证用户）改为限定显式
  用户名 `ws-game-publisher`。任何依赖"匿名自注册后即可发布"的私服接入脚本需要改用
  `toolchain/registry/init_publisher.ps1` 无人值守建号。

## [1.0.0] - 2026-09-07

首个正式基线版本。此前 `0.1.0`/`0.2.0` 均为落地过程中的里程碑快照（供消费方演练与打包流程自测
使用，未作为正式对外发布版本），`1.0.0` 是阶段 0～5 全部完成、经三轮内部审计与一轮外部深度审核
修复收口后的第一个"可供真实游戏接入"的稳定基线。

### 新增

- **阶段 0～5 全部完成**：环境与仓库骨架、L0 基础层（12 模块）、L1+L2 数值与规则层、L3+L4 载体
  与玩法层、Unity 适配层 + 表现层 + UI 套件、美术管线与资产规格；`Core.sln` 六个测试工程合计
  1550+ 例单测全过，Unity EditMode/PlayMode 测试全绿，独立版无人值守冒烟（连续/离散两种时间
  模型）通过，消费方演练（从零搭建独立于框架源码树的最小 Unity 工程，只以分发包为输入）通过。
- **离散时间模型**：与连续时间模型并列的第二套时间驱动方式（`TurnScheduler`、先攻策略、行动点
  移动预算、回合 HUD 等），玩家意图经 `WorldSim` 路由到调度器，回合结束统一推进计时器。
- **框架级数据目录分层**：`data/_framework/`（事件词汇登记表、输入动作声明等，随分发包交付）与
  `data/_sample/`（框架自测数据，不随分发包交付）分离；`DataRegistry` 支持多根合并加载（主键/
  schema 冲突阻断）。
- **新游戏模板** `games/_template/`：可运行的最小闭环骨架（`GameBootstrap`/`GameOptions`/
  `Editor/GameSceneBuilder`/`data/game/`/`validate.ps1`/PlayMode 冒烟测试），对照 13 号文档口味
  配置项清单逐行落地。
- **资产管线**：`toolchain/import_assets.py` 资产导入工具、`assets/_placeholder/` 通用占位资产
  包、方向档位/纸娃娃分层/序列帧图集等资产契约（14 号文档）。
- **一键门禁** `check.ps1`（22 步）与提交前钩子 `.githooks/pre-commit`（快速子集）、持续集成
  `.github/workflows/ci.yml`（非 Unity 门禁子集）。
- **版本管理方案**：语义化版本、`CHANGELOG.md`、`build.ps1 -Release`/`-DryRun`/`-Publish`、
  维护分支流程（`release/X.Y.x`）、发布工作流 `.github/workflows/release.yml`、游戏侧引用工具
  `toolchain/get_framework.ps1` 与锁文件 `ws-game.lock`。
- **私服交付通道**：与 zip 快照通道并存的第二条消费通道——私有包仓库（`toolchain/registry/`，
  Verdaccio，npm 兼容协议）+ 三个可发布包拆分（`com.gamefoundation.adapter.unity`/
  `com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`）；`build.ps1 -Dist` 新增
  组装三个包 + `npm pack`，`-Release` 新增 `-PublishRegistry [-RegistryUrl]`；
  `toolchain/get_framework.ps1` 新增 `-FromRegistry`；`toolchain/sync_package_content.ps1`
  （新增）同步私服包内容到消费游戏工程；`check.ps1` 新增"包清单一致性"步骤。

### 修复

- **三轮内部文档代码一致性审计**（2026-09-05～2026-09-07）：逐轮核对 00～14 号架构文档与实现的
  一致性，修复审计发现的代码缺失（行动点、`SpellModDimension.Charges`、离散 GCD 接线、死亡复活
  三策略执行主体、种族被动光环应用、回合状态占位值等）与文档勘误，详见
  `architecture/落地计划/文档代码一致性审计_2026-09-05.md`、`_2026-09-06.md`、`_2026-09-07.md`。
- **外部深度审核 33 条发现根治**（分支 `codex/deep-review-b3b91ee-20260907`，报告见
  `architecture/落地计划/audit-b3b91ee-20260907/`）：FND-01～10、GP-01～10、RC-01～11、
  TOOL-01/02 共 33 条经三波并行核实全部成立并根治；排障过程中额外发现并修复 PlayMode 全量套件
  `VerticalSliceTests` 因跨夹具存档槽配额累积导致的隐性失败。
- **缺口收敛 G1/G2/G3**（2026-09-05）：16 条已知契约缺口中 13 条落地解决（`AnchorResolver`、
  `SpawnRequester`、`TeleportResolverDelegate`、`SaveRequesterDelegate`、回合状态显示等），3 条
  设计层判断维持"保留"（非拍板内容或本就只需单点承担的既定设计）。
- 修复：codex 第三轮深度审核 19 条（详见 audit-68c9bed-20260907/followup-2026-09-07c.md）。
- 修复：发布流程先提交后打包，lock/MANIFEST 的 `git_commit` 指向发布提交；写回覆盖
  `packages-lock.json`（`build.ps1 -Release` 首次实跑发现的时序与写回遗漏两处缺陷，根治后
  `1.0.0` 重新发布，详见根 `README.md`"版本与发布"一节）。

### 兼容性说明

- 存档格式：`save_version`（存档信封层字段，迁移链唯一依据）当前为初始版本，尚无历史存档需要
  迁移；后续存档结构不兼容变更须递增 `save_version` 并登记迁移函数（见
  `architecture/10_存档与持久化.md` 第 5 节）。
- 数据表：各表独立的 `schema_version`（见 `architecture/04_数据与内容管线.md`）随本版本一次性
  确定，表结构不兼容变更（字段删改）须递增 `schema_version` 并提供迁移路径。
- 构建产物公开契约：六个核心 DLL（`Core.Foundation`、`Core.Numbers`、`Core.Rules`、
  `Core.Carriers`、`Core.Gameplay`、`Presentation.Common`）随分发包 `dist/1.0.0/` 交付；对外公开
  的 L-1 接口签名（引擎适配层契约）、事件 key、数据表结构、存档结构变更均属 MAJOR 级变更范畴。
- 与 `0.2.0` 的差异：`1.0.0` 不改变任何公开契约或数据结构，只是把此前若干里程碑快照正式确立为
  第一个语义化版本基线，并新增本文件描述的版本管理方案本身（`build.ps1`/`check.ps1`/工作流/
  文档新增的发布相关能力）。

### 从 68c9bed 早期消费者迁移（早于 `0.1.0`/`0.2.0` 快照拉取过框架的消费方需核对）

`1.0.0` 基线包含 codex 第三轮深度审核（`audit-68c9bed-20260907/`）引入的以下破坏性/行为变更，若消费
方在提交 `68c9bed` 或更早时拉取过框架、并自行实现或依赖了下列契约，需要按下表核对：

| 契约/行为 | 变更内容 | 影响范围与迁移动作 |
|---|---|---|
| `Core.Gameplay.Common.IRewardDispatcher.Grant` | 签名由 `void Grant(...)` 改为 `bool Grant(...)`（破坏性签名变更） | 任何直接实现本接口的类型需要补返回值；调用方若忽略返回值仍可编译通过，但拿不到"是否实际发放成功"的信号，建议改为检查返回值以配合 `IQuestHost.TurnIn` 的原子化回滚（发放失败时任务不会被标记 `TurnedIn`，已消耗物品会回滚）。 |
| `Core.Rules.Common.ITargetHost` | 新增方法 `FilterExplicitTargets`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现；仓库内唯一实现 `TargetHost` 已补齐。修复前显式指定的非法目标（如链式过滤要求 undead 但玩家显式指定了非 undead 目标）会被直接放行，修复后统一经该方法校验并按 `NoValidTarget` 失败码拒绝。 |
| `Presentation.FeedbackBinder.Contracts.IFeedbackSink` | 新增事件 `PendingPlaybackChanged`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现（默认空 `add`/`remove` 亦可）。配套 `Presentation.VfxSfx.Contracts.IVfxPlayer`/`ISfxPlayer` 同批新增 `PendingSpawnCountChanged`/`PendingPlayCountChanged` 事件；`FeedbackBinder.TryPublishFinished` 改为"队列空 && 无 merger 待处理 && 无 sink 待处理"三者同时成立才发 `PlaybackFinishedEvent`，此前冷资源（首次加载中）的挂起播放会被误判为已完成。 |

以上三项详见 `architecture/落地计划/audit-68c9bed-20260907/followup-2026-09-07c.md`（N02/N10/N17）。

## [0.2.0]

里程碑快照（供内部打包流程与消费方演练自测使用）。收录工程收尾 K、加固波 J（契约一致性测试套件、
消费方演练脚本、PlayMode 隔离）、第三轮审计修复波（W1～W4）、第四方深度审核修复三波、离散时间
模型引擎侧接线、`found.time_model` 归属勘误、框架级数据目录与新游戏模板等一系列提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。

## [0.1.0]

首个里程碑快照。收录阶段 0～5 全部完成、缺口收敛 G1/G2/G3、框架收官（离散时间模型初版落地、
文档/数据/门禁收尾）、收边波 I/J1 等提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。
