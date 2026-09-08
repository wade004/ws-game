# com.gamefoundation.adapter.unity

框架的 Unity 引擎适配层（L-1）与表现层引擎侧实现，供各游戏工程以 `file:` 方式引用。本包
只做"把 `core/foundation/engine_adapter/contracts` 定义的 13 个中立接口落到 Unity 6000.3 +
URP 2D Renderer + Input System 1.20"这一件事，不包含任何具体游戏逻辑（见
`architecture/02_引擎适配层.md`）。

## 13 个接口实现清单

| 接口 | 实现类 | 关键判断 / 限制 |
|---|---|---|
| `IWindow` | `UnityWindow` | 运行期无法重建操作系统窗口本体、也无公开跨平台 API 改标题栏文字；`OnCloseRequested` 接到 `Application.wantsToQuit`（返回 false 阻止默认退出，交由上层决定何时调 `Destroy()` 真正退出）。 |
| `IClock` | `UnityClock` | `Now()` = `Time.realtimeSinceStartupAsDouble`（只供表现/UI 动画使用）；帧回调由 `UnityEngineHost.Update` 驱动（`Time.unscaledDeltaTime`），固定步长回调由 `FixedUpdate` 驱动（`Time.fixedDeltaTime`），满足"逻辑层 tick 由固定步长驱动"的确定性铁律；`OnFrame`/`RequestFixedStep` 均返回 `SubscriptionHandle`（ADR-0016 决策 1），`Dispose()` 后不再触发，退订对遍历安全。 |
| `IRenderer2D` | `UnityRenderer2D` | 句柄 = 根 GameObject（挂 `SortingGroup`）+ `LayersRoot` 子物体承载纸娃娃层；`sortingOrder = layer*1000 - round(sortY*1)`，纸娃娃层内序号追加为子渲染器自身 `sortingOrder`；`SetTransform` 的 `height` 参数（ADR-0016 决策 2）只平移 `LayersRoot` 本地 Y，不影响排序、不平移影子；资源缺失时用洋红色占位方块 + 一次性警告，不抛异常；`EmitParticle` 优先经 `UnityResourceLoader.TryGetEffect` 把 `effectId` 解析到具体特效资产（序列帧，`ResourceKind.Effect`，见 ADR-0016 决策 5），解析不到才回退播放内建通用爆发效果。 |
| `IAudio` | `UnityAudio` | 总线音量方案选"简单分组乘算"而非 AudioMixer（避免引入需要手工创建的 `.mixer` 资产）；SFX 用 `AudioSource` 对象池，音乐用两路 `AudioSource` 做交叉淡入淡出；SFX 播放期间总线音量变化不影响"已经在播的那次"，只影响之后新播放的；`PlaySfx` 的 `position` 参数（ADR-0016 决策 3）非空时把音源移到该坐标并启用 `spatialBlend=1`（Unity 默认对数衰减），空时 `spatialBlend=0`（无空间衰减，与既有行为一致）。 |
| `IInput` | `UnityInput` | 运行时用代码搭建 `InputActionMap`（不依赖 `.inputactions` 资产）；键盘离散事件靠 `<Keyboard>/anyKey` 触发后扫描 `wasPressedThisFrame/wasReleasedThisFrame` 精确定位具体按键（避免 `anyKey` 聚合控件"拿不到具体是哪个键"的限制）；鼠标左/右/中键各自独立绑定；手柄连接/断开走 `InputSystem.onDeviceChange`；手柄按钮离散事件目前没有专用绑定，测试/上层可用 `SimulateGamepadButtonForTest` 驱动；文本输入用 `Keyboard.current.onTextInput` 累积字符。 |
| `IFileSystem` | `UnityFileSystem` | 用户目录 = `Application.persistentDataPath`；内容根目录（`GetContentRootDir`，ADR-0016 决策 8）= `Application.streamingAssetsPath/GameFoundation`；原子写入 = 写临时文件 + `File.Replace`（目标已存在）/ `File.Move`（目标不存在），失败时清理临时文件、旧内容保持不变；构造参数 `readOnlyContentMode` 切换"用户数据可写"/"内容根只读"两种角色（原独立的 `StreamingAssetsFileSystem` 已合并进本类，见其判断记录 1），只读模式下 `WriteTextAtomic`/`DeleteFile` 恒返回 `false`。 |
| `IResourceLoader` | `UnityResourceLoader` | 后台 `Task` 读取文件字节（纯 `System.IO`，不碰任何 UnityEngine API），解码与回调统一在 `Tick()`（由宿主 `Update` 每帧调用）里于主线程完成，满足"回调总在主线程排队执行"的线程约定；音频只支持标准 PCM16 WAV（内置 `WavDecoder`，不依赖 UnityWebRequest/协程）；`Font` 种类只能读原始字节，不能产出可用的 TMP 字体资产（见下）；`Scene`/`NavMesh`（ADR-0016 决策 5）按"只校验存在性"读文本处理，`Effect` 额外读取 `atlas.png`+`frames.json` 组成 `EffectAsset`（序列帧）。 |
| `INavigation2D` | `UnityNavigation2D` | 网格 A*（不引入第三方寻路包，也不用 Unity 内置三维 NavMesh）；契约方法 `SetBlocking`/`Clear`（ADR-0016 决策 7，`SetBlocking` 整批替换）为主，另保留非契约便捷方法 `RegisterBlockingRect`/`RegisterBlockingFromTilemap`（增量追加，供地图加载代码按格子/瓦片逐个登记）；网格尺寸自适应（默认格子 0.25 世界单位，超过 192×192 格时放大格子），起止点落在已登记范围外时退化为直线可达性检查。 |
| `ISpatialQuery` | `UnitySpatialQuery` | 自维护登记表 + 均匀网格分桶（非 Physics2D，避免同步 Collider2D 的额外成本与结果顺序不确定性）；结果一律按 `Id` 排序；`Register`/`UpdatePosition`/`Unregister`/`Clear` 均为契约方法（ADR-0016 决策 7，此前是本类自行拍板的协作方法）；`HasLineOfSight` 默认恒真，可用 `SetLineOfSightBlocker` 接入 `UnityNavigation2D.Raycast` 做真实遮挡判定（`UnityEngineHost` 已默认接好）。 |
| `IUISurface` | `UnityUISurface` | uGUI `Canvas`（Screen Space - Overlay）+ TextMeshPro；`DrawText` 没有契约层面的句柄/去重机制，按调用顺序累加创建文本元素，非契约方法 `ClearSurface` 供逐帧刷新场景复位；`fontId` 目前不区分具体字体资源，统一用包内占位字体运行期 `TMP_FontAsset.CreateFontAsset` 生成，失败时回退 `TMP_Settings.defaultFontAsset`。 |
| `IPlatform` | `UnityPlatform` | 语言映射 `Application.systemLanguage` → 常见 BCP-47 短代码；剪贴板 = `GUIUtility.systemCopyBuffer`；崩溃日志经 `UnityFileSystem` 原子写入用户目录 `crash_log.txt`，`UnityEngineHost` 额外把 `Application.logMessageReceived` 的 Error/Exception 日志自动转发到 `ReportCrash`。 |
| `IRenderer3D` | `UnityRenderer3D` | **W6-B 收口，真实实现**（ADR-0017 决策 b）：句柄 = 模型预制体实例根 GameObject（`ModelInstance.Root`，只承载 `planePos`/`facing`/`scale`）+ 一个承载 height 偏移的可见内容子物体（`ModelInstance.VisualRoot`）；三维放置换算与 `IRenderer2D`/`ICamera` 同一套 Unity 世界 XY 地面平面约定：`Root` 世界坐标 = `(planePos.X, planePos.Y, 0)`、`Y 轴欧拉角=-facing×(180/π)`、`VisualRoot` 局部坐标 = `(0, height, 0)`（PR130-01 根治，取代此前借用 Z 轴表达 `planePos.Y` 的旧约定，见类型顶部判断记录）；`CreateModelInstance` 统一经 `UnityResourceLoader.TryLoadModelSync` 解析（缓存优先、未命中时由加载器同步解析一次，本类型自身不直接调用 `Resources.Load`；ADR-0017 决策 1 禁止的是 renderer 实现绕过 `IResourceLoader` 直接读取资源，首次引用某资源 id 时由 `IResourceLoader` 的实现同步或异步完成该资源的加载均属允许，本类型只经 `IResourceLoader` 取用资源，符合该决策，见 `TryResolvePrefab`/`TryLoadModelSync` 判断记录、`architecture/02_引擎适配层.md` 第 1.7 节勘误，2026-09-08），两条路径都解析不到时不抛异常——落地占位内容（优先复用 `model.placeholder_biped`，连它都取不到时兜底内建胶囊体）并发起一次真正的 `IResourceLoader.LoadAsync`，加载成功后原地把占位替换为真实内容（PR130-05 根治，与 sprite 型"缺资源用占位方块顶上"同一套宽容立场）；**PR140-02 根治**：原地替换（`AttachVisual`）除重放槽位网格/材质参数/最近一次播放剪辑外，还会把仍挂在旧可见内容 socket 挂点下的子模型实例摘出来暂存、待新内容就位后按原挂点名重新挂回（找不到同名挂点时保持暂存，不销毁不抛异常），并按 `ModelInstance.Shadow` 重新应用投影阴影开关到新内容的全部 `Renderer`——不再出现"替换后子模型句柄悬空"或"投影阴影悄悄回到 Unity 默认值"。`PlayAnim` 优先用 Animator 按状态名（`clipId` 末段）`CrossFadeInFixedTime`（同状态重入改用 `Animator.Play(stateName, -1, 0f)` 硬切重播，见 H5b 判断记录），找不到对应状态时回退 `UnityEngine.Animation` 组件 + 约定路径加载的 `AnimationClip`；命中帧等关键帧事件经 Unity `AnimationEvent`（函数名固定 `OnAnimEvent`，字符串参数＝裸事件名）中继回调；**H5b 根治新增、PR140-03 再次根治**：`UnityEngineHost.Update` 每帧驱动 `Tick()`，逐实例侦测"非循环剪辑自然播放完成"（Animator 分支不再只看"当前状态是否恰好等于目标状态"——若该状态在 `AnimatorController` 里配置了自动过渡且过渡的开始与结束都发生在两次检测帧之间，改为"曾经确认进入过目标状态、现在稳定停留在别的状态"同样判定完成，避免自动过渡漏发完成事件；Animation 组件兜底路径用 `IsPlaying` 转 false 不变），完成时经 `OnAnimEvent` 通道额外发出一次约定的 `anim_event.finished`（循环剪辑不发）——这是 02 第 1.12 节新增的契约义务，`Presentation.Render.ModelCharacterRig` 一侧的装配代码把它接回动画状态机解除瞬态状态的优先级锁；`SetSlotMesh`/`AttachToSocket` 按子对象精确名字查找（约定子对象名＝挂点/槽位 `Id` 原文，见占位资产生成脚本）；`SetSlotMesh` 的 `meshId`（`mesh_ref`）经 `UnityResourceLoader.TryGetOrLoadSlotMesh` 解析（AUD-05 根治，不再直接 `Resources.Load<Mesh>`，详见上"资源 id → 路径规则"一节判断记录），未加载/无法提取时保留槽位当前网格并发起真正的异步加载，不静默清空为不可见；**PR140-01 根治**：`SetShadow` 的 `Blob` 用贴地占位 Quad，世界旋转钉死为 `Quaternion.identity`（Quad 图元局部法线沿 -Z，恰好正对相机固定的 `forward=(0,0,1)`）、位置只依赖锚点根世界位置外加沿世界 Z 轴的微小防 z-fighting 偏移，且每次 `SetPlacement` 都重新计算一遍，不随 `Root` 的 facing 旋转偏出地面画面平面（取代此前"局部旋转固定 `Euler(90,0,0)`"这一套已废弃的 XZ 地面旧约定写法）；`Projected` 直接用 Unity 内建实时阴影（与 sprite 路线"Projected 降级为 Blob"不同——model 型有真正三维几何体）。 |
| `ICamera` | `UnityCamera` | 正交投影，世界平面固定为 Unity 的 XY 平面（Z=0），与 `IRenderer2D` 精灵摆放平面一致；`pitchDegrees`/`yawDegrees` 只记录配置值，不据此做真实透视投影（URP 2D Renderer 不支持）；`zoom` 直接映射 `orthographicSize`；`SetTransform`/`WorldToScreen` 的 `height` 参数作为世界 Y 附加偏移；`Shake` 的 `frequency` 参数（ADR-0016 决策 4）驱动 Perlin 噪声按 `elapsed*frequency` 采样生成抖动偏移，取代此前逐帧独立采样的 `UnityEngine.Random`。 |

`UnityEngineHost`（`MonoBehaviour`，`DontDestroyOnLoad`）是组合根，持有以上 13 个实例；静态
`UnityEngineHost.Ensure()` 获取（必要时创建）全局唯一实例。`Update`/`FixedUpdate`/
`OnApplicationQuit` 的驱动分工见该类型顶部注释。

## 资源 id → 路径规则

见 `UnityResourceLoader.cs` 顶部注释，摘要：

```
Application.streamingAssetsPath/GameFoundation/<kind 子目录>/<资源引用id去掉类别前缀，点号换下划线>.<扩展名>
  Image     -> sprites/<name>.png
  Audio     -> audio/<name>.wav（仅支持标准 PCM16 WAV）
  DataTable -> data/<name>.json
  Scene     -> scene/<name>.json（ADR-0016 决策 5 新增，内容不被解析，见 build.ps1 判断记录）
  NavMesh   -> nav_mesh/<name>.json（同上）
  Effect    -> vfx/<name>/atlas.png + vfx/<name>/frames.json（ADR-0016 决策 5 新增；不是单一文件，
              走 UnityResourceLoader.ResolveEffectDir，不经上面的通用扩展名规则）
```

Font 种类不走上表这条 StreamingAssets 规则（缺口 1 已解决，约定）：

```
Resources/Fonts/<资源引用id去掉 "font." 前缀，点号换下划线>（不带扩展名，Resources.Load<Font> 自动匹配）
  例：font.noto_sans_cjk_sc -> Resources/Fonts/noto_sans_cjk_sc（对应 assets/_placeholder/fonts/
      noto_sans_cjk_sc.otf，由 build.ps1 -SyncContent 同步进 adapters/unity/Assets/Framework/
      Resources/Fonts/，见下"内容同步"一节）
```

该资产必须是已被 Unity 资产管线导入过的 `UnityEngine.Font` 对象（不能是任意字节数组，见下"已知
契约缺口"第 4 条）；`UnityResourceLoader.LoadAsync(kind=Font)` 对该路径存在的字体资产标记为
"已加载"（`IsLoaded` 一致），`UnityUISurface` 按 `fontId` 解析对应 `TMP_FontAsset`（找不到时回退
默认字体并记一条诊断）。

命名规则与 `architecture/14_资产规格书模板.md` 第 1.2 节文件名模板、
`presentation/render/core/SpriteViewBase.ResolveLayerResourceId` 的"去掉类别前缀、点号换下划线"
规则保持同一套口径。

Model/AnimationClip 两个种类（W6-B 新增，ADR-0017 决策 a/c；`AnimationClip` 种类 12 §5 勘误新增，
见下文判断记录）同样不走 StreamingAssets——运行期没有公开 API 能把裸字节数组反序列化成可用的
GameObject 层级/骨骼/Animator 绑定或 AnimationClip 资产，只能消费已经被 Unity 资产管线预先导入好
的资源（与 Font 种类同一处境）：

```
Resources/GameFoundation/models/<资源引用id去掉类别前缀，点号换下划线>（不带扩展名）
  例：model.placeholder_biped -> Resources/GameFoundation/models/placeholder_biped
      （对应 Assets/Resources/GameFoundation/models/placeholder_biped.prefab，见下"model 型外形
      占位资产"一节）。

Resources/GameFoundation/anim_clips/<资源引用id去掉类别前缀，点号换下划线>（不带扩展名）
  例：anim.attack -> Resources/GameFoundation/anim_clips/attack
      （对应 Assets/Resources/GameFoundation/anim_clips/attack.anim）；model 型
      display.anim_set.clips[*].resource_ref 走这条约定——判断记录（W6-B）：resource_ref 建议只用
      一个点分段（如 "anim.idle"，不要写成 "anim.<角色名>_idle"）——UnityRenderer3D.PlayAnim 用
      clipId 的"最后一个点分段"匹配 Animator 状态名，UnityResourceLoader.ResolveAnimClipResourcesPath
      用"去掉第一个点分段"解析磁盘路径，两者在只有一个点号时结果自动一致；多角色共用不同剪辑时
      应各自登记不同的 resource_ref（如 "anim.hero_idle"/"anim.beast_idle"），不要求全局唯一命名
      规则，只要求"同一 resource_ref 只对应一份 Animator 状态名 + 一份剪辑资产"。
```

`UnityResourceLoader.LoadAsync(kind=Model)`/`LoadAsync(kind=AnimationClip)` 同 Font 一样排队到
下一次 `Tick()` 于主线程完成判定（结果分别缓存进 `_modelPrefabs`/`_animationClips`，供
`UnityRenderer3D.CreateModelInstance`/`ResolveLegacyClip` 与 `UnityViewFactory.RegisterModelClipEvents`
优先复用，避免重复 `Resources.Load`）；两条同步兜底路径分别是
`UnityResourceLoader.TryLoadModelSync`/`TryLoadAnimationClipSync`（消费方调用点本身是同步的，无法
等 `LoadAsync` 走完 `Tick` 排队，但仍与异步路径共用同一份缓存与解析逻辑，`Resources.Load` 调用只
收口在 `UnityResourceLoader` 一处）。

判断记录（`AnimationClip` 种类改为经 `IResourceLoader`，取代此前"`AnimClip` 种类不经
`IResourceLoader`，`Resources.Load<AnimationClip>` 由 `AnimClipResolver`/`UnityViewFactory`/
`UnityRenderer3D` 各自直接同步调用"的立场——`architecture/落地计划/audit-85f1f4f-20260908/` 第九方
审核"动画剪辑事件登记契约差异"）：`ResourceKind` 新增 `AnimationClip`，`UnityRenderer3D.ResolveLegacyClip`
（Animation 组件兜底播放路径）与 `UnityViewFactory.RegisterModelClipEvents`（关键帧事件登记）均改为
经 `UnityResourceLoader.TryLoadAnimationClipSync` 取用，本类型自身不再出现任何 `Resources.Load`
调用，与 `architecture/02_引擎适配层.md` 第 1.7 节勘误口径一致。

判断记录（`SetSlotMesh`/`mesh_ref` 资源合同，AUD-05 根治，取代此前"`mesh_ref` 复用 Model 种类同一套
约定与子目录，直接 `Resources.Load<Mesh>`"的立场——同一份审核 PRES-85-01"样例 model 槽位换装把
prefab 引用按 Mesh 读取，槽位网格被清空"）：`mesh_ref` 仍然复用 `ResourceKind.Model` 同一套资源
种类与磁盘路径约定（不新增资源种类，与 `model_ref` 同一命名空间），但不再直接把解析结果当独立
`Mesh` 资产读取——`UnityResourceLoader.TryGetOrLoadSlotMesh(resourceId, slotId)` 按顺序解析：命中
已加载的模型预制体缓存/已提取网格缓存时直接复用并提取（优先取与 `slotId` 同名子对象上的网格渲染
组件，找不到时退回预制体上首个网格渲染组件的子对象）；未命中时先尝试 `TryLoadModelSync` 同步解析
为预制体再提取；预制体解析失败时退回尝试直接 `Resources.Load<Mesh>`（"若是独立网格资源也可直接
使用"）。`UnityRenderer3D.ApplySlotMesh` 只从该方法取网格，解析失败时保留槽位当前网格（不清空为
`null`）并记一条诊断，经 `LoadAsync(meshId, ResourceKind.Model, ...)` 发起一次真正的异步加载，完成
后对仍存活、且该槽位登记未被更晚一次调用覆盖的实例原地替换。

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
都 `Debug.LogError` 并把 `BootstrapFailed` 置 `true`，不抛异常穿透、不注册 `OnFixedStep`/`OnFrameTick`
（见下"固定步长驱动"一节）。

数据集根、示例地图 id、玩家模板 id 等均为 Inspector 可配置字段（`[SerializeField]`），默认值指向
框架自带的中性示例数据（`data/_sample`、`creature.sample_hero` 等，与 `GameWorldFixture` 同一套
id）。

固定步长驱动（2026-09-07 改写，引擎侧收口任务）：`BuildWorld` 末尾用
`host.Clock.RequestFixedStep(Time.fixedDeltaTime, OnFixedStep)`/`host.Clock.OnFrame(OnFrameTick)`
各注册一次固定步/逐帧回调，持有返回的 `SubscriptionHandle`（`_fixedStepHandle`/`_frameHandle`），
`OnDestroy` 里显式 `Dispose()` 退订。`OnFixedStep` 内 `IInputMapHost.Update` 轮询本帧输入 → 按当前
动作状态提交 "move"/"cast" 意图或调用 `GameObjectHost.Interact`（见下"输入→意图链路"）→调用
`GameplayAssembly.Advance(stepSeconds)`（连续模式下等价于原先的
`WorldSim.Tick(SimStep.Continuous(stepSeconds))`）；`OnFrameTick` 只做表现（`ViewBinder.SyncAll`/
`CameraHost.Update` 插值同步、三个反馈接收器的 `Tick`）。此前本类型保留 `MonoBehaviour`
`FixedUpdate`/`Update` 直驱写法，原因是 `IClock` 契约当时只有注册方法、没有取消注册方法；
ADR-0016 决策 1 给 `RequestFixedStep`/`OnFrame` 补上 `SubscriptionHandle` 返回值后这条限制已解除，
改为经 `IClock.RequestFixedStep`/`OnFrame` 显式注册 + 退订，与 `core/carriers/unit` 等真正跨场景
常驻调用方使用同一套契约，不再是两套并存的驱动方式；固定步长直接取 `Time.fixedDeltaTime`（Unity
自身固定步长本就是恒定配置值），与内部构造的 `SimClockHost.StepSeconds` 取同一个值，两者按相同
节拍推进，误差为零。

### 输入→意图链路

`GameFoundationBootstrap` 复用 `PresentationAssembly.InputMap`（同一个 `IInputMapHost` 实例，UI
设置面板等也持有它），`DeclareActionSet` 时整批读取 `data/_sample/found/found.input_action.json`
（经 `registry.GetAll("found.input_action")` + `ActionDefinition.FromRecord` 逐行转换）已加载的
全部动作行——缺口 2 已给该表补上 `attack`/`skill_1`~`skill_4`/`use_item` 若干战斗类按钮动作，本
类型不再像此前那样在代码里另造一个绕开数据表的补充动作集（`actionset.greybox`），见下"已知
契约缺口"第 7 条。移动经 `MovementHost.Request(MoveRequest.InDirection(...))`（内部转成 `move`
意图提交）；普攻/技能 1 经 `IWorldSim.SubmitIntent` 提交一条 `cast` 意图（`skill_id` 分别为
`skill.sample_strike`/`skill.sample_burn`，玩家注册等级设为 3 以同时解锁两个技能）；交互同样经
`IWorldSim.SubmitIntent` 提交一条 `interact` 意图（`Core.Carriers.Gobj.InteractIntentTickHandler`
消费，见下"已知契约缺口"第 6 条，`GameFoundationBootstrap.Interact()` 已不再直接窄契约调用
`GameObjectHost.Interact`）。三条路径均不直接改 `WorldSim`/`Carriers` 状态，满足表现层铁律。

### 视图与反馈接收器（`Runtime/Presentation/`）

- `UnityViewFactory : IViewFactory` + `UnitySpriteView : SpriteViewBase`：按 `DisplayInfo.Kind`
  选择创建真实 sprite 型 View 还是退化成不渲染的 `NullView`（没有 `kind=sprite` 的
  `DisplayInfo` 时，如 gobj 类未来接入非 sprite 外形）。`UnitySpriteView` 在朝向变化时重新解析并
  提交纸娃娃层资源加载（见下"判断记录：主动 LoadAsync"），`SetFlash`/`ClearFlash` 经既有
  `SetShaderParam` 通道触发/复原过曝白色 hit-flash（`UnityRenderer2D` 新增
  `"flash_intensity"` 参数名解释，见该类型判断记录）。外部审核阻塞项 3 收口（2026-09-07）：
  `CreateView` 对 `DisplayCategory.Creature` 分类默认挂接 `UnityFrameAnimPlayer` +
  全局单例 `AnimClipResolver`（构造函数新增可选 `bus`/`dataRegistry` 参数），使
  `Rig.PlayClip` 在不需要 `FrameworkResidentHost`/`GameFoundationBootstrap`/
  `games/_template.GameBootstrap` 任何一方手工接线的情况下默认可用——此前
  `UnityFrameAnimPlayer`/`AnimClipResolver` 均已实现且有独立单测
  （`Tests/Runtime/AnimationLayerTests.cs`），但没有任何生产代码路径真正构造并挂接它们，见
  `Runtime/Presentation/UnityViewFactory.cs` `AttachDefaultAnimation`/`RegisterDefaultClips`
  判断记录。
- `FloatingTextReceiver`：世界空间 `TextMeshPro` 对象池，颜色按 `FloatingTextStyleDef.ColorRef`
  的字面量做"是否含 crit"启发式区分（框架没有 id → 具体色值的查询能力，见类型注释）。
- `FreezeFrameReceiver`：只暂停 `OnFrameTick` 里的 `ViewBinder.SyncAll`/`CameraHost.Update`
  两步调用，不影响 `OnFixedStep`（经 `IClock.RequestFixedStep` 驱动，见上"引导流程"一节）里的
  `GameplayAssembly.Advance`/`WorldSim.Tick`（09 第 6 节"顿帧"落地，判断记录见类型顶部）。
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
GameFoundation/` 整体 `.gitignore`，只提交同步脚本本身。`sprites`/`audio`/`vfx` 三个目标目录
同时同步 `assets/_placeholder/` 与 `assets/_sample/`（后者由 `toolchain/import_sample_assets.py`
导入，见该脚本与 `toolchain/README.md`），二者汇入同一棵目标目录树。

`UnityResourceLoader.ResolvePath` 新增"`layer.` 类别"特例（三级目录路径，见该方法判断记录）：
`presentation/render/core/SpriteViewBase.ResolveLayerResourceId` 产出的纸娃娃层资源 id 形如
`"layer.<spriteSetName>__<direction>__<layerName>"`，但 `toolchain/gen_placeholder_assets.py`
生成的占位精灵集（以及 `toolchain/import_sample_assets.py` 经 `import_assets.py` `sprite` 子命令
导入的 `assets/_sample/sprites/` 精灵集，两者磁盘布局一致）实际磁盘布局是"目录按方向/层分层"
（`sprites/<spriteSet>/<direction>/<layer>.png`），不是单一扁平文件名；本方法只对 `"layer."` 这一
个类别把双下划线分隔的三段还原成三级目录，其余类别（`sprite.`/`icon.` 等）的既有扁平解析规则
不变。

### 灰盒场景与测试/构建

见 `adapters/unity/README.md`"命令行跑测试/编译检查/构建"一节；场景由
`Assets/Editor/GreyBoxSceneBuilder.cs`（`[MenuItem]`/`-executeMethod` 均可触发）程序化生成，不
手工在编辑器里搭建（满足"全程命令行、不开 GUI"的硬性规则，也让场景内容可重复重建）。PlayMode
测试见 `Tests/Runtime/GreyBoxTests.cs`（6 条，覆盖启动零阻断错误、玩家视图真实资源加载、移动、
攻击伤害+飘字、SFX 播放、场景重进不重复）。

## 已知契约缺口

以下 1、2、3、5、6、8 六条已由 ADR-0016（`architecture/adr/0016-引擎适配层契约阶段4联调补齐.md`）
解决，4、7 两条已由本任务（工具链/引擎侧缺口收敛）约定解决，均保留在此作为历史记录。

1. **已由 ADR-0016 决策 2 解决**：`IRenderer2D.SetTransform` 此前没有高度参数（不同于
   `IRenderer3D.SetPlacement` 显式带 `height`），表现层曾借用 `SetShaderParam` 的
   `"height_offset_px"` 参数名传递像素高度。`SetTransform` 现已正式携带 `height` 参数，
   `SpriteViewBase`/`UnityRenderer2D` 均已改走该参数，`HeightOffsetShaderParam` 常量与相关工作绕
   已删除。
2. **已由 ADR-0016 决策 3 解决**：`IAudio.PlaySfx` 此前没有位置参数。现已增加
   `position: Optional<Vec2>`，`SfxPlayer.Play` 的 `at` 透传给它，`UnityAudio` 非空时启用
   `spatialBlend=1` 做基本的 2D 声像。
3. **已由 ADR-0016 决策 5 解决**：`ResourceKind` 此前缺 `Scene`/`NavMesh`，也缺"粒子/特效预制体"
   这一种类。现已增加 `Scene`/`NavMesh`/`Effect` 三个取值，`SceneRouter` 改用前两者加载
   `scene_ref`/`nav_ref`，`UnityRenderer2D.EmitParticle` 优先经 `UnityResourceLoader.TryGetEffect`
   把 `effectId` 解析到具体特效资产（序列帧），解析不到才回退内建通用效果。
4. **已解决**（约定：字体资源 id `font.<name>` → 引擎侧预导入字体资产 `Resources/Fonts/<name>`，
   `<name>` 为该 id 去掉 `font.` 前缀、点号换下划线后的结果，见下"资源 id → 路径规则"）：
   `IResourceLoader` 的 `Font` 种类此前只能提供原始字节，Unity 运行期没有公开 API 能把任意字体
   字节数组转换成可用于 TMP 渲染的字体资产（`TMP_FontAsset.CreateFontAsset` 需要一个已被 Unity
   资产管线导入过的 `UnityEngine.Font` 对象）。现改为：`UnityResourceLoader.LoadAsync(kind=Font)`
   在主线程对 `Resources/Fonts/<name>` 调用 `Resources.Load<Font>` 判定"已加载"（`IsLoaded` 与之
   一致，不再只读字节，见该类型顶部"判断记录（Font 资源种类）"）；`UnityUISurface.ResolveFontAsset`
   按 `fontId` 各自缓存一份 `TMP_FontAsset.CreateFontAsset` 生成的字体资产，解析不到（未导入该
   字体资产、fontId 未知等）才回退到包内默认占位字体 `Resources/Fonts/noto_sans_cjk_sc`（原
   `NotoSansCJKsc-Regular`，已重命名为与 id 约定一致的裸名，见 `assets/_placeholder/fonts/README.md`
   "用途/引用名"），仍失败才最终回退 TMP 内置默认字体，每次回退都记一条诊断
   （`Debug.LogWarning`）。`build.ps1 -SyncContent` 把 `assets/_placeholder/fonts/*.otf|*.ttf`
   哈希比对同步到 `Assets/Framework/Resources/Fonts/`（见 `adapters/unity/README.md`"内容同步"
   一节）。该路径仍依赖 `adapters/unity/Assets/TextMesh Pro/`（TMP 官方 Essential Resources 的
   标准内容——`TMP Settings.asset`、SDF 着色器、默认字体等，与手动执行编辑器菜单"Import TMP
   Essential Resources"产生的文件完全一致）已提交进本仓库，见 `UnityUISurface.cs` 顶部"判断记录
   （TMP 运行期依赖）"（这部分未变）。
5. **已由 ADR-0016 决策 7 解决**：`ISpatialQuery`/`INavigation2D` 契约本身此前都没有定义"如何把
   地图对象/阻挡数据登记进实现"的方法，本适配层曾提供非契约协作方法。现已分别补上契约方法
   `ISpatialQuery.Register`/`UpdatePosition`/`Unregister`/`Clear`、
   `INavigation2D.SetBlocking`/`Clear`；`UnitySpatialQuery` 的 `Register`/`UpdatePosition`/
   `Unregister`/`Clear` 现直接就是契约实现，`UnityNavigation2D` 新增契约方法 `SetBlocking`/
   `Clear`（整批替换），另保留非契约便捷方法 `RegisterBlockingRect`/`RegisterBlockingFromTilemap`
   （增量追加）供地图加载代码使用。空间索引的登记/注销时机现由
   `core/carriers/assembly.EntitySpatialSyncHost`（创建/销毁）与
   `Core.Carriers.Unit.WorldUnitAccess.SetPosition`（移动）统一驱动，引擎侧/游戏侧不再需要手工
   调用 `Register`/`Unregister`。
6. **已由 ADR-0016 背景一节联动解决**（U2 新增）：`found.event_catalog`/`IWorldSim` 的 tick 阶段
   编排里此前没有任何消费 `Kind=="interact"` 意图的处理器，`GameFoundationBootstrap` 曾把"交互"
   落成对 `GameObjectHost.Interact` 的窄契约调用。现已补上
   `Core.Carriers.Gobj.InteractIntentTickHandler`（挂在 `TickPhase.TriggerEvaluation`，见
   `core/carriers/assembly/CarriersAssembly.cs`），`GameFoundationBootstrap.Interact()` 已改为
   提交 `interact` 意图，不再直接调用 `GameObjectHost.Interact`。
7. **已解决**（U2 新增，本任务补齐）：`data/_sample/found/found.input_action.json` 原先的示例
   动作集只有 move/confirm/cancel/interact/open_menu/pause/camera_adjust 七个动作，不含任何战斗
   类动作（`GameFoundationBootstrap`/`FrameworkResidentHost` 因此此前各自在代码里另造一个绕开
   数据表的补充动作集）。现已给该表补上 `attack`/`skill_1`~`skill_4`/`use_item`（可选）若干战斗
   类按钮动作（`description` 字段仍声明"示例动作集，不构成任何游戏的操作定论"），
   `GameFoundationBootstrap`/`FrameworkResidentHost` 均已改为整批读取该表已加载的全部行
   （`registry.GetAll("found.input_action")` + `ActionDefinition.FromRecord` 逐行转换后
   `DeclareActionSet`），删除了此前各自代码内的补充声明（`actionset.greybox`/`actionset.shell`
   两个自造动作集、`input.action.greybox_attack`/`input.action.greybox_skill_1`/
   `input.action.shell_attack`/`input.action.shell_skill_1` 四个绕开数据表的动作 id），不再修改
   `data/` 之外任何逻辑。
8. **已由 ADR-0016 决策 6 解决**（U2 新增）：`IResourceLoader` 契约此前没有规定"谁来触发某个
   资源 id 的首次加载"。现已在 02 第 1.7 节写入"谁首次引用谁加载"条款；`presentation/common`
   新增共享实现 `ResourceReferenceTracker`，`SpriteViewBase`/`VfxPlayer`/`SfxPlayer` 均已接入
   （可选注入 `IResourceLoader`），`UnitySpriteView` 此前自行实现的
   `_requestedLoads`/`RequestLoads` 已删除，改由基类统一负责。

## 判断记录索引

详细判断记录写在各实现文件顶部注释里，此处只列索引：

- `UnityClock.cs`：确定性铁律的落地方式（帧回调 vs 固定步回调的驱动源）；`SubscriptionHandle`
  退订的快照遍历安全性。
- `UnityRenderer2D.cs`：排序公式、`height` 参数落地、资源缺失占位、`Effect` 资源优先解析
  （ADR-0016 决策 2、5）。
- `UnityAudio.cs`：总线音量方案二选一的取舍理由；`position` 参数的 2D 声像落地（ADR-0016 决策 3）。
- `UnityCamera.cs`：世界平面坐标系与投影简化的判断记录；`frequency` 参数驱动 Perlin 噪声采样
  取代 `UnityEngine.Random`（ADR-0016 决策 4）。
- `UnityInput.cs`：不依赖 `.inputactions` 资产、`anyKey` 聚合控件的绕过方式。
- `UnityResourceLoader.cs`：资源 id→路径映射、后台线程 + 主线程完成队列的线程模型、音频/字体
  解码能力边界、`Scene`/`NavMesh`/`Effect` 三个新种类的加载路径（ADR-0016 决策 5）。
- `UnityNavigation2D.cs`：网格 A* 选型理由、网格自适应策略、`SetBlocking`（契约方法，整批替换）
  与 `RegisterBlockingRect`（非契约便捷方法，增量追加）的分工（ADR-0016 决策 7）。
- `UnitySpatialQuery.cs`：自维护登记表 vs Physics2D 的取舍理由；`Register`/`UpdatePosition`/
  `Unregister`/`Clear` 现为契约方法（ADR-0016 决策 7）。
- `UnityUISurface.cs`：`DrawText` 语义解释、占位字体生成方式。
- `UnityRenderer3D.cs`：三维放置坐标换算（与 sprite/相机同一套 Unity 世界 XY 地面平面约定）、资源
  缺失降级（占位 + 异步替换，不抛异常）、Blob 影子朝向与地面平面对齐（PR140-01）、异步模型替换的
  实例状态恢复（PR140-02）、Animator 自动过渡完成事件判定（PR140-03）等判断记录。
- `UnityFileSystem.cs`：`readOnlyContentMode` 构造参数合并原 `StreamingAssetsFileSystem`
  的判断记录 1、`GetContentRootDir` 两种模式下语义一致的判断记录 2（ADR-0016 决策 8）。
- `EffectSequencePlayer.cs`：`ResourceKind.Effect` 序列帧动画的最小播放组件。
- `Runtime/Bootstrap/GameFoundationBootstrap.cs`：装配顺序、固定步驱动已改走
  `IClock.RequestFixedStep`/`OnFrame`（ADR-0016 决策 1 补上 `SubscriptionHandle` 退订能力后，
  引擎侧收口任务把此前保留的 `FixedUpdate`/`Update` 直驱写法收口为显式注册 + `OnDestroy` 退订，
  见上"引导流程"一节）、交互为什么改为提交意图（原判断记录 2，已由 ADR-0016 联动解决）、普攻/
  技能 1 为什么不读 `found.input_action` 表、空间索引登记为什么改由 L3 同步（判断记录见文件
  顶部与各处内联注释）。
- `Runtime/Presentation/UnitySpriteView.cs`：资源加载已下沉到 `SpriteViewBase`
  （ADR-0016 决策 6），本类型的重复实现已删除。
- `Runtime/Presentation/UnityViewFactory.cs`：`DestroyAllCreatedViews` 弥补
  `ViewBinder`/`CameraHost` 不支持退订的已知缺口。
- `Runtime/Presentation/FlashReceiver.cs`：闪白时长/强度默认值的取舍理由。
- `Runtime/Presentation/FreezeFrameReceiver.cs`：顿帧只暂停表现层、不暂停逻辑 tick 的落地方式。
- `UnityResourceLoader.cs` `ResolvePath`：`"layer."` 类别嵌套路径特例的判断记录。
- `Runtime/Shell/FrameworkResidentHost.cs`：框架常驻部分为什么不直接改造
  `GameFoundationBootstrap`、世界/装配根只构造一次、`AiHost`/`SpawnHost` 级联清理缺口（核心一半
  已由 ADR-0016 解决，`GameplayAssembly.LeaveMap` 承担出图退场，见 `HandlePreUnload` 判断记录）。

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
  `post_load`/`pre_unload` 钩子负责"进入/离开地图部分"——按需重新 `AddEntity`、`EnterMap`、
  （仅第一次）生成示例生物，`pre_unload` 里统一调用一次 `Gameplay.LeaveMap(mapId)`（ADR-0016
  背景一节联动新增，见 `GameplayAssembly.LeaveMap` 判断记录；此前逐个实体手工
  `AiHost.UnregisterUnit`+`ISpatialQuery.Unregister`+`SpawnHost.NotifyDespawn` 的窄契约兜底已删除，
  见下"契约缺口发现"）。
- `ShellRoot`：Awake 时 `Ensure()` 常驻部分、建 `UiRoot`/`UiPanelHost`/主菜单/新游戏难度选择/加载画面
  四块 UI，`Update()` 按 `ShellHost.Page` 切换显示哪一块、驱动 `Esc` 暂停/恢复。
- 示例 `NewGameStarter`（`FrameworkResidentHost.SampleNewGameStarter`）：**这是灰盒验收用的示例
  实现**——只重置玩家对象字段（位置、地图 id）到新游戏默认值、返回固定示例地图
  `world.sample_field`；具体游戏必须提供自己的 `NewGameStarter`（见
  `presentation/shell/contracts/ShellHostTypes.cs` 判断记录）。`SaveGameId` 设为中性 id
  `game.sample`；示例存档槽固定为 `game.sample.slot_1/2/3`（`ShellRoot.SaveSlotCandidates`）。
- 场景资源占位（判断记录）：`world.sample_field` 的 `scene_ref`/`nav_ref` 需要
  `UnityResourceLoader` 能读到对应文件才能让 `SceneRouter.LoadScene` 成功；`ResourceKind` 现已有
  `Scene`/`NavMesh` 专用取值（ADR-0016 决策 5），`build.ps1` 因此在内容同步步骤里分别生成两份
  静态占位字节 `StreamingAssets/GameFoundation/scene/sample_field.json`、
  `StreamingAssets/GameFoundation/nav_mesh/sample_field.json`（内容本就不被解析，见 build.ps1
  该步骤判断记录），不修改 `data/_sample`/`assets/_placeholder`。

### 契约缺口发现（U3 新增，核心一半已由 ADR-0016 背景一节联动解决）

9. `Core.Foundation.SceneRouter.SceneRouter.FinishLoading` 在"非本实例首次 LoadScene"时调用
   `world.ClearAll()`，但此前 `Core.Rules.Ai.AiHost`（内部 `RegisteredUnitIds`）与
   `Core.Gameplay.Spawn.SpawnHost`（`on_map_enter` 重生策略靠 `runtime.EntityId` 判断"该出生点
   是否已有存活实体"）都不知道 WorldSim 那边已经清空——第二次进入地图时，上一局残留的 AI 注册表
   项会在下一次 `AiTickHandler.Execute` 让 `WorldUnitAccess.Require` 抛
   `InvalidOperationException`（U3 实测复现）。现已分两处解决：`AiHost` 直接订阅
   `entity.destroyed` 自行静默清理（`core/rules/ai/core/AiHost.cs` 构造函数），
   `GameplayAssembly.LeaveMap` 把 `AreaTrigger.UnloadMap`/`Spawn.UnloadMap` 打包成"出图"入口。
   `FrameworkResidentHost.HandlePreUnload`/`GameFoundationBootstrap` 因此不再需要逐个实体手工
   `AiHost.UnregisterUnit`+`ISpatialQuery.Unregister`+`SpawnHost.NotifyDespawn`，改为统一调用
   `Gameplay.LeaveMap(mapId)`（`ISpatialQuery.Unregister` 现也是契约方法，见 ADR-0016 决策 7）。
   同一缺口也会在"生物战斗至真正死亡"这条路径（`unit.died` 触发，非地图切换触发）上单独出现，
   `AiHost` 的 `entity.destroyed` 订阅同样覆盖——`GameFoundationBootstrap`/`FrameworkResidentHost`
   订阅的 `unit.died` 处理器改为体验优化（尸体立刻停止 AI 决策/退出战斗目标空间索引），不再是
   崩溃安全网，见两处 `OnUnitDied` 判断记录。
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
    U3 排障新增（阶段 4 U3 修复补充）：`FullVerticalSlice_...` 原先只等 60 个固定 tick（约 1.2
    秒）就认为 `skill.sample_burn` "结算完"，但 `skill.aura_def.sample_burn` 的
    `duration` 是 6.0 秒（`interval` 1.0 秒周期伤害）——60 帧只够等到第一次周期伤害，光环本身
    远未过期；这条时序漏洞此前没有暴露纯粹是因为 `VerticalSliceTests.cs` 自己的死亡判断逻辑写错
    （见下条 12），测试总在走到存档/读档触发 `ClearAll` 之前就已经因为断言"生物应当死亡"失败而
    提前中止，光环实例的后续周期 tick 从未真正对着一个已被 `ClearAll` 移出 `WorldSim` 的旧目标
    结算过。修正死亡判断后，测试能正常走到存档→篡改→读档这一步，`ClearAll` 真的会在光环还没
    过期时把旧生物实体移出世界，下一次周期 tick 命中本条已记录的核心缺口而崩溃
    （`WorldUnitAccess.Require` 抛 `InvalidOperationException`，实测复现于崩溃到下一条不相关
    用例 `YSorting_TwoEntitiesWithDifferentY_SortingOrderReflectsY`）。已把测试里的固定帧数等待
    改为轮询 `IAuraQuery.HasAura` 直到光环真正消失（上限 500 帧，远大于 6 秒对应的约 300 帧），
    未改动 core/。
12. `Core.Carriers.Unit.WorldUnitAccess.SetAlive` 顶部注释记录的既定设计：死亡是逻辑状态，不是
    生命周期状态——生物战斗死亡后仍以 `alive = false` 的"尸体"形态留在 `IWorldSim` 里
    （`Entity.Lifecycle` 不变、`World.GetEntity` 仍查得到），直到刷新表/复活策略另行处理，不会被
    立即移出世界。`VerticalSliceTests.FullVerticalSlice_...` 原先用
    `World.GetEntity(beastId) == null || Lifecycle != EntityLifecycle.Active` 判断"死亡"，与这条
    既定设计不符——该条件在正常战斗死亡路径下永远不会为真（U3 排障最初曾按"死亡该让实体真正退场"
    这一假设给 `core/gameplay/spawn` 新增了一个 `unit.died -> ICreatureFactory.Despawn` 的监听器
    去补"退场"，但这会立即销毁尸体，与上述既定设计直接冲突，且会让 `GreyBoxTests`/`UiSuiteTests`
    等此前一直稳定通过的"击中生物后继续查询其血量"类用例因为生物已被销毁而抛
    `InvalidOperationException`——已废弃该方案，未改动 core/）。正确修法是让测试改用与既定设计
    一致的死亡信号：`Core.Rules.Common.IUnitAccess.IsAlive`（`Core.Rules.Combat.Resolver` 结算
    落地生命值 `<= 0` 时会同步调 `SetAlive(id, false)`，见该类型"步骤 8：落地"），
    `World.GetEntity(beastId) == null` 仅保留作防御性判断。
13. `data/_sample/feedback/feedback.binding.json` 的 `feedback.sample_death`（绑定 `unit.died`）
    flash 动作 `params.target` 原先写的是 `"target"`，但 `Presentation.FeedbackBinder.Core.
    FeedbackBinder.OnEvent` 对通用事件的 `selfId`/`targetId` 提取规则是
    `selfId ExtractId(evt, "sourceId","casterId","unitId")` /
    `targetId ExtractId(evt, "targetId")`——`unit.died`（`Core.Rules.Common.UnitDiedEvent`）只有
    `unitId`/`killerId` 两个字段，没有字面量 `targetId`，因此死亡的那个单位按上述规则会被解析成
    `selfId`，`targetId` 恒为空。`DispatchFlash` 对 `target: target` 但 `targetId` 缺失的情况是
    "记一条诊断、直接跳过"（不抛异常，见该方法源码），`floating_text`/`shake_camera` 两个动作因为
    各自的 entityId 解析逻辑都带了"`targetId` 缺失退回 `selfId`"的兜底所以不受影响——这条数据笔误
    因此从未被任何人工/自动化验收发现过（此前也没有任何测试真正验证过"生物死亡触发闪白"，见
    `VerticalSliceTests.FullVerticalSlice_...` 判断记录 12：死亡判断逻辑本身写错，从未真正走到过
    这条断言）。已把该 flash 动作的 `"target": "target"` 改为 `"target": "source"`（纯粹的取值
    笔误修复，不改变表结构/新增内容，`python toolchain/validate_data.py` 复核 0 错误）。

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

# 无人值守冒烟（缺口 3 已解决，见下"独立版无头冒烟"一节；注意不加 -nographics）：
<out>\Shell.exe -batchmode -gf-smoke -logFile <out>\smoke_player.log -screen-width 800 -screen-height 600
```

### 独立版无头冒烟（缺口 3 已解决）

`Runtime/Shell/SmokeRunner.cs`：`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 检测命令行参数
`-gf-smoke`，命中时新建一个 `DontDestroyOnLoad` 的 `SmokeRunner` 组件跑一遍"主菜单 → 新游戏
（`diff.tier` 表 `sort_weight` 最小的一档为默认难度）→ 进入地图 → 向右移动 1 秒 → 普攻一次
（`skill.sample_strike`）→ 存档到 `slot.smoke` → 读档 → 退出"，每步
`Debug.Log("[GF-SMOKE] step=<name> ok")`；成功以 `[GF-SMOKE] RESULT=OK` +
`Application.Quit(0)` 收尾，任一步失败 `[GF-SMOKE] RESULT=FAIL reason=...` +
`Application.Quit(2)`，总耗时超过 60 秒（`RunWatchdog` 协程）`Application.Quit(3)`。命令行未带
`-gf-smoke` 时本类型完全不介入（不新建任何 GameObject），正常游戏/编辑器/既有 PlayMode 测试运行
路径不受影响。

判断记录（为什么直接调用 `Presentation.Shell`/`GameplayAssembly` API，不模拟鼠标点击 UI 按钮，
详见类型顶部注释）：`ShellRoot.cs` 类型顶部已有判断记录指出按钮 `OnClick` 回调本身就是直接调用
`Framework.Presentation.Shell` 的同一批方法，`ShellFlowTests`/`VerticalSliceTests` 也一律走这条
路径而不模拟点击；`SmokeRunner` 沿用同一惯例，是"真实点击"与"程序化驱动"共享的同一条调用路径，
不是另一条需要额外验证是否等价的旁路。

判断记录（为什么不加 `-nographics`，且这次冒烟真的能正常退出）：`SmokeRunner` 不依赖任何鼠标/
键盘物理事件（全走程序化 API 调用），理论上可以带 `-nographics` 跑；但任务书明确要求命令行不加
该参数（允许弹窗），故按原样执行，也因此不再触碰"已知限制"一节记录的"`-nographics` 强制
`NullGfxDevice` 时 `UiRoot`/`InputSystemUIInputModule` 依赖的渲染/输入子系统可能无法正确初始化"
这个问题——那条限制描述的是 U3 阶段"不加脚本化驱动、单纯启动后干等"时 `-nographics` 场景下的
观察，不是本类型的实测结果。另外勘察 `UnityWindow.cs` 确认 `Application.Quit()`
不会被 `Application.wantsToQuit`（`HandleWantsToQuit` 返回 `false` 阻止默认退出）拦截——该钩子
只在 `IWindow.Create` 被调用过之后才会挂上，全仓库勘察确认 Shell 运行时路径从未调用过
`UnityEngineHost.Window.Create`（只有 `UnityWindowTests.cs` 单独测试过该方法），因此本类型可以
直接调用 `Application.Quit(exitCode)` 正常退出进程。

实测记录（U-缺口收敛阶段，Windows 独立版，`adapters/unity/Assets/Editor` 构建入口产出的
`Shell.exe`）：

```
Shell.exe -batchmode -gf-smoke -logFile <out>\smoke_player.log -screen-width 800 -screen-height 600
```

退出码 `0`；日志全部 `[GF-SMOKE]` 行：

```
[GF-SMOKE] step=boot_main_menu ok
[GF-SMOKE] step=show_slots ok
[GF-SMOKE] step=show_new_game_setup ok
[GF-SMOKE] step=new_game_enter_world ok
[GF-SMOKE] step=move_right_1s ok
[GF-SMOKE] step=attack_once ok
[GF-SMOKE] step=save_slot_smoke ok
[GF-SMOKE] step=load_slot_smoke ok
[GF-SMOKE] RESULT=OK
```

进程自行正常退出（未强制终止），日志里唯一的非诊断输出是若干条已知的
`[UnityRenderer2D] 精灵资源未加载或不存在，使用占位方块：...`（异步加载完成前的第一帧占位方块，
既有设计行为，见 `UnityRenderer2D.cs` 判断记录，不影响冒烟结论）。

## 默认动画生命周期

`UnityViewFactory`（`Runtime/Presentation/UnityViewFactory.cs`）对 `CreateView` 命中
`DisplayCategory.Creature` 分类（玩家/NPC/怪物——唯一会收到 `AnimStateMachine`
idle/move/attack/cast/hit/death 状态切换的分类）的调用默认执行 `AttachDefaultAnimation`：给
该 View 的精灵根节点挂一个 `UnityFrameAnimPlayer`（六个状态各注册一条剪辑）+ 全局单例
`AnimClipResolver`（首次挂接时懒构造，跨全部实体共用同一份），使 `Rig.PlayClip` 不需要
`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.GameBootstrap` 任一方手工
接线即可用；`item`/`gobj`/`projectile` 等不触发状态切换的分类不挂（构造函数未传入
`IEventBus`/`IDataRegistryView` 时整体静默跳过，行为等同未接线）。

- **瞬态完成通知**：`player.OnComplete` 回调把"回调触发那一刻状态机记录的当前状态"转发给
  `AnimStateMachine.NotifyTransientStateFinished`，解除 Hit/Attack/Cast 等瞬态状态的优先级锁；
  不接这一步会让角色播完 Hit 后永久卡住、再也进不了 Move/Attack/Cast。
- **复活/销毁回收**：订阅 `entity.destroyed`（`OnEntityDestroyedForAnim`）清空该实体的
  `AnimStateMachine` 记账、播放器引用、剪辑表三项——实体 id 可能被全新实体复用，字典里残留的
  引用会读到已销毁旧组件；订阅 `unit.respawned`（`OnUnitRespawnedForAnim`）只 `Forget` 状态机
  记账，不清播放器/剪辑表（原地复用同一组件，清空反而会让实体此后彻底哑掉），使复活后的角色能
  从 Idle 重新开始而不是卡在死亡姿势。
- **冷启动剪辑升级**：`RegisterDefaultClips` 按 `display.anim_set.<displayId 末段>` 查表；
  声明了 `resource_ref` 但尚未加载进 `UnityResourceLoader` 缓存时（冷启动，非"压根没配"），先
  `RegisterSingleFrameClip` 登记单帧占位保证 `PlayClip` 立即可执行，同时由
  `RequestAnimClipUpgrade` 发起 `LoadAsync`（同一资源多方引用只发起一次），加载完成后把全部
  等待方原地升级为真实多帧剪辑；加载失败只记一条诊断、不重试，与 `VfxPlayer`/`SfxPlayer` 既有
  惯例一致。真的没有声明该状态动画的情形维持原有单帧退化 + 一次性诊断。
- **引导帧驱动**：`GameFoundationBootstrap.OnFrameTick`/`FrameworkResidentHost.OnFrameTick`/
  `games/_template/Runtime/GameBootstrap.cs` 的帧循环现在三处都调用 `AdvanceCharacterRigs`
  （推进每个存活 sprite View 持有的 `ICharacterRig.ProceduralAnim` 时间轴）与
  `Presentation.Vfx.Update(dt)`；此前只有组件本身经单测验证过，没有任何生产帧循环真正驱动它们。

对应测试：`Tests/Runtime/UnityViewFactoryDefaultAnimationTests.cs`
（`CreateView_ForCreatureCategory_MoveCastHit_PlayDistinctDefaultClips`/
`CreateView_ForNonCreatureCategory_DoesNotAttachAnimPlayer`/
`UnitRespawned_SamePlayerInstance_CanTransitionToMoveAgain`）、`Tests/Runtime/AnimationLayerTests.cs`
（`UnityFrameAnimPlayer`/`AnimClipResolver` 组件本身的独立单测）。

## model 型外形（W6-B 收口，ADR-0017）

### 占位模型资产

`Assets/Editor/GeneratePlaceholderModelAssets.cs`（`Adapter.Unity.EditorTools` 命名空间，编辑器
菜单 `GameFoundation/Generate Placeholder Model Assets`，或
`-executeMethod Adapter.Unity.EditorTools.GeneratePlaceholderModelAssets.GenerateAndExit` 批处理）
一次性（可重复运行、覆盖重建）生成：

- `Assets/Resources/GameFoundation/models/placeholder_biped.prefab`：胶囊体本体（`Animator` 直接
  挂在该 GameObject 上）+ 两个子对象——`socket.main_hand`（空挂点）、`slot.head`（球体占位头部，带
  `MeshFilter`/`MeshRenderer`）。**子对象名逐字等于挂点/槽位 `Id` 的 `Value`**（含域前缀，不做
  "去掉前缀"处理）——`UnityRenderer3D.SetSlotMesh`/`AttachToSocket` 按这个精确名字递归查找，具体
  游戏的美术资产接入时子对象命名必须遵循同一约定。
- `Assets/Resources/GameFoundation/models/placeholder_biped.controller`：`AnimatorController`，五个
  状态 `idle`（loop）/`attack`/`cast`/`hit`（H5b 新增，受击回落用）/`test_autoexit`（PR140-03 新增，
  见下），默认状态 `idle`；`test_autoexit` 自带一条 `hasExitTime=true`、`duration=0`（瞬时切换）的
  自动过渡直接回 `idle`，与其余四个状态相互独立，专供 `UnityRenderer3D.Tick` 的"Animator 自动过渡时
  完成事件是否漏发"回归测试直接调用，不经任何 `display.anim_set` 登记，不影响其余状态。
- `Assets/Resources/GameFoundation/anim_clips/{idle,attack,cast,hit,test_autoexit}.anim`：对应
  `AnimationClip`；`attack.anim` 在 50% 时间点内嵌一个 `AnimationEvent`（`functionName="OnAnimEvent"`,
  `stringParameter="hit_frame"`）；`hit.anim` 0.3 秒非循环、不内嵌事件；`test_autoexit.anim` 0.2 秒
  非循环、不内嵌事件。

### 三维放置坐标换算、资源缺失降级、影子（PR130-01/05/08 根治）

第六轮文档—代码深度审计（`architecture/落地计划/audit-5c444f1-20260908/`）发现 `UnityRenderer3D`
的坐标换算与 sprite/相机不在同一个 Unity 世界平面上、资源缺失时抛异常而不是按 ADR-0017 决策 1 降级、
影子随高度一起被抬离地面三项问题，均已根治（见 `UnityRenderer3D.cs` 类型顶部判断记录、
`Tests/Runtime/UnityRenderer3DTests.cs`）：

- **坐标换算（PR130-01）**：`SetPlacement` 的锚点根（`ModelInstance.Root`）现落在
  `(planePos.X, planePos.Y, 0)`——与 `UnityRenderer2D.SetTransform`/`UnityCamera` 共用的既有平面约定
  逐字一致（不再借用 Unity Z 轴表达 `planePos.Y`）；`height` 只平移一个新增的可见内容子物体
  （`ModelInstance.VisualRoot`，取回方法 `GetModelVisualRoot`，与 `UnityRenderer2D` 的
  `LayersRoot`/`GetLayersRoot` 同一结构），不改变锚点根位置。同一份 `planePos`/`height`/`facing`
  下，model 与 sprite 经相机投影得到的屏幕坐标现在严格一致（`scale = 1` 时）。
- **资源缺失降级（PR130-05）**：`CreateModelInstance` 缓存未命中、同步 `Resources.Load` 也找不到时
  不再抛 `InvalidOperationException`——记一次诊断（按 `modelId` 去重）后落地一个占位可见内容（优先
  复用 `model.placeholder_biped`，连它都取不到时兜底一个内建胶囊体），同时发起一次真正的
  `IResourceLoader.LoadAsync(modelId, ResourceKind.Model, ...)`，加载成功后原地把占位内容替换为
  真实预制体（同一句柄不变，已登记的槽位网格/材质参数/最近一次播放的剪辑会在替换后重新应用一遍，
  保持视觉连续）；加载失败则保持占位、记一次诊断、不重试。`IsShowingPlaceholder(handle)` 供测试/
  诊断查询当前是否仍在展示占位内容。
- **影子（PR130-08）**：`ModelInstance.BlobShadow` 挂在锚点根（不随 `height` 位移）下，是上一条
  坐标换算修复的自然结果——height 现在只写入 `VisualRoot` 的局部偏移，不再写进锚点根本身。

### Blob 影子朝向、异步替换状态恢复、Animator 自动过渡完成事件（PR140-01/02/03 根治）

第七方深度审计（`architecture/落地计划/audit-c86bfa9-20260908/`）复核 PR130-01/05/08 落地后确认
model 与 sprite/相机已经共用同一张 Unity 世界 XY 地面平面（上一节），但发现三项新问题：Blob 影子
Quad 仍按上一版 XZ 地面旧约定固定旋转、条件异步模型替换不恢复挂点子模型与投影阴影状态、Animator
自动过渡可能让完成事件永久漏发，均已根治（见 `UnityRenderer3D.cs` 类型顶部与各方法判断记录、
`Tests/Runtime/UnityRenderer3DTests.cs`、`Tests/Runtime/ModelViewTests.cs`）：

- **Blob 影子朝向（PR140-01）**：`SetShadow`/`SetPlacement` 改经共用的 `ApplyBlobShadowTransform`
  把 Blob 的世界旋转钉死为 `Quaternion.identity`（Quad 图元局部法线沿 -Z，恰好正对相机固定的
  `forward=(0,0,1)`，不需要任何旋转），位置只依赖锚点根世界位置外加一个沿世界 Z 轴的微小防
  z-fighting 偏移，且每次 `SetPlacement`（facing 可能已经变化）都重新计算一遍——取代此前
  `localRotation` 固定 `Euler(90,0,0)`（XZ 地面旧约定下的写法，在当前 XY 地面约定下把 Quad 转成了
  侧立的竖直薄片）；额外效果：Blob 不再随角色 facing 旋转偏出地面画面平面（贴地阴影不会因为转身
  跟着立起来）。
- **异步替换状态恢复（PR140-02）**：`AttachVisual` 原地替换视觉内容时，除已有的槽位网格/材质参数/
  最近一次播放剪辑重放外，新增两项：(a) 把仍挂在旧可见内容 socket 挂点下的子模型实例（`AttachToSocket`
  登记进 `ModelInstance.SocketChildren`）先摘出来暂存，避免随旧内容一起被 `Destroy`，新内容就位后
  按原挂点名逐条重新挂回（找不到同名挂点时保持暂存，不销毁、不抛异常）；(b) 按 `ModelInstance.Shadow`
  重新调用 `ApplyShadowCastingMode`，避免新内容的 `Renderer` 悄悄回到 Unity 默认的
  `ShadowCastingMode.On`。
- **Animator 自动过渡完成事件（PR140-03）**：`IsAnimatorStateFinished` 新增 `everEnteredTarget`
  记账（对应 `ModelInstance.CurrentPlayEnteredState`，每次新播放随 `FinishNotified` 一并重置）：
  除"仍稳定停留在目标状态且 `normalizedTime>=1`"这一原有判定外，新增"曾经确认进入过目标状态、现在
  稳定停留在别的状态（不在过渡中）"同样判定完成——覆盖"`AnimatorController` 里配置了自动过渡、且
  过渡的开始与结束都发生在两次检测帧之间"这一此前会永久漏发 `anim_event.finished` 的窗口；判定只看
  `CrossFadeInFixedTime` 实际落地的那一层（`layer=-1` 语义="第一个含有该状态名的层"），不再对全部层
  做 OR 判定，避免多层场景下与目标无关的层把判定搅乱。

### 生产装配根共享同一个 `renderer3D`（PR130-06 根治）

三处生产装配根此前只把 `_host.Renderer3D` 传给了 `UnityViewFactory`（用于创建 model 型 View），构造
`Presentation.Assembly.PresentationAssembly` 时却漏传该参数——`PresentationAssembly` 内部的
`VfxPlayer` 因此始终拿到 `renderer3D: null`，`attach_mode: socket` 的特效即便在选用 model 型外形的
游戏里也固定降级为 world 坐标播放（`modelHandleResolver` 本身不受影响，`PresentationAssembly` 内部
一直有正确构造，只是没有 `renderer3D` 配合就不会被 `VfxPlayer` 使用，见
`presentation/assembly/README.md` 判断记录）。三处装配根（`GameFoundationBootstrap`/
`Adapter.Unity.Shell.FrameworkResidentHost`/`games/_template.GameBootstrap`）现已把同一个
`_host.Renderer3D` 实例同时传给 `UnityViewFactory` 与 `PresentationAssembly` 的 `renderer3D` 构造
参数，与 `hitFrameSource`/`weaponStyleSource` 同一套"三处装配根共享同一份 provider 实例"惯例。

### 命中帧同步接线步骤（ADR-0017 决策 d）

1. 装配根构造一个 `Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource` 实例。
2. 传给 `UnityViewFactory` 构造函数的 `hitFrameSource` 参数——该工厂此后对每个创建的
   sprite/model 型 View（`DisplayCategory.Creature`）自动 `RegisterRig`/`UnregisterRig`（随
   `entity.destroyed` 清理）。
3. 需要真正启用 `anim_keyframe_driven` 策略时，构造**同一个** `Presentation.Render.RenderOptions`
   实例（`HitFrameSync = AnimKeyframeDriven`），分别传给：
   - `UnityViewFactory` 构造函数新增的 `renderOptions` 参数（W6 收口新增——决定
     `ModelCharacterRig`/`SpriteCharacterRig` 构造期实际拿到的 `HitFrameSync` 策略，是否订阅
     `HitFrameReached` 由这一份 `RenderOptions` 说了算，不是随便哪份同值的 `RenderOptions` 都行，
     必须是传给 `UnityViewFactory` 的这同一份）；
   - `Presentation.Assembly.PresentationAssemblyOptions.RenderOptions`（`PresentationAssembly.Render`
     对外报告的只读投影，与上面这份保持同一实例，避免"rig 侧已切到 AnimKeyframeDriven，但
     `PresentationAssembly.Render` 报告默认值"这类装配内部不一致）。
   同时把第 1 步的 `CharacterRigHitFrameSource` 实例传给
   `Presentation.Assembly.PresentationAssembly` 构造函数新增的 `hitFrameSource` 参数（W6 收口新增，
   内部原样转发给 `Presentation.FeedbackBinder.Core.FeedbackBinder` 同名构造参数），并把
   `Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync` 同步切到
   `AnimKeyframeDriven`。
4. 三处引擎侧装配根（`GameFoundationBootstrap`/`Adapter.Unity.Shell.FrameworkResidentHost`/
   `games/_template.GameBootstrap`）均已完成上述 1～3 步的接线，由各自的口味配置项（前两者是
   `[SerializeField] private bool _hitFrameSyncEnabled`，后者是 `GameOptions.HitFrameSyncEnabled`）
   一键切换，默认 `false`（`LogicDriven`，行为与本次收口前完全一致）；端到端链路见
   `Tests/Runtime/HitFrameSyncEndToEndTests.cs`（经真实 `GameFoundationBootstrap` 生产装配根，不是
   手工构造 `FeedbackBinderCore`）。

### 武器风格接线步骤（ADR-0017 决策 e）

1. 提供一个 `Presentation.VfxSfx.Contracts.MainHandWeaponTemplateResolver` 委托（按单位 id 查询其
   当前装备的主手武器物品模板 id，通常包一层
   `EquipmentHost.GetAllEquippedInstances(unitId)[主手槽位id].TemplateId`——"主手槽位具体是哪个
   id"由具体游戏决定，`GameFoundationBootstrap`/`games/_template.GameOptions` 分别用
   `_mainHandSlotId` 字段/`GameOptions.MainHandSlotId` 承载这一口味配置项）。
2. 用该委托构造 `Presentation.VfxSfx.Core.EquipmentWeaponStyleSource`（还需要 `IEventBus` 与
   `IDisplayInfoRegistry`），传给 `UnityViewFactory` 构造函数的 `weaponStyleSource` 参数。
3. `UnityViewFactory` 内部懒解析一次 `display.weapon_style` 全表，与 `weaponStyleSource` 一并
   注入 `AnimClipResolver`——Attack 状态优先用 `WeaponStyleDef.AutoAttackAnim`、Cast 状态按触发
   技能 id 优先用 `WeaponStyleDef.CastAnimOverride`，两者都查不到才退回按状态查的默认剪辑表。
4. 数据侧关联链路：`item.template.display_ref → display.map.logical_id`（该行本身即物品模板
   id）→ `display.map.weapon_style_ref → display.weapon_style` 行——不需要在 `item.template`
   新增任何字段（见 `EquipmentWeaponStyleSource` 类型判断记录）。

**PR130-03 根治（sprite 一侧剪辑登记）**：武器风格/技能覆盖解析出的 clipId（`WeaponStyleDef.
AutoAttackAnim`/`CastAnimOverride`）此前从未随 `UnityViewFactory.RegisterDefaultClips` 的六个默认
状态一起登记进 `UnityFrameAnimPlayer`，`FrameAnimPlayer.Play` 对未登记的 clipId 直接抛
`ArgumentException`（`model` 一侧不受影响——`IRenderer3D.PlayAnim` 按 Animator 状态名现查现用，不需要
预注册）。`AnimClipResolver` 决策出的 clipId 现经 `UnityViewFactory.EnsureSpriteClipRegistered`
在调用 `player.Play` 前保证已登记：已在 `UnityResourceLoader` 缓存里则直接登记真实多帧剪辑；未命中
则先登记单帧占位剪辑（保证立即可播放）并记一次诊断，同时发起一次真正的
`IResourceLoader.LoadAsync(clipId, ResourceKind.Effect, ...)`，加载成功后原地升级为真实多帧剪辑（与
`RequestAnimClipUpgrade` 同一套去重 + 多等待方机制，只是推广到任意 clipId 而不局限于六个默认状态
键）。对应测试：`Tests/Runtime/WeaponClipRegistrationTests.cs`。

### 装备外观（equip visual）接线步骤（PR130-07 根治）

doc-code-matrix 此前记录的能力边界"`UnityViewFactory` 构造函数没有 `equipVisual` 参数……默认 factory
仍缺入口"已补上，接线步骤与命中帧同步/武器风格同一套惯例：

1. 装配根按 `display.equip_visual` 全表构造一张"物品模板 id（`item_id` 字段）→ `EquipVisualDef`"
   目录（`EquipVisualDef.FromRecord` 逐行解析，与 `AnimClipResolver` 的 `weaponStyles` 目录同一套
   "内容表只读、装配期加载一次"惯例）。
2. 用该目录构造 `Presentation.Render.EquipmentVisualSource`（还需要 `IEventBus`）——订阅
   `item.added`/`item.equipped`/`item.unequipped`，按 `item.added` 携带的 `ItemTemplateId` 累积
   "物品实例 id → 模板 id"表，`item.equipped` 时反查上一步的目录得到对应 `EquipVisualDef`，暴露一个
   随事件实时增删的 `VisualByItemInstanceId` 只读字典（同一个对象引用，不是每次访问都重新计算的
   快照）。
3. 把 `EquipmentVisualSource.VisualByItemInstanceId` 传给 `UnityViewFactory` 构造函数新增的
   `equipVisualByItemInstanceId` 参数——该工厂据此构造 `UnityModelView` 时透传给其同名构造参数，未
   提供时 `UnityModelView.OnEvent` 对装备变化事件保持默认空处理（行为与本条修复之前完全一致）。
4. 三处引擎侧装配根（`GameFoundationBootstrap`/`Adapter.Unity.Shell.FrameworkResidentHost`/
   `games/_template.GameBootstrap`）均已完成上述 1～3 步的接线，`EquipmentVisualSource` 与
   `EquipmentWeaponStyleSource` 同批构造/`Dispose`。

同批根治 `UnityModelView` 卸装路径的 socket 残留问题：此前卸装事件处理同时用事件携带的逻辑
`item.slot` 调用 `ClearSlot`（slot_mesh 槽位 id）与 `ClearSocket`（socket_attach 挂点 id）——但装备
生效时用的是 `EquipVisualDef.SocketId`，与逻辑 slot 不是同一个 id 域，`ClearSocket(unequipped.Slot)`
几乎总是查不到对应挂接，子模型实例卸装后残留不清理。`UnityModelView` 现按
`ItemUnequippedEvent.ItemInstanceId` 反查本 View 生命周期内实际应用过的 `EquipVisualDef`
（`_appliedEquipVisualsByItemInstanceId` 可逆索引），按其 `Mode` 精确调用 `ClearSlot`/`ClearSocket`
中的一个，不再两个都盲试；反查不到（存档恢复后的初始装备状态一类场景）时退回按逻辑 slot 尝试
`ClearSlot` 一次（幂等，安全）。对应测试：`Tests/Runtime/EquipVisualSocketClearTests.cs`。

### model 型 View（`Runtime/Presentation/UnityModelView.cs`）

`UnityViewFactory.CreateView` 解析到 `kind=model` 的 `DisplayInfo` 且装配方提供了
`IRenderer3D`（构造函数 `renderer3D` 参数）时创建 `UnityModelView`（直接实现
`IView`/`IHasCharacterRig`/`IModelHandleProvider` 三件套，持有一个
`Presentation.Render.ModelCharacterRig`——presentation/render/core 目前没有对等 sprite 型
`SpriteViewBase` 的 model 型基类可继承，见该类型判断记录）；未提供 `IRenderer3D` 时退化为
`NullView`（同"没有可用 sprite 型 DisplayInfo"一致的宽容处理，不阻断装配）。`AttachDefaultModelAnimation`
（`DisplayCategory.Creature` 专属）解析 `DisplayInfo.Model.AnimSetRef` 指向的 `display.anim_set`
行——与 sprite 路线"按 `display.map` 行 id 最后一段猜测约定 id"不同，model 型 `anim_set_ref` 是
显式字段，直接可用；同时把每条剪辑的 `events`（ADR-0017 决策 c）数据驱动登记进对应
`UnityResourceLoader.TryLoadAnimationClipSync` 取到的资产（`AnimationClip.events` 运行期可写属性，
不是 `UnityEditor.AnimationUtility` 编辑器专属 API），命中帧固定映射到
`ModelCharacterRig.HitFrameEventId`。

判断记录（`RegisterModelClipEvents` 合并 + 按 anim_set 隔离，12 §5 二次勘误，取代上一轮"首个
`resource_ref` 直接合并写在共享资产上、后续不同签名才克隆隔离"的立场——第十轮审核
`architecture/落地计划/audit-8160178-20260908/presentation/presentation-findings.md`「PRES-17-01」，
汇总编号 PRES-170-01：空 `events` 配置提前 `return`、不建立任何基线/隔离，首个非空配置仍然合并写回
共享资产，且基线/签名/覆盖三张表都是 `UnityViewFactory` 实例字段，跨 factory 生命周期不稳定——三者
叠加导致"空配置事后被污染""场景重进后把上一份数据事件当成美术自带基线"两类真实复现）：登记改为
**绝不写回共享 `AnimationClip.events`**。`s_authoredClipEvents`（进程级静态表，键为 `resource_ref`
这一资源身份，与 `UnityViewFactory` 实例生命周期无关）在本进程内第一次触碰某份剪辑资产时捕获它的
原始（美术自带）`events` 作为基线快照并永久缓存；因为此后再也没有任何写入路径，这份快照与共享资产
状态永远一致。空配置（`clipDef.Events.Count == 0`）不再是"提前 return、什么都不做"的特例，而是
"直接播放共享 `baseClip`"——因为共享资产从不被写入，它天生等于 authored 基线，不需要任何覆盖即可
获得正确结果。非空配置一律以基线为底合并 `clipDef.Events`（数据驱动定义中出现的事件名替换掉基线里
的同名旧定义，其余美术自带事件原样保留），结果只写进 `s_modelClipOverrides`（同样是进程级静态表，
键为 `(resource_ref, 事件配置签名)`）缓存的一份运行期 `Object.Instantiate` 克隆剪辑，只经
`UnityRenderer3D.ApplyAnimClipOverride`（`internal` 方法，把该实例 Animator 的
`runtimeAnimatorController` 包一层 `AnimatorOverrideController`，`[原始剪辑] = 覆盖剪辑`）套用到
这一个 `ModelHandle` 对应的实例上——不区分"第一个 vs 后续"签名，任意顺序、任意 factory
生命周期（场景重进、实体销毁重建）下配置同一 `resource_ref` 的多个 anim_set 因此各自只看到自己
配置的事件，互不影响；相同签名的多个实例（含跨 factory）复用同一份缓存的克隆剪辑，不重复
`Instantiate`。

对应测试：`Tests/Runtime/ModelViewTests.cs`（创建/销毁、`PlayAnim(attack)` 经真实 Animator 播放到
50% 触发命中帧、挂点/槽位、height/flash/fade）、`Tests/Runtime/AnimClipResolverTests.cs`
（Attack/Cast 武器风格决策逻辑）、`Tests/Runtime/ModelIntegrationTests.cs`（武器风格端到端播放到
真实 Animator 状态、命中帧同步端到端延迟/超时兜底）、`Tests/Runtime/UnityRenderer3DTests.cs`、
`Tests/Runtime/ConformanceUnityTests.cs`（`Renderer3D`/`ResourceLoader` 场景组，`SupportsRenderer3D`
现为 `true`）、`Tests/Runtime/SlotMeshResourceContractTests.cs`（AUD-05 复现与根治：`mesh_ref` 解析出
真实网格、缺失时保留当前网格并经 `IResourceLoader.LoadAsync` 发起真正的请求、显式卸下/销毁重建的
幂等性）、`Tests/Runtime/ModelClipEventIsolationTests.cs`（同一 `resource_ref` 被一个或多个签名不同
的 anim_set 引用时事件互不影响、美术自带事件保留、共享资产全程不被写入、相同签名复用同一份运行期
克隆剪辑对象）、`Tests/Runtime/Pres170_01SharedClipEventIsolationTests.cs`（PRES-170-01 专项：非空后
空/空后非空/跨 factory 三种触发顺序 + 场景重建 + 实体销毁重建共五组，均以真实 `PlayAnim` 播放期间
经 `IRenderer3D.OnAnimEvent` 实际触发的事件裸名集合为最终断言，取代审核归档探针的"故障现状"断言）。

## 游戏模板如何接入本包（数据目录框架/游戏分层任务）

`games/_template/`（`com.gamefoundation.game-template` 包）是"复制即可起步"的新游戏模板，`Runtime/
GameBootstrap.cs` 按本包同一套装配顺序（`DataRegistry` → `GameplayAssembly` → `PresentationAssembly`
→ `SceneRouter`，与本包 `FrameworkResidentHost.Bootstrap`/`GameFoundationBootstrap.BuildWorld` 同一
惯例）重新组装，但改由 `GameOptions` 参数化，不出现任何硬编码的具体游戏内容——本包（`Adapter.Unity`）
不反向依赖 `games/_template`，两者的耦合方向仍是"游戏层引用引擎适配层"单向，未破坏依赖方向。

数据根：本包自身的 `FrameworkResidentHost`/`GameFoundationBootstrap` 现从两个数据根合并加载
（`data/_framework` + `data/_sample`，见两个类型各自的判断记录），`games/_template/Runtime/
GameBootstrap.cs` 同样两根合并（`data/_framework` + 游戏自己的 `data/<game>`）——`data/_framework`
是分发包随本包一起提供的框架级数据表（`found.event_catalog`/`found.input_action` 等，见仓库根
`data/README.md`"两类目录"一节），新游戏不需要也不应该重新提供这些表。

`Editor/GameSceneBuilder.cs`（模板自己的编辑器工具，不在本包内）复用本包 `Assets/Editor/
{GreyBoxSceneBuilder,ShellSceneBuilder}.cs` 的相机/`EventSystem`/占位地面搭建手法（因这两个类型
是工作台专属脚本、不在任何 asmdef 包里，无法被独立包引用，只能复制适配，见该文件顶部判断记录），
一键生成"Shell + 首张地图"两个场景。详见 `games/_template/README.md`。
