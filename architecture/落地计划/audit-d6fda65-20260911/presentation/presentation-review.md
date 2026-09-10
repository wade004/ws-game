# ws-game 1.18.0 表现、适配与模板审核

基线：`d6fda65cd6b00ede5c5f6f606f724d2ab0f60603`，只读源码根 `D:\workespace\ws-game-audit-d6fda65-20260911`。本报告代码位置均相对此根。审核输出和 Unity 副本均在 `D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\presentation`。原项目未改动；结束时 frozen worktree 的 `git status --porcelain` 为空。

结论：确认 3 个 P2 问题。其中默认常驻入口进图后失去镜头跟随具有真实 Unity Runtime 失败证据；两个其他入口漏驱动 SFX 超时具有 .NET 语义复现、真实 Unity 资源加载前提验证和静态装配证据；既有 View 的同图读档位置快照失效具有真实 SaveSystem/WorldSim 的 .NET 复现。没有把样例内容、可替换默认策略或模板主动缩小的内容范围认定为游戏耦合。

## 证据边界

| 层级 | 本轮证据 | 不代表什么 |
|---|---|---|
| 静态/API | 02/09/13/14、相关 ADR、模块 README、三个生产入口与关键实现逐路径核查 | API 存在不等于默认接线或 Unity 已验证 |
| 默认装配 | `PresentationAssembly`、`UnityViewFactory`、`GameFoundationBootstrap`、`FrameworkResidentHost`、模板 `GameBootstrap` | 示例入口中的数据 id 不自动成为框架强耦合 |
| .NET Runtime | 冻结源码独立编译；`probe/Program.cs` + `probe/probe.log`；两组故障与显式控制组 | 不是 Unity 渲染证据 |
| Unity Runtime | 6000.3.23f1；`playmode-selected.xml`/`.log`：10 项，9 通过、1 失败（新增镜头 oracle） | 非全量 PlayMode；未跑本轮 EditMode、Standalone、IL2CPP、正式分发消费方 |

独立编译命令：`dotnet build presentation/Presentation.Common.csproj -c Release --artifacts-path <本输出根>/build --nologo -v quiet`，以及 `adapters/stub/Adapters.Stub.csproj` 同参。均 0 warning / 0 error。探针只引用这次生成的七 DLL，哈希在 `probe/assembly-hashes.json`；未引用旧审核 DLL。六个生产 DLL 同步进入 UnityCopy。

UnityCopy 复制 frozen 的 adapters/games/data/assets/toolchain，保留本地 conformance 和 template 包相对路径；执行副本 `build.ps1 -SyncContent` 同步 StreamingAssets、sample/template 场景、nav、字体。仅增加审核测试 `Audit118PresentationTests.cs`，并把测试夹具 `GlobalPlayModeTestSetup.TestUserDataRoot` 重定向到本输出根下 `test-userdata`，避免访问用户原测试存档。没有改生产 C#。

## PRES118-01 — P2：场景加载完成后默认镜头跟随被清空，生产入口未恢复

- **准确位置**：`presentation/assembly/PresentationAssembly.cs:314-322`；`presentation/camera/core/CameraHost.cs:157-163`；`presentation/camera/core/CameraHost.cs:96-105`；`games/_template/Runtime/GameOptions.cs:127` 和 `GameBootstrap.cs:419-439`。
- **契约**：09 §3.5/§8 的固定镜头跟随；02 §1.13 `ICamera.Follow`；13 §4 明示模板保留“场景完成后是否重置跟随”配置。
- **触发**：使用带 camera_profile 的默认 `FrameworkResidentHost`，调用 Shell.Start，再 NewGame，完成真实 SceneRouter 进图。
- **根因**：PresentationAssembly 构造时自动 Configure+Follow(player)。CameraHost 默认选项 `ResetFollowOnSceneLoadFinished=true`，收到 `scene.load_finished` 时 `_followEntityId=null`。三个入口中未找到后续 `Camera.Follow`；SceneRouter 在 PostLoad hook 之后才派发 load_finished（`core/foundation/scene_router/core/SceneRouter.cs:297-298`），仅在现有 PostLoad hook 提前 Follow 也仍会被清空。模板声明的开关亦没有传进 CameraHostOptions。
- **实际/预期**：Unity oracle 输出 `[AUDIT118 CAMERA] before=unit.sample_player;after=;expected=unit.sample_player;page=InWorld`；NUnit `Expected: unit.sample_player; But was: null`。预期默认游戏进入世界后镜头继续跟随选定玩家。后续 Camera.Update 因无目标直接返回，镜头不随玩家继续移动。
- **修复方向**：由生产装配明确选择持续跟随（例如传 `resetFollowOnSceneLoadFinished:false`），或者在 load_finished 完成后按新场景重新指定目标；同时真正转接模板公开开关。不要仅在 PostLoad hook 写一次 Follow。
- **验收**：保留独立 oracle；覆盖首次新游戏、跨图、读档重进，检查 FollowEntityId 和实际 ICamera.Follow/相机位置变化；模板 true/false 配置分别验证。
- **证据**：`UnityCopy/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/Audit118PresentationTests.cs`、`playmode-selected.xml`、`playmode-selected.log:5693`。本轮 Runtime 直接确认 Resident；其余入口范围由相同默认选项和无重接线的静态路径确认。

## PRES118-02 — P2：两个引导入口漏驱动 Sfx.Update，可永久关闭回放节奏门

- **准确位置**：`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:748-769`；`games/_template/Runtime/GameBootstrap.cs:538-567`。对照正确入口：`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:851`。
- **契约**：02 §1.7 未加载表现资源应使用占位且不令调用方崩溃；09 §6.4 等待首次资源完成后解除 `presentation.playback_finished` 节奏门；SfxPlayer 自身提供 `Update` 超时回收责任（`presentation/vfx_sfx/core/SfxPlayer.cs:208-249`）。
- **可到达触发**：连续两次播放同一个缺失音效资源。第一次真实加载失败，回调正常丢弃请求。第二次仍进入 QueuePendingPlay，但 `_pendingResourceLoads` 保留已尝试 id，不再次 LoadAsync（SfxPlayer:151-157），因此第二次请求没有未来回调可等，只能靠 Update 清理。无须假设资源加载器违反回调契约，也无须构造异常线程。
- **根因链**：两个 OnFrameTick 只驱动 Feedback.Update 和 Vfx.Update，均不驱动 Sfx.Update。`FeedbackBinder.Update` 仅更新 merger/queue/hitframe；`CompositeFeedbackSink.cs:81` 将 Sfx.PendingPlayCount 纳入 pending；PresentationAssembly:424 用 Feedback.HasPendingPlayback 回填 pacing probe。于是等待回放时，新模拟步停下，也通常不会产生后续 Play 来触发惰性清理。
- **.NET 实际**：`first_failed_load_pending=0`；第二次请求后 600 个等价表现帧仍 `pending=1;pacing_finished=False`；显式 `Sfx.Update` 控制组立即 `pending=0;pacing_finished=True`。探针 timeout=0 用于消除挂钟等待；不是依赖累计 dt 充当真实挂钟。
- **Unity 实际**：独立播放器使用真实 UnityEngineHost.ResourceLoader；首次缺文件的异步失败回调确实到达；第二次等 10 个真实引擎帧仍 pending=1，显式 Sfx.Update 后为 0。它验证默认适配触发前提，不伪称已用 Unity 执行两个生产入口的全部反馈/pacing链。
- **修复方向**：两个入口以 unscaled 生命周期驱动 Sfx.Update，放在状态/冻结提前返回之前，与 Resident 一致；也可明确失败资源再次引用时直接丢弃而不排一个永无加载任务的请求。必须保留可选异步资源的超时回收。
- **验收**：对三个入口分别加入重复缺音效 + WaitForPlayback 测试，超时后恢复、只影响表现不改变逻辑结算；覆盖非 InWorld 时清理。只测 SfxPlayer.Update 本身不足以验收接线。
- **证据**：`probe/Program.cs`、`probe/probe.log`；Unity `Audit118PresentationTests.MissingSound_SecondPlay_StillPendingAfterHostEquivalentFrames_Observation` 通过是“缺陷前提/控制组观察通过”，不是产品修复通过。

## PRES118-03 — P2：同图读档后既有 View 继续使用读档前的位置快照

- **准确位置**：`presentation/view_binding/core/ViewBinder.cs:298-349`（OnSaveLoaded），尤其 314-316 对已有 View 跳过；`ViewBinder.cs:494-498` 是后续 Tick 才重采位置；`core/carriers/unit/core/UnitPersistable.cs:211` 直接恢复玩家 Position。
- **契约**：09 §2 View 同步逻辑位姿；ADR-0009/10 存档恢复；ADR-0013 §4 等待玩家输入时模拟不推进但表现照常运行。现有 ViewBinder 已承诺 save.loaded 后立即对账，无须人为补 sim.tick_finished。
- **触发**：玩家在 (3,4) 保存，移动到 (30,40) 且旧 View 的 prev/curr 均已为旧位置，再直接 SaveSystem.Load 同图存档；同一个实体和 View 存活。暂停/离散等待输入时不会马上有下一模拟步。
- **实际/预期**：真实 SaveSystem + UnitPersistable.CurrentPosition + WorldSim + ViewBinder，`status=Loaded;logical=(3,4);rendered=(30,40)`；无额外模拟 tick 时仍是旧坐标。控制组补一次 world.Tick 后 alpha=1 才变为 (3,4)。预期 Load 完成后既有 View 的 prev/curr 都以恢复位置重新初始化，任何插值 alpha 均应停在恢复点。
- **边界**：这是直接同图 Load、保留现有 View 的默认 API 链路；ShellHost.LoadGame 再经 SceneRouter 卸载重建场景的路径可能通过重建 View 避免此现象，不把那条路径也笼统宣称失败。本轮没有 Unity 实景位置截图证据。
- **修复方向**：在 OnSaveLoaded 对所有存活既有 View 重采位置并设置 prev=curr=restored；新建 View 已有此初始化。可同时审查其它不依赖普通业务事件的派生表现状态恢复，但不把未复现项混入本问题。
- **验收**：真实 SaveSystem 无附加 world.Tick，alpha=0/0.5/1 全部落在恢复点；覆盖离散 awaiting_input、Pause、同图重复读档。不能以“下一 tick 会好”替代恢复瞬间的表现同步。

## 文档与实现漂移（独立于以上产品问题）

1. `architecture/14_资产规格书模板.md:388` 将“导入工具的具体实现”列为游戏层提供，与 ADR-0014 决策的框架级导入工具交付物、仓库实际工具实现冲突。应把游戏特定阈值/美术输入与框架工具实现分开。
2. `architecture/13_新游戏接入指南.md:137` 泛称命中帧和武器风格“默认关闭/未接线”。命中帧策略默认关闭属实；三个默认入口已构造 EquipmentWeaponStyleSource 并传入 UnityViewFactory，武器风格机制不再是默认未接线。具体武器数据/主手槽选择仍需游戏配置。
3. `architecture/09_表现层.md:309-314` SfxPlayer 伪接口未列 Update；实际接口及生命周期需要它，应补充装配责任，避免两个入口重复漏接。
4. `adapters/conformance/README.md` 部分历史判断仍叙述 UnityRenderer3D 为全抛 NotSupportedException 的降级实现；当前 UnityRenderer3D 已有真实模型路线。该历史语句不可当现状。

## 逐模块覆盖与责任分类

| 模块/区域 | 当前能力与默认装配核查 | 责任/未覆盖边界 |
|---|---|---|
| presentation/common | WorldSimSnapshot 只读即时查询；中立 Id、Vec2、ViewKind；资源首次引用跟踪 | 框架；自定义旧 ISimSnapshot 默认枚举为空时只能销毁对账，已明确兼容退化 |
| view_binding | created/destroyed、tick snapshot、事件按相关实体转发；save.loaded 补建/销毁、既有装备重置 | 框架；PRES118-03。真实 .NET 对账探针；Unity 装备外观读档通过 |
| render | Sprite/Model rig、方向镜像、层/槽位、八程序动画、动画状态优先级、hitframe/finished 信号 | 框架机制；选择 model 后适配器须提供真实3D；动画资源和动作设计属游戏 |
| camera | CameraProfile、Follow/Bounds/Zoom/Shake、阶段切档映射 | 框架；生产装配 PRES118-01。每游戏的俯角/边界/阶段映射为配置 |
| vfx_sfx | world/anchor/socket/screen、pool/lifetime、冷资源排队、分层音量/静音 | 框架；PRES118-02。没有 IParticleRepositioner 可退化为初始定位，按09允许；真实Unity anchor变换通过 |
| feedback_binder | 六动作、条件、本地化、数值合并、顺序回放、pending统计、按攻击批次命中帧释放 | 框架；Unity model 真命中帧及 timeout 原因分别验证通过；具体反馈规则/震屏风格属游戏 |
| ui | 路径查询/数据源、11个VM、意图入口、技能书/商店/回合HUD、设置和音量归口 | 框架UI套件；布局、配色、具体选中目标与业务内容属游戏；本轮无完整面板视觉验收 |
| shell | NewGameStarter注入、菜单页、新游戏/存档/场景加载流程、设置读写 | 框架编排；起始状态由游戏NewGameStarter负责，无默认具体游戏新局reset不是缺陷 |
| assembly/schema | 统一 schema 目录、ContentValidationAssembly、Gameplay→Presentation装配、三个知会点 | 框架；资源/VFX/武器目录多为构造期快照，不能将DataHotReload登记成功外推为所有已创建表现对象原地更新 |
| Unity window/clock/input/platform | 13接口实现存在；clock退订句柄/固定步、输入与平台资源路径核查 | 引擎适配职责；本轮未对全部接口重跑conformance、无设备覆盖承诺 |
| Unity renderer2D/renderer3D/camera | 统一sortY、中立平面、model真实实例/槽位/挂点、完成事件、动画配置隔离 | 引擎适配职责；选测验证model hitframe、装备读档、anchor；未做完整美术质量/排序截图验收 |
| Unity audio/resource/fileSystem | 异步文件加载主线程落地、资源占位/缓存、用户与内容根分开、原子写入口 | 引擎适配职责；SFX前提真实验证；未做平台音频品质或磁盘故障注入 |
| Unity navigation/spatial/UI | 中立几何查询/动态阻挡版本/导航、UI承载与默认面板实现存在 | 可替换适配实现；本轮静态核查，不声称导航性能、全查询形状Runtime已覆盖 |
| adapters/stub | 13个确定性无头实现，正式可分发；同步资源失败/内存FS用于探针 | 框架无头交付物，不是游戏运行时视觉替代；直线导航/线性空间查询允许退化 |
| adapters/conformance | 引擎中立IEnumerator场景，xUnit/Unity包装复用 | 框架测试设施；并非所有接口语义/性能保证全覆盖，Unity FS故障场景与资源成功路径存在明确覆盖边界 |
| GameFoundationBootstrap | sample/greybox装配；真实3D、装备风格/外观、hitframe入口、固定/表现双时钟 | 示例组合根可更换，不以样例id定耦合；PRES118-02；3项真实离散装配选测通过 |
| FrameworkResidentHost | Shell常驻装配、3D/风格/外观/资源/回放/SFX时钟已接 | 默认生产样例组合根；PRES118-01；真实新游戏进图确认 |
| games/_template | framework+game叠加数据、GameOptions、watcher、最小新游戏/移动/存读档基建 | 游戏起步模板；PRES118-02及镜头开关漏传。模板无战斗/任务/可见玩家/完整菜单均为README明示范围 |

## 架构章节与 ADR 对照

| 文档 | 对应实现 | 审核判断 |
|---|---|---|
| 02 引擎适配层 13接口 | foundation/engine_adapter、adapters/stub、Unity EngineAdapter、conformance | API已提供；3D为条件必需；不同接口Runtime覆盖不等同全通过 |
| 09 表现层 | 上述 common/view_binding/render/camera/vfx_sfx/feedback/ui/shell/assembly | 机制主体已实现；本轮3项跨生命周期缺口；不把反馈内容/美术质量当框架逻辑 |
| 13 新游戏接入 | games/_template、三个入口、内容schema/SceneRouter/Shell | 最小模板范围有明确说明；具体 owner/day/vendor、目标解析、新局逻辑可注入；camera配置声明未兑现 |
| 14 资产规格 | toolchain 资产导入/占位包、DisplayMap/AnimSet/EquipVisual/资源路径 | 模板/字段与资产具体值分层合理；工具归属表需纠正；本轮不重做完整资产工具验证 |
| ADR-0002/0009 | 中立实体、snapshot/intent、SaveSystem/UnitPersistable | 无Unity逻辑对象依赖证据；存读档表现缓存需与业务恢复一致 |
| ADR-0012/0017 | sprite/model分支、真实UnityRenderer3D、AnimClipResolver、HitFrameSyncPolicy | model路线已实现，非仅接口占位；命中帧和timeout实测可区分；数据事件合并/资源缓存静态路径已核查 |
| ADR-0013 | Gameplay Pacing、Feedback.HasPendingPlayback、OnFrameTick | 离散模拟等待与表现继续更新分开；SFX遗漏破坏节奏门恢复 |
| ADR-0014 | 框架资产工具/UI套件/规格模板 | 框架交付承诺，游戏负责内容/参数；14责任表漂移 |
| ADR-0016 | 回调退订/资源加载/空间登记/内容只读 | 当前默认实现已接大部分生命周期；不把可选资源同步解析误判绕过加载器 |
| ADR-0018/0019及字段元数据后续ADR | headless、ContentValidationAssembly、schema目录/登记表 | 框架提供无头与校验；编辑器产品不属此仓；完整登记表/发布ABI审核由主审核及另代理覆盖 |

## Runtime 选测明细

- 原有8项均通过：EquipmentVisualSaveLoadReset 1；HitFrameSyncEndToEnd 2（HitFrame/Timeout各自原因）；SharedBootstrapDiscrete 3；VfxAnchorFollow 2。
- 新增 SFX 观察/控制组 1通过；新增 Resident camera正确性 oracle 1失败。不能把总结果写为Unity PASS。
- .NET probe runner退出0表示探针完成；日志中的expected/actual明确暴露两处产品失败，不能写成两项测试PASS。
- 未跑：本轮全量Unity EditMode/PlayMode、独立版、IL2CPP、正式包consumer smoke、视觉图像比较。旧版本测试结果不作为1.18证据。

运行脚本见 `run-selected-unity.ps1`。所有故障修复仍未实施，本轮仅审核。

