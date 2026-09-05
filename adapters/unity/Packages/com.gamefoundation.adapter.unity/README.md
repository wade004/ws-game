# com.gamefoundation.adapter.unity

框架的 Unity 引擎适配层（L-1）与表现层引擎侧实现，供各游戏工程以 `file:` 方式引用。本包
只做"把 `core/foundation/engine_adapter/contracts` 定义的 13 个中立接口落到 Unity 6000.3 +
URP 2D Renderer + Input System 1.20"这一件事，不包含任何具体游戏逻辑（见
`architecture/02_引擎适配层.md`）。

## 13 个接口实现清单

| 接口 | 实现类 | 关键判断 / 限制 |
|---|---|---|
| `IWindow` | `UnityWindow` | 运行期无法重建操作系统窗口本体、也无公开跨平台 API 改标题栏文字；`OnCloseRequested` 接到 `Application.wantsToQuit`（返回 false 阻止默认退出，交由上层决定何时调 `Destroy()` 真正退出）。 |
| `IClock` | `UnityClock` | `Now()` = `Time.realtimeSinceStartupAsDouble`（只供表现/UI 动画使用）；帧回调由 `UnityEngineHost.Update` 驱动（`Time.unscaledDeltaTime`），固定步长回调由 `FixedUpdate` 驱动（`Time.fixedDeltaTime`），满足"逻辑层 tick 由固定步长驱动"的确定性铁律。 |
| `IRenderer2D` | `UnityRenderer2D` | 句柄 = 根 GameObject（挂 `SortingGroup`）+ `LayersRoot` 子物体承载纸娃娃层；`sortingOrder = layer*100000 - round(sortY*1000)`，纸娃娃层内序号追加为子渲染器自身 `sortingOrder`；`height_offset_px` 只平移 `LayersRoot` 本地 Y，不影响排序；资源缺失时用洋红色占位方块 + 一次性警告，不抛异常；`EmitParticle` 目前不解析 `effectId` 到具体制作的粒子资产（`ResourceKind` 没有粒子/预制体种类，见下"已知契约缺口"），一律播放内建通用爆发效果。 |
| `IAudio` | `UnityAudio` | 总线音量方案选"简单分组乘算"而非 AudioMixer（避免引入需要手工创建的 `.mixer` 资产）；SFX 用 `AudioSource` 对象池，音乐用两路 `AudioSource` 做交叉淡入淡出；SFX 播放期间总线音量变化不影响"已经在播的那次"，只影响之后新播放的。 |
| `IInput` | `UnityInput` | 运行时用代码搭建 `InputActionMap`（不依赖 `.inputactions` 资产）；键盘离散事件靠 `<Keyboard>/anyKey` 触发后扫描 `wasPressedThisFrame/wasReleasedThisFrame` 精确定位具体按键（避免 `anyKey` 聚合控件"拿不到具体是哪个键"的限制）；鼠标左/右/中键各自独立绑定；手柄连接/断开走 `InputSystem.onDeviceChange`；手柄按钮离散事件目前没有专用绑定，测试/上层可用 `SimulateGamepadButtonForTest` 驱动；文本输入用 `Keyboard.current.onTextInput` 累积字符。 |
| `IFileSystem` | `UnityFileSystem` | 用户目录 = `Application.persistentDataPath`；原子写入 = 写临时文件 + `File.Replace`（目标已存在）/ `File.Move`（目标不存在），失败时清理临时文件、旧内容保持不变。 |
| `IResourceLoader` | `UnityResourceLoader` | 后台 `Task` 读取文件字节（纯 `System.IO`，不碰任何 UnityEngine API），解码与回调统一在 `Tick()`（由宿主 `Update` 每帧调用）里于主线程完成，满足"回调总在主线程排队执行"的线程约定；音频只支持标准 PCM16 WAV（内置 `WavDecoder`，不依赖 UnityWebRequest/协程）；`Font` 种类只能读原始字节，不能产出可用的 TMP 字体资产（见下）。 |
| `INavigation2D` | `UnityNavigation2D` | 网格 A*（不引入第三方寻路包，也不用 Unity 内置三维 NavMesh）；阻挡数据靠非契约方法 `RegisterBlockingRect`/`RegisterBlockingFromTilemap` 登记；网格尺寸自适应（默认格子 0.25 世界单位，超过 192×192 格时放大格子），起止点落在已登记范围外时退化为直线可达性检查。 |
| `ISpatialQuery` | `UnitySpatialQuery` | 自维护登记表 + 均匀网格分桶（非 Physics2D，避免同步 Collider2D 的额外成本与结果顺序不确定性）；结果一律按 `Id` 排序；`HasLineOfSight` 默认恒真，可用 `SetLineOfSightBlocker` 接入 `UnityNavigation2D.Raycast` 做真实遮挡判定（`UnityEngineHost` 已默认接好）。 |
| `IUISurface` | `UnityUISurface` | uGUI `Canvas`（Screen Space - Overlay）+ TextMeshPro；`DrawText` 没有契约层面的句柄/去重机制，按调用顺序累加创建文本元素，非契约方法 `ClearSurface` 供逐帧刷新场景复位；`fontId` 目前不区分具体字体资源，统一用包内占位字体运行期 `TMP_FontAsset.CreateFontAsset` 生成，失败时回退 `TMP_Settings.defaultFontAsset`。 |
| `IPlatform` | `UnityPlatform` | 语言映射 `Application.systemLanguage` → 常见 BCP-47 短代码；剪贴板 = `GUIUtility.systemCopyBuffer`；崩溃日志经 `UnityFileSystem` 原子写入用户目录 `crash_log.txt`，`UnityEngineHost` 额外把 `Application.logMessageReceived` 的 Error/Exception 日志自动转发到 `ReportCrash`。 |
| `IRenderer3D` | `UnityRenderer3D` | **声明降级**：全部方法一律抛 `NotSupportedException`（本迭代表现路线固定 sprite 型外形，02 第 1.12 节该接口"条件必需"，本框架未选用 model 型外形）。 |
| `ICamera` | `UnityCamera` | 正交投影，世界平面固定为 Unity 的 XY 平面（Z=0），与 `IRenderer2D` 精灵摆放平面一致；`pitchDegrees`/`yawDegrees` 只记录配置值，不据此做真实透视投影（URP 2D Renderer 不支持）；`zoom` 直接映射 `orthographicSize`；`height` 参数按与 `height_offset_px` 一致的方向作为世界 Y 附加偏移。 |

`UnityEngineHost`（`MonoBehaviour`，`DontDestroyOnLoad`）是组合根，持有以上 13 个实例；静态
`UnityEngineHost.Ensure()` 获取（必要时创建）全局唯一实例。`Update`/`FixedUpdate`/
`OnApplicationQuit` 的驱动分工见该类型顶部注释。

## 资源 id → 路径规则

见 `UnityResourceLoader.cs` 顶部注释，摘要：

```
Application.streamingAssetsPath/GameFoundation/<kind 子目录>/<资源引用id去掉类别前缀，点号换下划线>.<扩展名>
  Image     -> sprites/<name>.png
  Audio     -> audio/<name>.wav（仅支持标准 PCM16 WAV）
  Font      -> fonts/<name>.ttf（只读字节，见下"已知契约缺口"）
  DataTable -> data/<name>.json
```

命名规则与 `architecture/14_资产规格书模板.md` 第 1.2 节文件名模板、
`presentation/render/core/SpriteViewBase.ResolveLayerResourceId` 的"去掉类别前缀、点号换下划线"
规则保持同一套口径。

## 命令行跑测试

```
"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" -batchmode -nographics ^
  -projectPath <repo>\adapters\unity ^
  -runTests -testPlatform EditMode ^
  -testResults <输出目录>\unity_editmode.xml ^
  -logFile <输出目录>\unity_editmode.log

"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" -batchmode ^
  -projectPath <repo>\adapters\unity ^
  -runTests -testPlatform PlayMode ^
  -testResults <输出目录>\unity_playmode.xml ^
  -logFile <输出目录>\unity_playmode.log
```

PlayMode 测试用到真实的渲染/输入子系统，`-nographics` 下可能无法正确初始化，因此 PlayMode
命令行未加该参数；EditMode 纯逻辑测试可以安全加 `-nographics`。

判断记录（U2 阶段实测追加）：`-runTests` 命令行**不要**再加 `-quit`——两者同传时实测会出现
`-quit` 先于测试真正跑起来就触发关闭（"Batchmode quit successfully invoked" 紧跟在资产刷新之后
出现，测试结果 XML 从未生成），`-runTests` 本身在测试跑完后会自动退出进程，不需要也不应该再叠加
`-quit`。`-quit` 只用于纯编译检查（`-executeMethod`/无 `-runTests` 时）与
`-buildWindows64Player` 这类"跑完一件事就退出"的调用。

跑测试/灰盒场景前需要先执行过一次内容同步（`build.ps1` 默认流程或 `-SyncContent`，见
`adapters/unity/README.md`），否则 `data/_sample`/`assets/_placeholder` 不会出现在
`Assets/StreamingAssets/GameFoundation/` 下，`GameFoundationBootstrap` 会在 `Awake` 里报数据集
加载失败（`BootstrapFailed = true`，已 `Debug.LogError` 具体原因，不会抛异常穿透）。

## U2：引导组装根、View、反馈接收器、内容同步、灰盒场景

### 引导流程（`Runtime/Bootstrap/GameFoundationBootstrap.cs`）

灰盒场景 `Assets/Framework/Scenes/GreyBox.unity` 唯一挂载的 `MonoBehaviour`。`Awake` 里按
`core/gameplay/tests/EndToEnd/GameWorldFixture.cs` 同一套装配顺序（事件总线 → 只读数据集 →
`WorldSim`/`GameplayAssembly`（`ISpatialQuery`/`INavigation2D` 接 `UnityEngineHost` 的真实实现，
不是桩实现）→ 玩家单位 → `EnterMap`（自动触发 `spawn.sample_beast_field` 生成一只生物）→ 手动
生成一个可交互 gobj → `PresentationAssembly`）构造整套世界，任一步骤失败（数据集校验阻断、异常）
都 `Debug.LogError` 并把 `BootstrapFailed` 置 `true`，不抛异常穿透、不继续跑 `Update`/`FixedUpdate`。

数据集根、示例地图 id、玩家模板 id 等均为 Inspector 可配置字段（`[SerializeField]`），默认值指向
框架自带的中性示例数据（`data/_sample`、`creature.sample_hero` 等，与 `GameWorldFixture` 同一套
id）。

固定步长驱动：`FixedUpdate` 里 `IInputMapHost.Update` 轮询本帧输入 → 按当前动作状态提交
"move"/"cast" 意图或调用 `GameObjectHost.Interact`（见下"输入→意图链路"）→
`WorldSim.Tick(SimStep.Continuous(Time.fixedDeltaTime))`；`Update` 只做表现（`ViewBinder.SyncAll`/
`CameraHost.Update` 插值同步、三个反馈接收器的 `Tick`）。判断记录：没有用
`IClock.RequestFixedStep`（该契约没有取消订阅方法，跨场景重进会让旧回调永久残留在
`UnityEngineHost` 持有的 `UnityClock` 里），改为直接在本组件自己的 `FixedUpdate`/`Update` 里驱动
（组件销毁后引擎自动停止调用，不残留任何注册），插值 alpha 用"Update 累加、FixedUpdate 清零"的
标准写法。

### 输入→意图链路

`GameFoundationBootstrap` 复用 `PresentationAssembly.InputMap`（同一个 `IInputMapHost` 实例，UI
设置面板等也持有它），额外 `DeclareActionSet` 一个补充动作集（`actionset.greybox`：
`input.action.move`/`input.action.interact` 复用与 `data/_sample/found/found.input_action.json`
相同的绑定字符串，`input.action.greybox_attack`/`input.action.greybox_skill_1` 是本次新增的两个
按钮动作——该示例数据表本身没有战斗类动作，见下"契约缺口"）。移动经
`MovementHost.Request(MoveRequest.InDirection(...))`（内部转成 `move` 意图提交）；普攻/技能 1
经 `IWorldSim.SubmitIntent` 提交一条 `cast` 意图（`skill_id` 分别为
`skill.sample_strike`/`skill.sample_burn`，玩家注册等级设为 3 以同时解锁两个技能）；交互经
`GameObjectHost.Interact(playerId, chestId)` 窄契约调用（`found.event_catalog`/`WorldSim` 没有任何
消费 `interact` 意图的 tick 处理器，见 09/03 文档与该类型顶部"判断记录 2"）。三条路径均不直接改
`WorldSim`/`Carriers` 状态，满足表现层铁律。

### 视图与反馈接收器（`Runtime/Presentation/`）

- `UnityViewFactory : IViewFactory` + `UnitySpriteView : SpriteViewBase`：按 `DisplayInfo.Kind`
  选择创建真实 sprite 型 View 还是退化成不渲染的 `NullView`（没有 `kind=sprite` 的
  `DisplayInfo` 时，如 gobj 类未来接入非 sprite 外形）。`UnitySpriteView` 在朝向变化时重新解析并
  提交纸娃娃层资源加载（见下"判断记录：主动 LoadAsync"），`SetFlash`/`ClearFlash` 经既有
  `SetShaderParam` 通道触发/复原过曝白色 hit-flash（`UnityRenderer2D` 新增
  `"flash_intensity"` 参数名解释，见该类型判断记录）。
- `FloatingTextReceiver`：世界空间 `TextMeshPro` 对象池，颜色按 `FloatingTextStyleDef.ColorRef`
  的字面量做"是否含 crit"启发式区分（框架没有 id → 具体色值的查询能力，见类型注释）。
- `FreezeFrameReceiver`：只暂停 `Update` 里的 `ViewBinder.SyncAll`/`CameraHost.Update`
  两步调用，不影响 `FixedUpdate` 里的 `WorldSim.Tick`（09 第 6 节"顿帧"落地，判断记录见类型顶部）。
- `FlashReceiver`：查 `ViewBinder.TryGetView` 拿到 `UnitySpriteView` 后调用 `SetFlash`/
  `ClearFlash`；`Flash(entityId, profileId)` 没有随行的时长/强度数据（框架未定义
  `flash_profile` 一类的表），固定用 0.15 秒/2 倍过曝，`profileId` 暂不参与具体数值解析。

判断记录（`UnitySpriteView` 为什么要主动调 `IResourceLoader.LoadAsync`）：勘察
`UnityRenderer2D.ResolveSprite`（`SetLayers`/`CreateSpriteInstance` 内部把资源 id 转成 `Sprite`
的私有方法）发现它只读 `IResourceLoader.TryGetSprite` 缓存，从不主动发起加载——`IResourceLoader`
契约本身没有规定"谁来触发首次加载"。若没有任何一方主动调用 `LoadAsync`，纸娃娃层会永远停在
`UnityRenderer2D` 的洋红色占位方块上。`UnitySpriteView` 按"View 知道自己接下来要展示哪些方向/层，
理应负责预取这些资源"的原则，在朝向变化时对该朝向用到的每个层资源 id 发起一次 `LoadAsync`（去重、
幂等）。`UnityAudio.PlaySfx` 有完全相同的缺口（也只读 `TryGetAudioClip` 缓存），本任务只加了一个
诊断计数器 `PlaySfxCallCount`（验证"播放调用链路已打通"），未追加音频资源预加载——这不在
U2-1 明确要求范围内，记为下一步可选加强项。

### 内容同步（`build.ps1 -SyncContent`）

见 `adapters/unity/README.md`"build.ps1 各开关"一节；产物目录 `Assets/StreamingAssets/
GameFoundation/` 整体 `.gitignore`，只提交同步脚本本身。

`UnityResourceLoader.ResolvePath` 新增"`layer.` 类别"特例（三级目录路径，见该方法判断记录）：
`presentation/render/core/SpriteViewBase.ResolveLayerResourceId` 产出的纸娃娃层资源 id 形如
`"layer.<spriteSetName>__<direction>__<layerName>"`，但 `toolchain/gen_placeholder_assets.py`
生成的占位精灵集实际磁盘布局是"目录按方向/层分层"（`sprites/<spriteSet>/<direction>/
<layer>.png`），不是单一扁平文件名；本方法只对 `"layer."` 这一个类别把双下划线分隔的三段还原成
三级目录，其余类别（`sprite.`/`icon.` 等）的既有扁平解析规则不变。

### 灰盒场景与测试/构建

见 `adapters/unity/README.md`"命令行跑测试/编译检查/构建"一节；场景由
`Assets/Editor/GreyBoxSceneBuilder.cs`（`[MenuItem]`/`-executeMethod` 均可触发）程序化生成，不
手工在编辑器里搭建（满足"全程命令行、不开 GUI"的硬性规则，也让场景内容可重复重建）。PlayMode
测试见 `Tests/Runtime/GreyBoxTests.cs`（6 条，覆盖启动零阻断错误、玩家视图真实资源加载、移动、
攻击伤害+飘字、SFX 播放、场景重进不重复）。

## 已知契约缺口

以下缺口不在本次任务的契约修改范围内（契约本身不能改，只记录）：

1. `IRenderer2D.SetTransform` 没有高度参数（不同于 `IRenderer3D.SetPlacement` 显式带
   `height`）；表现层（`presentation/render/core/SpriteViewBase`）已经把换算出的像素高度经
   `SetShaderParam` 的 `"height_offset_px"` 参数名传递，本适配层把该参数名解释为"平移
   `LayersRoot` 子物体的本地纵向像素偏移"。
2. `IAudio.PlaySfx` 没有位置参数——本适配层的 SFX 播放不做 3D/2D 空间衰减，全部按二维
   （无空间）方式播放。
3. `ResourceKind` 枚举缺 `Scene`/`NavMesh`，也缺"粒子/特效预制体"这一种类；`IRenderer2D.
   EmitParticle` 因此无法把 `effectId` 解析到具体制作的粒子资产，一律播放内建通用效果。
4. `IResourceLoader` 的 `Font` 种类只能提供原始字节，Unity 运行期没有公开 API 能把任意字体
   字节数组转换成可用于 TMP 渲染的字体资产（`TMP_FontAsset.CreateFontAsset` 需要一个已被
   Unity 资产管线导入过的 `UnityEngine.Font` 对象）；`UnityUISurface` 的默认字体因此改走专用
   路径（包内预先导入好的占位字体 `Resources/Fonts/NotoSansCJKsc-Regular`），不经过通用资源
   加载器，`fontId` 参数目前不区分具体字体资源。该路径还依赖 `adapters/unity/Assets/
   TextMesh Pro/`（TMP 官方 Essential Resources 的标准内容——`TMP Settings.asset`、
   SDF 着色器、默认字体等，与手动执行编辑器菜单"Import TMP Essential Resources"产生的文件
   完全一致）已提交进本仓库，见 `UnityUISurface.cs` 顶部"判断记录（TMP 运行期依赖）"。
5. `ISpatialQuery`/`INavigation2D` 契约本身都没有定义"如何把地图对象/阻挡数据登记进实现"的
   方法；本适配层与 `adapters/stub` 同样的处理方式——提供非契约的 `Register`/
   `RegisterBlockingRect` 等协作方法。
6. （U2 新增）`found.event_catalog`/`IWorldSim` 的 tick 阶段编排里没有任何消费 `Kind=="interact"`
   意图的处理器——`core/carriers/gobj.GameObjectHost.Interact(unitId, gobjInstanceId)` 是一个直接
   方法调用，不是意图驱动；`GameFoundationBootstrap` 按此把"交互"落成窄契约调用（09/03 文档"P3
   窄契约调用"允许的落地方式之一），不是绕过表现层铁律。
7. （U2 新增）`data/_sample/found/found.input_action.json` 的示例动作集只有
   move/confirm/cancel/interact/open_menu/pause/camera_adjust 七个动作，不含任何战斗类动作
   （数据本身在 `description` 字段声明"示例动作集，不构成任何游戏的操作定论"）；
   `GameFoundationBootstrap` 需要"普攻"/"技能 1"两个按钮动作时，直接用 `IInputMapHost.
   DeclareActionSet` 在代码里补充声明，不修改 `data/` 下任何文件。
8. （U2 新增）`IResourceLoader` 契约没有规定"谁来触发某个资源 id 的首次加载"——
   `UnityRenderer2D`/`UnityAudio` 都只读缓存（`TryGetSprite`/`TryGetAudioClip`），从不主动
   `LoadAsync`；本任务在 `UnitySpriteView`（sprite 型 View）补了按需预取，`UnityAudio.PlaySfx`
   只加了调用计数诊断，未补音频预取，见"内容同步"一节判断记录。

## 判断记录索引

详细判断记录写在各实现文件顶部注释里，此处只列索引：

- `UnityClock.cs`：确定性铁律的落地方式（帧回调 vs 固定步回调的驱动源）。
- `UnityRenderer2D.cs`：排序公式、高度偏移契约缺口落地、资源缺失占位、粒子契约缺口。
- `UnityAudio.cs`：总线音量方案二选一的取舍理由。
- `UnityInput.cs`：不依赖 `.inputactions` 资产、`anyKey` 聚合控件的绕过方式。
- `UnityResourceLoader.cs`：资源 id→路径映射、后台线程 + 主线程完成队列的线程模型、音频/字体
  解码能力边界。
- `UnityNavigation2D.cs`：网格 A* 选型理由、网格自适应策略。
- `UnitySpatialQuery.cs`：自维护登记表 vs Physics2D 的取舍理由。
- `UnityUISurface.cs`：`DrawText` 语义解释、占位字体生成方式。
- `UnityCamera.cs`：世界平面坐标系与投影简化的判断记录。
- `UnityRenderer3D.cs`：声明降级的理由。
- `Runtime/Bootstrap/GameFoundationBootstrap.cs`：装配顺序、固定步驱动为什么不用
  `IClock.RequestFixedStep`、交互为什么是窄契约调用、普攻/技能 1 为什么不读 `found.input_action`
  表（三条判断记录见文件顶部）。
- `Runtime/Bootstrap/StreamingAssetsFileSystem.cs`：为什么需要第二份 `IFileSystem` 实现（只读
  内容数据集 vs 用户数据目录）。
- `Runtime/Presentation/UnitySpriteView.cs`：为什么要主动调用 `IResourceLoader.LoadAsync`。
- `Runtime/Presentation/UnityViewFactory.cs`：`DestroyAllCreatedViews` 弥补
  `ViewBinder`/`CameraHost` 不支持退订的已知缺口。
- `Runtime/Presentation/FlashReceiver.cs`：闪白时长/强度默认值的取舍理由。
- `Runtime/Presentation/FreezeFrameReceiver.cs`：顿帧只暂停表现层、不暂停逻辑 tick 的落地方式。
- `UnityResourceLoader.cs` `ResolvePath`：`"layer."` 类别嵌套路径特例的判断记录。
- `Runtime/Shell/FrameworkResidentHost.cs`：框架常驻部分为什么不直接改造
  `GameFoundationBootstrap`、世界/装配根只构造一次、`AiHost`/`SpawnHost` 不会级联清理的两个
  契约缺口发现与窄契约调用兜底。

## U3：UI 套件默认皮肤、Shell 流程、灰盒竖切测试与独立版冒烟

### UI 套件（`Runtime/Ui/`）：十个界面单元

| 面板 | 类型 | 视图模型 | 打开方式 |
|---|---|---|---|
| HUD | `Panels/HudPanel` | `HudViewModel` | 常驻显示 |
| 动作条 | `Panels/ActionBarPanel` | `ActionBarViewModel` | 常驻显示，点击槽位提交 `cast` |
| 背包 | `Panels/InventoryPanel` | `InventoryViewModel` | 热键 `I` |
| 任务日志 | `Panels/QuestLogPanel` | `QuestLogViewModel` | 热键 `J` |
| 对话框 | `Panels/DialogPanel` | `DialogViewModel` | `IDialogHost.OpenGossip/StartStory` 后自动显示内容 |
| 技能书 | `Panels/SkillBookPanel` | `SkillBookViewModel` | 热键 `K` |
| 角色属性 | `Panels/CharacterStatsPanel` | `CharacterStatsViewModel` | 热键 `C` |
| 设置 | `Panels/SettingsPanel`（`SharedPanels.cs`外，`DialogSettingsPanels.cs`） | `SettingsViewModel` | 热键 `N`；主菜单"设置"/暂停菜单"设置"入口复用同一实例 |
| 存档槽 | `Panels/SaveSlotsPanel`（`SharedPanels.cs`） | `SaveSlotsViewModel` | 热键 `L`；主菜单"存档槽"页复用同一实例 |
| 暂停菜单 | `Panels/PauseMenuPanel`（`SharedPanels.cs`） | `PauseMenuViewModel` | `Esc`（InWorld 时）触发 `UiIntents.Pause()` 后自动显示 |

- `UiSkin.cs`：字体（复用 `UnityUISurface` 已验证的 Noto Sans CJK 占位字体生成路径）、基础色板、
  运行期生成的九宫格占位面板精灵（`assets/_placeholder` 无专用 UI 素材，见类型判断记录）。
- `UiRoot.cs`：Screen Space - Overlay `Canvas` + `CanvasScaler` +
  `InputSystemUIInputModule`（若场景内尚无 `EventSystem` 则一并创建）。
- `UiPanelHost.cs`：按 `ui_layout_definition` 十个面板行登记（缺行只警告不阻断，见类型判断记录）；
  `GameplayGroup` 承载 Hud/ActionBar/Inventory/QuestLog/Dialog/SkillBook/CharacterStats 七个纯
  游戏内面板，随 `ShellRoot` 在非 InWorld/Paused 页面整体隐藏；Settings/SaveSlots/PauseMenu 三个
  面板同时被 Shell 复用（主菜单存档槽页、设置入口、暂停覆盖层），因此单独挂在 `content` 下不随
  `GameplayGroup` 一起隐藏。
- 绑定方式：**每帧 Refresh**（`IUiPanel.RefreshUi()` 由 `UiPanelHost.Update()` 对当前打开的面板
  逐一调用），不是订阅视图模型变更通知——presentation/ui 十个视图模型均未额外暴露 `event Action
  Changed` 一类通知（只在内部订阅 `IEventBus` 相关事件后自行 `Refresh()`），新增该通知需要改
  `presentation/ui`（不在本任务"被阻断时最小改动"范围内），详见 `IUi Panel.cs`（`Runtime/Ui/`）
  顶部判断记录。
- 本地化：面板真正的游戏内容文案（技能/物品/任务/属性显示名、对话选项、菜单条目、难度档位名）
  均经 `IL10nHost.Text`/`ExprValue` 查询 `l10n.text`；纯 UI 框架级 chrome 文案（"使用""删除"
  "（空）"等空态/通用动词）标注为占位调试文本未接入 l10n（见各面板文件顶部判断记录）。

### Shell 流程（`Runtime/Shell/`）

```
Boot ──Start()──> MainMenu ──ShowSlots()──> SaveSlots ──(选空槽)ShowNewGameSetup()──> NewGameSetup
                     │                         │                                        │
                     │                    (选已有槽)LoadGame(slotId)                 NewGame(slotId,难度)
                     │                         │                                        │
                     └──Settings 入口──> Settings（复用面板）                            ▼
                                                                                     Loading
                                                                                        │
                                                                                        ▼
                                                                                     InWorld ──Esc(Pause())──> Paused
                                                                                        ▲                       │
                                                                                        └──Resume()/主菜单入口ReturnToMainMenu()┘
```

- `FrameworkResidentHost`（DontDestroyOnLoad 单例，`Ensure()` 幂等获取）：框架常驻部分——数据集/
  `EventBus`/`GameplayAssembly`/`PresentationAssembly` 全程只构造一次；玩家 `PlayerUnit` 对象在
  Awake 构造一次（不立即 `AddEntity`），`RegisterPersistables` 绑定同一对象引用；`SceneRouter`
  `post_load`/`pre_unload` 钩子负责"进入地图部分"——按需重新 `AddEntity`、`EnterMap`、（仅第一次）
  生成示例生物，`pre_unload` 里对追踪的生物做 `AiHost.UnregisterUnit`+`ISpatialQuery.Unregister`+
  `SpawnHost.NotifyDespawn` 退场清理（见下"契约缺口发现"）。
- `ShellRoot`：Awake 时 `Ensure()` 常驻部分、建 `UiRoot`/`UiPanelHost`/主菜单/新游戏难度选择/加载画面
  四块 UI，`Update()` 按 `ShellHost.Page` 切换显示哪一块、驱动 `Esc` 暂停/恢复。
- 示例 `NewGameStarter`（`FrameworkResidentHost.SampleNewGameStarter`）：**这是灰盒验收用的示例
  实现**——只重置玩家对象字段（位置、地图 id）到新游戏默认值、返回固定示例地图
  `world.sample_field`；具体游戏必须提供自己的 `NewGameStarter`（见
  `presentation/shell/contracts/ShellHostTypes.cs` 判断记录）。`SaveGameId` 设为中性 id
  `game.sample`；示例存档槽固定为 `game.sample.slot_1/2/3`（`ShellRoot.SaveSlotCandidates`）。
- 场景资源占位（判断记录）：`world.sample_field` 的 `scene_ref`/`nav_ref` 需要
  `UnityResourceLoader` 能读到对应文件才能让 `SceneRouter.LoadScene` 成功；框架目前没有场景/
  导航资源内容管线（`ResourceKind` 缺 `Scene`/`NavMesh` 专用取值，见本文件"资源种类映射"一节），
  `build.ps1` 因此在内容同步步骤里额外生成一份静态占位字节
  `StreamingAssets/GameFoundation/data/sample_field.json`（`scene.sample_field`/
  `nav.sample_field` 去掉类别前缀后恰好是同一文件名，内容本就不被解析，见 build.ps1 该步骤判断
  记录），不修改 `data/_sample`/`assets/_placeholder`。

### 契约缺口发现（U3 新增，均已用"窄契约调用"兜底，core/ 本身未改动）

9. `Core.Foundation.SceneRouter.SceneRouter.FinishLoading` 在"非本实例首次 LoadScene"时调用
   `world.ClearAll()`，但 `Core.Rules.Ai.AiHost`（内部 `RegisteredUnitIds`）与
   `Core.Gameplay.Spawn.SpawnHost`（`on_map_enter` 重生策略靠 `runtime.EntityId` 判断"该出生点
   是否已有存活实体"）都不知道 WorldSim 那边已经清空——第二次进入地图时，上一局残留的 AI 注册表
   项会在下一次 `AiTickHandler.Execute` 让 `WorldUnitAccess.Require` 抛
   `InvalidOperationException`（U3 实测复现：此前 U1/U2 的 PlayMode 测试从未在同一个 SceneRouter
   实例上调用过第二次 `LoadScene`）。`FrameworkResidentHost.HandlePreUnload` 按窄契约调用兜底：
   `AiHost.UnregisterUnit` + `ISpatialQuery.Unregister`（非契约协作方法）+
   `SpawnHost.NotifyDespawn`。同一缺口也会在"生物战斗至真正死亡"这条此前从未被走通的路径上单独
   出现（`unit.died` 触发，非地图切换触发）——`GameFoundationBootstrap`/`FrameworkResidentHost`
   均额外订阅 `unit.died` 做相同兜底。建议设计层复核是否需要在 `core/foundation/scene_router` 或
   `core/gameplay/assembly` 补一个真正的"地图卸载/死亡退场编排"。
10. `data/_sample/skill/skill.def.json` 的 `apply_aura` 效果 `params` 字段名写成了 `aura_id`，但
    `Core.Rules.Skill.EffectDispatcher.ApplyAuraEffectPrimitive` 读取的键是 `aura_def`——
    `skill.sample_burn`/`skill.sample_passive` 的光环因此从未真正生效过（`ParamsX.GetId` 找不到
    键，退化为 `default(Id)`，后续 `SkillDefCache.TryGetAuraDef` 用空 `Id.Value` 查字典抛
    `ArgumentNullException`）。此前没有任何测试真正施放过这两个技能，U3 是第一次施放
    `skill.sample_burn` 走到这条路径。已在 `data/_sample/skill/skill.def.json` 把两处
    `"aura_id"` 改为 `"aura_def"`（纯粹的键名拼写修复，不改变表结构/新增内容）。
11. `Core.Rules.Skill`/`Core.Rules.Combat` 的施法/伤害结算对"目标在结算前已经被移除出 WorldSim"
    没有防御性判断（`WorldUnitAccess.Require` 直接抛异常，不是返回失败结果）；有 `cast_time` 的
    技能（如 `skill.sample_burn`，1 秒）在场景切换/世界清空后才结算时会命中这条路径。U3 的做法是
    在测试里"先单独施放一次并等待其结算完，再进入不产生在途施法的普攻循环"规避，未改动 core/。

### 人工验收清单（阶段 4 验收标准 1～5）

| 验收 | 自动化证据 | 人工核对步骤（补充） |
|---|---|---|
| 1. 竖切全流程 | `VerticalSliceTests.FullVerticalSlice_...` | 独立版 `Shell.exe` 手动操作一遍：主菜单→新游戏→移动→普攻→技能→击杀→存档→改变位置→读档→位置恢复。 |
| 2. Shell 流程 | `ShellFlowTests` 全部用例 | 独立版手动核对主菜单四个按钮、存档槽页显示摘要、暂停菜单四个选项、Esc 暂停/恢复。 |
| 3. UI 套件与背包一致性 | `UiSuiteTests.AllTenPanels_.../Inventory_SlotCount_...` | 独立版打开背包（`I`）目视核对格子数与实际拾取/添加的物品数量一致。 |
| 4. 命中/暴击/死亡反馈组合 | `VerticalSliceTests.Feedback_NormalDamage_.../Feedback_CritDamage_.../FullVerticalSlice`（死亡→Flash） | 独立版观察普攻飘字颜色（暴击应更明显）、死亡瞬间的震屏/闪白。 |
| 5. Y 排序/纸娃娃 | `VerticalSliceTests.YSorting_.../Paperdoll_LayerOrder_...` | 编辑器里选中玩家 `LayersRoot` 下三个 `SpriteRenderer`（`body`/`hand_main`/`head`），核对 Inspector `Sorting Order` 依次递增；移动玩家使其与生物 Y 坐标交替，肉眼确认前后遮挡关系随之切换。 |

### 如何跑竖切测试与冒烟

```
# EditMode/PlayMode（见上"命令行跑测试"一节，新增 UiSuiteTests/ShellFlowTests/VerticalSliceTests
# 共 21 条 PlayMode 用例，随同一条 -runTests -testPlatform PlayMode 命令一起跑）

# 独立版构建（Shell.unity 已经是 Build Settings 第 0 位，直接沿用 U2 命令）：
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -buildWindows64Player <out>\Shell.exe -logFile <out>\build.log

# 无人值守冒烟（已知限制见下）：
<out>\Shell.exe -batchmode -nographics -logFile <out>\smoke_player.log
```

判断记录（独立版无头冒烟的限制）：`-nographics` 强制 `NullGfxDevice`，`UiRoot`/`InputSystemUIInputModule`
依赖的渲染/输入子系统在该模式下按包 README 既有判断记录"可能无法正确初始化"；U3 实测复现——
进程能正常启动、加载全部程序集、不抛异常，但日志在引擎子系统初始化完成后不再产生新内容（无法
从日志判断是否真的推进到了"新游戏→游戏内"），需要强制终止进程。本任务未在 `ShellRoot`/
`FrameworkResidentHost` 里实现命令行参数驱动的"自动新游戏→移动→普攻→存档→退出"脚本化流程
（超出本任务时间范围），因此以 `VerticalSliceTests.FullVerticalSlice_...`（PlayMode，全程带渲染/
输入子系统真实初始化，覆盖完全相同的操作序列并断言每一步）作为替代证据。
