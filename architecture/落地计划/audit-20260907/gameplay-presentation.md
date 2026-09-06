# Gameplay / Presentation 有界深度对照审查（2026-09-07）

## 审查口径

本审查固定在基线 `8d7057c049aa623ffd916d61540608550790b801`（工作树 `D:\workespace\ws-game-main-audit`）。范围是 `architecture/08_玩法层_掉落任务对话关卡.md`、`architecture/09_表现层.md`、相关 ADR（重点 ADR-0012、0013、0016）、`core/gameplay`、`presentation`、`adapters/unity`、`games/_template` 的文档、代码、装配和测试。历史审计只作为索引，本文件的结论均重新对当前基线取证。

本轮完成静态调用链、数据结构、装配构造和测试覆盖核对；没有跑 Unity 全套构建或全套测试，也没有把文档中的目标状态当作运行时证据。下文的“确定”表示当前源码链已经足以推出触发条件与结果；“未证实”表示需要 Unity/运行时复现才能补强的部分。

## 覆盖矩阵

| 链路/范围 | 文档与 ADR | 代码与装配 | 现有测试覆盖 | 结论 |
|---|---|---|---|---|
| 掉落生成 → `world.dropped_loot` → 读档 → 场景切换 → View | 08 §1.2、10 §2.3/§3（10:68、90-113）、ADR-0009 | `DroppedLootPersistable`、`LootHost`、`SaveSystem`、`ShellHost`、`SceneRouter`、模板 `HandlePostLoad` | Unity `VerticalSliceTests` 只断言读档后玩家位置（`VerticalSliceTests.cs:190-205`） | **P1 确定缺口**：恢复的掉落进入世界后被 `ClearAll` 清掉，且没有 post-load 重挂 |
| UI 输入 → 窄契约 → 移动 tick | 09 §1 P3、§7.2（09:28、353-364） | `UiIntents.Move`、`MovementTickHandler`；默认键盘走 `MovementHost.Request` | `UiIntentsTests` 只检查 intent kind（`UiIntentsTests.cs:79-88`） | **P2 确定语义错误**：UI 便利 API 字段名不匹配，默认键盘路径仍正常 |
| 离散步 → Feedback 播放队列 → `WaitForPlayback` 门 | 09 §6.4（09:335-341）、ADR-0013 | 模板 `GameBootstrap.OnFixedStep/OnFrameTick`、`FeedbackBinder`、`GameOptions` | 模板 smoke 默认 continuous/immediate；没有“离散 + 无反馈事件”模板验收 | **P1 条件性确定缺口**：模板路径没有空队列完成回退，配置为离散等待时会卡门 |
| 反馈规则 → VFX/SFX → 首次资源加载 → Unity 资产 | ADR-0016 决策 5/6（0016:28-29）、09 §5.3 | `PresentationAssembly`、`VfxPlayer`、`SfxPlayer`、模板 `GameBootstrap`、Unity Renderer/Audio | 播放器注入 `IResourceLoader` 的单元测试存在；模板端到端首播加载未覆盖 | **P2 确定装配缺口**：模板路径未把资源加载器交给播放器 |
| DisplayInfo 阴影 → 2D/3D 表现 | 09 §3.4（09:133-136） | `IRenderer2D`、`SpriteViewBase`、`UnityRenderer2D`、`UnityRenderer3D` | `ShadowSpec` 只有纯转换测试 | **P2 确定未接线**：数据/转换存在，实际 View 没有阴影落地 |
| CharacterRig → AnimState/程序动画 → View | 09 §4.1-4.2、09:405-406 | `SpriteViewBase` 只有 pose/layer/材质参数骨架；未找到 `CharacterRig`、`AnimState` 或原语执行器 | 无状态机/命中关键帧/原语端到端测试 | **P2 声明未实现**：需补代码或把文档降级为扩展点 |
| 锚点数据 → 方向/层 → `IAnchorQuery` | 09 §3.3.1（09:101-110） | `display.map` schema、`SpriteInfo`、`DisplayInfo` parser、`ViewBinder` | 有裸 `Vec2` + 镜像测试 | **P2 确定数据语义收窄**：`parent_layer`/`offset_by_direction` 无法进入运行期模型 |
| 回合状态 → HUD 顺序条/AP/结束回合 | 09 §7.1（09:343-364）、ADR-0013 | `HudViewModel`、Unity `GameplayPanels`、`TurnScheduler` | `HudViewModel` 只测当前行动者/轮次/结束回合 | **P2 部分实现**：只有当前行动者、轮次和结束回合，未提供完整顺序与剩余 AP |
| 模板 Shell 文案 → 本地化 | 09 §7.3（09:366-369）、模板 README | `TemplateShellUi`、模板 `shell_menu_definition` | 只验证主菜单可见 | **P3 文档/模板漂移**：入口可数据驱动，但标题和兜底按钮仍硬编码中文 |

## L4 玩法层快速覆盖矩阵

| 模块 | 当前契约/实现与测试 | 本轮结论 |
|---|---|---|
| Loot | `core/gameplay/loot/core` 的 roll/drop/pickup 与 `loot/tests` 覆盖；`DroppedLootPersistable` 接入存档 | 生成/拾取有单元覆盖；读档后场景生命周期是 GP-PRES-01 |
| Quest | `QuestHost`、目标状态机、`quest/tests` 与 gameplay 端到端测试 | 本轮未见已证实的静态断链；未把单元测试当成存读档/Unity 完成证据 |
| Dialog | `DialogHost`、gossip/story parser 与 `dialog/tests` | 菜单过滤和动作分发有测试；具体 UI 接线仍属 Presentation |
| Encounter / Level | `EncounterHost`、波次/阶段与 `encounter/tests` | 机制有测试；战斗时间模型切换与播放门由跨模块主审负责 |
| Difficulty | `DifficultyHost`、tier parser/validation 与 `difficulty/tests` | 修正光环/掉落倍率路径有测试；未声称所有游戏口味已落地 |
| Achievement | `AchievementHost` 订阅事件与 `achievement/tests` | 事件累计/解锁有测试；未单独复核每种游戏内容数据 |
| Economy | `EconomyHost`、vendor/currency 与 `economy/tests` | 买卖/补货有测试；设置与 UI 仅核对装配边界 |
| Spawn / AreaTrigger / WorldState | 各自 `core`、schema 与 `tests` 存在；`GameplayAssembly` 负责装配 | 本轮抽查未发现确定断链；需要专门运行时/数据集验收才能扩大结论 |
| DeathPolicy | 模板暴露 `GameOptions.DeathPolicy`（`games/_template/Runtime/GameOptions.cs:129-130`），传入 `CombatOptions`（`games/_template/Runtime/GameOptions.cs:169-174`）；`combat/README` 明确它只透传给 L3/L4（`core/rules/combat/README.md:144-148`），生产 `CombatHost`/`Resolver` 没有读取该字段 | **P2 模板接入边界**：切换策略目前不改变模板生产行为；这不是 CombatHost 未实现复活的缺陷，接入指南要求游戏层/载体层提供消费者，需补消费者或删掉模板口味项并改文档 |

## 确定发现

### GP-PRES-01 [P1] 读档恢复的地面掉落在切场景时被清空

**证据。** 架构把 `dropped_loot` 列为 world 段可持久化数据（`architecture/10_存档与持久化.md:59-70`），并规定世界附属段在读档顺序中恢复（`architecture/10_存档与持久化.md:99-113`）。当前实现中，`SaveSystem.Load` 按注册段调用 `persistable.Load`（`core/foundation/save_system/core/SaveSystem.cs:306-324`）；`DroppedLootPersistable.Load` 对每个记录调用 `_lootHost.RestoreDropped(entity)`（`core/gameplay/loot/core/DroppedLootPersistable.cs:63-75`）。

`ShellHost.LoadGame` 先调用 `_saveSystem.Load(slotId)`，再调用 `_sceneRouter.LoadScene(mapId)`（`presentation/shell/core/ShellHost.cs:155-176`）。而 `SceneRouter.FinishLoading` 在已有场景时执行 pre-unload 后 `_world.ClearAll()`（`core/foundation/scene_router/core/SceneRouter.cs:241-257`）。模板的 post-load 只在缺失时重新添加玩家，然后调用 `Gameplay.EnterMap`；没有恢复掉落物或重新派发其 `entity.created`（`games/_template/Runtime/GameBootstrap.cs:308-329`）。`LootHost` 的注释也明确记录了“先恢复段、后切场景”的顺序（`core/gameplay/loot/core/LootHost.cs:521-542`），但现有的同 id 原地覆盖只避免了重复 id 异常，不能抵抗后续 `ClearAll`。

**触发与影响。** 当 `PersistDropped=true`、存档含未拾取掉落，且读档时当前场景已存在时，掉落先被恢复进当前 WorldSim，随后被场景切换的 `ClearAll` 删除。结果可以是 `LoadResult.Status=Loaded` 且玩家位置恢复，但地面物品静默消失；如果 post-load 重新进入表现绑定，掉落 View 也不会出现。当前 `VerticalSliceTests` 在读档后只检查玩家位置（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/VerticalSliceTests.cs:190-205`），因此会漏掉该链路。

**处理建议。** 这是代码落地项，不应只改 README。将依赖当前地图/WorldSim 的段延迟到场景清理完成后恢复，或在 `ScenePostLoad` 后按确定顺序重放恢复记录；同时保证模块跟踪表、空间索引和 View 绑定事件一致。不要用再次保存空世界覆盖存档来掩盖问题。

**验收标准。** 集成测试应创建至少一件带位置和物品列表的掉落，保存后改变玩家位置并读档；断言 `LoadResult` 成功、同一掉落实体仍在 WorldSim、位置/物品/OwnerHint/ExpireAt 与存档一致、ViewBinder 已绑定对应 View、后续新掉落 id 不冲突，并覆盖“当前场景存在”和“首次进入地图”两种顺序。

**证据边界。** 以上清除链由静态代码顺序确定；另用真实 `WorldSim`/`DroppedLootEntity` 做了不依赖 Unity 的有界复现：恢复实体已在 WorldSim 后调用真实 `WorldSim.ClearAll()`，输出 `restored_before_clear=true; loot_after_scene_clear=False`。可用附件 [`GP-PRES-01-world-clear-repro.cs`](evidence/GP-PRES-01-world-clear-repro.cs)、[`GP-PRES-01-world-clear-repro.csproj`](evidence/GP-PRES-01-world-clear-repro.csproj) 重跑，输出记录见 [`GP-PRES-01-world-clear-repro.txt`](evidence/GP-PRES-01-world-clear-repro.txt)。该复现证明清理段，不声称已经观察到完整 Unity ShellHost 的画面或存档文件损失。

### GP-PRES-02 [P2] `UiIntents.Move` 产生的方向字段不会被移动处理器消费

**证据。** 09 的 P3 要求 UI 输入封装为意图请求（`architecture/09_表现层.md:26-29`、`architecture/09_表现层.md:351-364`）。但 `presentation/ui/core/UiIntents.cs:105-112` 的 `Move(Vec2 direction)` 生成的是 `dir_x`、`dir_y`。`MovementTickHandler.ApplyIntent` 只接受 target 或方向，缺失时发出警告并忽略（`core/carriers/unit/core/MovementTickHandler.cs:118-147`）；`TryReadDirection` 的实际读取键只有 `dx`、`dy`（`core/carriers/unit/core/MovementTickHandler.cs:388-399`）。对应单元测试只断言 kind 为 `move`，没有断言参数或跑过移动处理器（`presentation/ui/tests/UiIntentsTests.cs:79-88`）。

**触发与影响。** 任一 UI 调用 `UiIntents.Move(direction)` 后提交的 intent 会到达移动 tick，但没有 `target(x,y)` 或 `direction(dx,dy)` 可读字段，方向移动被忽略，位置不变并记录诊断。这个问题不属于“MovementHost 绕过 SubmitIntent”；模板默认键盘路径是 `GameBootstrap` 读取轴后调用 `MovementHost.Request`（`games/_template/Runtime/GameBootstrap.cs:353-359`），而 `MovementHost.Request` 正确写入 `dx/dy` 并提交 intent（`core/carriers/unit/core/MovementHost.cs:35-66`），所以默认键盘移动不受此缺陷影响；错误仅在 `UiIntents.Move` 便利 API 与消费端的字段契约。

**处理建议。** 代码统一为一个窄契约：优先把 UI 输出改为 `dx`/`dy`，或在处理器兼容读取 `dir_x`/`dir_y` 并明确弃用别名；不要继续增加第三套字段名。补一条从 `UiIntents.Move` 到 `MovementTickHandler` 的纯 .NET 测试，断言提交的参数、位移、朝向和无效输入诊断。

**验收标准。** 以 `(1,0)`、`(0,1)`、零向量和离散/连续两种模式调用 UI intent；处理器分别产生预期位移或明确拒绝，且测试断言不依赖“只提交成功”。

### GP-PRES-03 [P1 条件性] 模板离散等待模式缺少空播放队列完成回退

**证据。** 09 §6.4 规定离散步事件进入表现播放队列，队列清空后发出 `presentation.playback_finished`，等待模式再解除节奏门（`architecture/09_表现层.md:335-341`）。模板 `OnFixedStep` 在 `WaitForPlaybackPacingPolicy.IsPlaybackFinished=false` 时直接返回（`games/_template/Runtime/GameBootstrap.cs:341-359`）；模板只在构造时订阅播放完成事件（`games/_template/Runtime/GameBootstrap.cs:258-262`）。其 `OnFrameTick` 只调用 `Presentation.Feedback.Update`，没有“队列本来就是空”时的 `NotifyPlaybackFinished` 回退（`games/_template/Runtime/GameBootstrap.cs:364-398`）。`PlaybackQueue` 的 `Finished` 事件语义是“从非空变为空”，且 `Update` 对空队列直接返回（`presentation/feedback_binder/core/PlaybackQueue.cs:40-42`、`71-78`）；空队列不会自行发事件。对照之下，Unity 工作台宿主已经在 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:589-603` 加了同类回退。

**触发与影响。** 将模板 `GameOptions.PacingWaitForPlayback` 设为 true，并让当前 `found.time_model` 使用离散步；某一步没有命中任何 `feedback.binding`（包括无战斗/无反馈的动作）时，`Gameplay.Advance` 进入等待态，但队列从未由非空变空，模板没有完成信号，后续固定步永久停在 347-350。模板默认值是 `PacingWaitForPlayback=false`（`games/_template/Runtime/GameOptions.cs:164-167`），因此默认连续路径不触发；这是把模板接入离散模式时的确定性缺口。

**处理建议。** 不能把 `Feedback.Queue.PendingCount==0` 单独当成完成条件。`FeedbackBinder.Update` 先推进 merger、再推进播放队列（`presentation/feedback_binder/core/FeedbackBinder.cs:91-97`）；数值飘字在 `MergeWindow>0` 时会停留在 `_merger` 的 pending 列表而尚未入队（`presentation/feedback_binder/core/FloatingTextMerger.cs:54-82`），此时队列为空会被错误解锁。应抽出统一的“当前离散步表现已完成”状态/查询，同时覆盖 merger pending、播放队列 pending 以及本步待提交动作；只有两者都完成后才发 `presentation.playback_finished` 或解除节奏门。零反馈步也要通过同一完成协议立即完成，避免模板和工作台各自复制判断。

**验收标准。** 离散等待配置下，连续执行至少两个无反馈步骤仍能推进 tick/回合；有普通反馈动作时必须等队列实际排空再推进；`MergeWindow>0` 的金额飘字在窗口到期、进入队列并播放完成前不得解除门；immediate/continuous 默认路径行为不变。测试应断言 tick 计数、merger/queue 状态和完成事件，而非只依赖超时。

### GP-PRES-04 [P2] 模板 PresentationAssembly 没有把资源加载责任接到 VFX/SFX

**证据。** ADR-0016 决定首次引用资源的一方调用 `IResourceLoader.LoadAsync`，播放器必须按 `Effect`/`Audio` 类型触发加载；渲染器和音频只消费已加载资源（`architecture/adr/0016-引擎适配层契约阶段4联调补齐.md:24-31`）。`VfxPlayer` 与 `SfxPlayer` 都有可选 `IResourceLoader`，并仅在该依赖非空时创建 tracker（`presentation/vfx_sfx/core/VfxPlayer.cs:44-70`、`presentation/vfx_sfx/core/VfxPlayer.cs:73-118`；`presentation/vfx_sfx/core/SfxPlayer.cs:44-62`、`presentation/vfx_sfx/core/SfxPlayer.cs:64-82`）。

`PresentationAssembly` 构造函数没有 `IResourceLoader` 参数（`presentation/assembly/PresentationAssembly.cs:207-220`），构造播放器时也没有传 loader（`presentation/assembly/PresentationAssembly.cs:287-291`）。模板 `GameBootstrap` 虽把 `_host.ResourceLoader` 传给 `SceneRouter` 和 `UnityViewFactory`，却没有传给 PresentationAssembly（`games/_template/Runtime/GameBootstrap.cs:221-235`）。当前 Unity 音频未找到已加载 clip 时会记录警告并跳过播放（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:89-114`）；2D 特效未找到资源时回退通用粒子（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:205-233`）。工作台 `FrameworkResidentHost` 有 SFX 预热路径（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:441-464`），模板路径没有等价的 VFX/SFX 预热或注入。

**触发与影响。** 当游戏层在模板路径登记 `feedback.binding` 的 `play_sfx`/`play_vfx`，且资源尚未由别的路径预热时，首次播放不会发出 `LoadAsync`。SFX 明确跳过，VFX 使用通用回退；数据中已有的资源不能按 ADR 的“谁首次引用谁加载”语义进入实际表现。模板最小数据当前可以没有反馈资源，因此这是配置反馈内容时必现的接线缺口，而不是默认无战斗模板的运行时失败。

**处理建议。** 代码落地：给 `PresentationAssembly` 注入 `IResourceLoader` 并由模板传入 `_host.ResourceLoader`，让播放器保持首次引用加载；如果产品决定统一预热，则必须在模板的装配阶段覆盖所有 `vfx.def` 资源和 `sfx.def` 主/变体资源，并写清异步完成边界。不要只在 Unity Renderer 内隐式加载。

**验收标准。** 使用可观测 loader 装配模板，触发一条反馈规则：断言每个资源 id 按种类只调用一次 `LoadAsync`，SFX 在加载完成后能拿到 clip，VFX 在加载完成后走具体 effect；未加载时仍只产生诊断/占位，不抛异常。另测无反馈资源的默认模板启动不受影响。

### GP-PRES-05 [P2] 阴影数据与渲染链没有实际接线

**证据。** 09 要求每个可见单位默认带地面影子，`sprite` 经 `IRenderer2D`，`model` 经 `IRenderer3D.setShadow`，并要求高度只移动角色不移动影子（`architecture/09_表现层.md:133-136`）。当前 `IRenderer2D` 只有创建、层、变换、材质、销毁和粒子接口，没有阴影方法（`core/foundation/engine_adapter/contracts/IRenderer2D.cs:49-80`）。`SpriteViewBase` 的构造、层合成和 `SyncPose` 也没有消费 `DisplayInfo.Shadow` 或调用阴影接口（`presentation/render/core/SpriteViewBase.cs:79-106`、`presentation/render/core/SpriteViewBase.cs:245-256`）。Unity 2D 实现只创建根、`LayersRoot` 与 SpriteRenderer，维护 transform/sorting/layers（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:54-64`、`86-165`）；Unity 3D 的 `SetShadow` 仍直接抛 `NotSupportedException`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:40-48`）。`ShadowSpec` 的测试只证明枚举转换，不证明任何 View 产生影子（`presentation/render/tests/RenderConventionHostTests.cs:102-115`）。

**触发与影响。** 任意使用默认 `shadow=blob` 的 sprite DisplayInfo 进入可见范围时，当前路径没有影子节点或阴影更新；设置高度也只移动 `LayersRoot`，无法满足“影子留在逻辑平面”的视觉语义。model 路线在 Unity 中还是显式未支持的产品边界，但 sprite 默认路线已经缺少基础能力。

**处理建议。** 基础架构应补一个明确的 2D 阴影契约（例如独立 shadow handle/配置与更新接口），由 `SpriteViewBase` 在创建/pose/destroy 时消费 `DisplayInfo.Shadow`；或若决定把 blob 阴影留给具体游戏，必须改 09 的“基础架构提供”和默认影子条款，并同步 14/模板验收。不能把枚举转换测试当成表现完成证据。

**验收标准。** 对 `none`、`blob`、`projected` 分别断言 2D 路径行为；对带高度的单位断言角色绘制位置变化、影子锚点仍等于逻辑位置；model 路线的 Unity 不支持状态需在边界文档与测试中明确。

### GP-PRES-06 [P2] 09 声明的 CharacterRig、AnimState 与程序动画原语没有实现入口

**证据。** 09 将 `CharacterRig` 定为表现层基础能力，要求层/槽位、锚点/挂点、动画状态机和程序动画原语，并列出 `move/rotate/scale/flash/trail/stagger/topple/fade`（`architecture/09_表现层.md:147-173`），同时把动画七态列为基础架构提供（`architecture/09_表现层.md:175-184`、`architecture/09_表现层.md:401-406`）。当前 `presentation/render/README.md` 将 `SpriteViewBase` 明确描述为“CharacterRig 职责的一部分”（`presentation/render/README.md:1-6`），而 `SpriteViewBase` 实际只提供 SpriteHandle、层合成、pose、shader 参数和装备事件（`presentation/render/core/SpriteViewBase.cs:37-45`、`108-158`、`245-275`）。当前基线源码中没有 `CharacterRig`、`AnimState`、命中关键帧或上述原语的运行期类型/调用入口；`IRenderer3D.PlayAnim` 的存在只代表引擎接口，不能证明有状态机驱动。

**触发与影响。** 一份 DisplayInfo/反馈数据无法只改数据就驱动 09 所述的状态切换、序列帧/骨骼剪辑或 trail/stagger/topple 等动作；当前可验证的 sprite 路径仅能跟随位置、方向、层和闪白材质。若游戏按 09 验收动画表现，能力缺失会在运行时表现为静态精灵或无动作，且没有统一事件/关键帧边界。

**处理建议。** 这是实现或文档二选一的架构决策：若 09 的“基础架构提供”保持不变，补 `CharacterRig`/状态机/原语调度，并以 `IView`/事件为输入、以渲染适配接口为输出；若当前迭代只交付 SpriteViewBase 骨架，则把 09 §4 和验收标准改写为明确扩展点，并在 13/模板中标注未覆盖。不能以 `presentation/vfx_sfx/README.md` 中“本模块不实现 CharacterRig”（该模块边界）替代全局能力的完成证据。

**验收标准。** 至少用一个 sprite 和一个 model（model 可声明 Unity 边界）驱动 idle/move/hit/death，断言状态由事件/只读运动状态切换；对 flash 之外的原语至少有执行/回收测试；命中关键帧应只产生表现事件，不改变战斗结算。

### GP-PRES-07 [P2] 锚点文档字段比运行期 schema/model 更丰富，方向偏移会丢失

**证据。** 09 的锚点表要求 `anchor_id`、`parent_layer`、必填 `offset` 和可选 `offset_by_direction`，并要求经 `IAnchorQuery` 查询（`architecture/09_表现层.md:101-110`）。实际 `display.map` schema 只把 `anchor_points` 声明为普通 Object（`core/foundation/display_info/core/DisplaySchemas.cs:52-58`）；`SpriteInfo.AnchorPoints` 是 `Dictionary<string, Vec2>`（`core/foundation/display_info/contracts/SpriteInfo.cs:40-60`）。解析器把每个键直接解析为一个 Vec2（`core/foundation/display_info/contracts/DisplayInfo.cs:175-186`），不读取层归属或按方向覆盖；`ViewBinder` 也只查裸键、按镜像翻转一个 base offset（`presentation/view_binding/core/ViewBinder.cs:272-291`）。

**触发与影响。** 内容若按 09 使用嵌套锚点记录、`parent_layer` 或 `offset_by_direction`，当前 parser 会把结构拒绝为非 Vec2 或直接无法表达；即使使用当前裸 Vec2 形式，方向差异也只能靠整体 `flipX`，不能得到文档承诺的方向覆盖和父层语义。挂点 VFX、武器和头顶信息因此可能错位。现有镜像测试只覆盖简化的裸 offset 语义。

**处理建议。** 需在 09/04/schema 和代码之间做一次明确收敛：要么补强 `AnchorDef`/方向 map/父层解析及查询，要么把文档字段删为当前 `Map<string, Vec2>` 的最小契约并把方向特化列为游戏扩展。若保留文档现状，代码修复优先于加更多调用方特判。

**验收标准。** 用同一实体配置 `front` 与 `side` 两套 offset、父层和镜像来源，断言 `IAnchorQuery` 在每个方向返回预期世界坐标；缺省方向回退 base offset；非法结构在数据校验阶段给出字段级诊断。

### GP-PRES-08 [P3] 模板主菜单的本地化与 README 的“数据驱动”描述不完全一致

**证据。** 09 明确要求面向玩家文案一律经本地化表，不在 UI 层硬编码（`architecture/09_表现层.md:366-369`）。`TemplateShellUi` 的菜单入口可以从 `shell_menu_definition` 取 `text_key`，但标题直接写死 `"<game> 示例主菜单"`，缺 key 时和空表兜底时都直接写死 `"开始游戏"`（`games/_template/Runtime/TemplateShellUi.cs:49-79`）。模板 README 将该 UI 描述为最小“数据驱动”主菜单（`games/_template/README.md:17-20`），同时确认其只覆盖新游戏入口（`games/_template/README.md:139-151`）。

**触发与影响。** 缺少本地化行、空菜单表或更换语言时，标题/兜底按钮绕开 L10n；复制模板后即使菜单入口使用数据，仍会出现不可翻译的玩家文案。它不影响默认模板装配，但违反 09 的表现层铁律并造成 README 对“数据驱动”的过宽表述。

**处理建议。** 模板若作为可复制的基础实现，应给标题和 fallback 配置稳定文本 key，并让缺 key 显示诊断占位；若产品明确允许示例占位，则在 README 和 13 §6 写出“示例文案例外”，不要继续称为完全数据驱动。优先改模板代码，必要时再补文档边界。

**验收标准。** 切换至少两种语言并覆盖入口有 key、无 key、表为空三种情况；所有面向玩家的文本均来自 L10n 或明确标记为开发诊断，不出现硬编码产品文案。

### GP-PRES-09 [P2] 回合 HUD 目前只有部分状态，未覆盖顺序条和行动点

**证据。** 09 §7.1 将“回合顺序条（仅离散模式）、行动点显示（启用 `action_points` 时）、结束回合按钮”同时列为 UI 组成（`architecture/09_表现层.md:343-364`）。当前 `HudViewModel` 只暴露 `CurrentActorId`、`RoundIndex`、`CanEndTurn`（`presentation/ui/core/ViewModels/HudViewModel.cs:95-112`），`Refresh` 也只从 `TurnScheduler` 读取当前行动者和轮次（`presentation/ui/core/ViewModels/HudViewModel.cs:154-185`）；没有 `GetOrder` 的 ViewModel 投影，也没有剩余行动点字段。Unity `GameplayPanels` 只渲染“行动者/轮次”和结束回合按钮（`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Ui/Panels/GameplayPanels.cs:112-124`）。底层 `TurnScheduler.GetOrder` 和行动点账本虽有实现/测试，但没有接到 HUD。

**触发与影响。** 离散模式启用后，玩家能看到当前行动者、轮次和是否可结束回合，却看不到完整行动顺序与剩余 AP；这属于“部分实现”，不能把当前变更记录中“回合 HUD 已合并”解释为全部 09 §7.1 能力已收口。旧审计“完全没有回合 HUD”的结论已过时，但“完整顺序/AP 已完成”同样没有证据。

**处理建议。** 补 `HudViewModel` 的只读顺序快照和行动点快照（或由 `TurnScheduler` 提供正式只读查询），由 Unity 面板消费；若产品决定不展示完整顺序/AP，则改 09 §7.1 与验收条款，明确这是可裁剪单元而非已完成能力。

**验收标准。** 在 `fixed_order` 与 `action_points` 两种策略下，HUD 显示当前行动者、稳定排序后的完整队列、当前轮次和当前行动者剩余 AP；结束回合后这些快照随事件刷新。没有回合制时不显示该组控件。

## L4 接入边界发现

### GP-GAME-01 [P2 接入边界，非 CombatHost 缺陷] 模板 DeathPolicy 有配置透传，但没有下游消费者

**证据。** 模板把 `DeathPolicy` 暴露为口味字段（`games/_template/Runtime/GameOptions.cs:129-130`），并在构造 `CombatOptions` 时透传（`games/_template/Runtime/GameOptions.cs:169-174`）。`CombatOptions` 的说明明确死亡复活不在 combat 模块处理，只把策略值交给 L3/L4（`core/rules/combat/contracts/CombatOptions.cs:49-51`）；combat README 同样写明 `Resolver` 只把生命置为 0、发出 `unit.died`，复活流程属于 L3/L4（`core/rules/combat/README.md:144-148`）。当前生产代码检索没有发现下游读取 `CombatOptions.DeathPolicy` 的消费者。

**触发与影响。** 在模板中把 `RespawnPoint` 改成 `Permadeath` 不会改变现有死亡后的流程；这是模板未提供 L3/L4 复活策略接入的边界，不能归咎于 `CombatHost` 没有实现复活。若把该字段当作模板已生效的产品配置，会造成配置与行为漂移。

**处理建议。** 由游戏层选择：实现一个明确的死亡策略消费者（订阅 `unit.died`，按策略重置/移除/读档，并发出 `unit.respawned`），或删除模板字段并在 README/13 §4 标为“待游戏层实现”。不要在 combat 中偷偷加入复活流程。

**验收标准。** 模板要么在配置说明中明确 `DeathPolicy` 仅透传、未生效，要么用最小游戏层消费者分别验证 `RespawnPoint` 与 `Permadeath` 的可观察差异、场景/存档边界和事件顺序。

## 明示扩展点与产品边界（不误报为缺陷）

1. Unity 包 README 明确把 `UnityRenderer3D` 的 model 路线标为当前迭代的 downgrade/未支持，`UnityViewFactory` 对非 sprite DisplayInfo 退化为 `NullView`；因此不能把“Unity 当前没有完整 3D 模型显示”单独算作本轮 P1。若游戏选择 model，必须在接入验收中声明其引擎能力和资源边界。
2. 模板 README 明确它是“主菜单 → 新游戏 → 一张地图”的最小数据集，故没有战斗、技能、物品、任务和玩家外观不是当前模板缺陷；但一旦模板配置了反馈资源、存档掉落或离散 pacing，上述装配契约仍必须成立。
3. 09 的方向索引重映射属于游戏口味配置；本模板 `GameOptions` 只构造 `DirectionCount`，没有提供 remap 字段，因此本轮不把“模板未暴露 remap”列为确定 bug。若游戏层注入非恒等 remap，必须把同一 `IRenderConventionHost` 同时交给 ViewFactory 和 ViewBinder，并补集成测试。
4. 09 的回合状态 HUD 已有部分实现：`CurrentActorId`、`RoundIndex`、`CanEndTurn` 已接通，但完整顺序条和行动点仍缺（GP-PRES-09）。因此本审查不重复旧审计的“完全无 UI”结论，也不把“回合 HUD 已合并”误报成 09 §7.1 全部收口。`WaitForPlayback` 无事件时的 pacing 门、Merger 延迟与 replay 语义在本文件只记录模板侧直接证据，跨模块主审可继续合并其全局结论。
5. `FeedbackBinder.TextSourceKind.Literal` 当前直接把 `text_key` 原文作为展示文本（`presentation/feedback_binder/core/FeedbackBinder.cs:202-208`），未接 `l10n.text`；模块 README 已将本项列为契约缺口。它与 GP-PRES-08 的模板硬编码文案是两条独立边界，本轮不另建发现 ID。

## 修复优先级与证据边界

建议先修 GP-PRES-01，因为它会造成持久化内容静默丢失；随后修 GP-PRES-02（UI 便利 API，默认键盘路径不受影响）与启用离散等待时的 GP-PRES-03；再修 GP-PRES-04/05，恢复配置资源与默认视觉的实际表现。GP-PRES-06/07/GP-PRES-09 需要设计层决定补齐通用能力还是收窄架构契约；GP-PRES-08 可随模板复制前处理；GP-GAME-01 需在模板文档或游戏层接入中明确消费者边界。

本文件只新增审计证据和落地计划，没有修改生产代码或原有架构文档。所有行号均指向本基线工作树的当前文件；代码修复后需重新取行号并补 Unity/纯 .NET 的针对性证据。静态证据不等同于 Runtime、存档文件兼容性、性能或完整 Unity 构建通过。
