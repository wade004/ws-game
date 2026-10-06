# com.gamefoundation.feel-lab.unity

手感实验室的 Unity 宿主与面板：**可选的开发期设施**（[ADR-0160](../../../../architecture/adr/0160-框架三层拆分运行时包手感实验室可选包与样板仓库.md)），
从引擎适配层包 `com.gamefoundation.adapter.unity` 拆出。适配层包只剩运行时；本包只在编辑器里编译，游戏的独立版构建里没有它的任何字节。

## 内容与约束

- `Runtime/LabHost/`（程序集 `FeelLab.Unity`，`defineConstraints: UNITY_EDITOR`，`autoReferenced` 为假）：引擎侧实验室宿主 `EngineLabHost`/`EngineLabStage`、人手试玩宿主 `LabPlayground`、F1 面板模型、表现闸与度量；
  `Runtime/Plugins/` 是预编译的 `Lab.Kernel`/`Core.Sim`/`Adapters.Stub`，`.dll.meta` 只启用 Editor 平台（`.dll` 由 `build.ps1` 同步，被 `.gitignore` 覆盖）。
- `Editor/LabHost/`（程序集 `FeelLab.Unity.Editor`）：编辑器窗口 `GameFoundation/手感实验室`、占位试玩场景构建器（菜单 `GameFoundation → 手感试玩 → 工程场景（占位美术）`，维护项 `重建试玩场景`）。
- `Tests/Runtime/LabHost/`、`Tests/Editor/LabHost/`：本包的 PlayMode 与 EditMode 用例（`module:lab`），在框架仓库的工作台工程里经 `testables` 运行。
- 自带实验室根 `LabRoot~/`（打包时写入）：没有框架仓库的游戏工程里，宿主经包路径定位数据与夹具。
- 依赖：同版本的 `com.gamefoundation.adapter.unity`（版本在打包时写入）。**不进游戏的默认依赖**：想校准手感时再在游戏工程里装。

## 公开扩展点（样板与演示场景所用）

演示场景不再在框架里；它们在样板仓库 `ws-game-samples` 里，只经本包的两个公开扩展点构建，不依赖任何友元程序集：

| 扩展点 | 级别 | 作用 |
|---|---|---|
| `LabPlaygroundExtension` | 试玩宿主（`LabPlayground.Extension`） | 额外数据根、舞台扩展工厂、环绕镜头开关、面板缺省可见性与提示、角落提示、会话起止通知 |
| `EngineStageExtension` + `StageSceneContext` + `IStageProjection` | 舞台（`EngineLabOptions.StageExtension`） | 外形登记替换、场景搭建（返回真则不建缺省地面）、投影（广告牌朝向与深度排序）、显示绑定、逻辑事件、出手、每帧推进、广告牌后置、朝向换算、释放 |

形状取舍：扩展点只暴露"观测 + 替换缺省表现"，不暴露逻辑改写，所以装了扩展的会话与占位试玩场景的逻辑指纹逐字节一致。
由 `Tests/Runtime/LabHost/ExtensionPointsPlayModeTests.cs` 钉住：调用次序、上下文内容、替换缺省行为与会话结束时的释放。

### 来历：手感落地 M4-H：实验室引擎宿主（2026-10-03）

可选组件，独立程序集，不进玩家构建；设计与度量口径见 `lab/README.md` 判断记录 46 与 `architecture/手感设计/06_手感实验室与验收.md` 第 4 节。

1. **程序集与目录**：`Runtime/LabHost/`（`FeelLab.Unity`，`autoReferenced` 为假，引用 `Adapter.Unity` 与预编译的 `Lab.Kernel`/`Core.Sim`/`Adapters.Stub` 等；这些库由 `build.ps1` 第 3b 步同步到本包被忽略的 `Runtime/Plugins/`，其 `.dll.meta` 入库且只启用 Editor 平台）、`Editor/LabHost/`（编辑器窗口，`GameFoundation/手感实验室`，需 Play Mode）、`Tests/Runtime/LabHost/`（PlayMode）、`Tests/Editor/LabHost/`（EditMode）。
2. **舞台 `EngineLabStage`**：是 `LabHostExtension`，把内核宿主的视图工厂换成真实 `UnityViewFactory`（命中帧同步 `AnimKeyframeDriven`），反馈流水线分流给真实 `VfxPlayer`/`SfxPlayer`/`UnityCamera`/`UnityRenderer2D/3D`/`UnityAudio`；舞台根物体放专用层并隔离其它相机；组件自己的 `Update` 全部关掉，由舞台按模拟时间推进帧动画（`UnityFrameAnimPlayer.Advance`）、特效序列（`UnityRenderer2D.AdvanceSequencePlayers`）、动画器与相机，快放慢放批处理下结果可复现。
3. **适配器侧新增（只增不改，缺省行为不变）**：`UnityFrameAnimPlayer.AdvancedSeconds`（公开，累计推进量）、`UnityCamera.ApplyYawRotation`（公开可选开关，缺省关）、三个推进入口（`UnityAudio.Tick`、`UnityCamera.Tick`、`UnityResourceLoader.Tick`，ADR-0160 起公开，原先靠对宿主程序集的 `InternalsVisibleTo`）；M4-H 当时的内部推进入口已由 M4-W4 换成公开的可注入时间源，见下节。
4. **引擎侧失败不改变逻辑**：舞台里的任何异常记入 `EngineRecording.Errors`（度量 `engine_errors`），引擎视图创建失败时该实体退回内核的假视图。
5. **复现/不变量（PlayMode，`-testCategory module:lab`）**：`EngineLabHostCrossHostTests`（全部手感场景脚本逐字节比较引擎宿主与无头宿主的逻辑组指纹；格子由环境变量 `GF_LAB_CELLS` 选，缺省 `2d_action`，`*` 为全部；`GF_LAB_MAX_RUNS` 限制组合数）、`EngineLabHostMechanismTests`（命中帧对齐、镜头冲量曲线、顿帧冻结与旁观/对照、帧耗时分布、三个平面组合、`camera_relative` 三向检验、输入噪声记录回放、引擎失败、渲染隔离、冷热加载一致、期望清单在引擎宿主上判定、换装图标与逐层剪辑核对及其反例）。EditMode：`LabPanelModelTests`（覆盖存储的写入、校验、持久化、A/B 与源数据不变）。
6. **手感相关边界（设计决定）**：手感相关的限制在 M4 全部解除或转为判断记录（见 `lab/README.md` 判断记录 54、60 与「范围与现状（设计决定）」）；真机手测是游戏团队的验收清单，帧耗时已补 GPU 度量，相机相对输入已由框架原生实现，命中对齐已由姿势集数据对齐（手感设计/04 第 10 节第 10 条）。

### 来历：手感实验室人手试玩宿主（2026-10-04，ADR-0141）

在实验室引擎宿主上加"真人实时玩"，口径与指南见 `lab/README.md` 判断记录 64 与「人手试玩指南」。

1. **文件**（`Runtime/LabHost/`，同一程序集 `FeelLab.Unity`，新增对 `Unity.InputSystem` 的引用，用来读手柄）：`LabPlayground`（控制器 `MonoBehaviour` + 屏上叠层，IMGUI，只从视图模型绘制）、`LabLiveModel`（面板视图模型与滚动指标，不依赖引擎）、`LabLiveInput`（从真实输入适配器轮询键盘/摇杆、手柄按钮读 Input System，产出与脚本同种的事件）、`LabEffectFilter`（呈现通道开关与计数）；`Editor/LabHost/LabPlaygroundSceneBuilder`（生成三个薄场景，菜单 `GameFoundation/手感试玩`，命令行 `-executeMethod FeelLab.Unity.Editor.LabPlaygroundSceneBuilder.BuildAll`）。
2. **舞台试玩模式**：`EngineLabOptions.Interactive`（缺省关；开启后舞台相机真正渲染并带音频监听、背景与地面网格、相机跟随玩家、震屏与闪白真实生效、呈现通道闸、视图创建后立即播待机）、`InteractiveZoom`（缺省 2.8）、`InteractiveFollowSmoothing`、`Effects`；`EngineLabHost` 新增接受基础数据根与额外根解析器的构造。缺省选项下脚本回放的行为逐位不变。
3. **适配器侧新增（只增不改）**：`UnityViewFactory.PlayLocomotionClip(entityId)`（视图创建后默认只显示静态占位图，直到状态机第一次切换才播剪辑；试玩舞台创建视图后调用它，生产装配入口不调用）。
4. **复现/不变量（PlayMode，`-testCategory module:lab`）**：`LabPlaygroundTests`——移动距离、命中（伤害量有变化）、顿帧时长取自激活预设行、A/B 切换后下一次命中的顿帧按另一预设行折算且切换事件盖在生效 tick、关顿帧是录进脚本的覆盖、呈现开关不改逻辑、磁盘读回的录制脚本无头重放逻辑组指纹逐字节一致、三个场景载入后注入移动与攻击。
5. **实验室面板（ADR-0150，`LabPlayground.Panels.cs` 与 `LabLiveModel.Tab`）**：调参、帧数据时间轴、轨迹叠层、评分四页加原有场景页，F12 循环；全部从内核视图模型绘制（`TuningPanel`/`TimelineModel`/`TrajectoryModel`/`RatingModel`），界面操作先入队、下一帧控制器开头统一执行，文本框占着键盘时试玩热键与输入轮询暂停；`EngineLabStage` 在试玩模式下按基础预设取模板相机配置（`CameraFraming`：缩放相对值、跟随平滑折成时间常数，非模板预设恢复缺省，`TemplateFraming`/`FollowSmoothingSeconds` 可读）。冒烟：`LabPlaygroundPanelsTests`（分页循环、字段行全覆盖、改攻击方顿帧字段后命中顿帧 tick 等于数据折算且无头重放逐字节一致、A/B 覆盖组、时间轴、模板取景、保存预设/写回/评分的本地产物；设 `GF_LAB_SCREENSHOT_DIR` 另有一条截图用例，门禁不设）。
