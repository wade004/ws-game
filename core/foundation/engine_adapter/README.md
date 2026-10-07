# L-1 引擎适配层 Engine Adapter · engine_adapter 契约

职责：给出 `02_引擎适配层.md` 定义的 13 个中立接口的 C# 签名——只有接口与其参数/返回值用到的
句柄、参数、枚举类型，**没有任何实现**。运行时其余各层（L0~L5）只依赖本目录里的接口类型，
不知道背后是哪种引擎实现；每种引擎各有一套完整实现（不在本目录范围内，见
`adapters/<engine_name>/`），供测试与 CI 使用的最小可用实现见 `adapters/stub/`。

依赖：仅依赖 `core/foundation/common`（`Id`、`Vec2`、`Callback`、`SubscriptionHandle`）与
.NET 标准库；不引用任何其他模块，尤其不引用 `adapters/` 下任何具体实现或 `games/` 下任何内容
（见 `01_分层与依赖.md` 第 3 节依赖矩阵：L-1 不得依赖 L0 以上任何东西，本目录属于 L-1，
仅向下依赖 `common` 这一份最基础的原语类型，不构成对 L0 其余模块的依赖）。

不负责什么：

- 不提供任何接口的实现（无论真实引擎还是桩），实现分别属于 `adapters/<engine_name>/` 与
  `adapters/stub/`。
- 不出现任何具体引擎、语言、框架的符号——**接口签名以 `02_引擎适配层.md` 为准，本目录不得
  出现任何具体引擎符号**。
- 不定义任何游戏规则或数值（技能、战斗、属性等），那些是 L1~L4 的职责。
- 不做数据引用完整性校验，那是 `DataRegistry`（`core/foundation/data_registry`，本阶段未创建）
  的职责。

## 目录

```
engine_adapter/
  README.md
  contracts/   IWindow.cs IClock.cs IRenderer2D.cs IAudio.cs IInput.cs IFileSystem.cs
               IResourceLoader.cs INavigation2D.cs ISpatialQuery.cs IUISurface.cs
               IPlatform.cs IRenderer3D.cs ICamera.cs
  tests/       StubClockTests.cs StubFileSystemTests.cs StubSpatialQueryTests.cs
               （对 adapters/stub 桩实现的验证；本模块自身不含可独立测试的逻辑，
               契约文件只有类型定义，没有行为，因此把"契约是否可用"的测试放在
               对桩实现的验证上，见 11_工程规范与测试.md 第 6 节测试分层）
```

## 13 个接口与文档章节对应表

| 接口 | 契约文件 | 02 文档章节 | 可选性 |
|---|---|---|---|
| `IWindow` | `contracts/IWindow.cs` | 第 1.1 节 | 必需 |
| `IClock` | `contracts/IClock.cs` | 第 1.2 节 | 必需 |
| `IRenderer2D` | `contracts/IRenderer2D.cs` | 第 1.3 节 | 必需 |
| `IAudio` | `contracts/IAudio.cs` | 第 1.4 节 | 必需 |
| `IInput` | `contracts/IInput.cs` | 第 1.5 节 | 必需 |
| `IFileSystem` | `contracts/IFileSystem.cs` | 第 1.6 节 | 必需 |
| `IResourceLoader` | `contracts/IResourceLoader.cs` | 第 1.7 节 | 必需 |
| `INavigation2D` | `contracts/INavigation2D.cs` | 第 1.8 节 | 可选 |
| `ISpatialQuery` | `contracts/ISpatialQuery.cs` | 第 1.9 节 | 必需 |
| `IUISurface` | `contracts/IUISurface.cs` | 第 1.10 节 | 必需 |
| `IPlatform` | `contracts/IPlatform.cs` | 第 1.11 节 | 可选 |
| `IRenderer3D` | `contracts/IRenderer3D.cs` | 第 1.12 节 | 条件必需（仅 model 型外形需要）|
| `ICamera` | `contracts/ICamera.cs` | 第 1.13 节 | 必需 |

接口签名以 `02_引擎适配层.md` 为准，本目录不得出现任何具体引擎符号。

### `IFileSystem.ListFiles` 语义（实现级约定，02 未限定）

`02_引擎适配层.md` 第 1.6 节只给出签名 `listFiles(dirPath: String): List<String>`，未规定返回
值是绝对路径还是相对 `dirPath` 的路径、是否递归子目录。数据目录约定为
`data/<dataset>/<domain>/<table>.json`（见 `data/README.md`），`data_registry`
（`core/foundation/data_registry/core/FileSystemDataSource.cs`，T1-4）需要列举 `<dataset>/`
下全部表文件，因此本仓库拍板以下实现级约定，供全部 `IFileSystem` 实现遵循：

- **递归**：返回 `dirPath` 之下递归全部文件（含子目录中的文件），不含目录条目本身。
- **相对路径**：每个结果路径相对 `dirPath`，不含 `dirPath` 本身的前缀。
- **分隔符**：统一用 `/` 分隔，不用平台相关的 `\`。
- **排序**：按序数（ordinal）排序，保证同一批文件每次调用结果顺序一致。

`adapters/stub/StubFileSystem.cs` 的 `ListFiles` 已按此约定实现（内存文件系统按路径前缀匹配、
截取前缀后的剩余部分即可）；各引擎的真实实现（`adapters/<engine_name>/`）需要同样遵循，
否则 `FileSystemDataSource` 在该引擎上会解析出错误的表名。

## 类型映射摘要

| 中立记法 | C# 类型 | 备注 |
|---|---|---|
| `Bool` | `bool` | |
| `Int` | `int` | |
| `Number` | `double` | 全架构数值统一 double，不与 float 混用 |
| `String` | `string` / `string?` | 依 `Optional<String>` 与否决定是否可空 |
| `Id` | `Core.Foundation.Common.Id` | |
| `Vec2` | `Core.Foundation.Common.Vec2` | |
| `Handle` | 各接口私有的 `readonly struct XxxHandle` | 如 `SpriteHandle`、`ModelHandle`、`SfxHandle`、`ParticleHandle`；`int Value`，不用 `object` |
| `List<T>` | `IReadOnlyList<T>` | 入参出参一致 |
| `Map<K,V>` | `IReadOnlyDictionary<K,V>` | |
| `Optional<T>` | 引用类型 `T?`；值类型 `T?`（`Nullable<T>`）| |
| `Callback` | 具名 `delegate` | 每个回调按参数单独定义，不用裸 `Action`/`Func` |
| 枚举 `a\|b\|c` | C# `enum`，值名 PascalCase | |

## ADR-0110：`INavigation2D` 最近可走点与可达最近点（2026-09-29，消费方反馈第六十批）

`INavigation2D` 新增三个默认接口成员：`TryFindNearestWalkable`/`FindNearestWalkableCandidates`（纯几何：最近可走点及前 N 个
候选）与 `TryFindNearestReachable(mapId, from, point, maxRadius, out reachable)`（与 `from` 连通的最近点，返回点必可
`FindPath`）。排序（写死，三者共用）：主键 = 到点击点距离按格宽量化，次键 = 到偏好点距离（可达查询固定取 `from`），再按坐标
字典序；点本身满足条件时原样返回；返回格心。取舍：

- **格几何与排序只有一份，且公开**：`contracts/NearestWalkableSearch.cs` 内 `NavGridLayout`（网格布局公式：阻挡包围盒外扩
  2.0、缺省格宽 0.25、单轴 ≤192 格自适应放大）与 `NearestWalkableSearch`（`CollectOnGrid` 按格精确、`CollectSampled` 只靠
  `IsWalkable` 的同心环采样）。公开是因为接口注释要求网格实现覆盖候选枚举并调用 `CollectOnGrid`（第三方网格实现同样要用）；
  对外契约成员见 ADR-0110 决策 4，比较器/候选结构/夹取/`ReachableProbeLimit` 是私有或内部细节。没收成内部类型加友元程序集：
  核心程序集得逐个登记下游程序集名，第三方无从使用。`StubNavigation2D` 按同一布局虚拟格子、`UnityNavigation2D` 用自己的 A*
  网格（构建也改经 `NavGridLayout.Compute`），同一张阻挡图上结果一致。`CollectOnGrid` 的"格子满足条件"谓词在 `maxCount=1`
  时只在候选能刷新当前最优时才调用（谓词可以是一次寻路）。
- **默认接口实现是近似**：候选枚举用环采样（步长 0.25、每环 `max(8, ceil(2πr/0.25))` 个方向、至多 1024 环），可达查询取前 64 个
  候选逐个 `FindPath`；前 64 个都不可达就返回 false。给不知道网格/连通结构的第三方实现者。锁在
  `tests/NearestWalkableDefaultImplTests.cs`（含"前 64 个候选都在隔间里"的耗尽用例，以及桩的精确结果对照）。
- **不夹取网格范围外的点**：导航对网格范围外的点判可走（只有登记的阻挡矩形不可走），点本身满足条件即原样返回；夹取会让开阔图上
  的远点击被截断。
- 一致性场景 `Navigation2DScenarios` 的"最近可走点"与"可达最近点"两个场景由测试桩（核心侧 `ConformanceStubTests`）与 Unity
  实现（引擎侧 `ConformanceUnityTests`）共用同一组输入与期望算法。

## `INavigation2D.RaycastWithNormal`：带碰撞法线的射线查询（2026-10-02，手感落地 S2b）

新增默认接口成员 `RaycastWithNormal(mapId, from, to)`，返回 `NavRayHit{Point, Normal}?`（`contracts/NavRayHit.cs`）。供运动层 `wall_slide` 沿墙切向滑动用
（手感设计/02 第 3 节）；`INavigation2D` 是本仓库自己的契约，按加法补，不是上游缺口。取舍：

- **同一次判定、命中点逐位相同**：`Point` 与 `Raycast` 返回值是同一个点，内置实现让 `Raycast` 直接取 `RaycastWithNormal(...)?.Point`，不并存两份相交判定。
  可通行规则不变（仅边界/角点相切不算命中）。
- **法线 = 被穿入表面的单位外法线**（指向自由空间一侧）。角点（射线恰好穿过两个面的交线，或两块矩形在同一点同时被命中的内角）取两个外法线之和归一化；起点已在
  阻挡内部或实现不能确定时为零向量，调用方视为"无法滑动，整体停下"，不得猜方向。轴向法线分量恰为 ±1/0（不经归一化运算），保证轴对齐阻挡下滑墙与逐轴处理逐位一致。
- **几何只有一份**：`NavRaycastNormals.RectEntryNormal`（按 slab 穿入参数判断被穿入的面）与 `Merge`（同点多面合并）由测试桩与 Unity 实现共用。
- **默认实现是近似**：命中点取 `Raycast`，法线用 `NavRaycastNormals.ProbeAxisNormal`（沿射线回退一小段后在 x、y 轴各向前探，恰有一轴被挡即该轴为法向；
  内角、擦角、斜面给零向量）。给只会 `Raycast` 的第三方实现源码兼容；能给精确法线的实现应覆盖。
- 一致性场景 `Navigation2DScenarios` 新增 `RaycastWithNormal_...`（命中点与 `Raycast` 逐位相同、四个面的法线、贴边不命中），测试桩（核心侧 `ConformanceStubTests`）与
  Unity 实现（引擎侧 `ConformanceUnityTests`）共用；`tests/RaycastWithNormalTests.cs` 覆盖桩的细节（随机线段命中点逐位对照、角点对角法线、内角合并与登记顺序无关、起点在内部、
  默认实现的边界）。


## `ShapeGeometry.RebaseAt` 与 `ShapeGeometry.ClosestPoint`（2026-10-02，手感落地 S3b）

- `RebaseAt(Shape template, Vec2 origin, double facing)`：把"形状模板"（目标选择链 `shape` 的存储形态，`Origin` 为零、方向/旋转为 0）按位置与朝向重新锚定成可直接查询的形状；锚定规则与 `TargetHost` 解析链时一致
  （circle 取位置；cone/line 方向取朝向；rect 旋转取朝向）。时间线 `continuous` 命中沿攻击方位姿移动形状时用。
- `ClosestPoint(Shape shape, Vec2 point)`：形状区域内离 `point` 最近的点（点在形状内返回其自身；circle/rect/line/cone 四种）。时间线命中给接触点用；契约里取不到目标碰撞半径，目标按中心点处理。
- 复现/不变量：`tests/ShapeGeometryPoseTests.cs`（重新锚定后的查询等于模板在局部坐标里的查询；`ClosestPoint` 在形状内、形状内点映射到自身、不存在更近的形状内采样点）。

## `INavigation2D` 增量阻挡：`AddBlocking` / `RemoveBlocking` / `GetBlocking`（2026-10-02，手感落地 M4-L）

1. **契约**（接口默认成员，ABI 只加不改，既有实现源码兼容）：`void AddBlocking(Id mapId, Rect rect)`、`bool RemoveBlocking(Id mapId, Rect rect)`、`IReadOnlyList<Rect>? GetBlocking(Id mapId)`。`AddBlocking` 追加一块动态阻挡；`RemoveBlocking` 按矩形值（`Rect.Equals`）移除登记顺序里**第一份**匹配项，返回是否真移除了（多重集合：重复登记同一矩形各算一份）；`GetBlocking` 返回当前登记快照，`null` 表示实现不暴露（默认）。
2. **版本语义不变**：每次有效的增/删让 `GetBlockingVersion` **恰好**递增一次（等价于一次 `SetBlocking`）；移除不存在的矩形返回 `false` 且版本不变；`SetBlocking`/`Clear`/`BuildNavMesh` 语义不动。
3. **默认实现**：经 `GetBlocking` 取当前集合，追加/移除后整批 `SetBlocking` 替换；实现若不暴露 `GetBlocking`（返回 `null`），默认实现抛 `NotSupportedException`——不静默退化成"只剩这一块"，那会悄悄丢掉其余阻挡。需要真增量的实现覆盖三个成员。
4. **实现**：`StubNavigation2D` 与 `UnityNavigation2D`（同时重置该地图网格缓存）覆盖为真增量。测试：`tests/StubNavigation2DTests.cs`（追加/移除只动一块并恰好改一次版本、多重集合、等价于整批替换、默认成员的整批退化与不暴露时抛错）；一致性场景 `adapters/conformance` 新增一条（Unity PlayMode 才跑）。首个消费者：实验室可破坏障碍（`lab/README.md` 判断记录 42）。

## `ITerrainHeight2D`：地面与天花板高度（2026-10-03，M4-V，ADR-0130 追加决定）

新增接口 `ITerrainHeight2D { GetGroundHeight(mapId, point); GetCeilingHeight(mapId, point) }`（后者默认 +inf）与缺省实现 `FlatTerrainHeight2D.Instance`（地面 0、没有天花板）。核心层用它做落地高度、天花板夹取、斜坡贴地与台阶阻挡（`VerticalAxisOptions.Terrain`，缺省 null）。实现：核心层数据版 `MapTerrainHeights`（读 `world.map.terrain`，无头宿主/实验室用）、Unity 物理射线版 `UnityTerrainHeight2D`（可选启用）。接口是只读纯查询，同一输入同一输出。

## 地形感知寻路与 `ITerrainStepConstraint`（2026-10-03，M4-W1a，ADR-0130 追加决定）

`INavigation2D` 追加两个**默认接口成员**（只加不改，既有实现无需改动、二进制兼容）：`FindPath(mapId, from, to, ITerrainStepConstraint? constraint)` 与 `TryFindNearestReachable(mapId, from, point, maxRadius, constraint, out reachable)`（参数同旧版加约束）。`ITerrainStepConstraint.FirstStepBlock(mapId, from, to)` 返回贴地行走者沿 from→to 第一个被台阶挡住的点（没有返回 null；**有向**：上台阶被挡不等于下台阶被挡）；实现者是核心层 `VerticalMotionHost`。默认实现：对旧 `FindPath` 的结果逐段验证约束，有一段被挡就返回 `null`（"没有网格的第三方实现不假装能绕行，也不静默忽略地形"）；`constraint == null` 时与旧重载完全一致。有网格的实现（测试桩 `StubNavigation2D`、Unity `UnityNavigation2D`、核心层 `OpenFieldNavigation`）覆盖它们，共用同一份规划器 `TerrainStepPathPlanner`（八邻接 A*，被台阶挡住的有向边不可通行、不切角、开放表按 `(f, 节点序号)` 排序、直线通畅时直接 `[from, to]`、视线剪枝，端点所在格心被阻挡盖住时用 8 邻格里与端点直连不受阻的格心做多源接合；绕行搜索窗口 = 两端点包围盒外扩 `max(4, 距离/2)` 加网格边距 2，超窗判无路，规划器不知道地形的全局范围，详见 `core/carriers/unit/README.md` 的判断记录）。台阶规则本身（滑窗 + 二分到 1e-10）在 `TerrainStepMath`，移动阻挡、寻路与路径校验共用。测试：`tests/TerrainAwareNavigationTests.cs`。

## `IInput.ConsumeScrollNotches`：滚轮格数（2026-10-08，样板游戏 C 缺口，[ADR-0165](../../../architecture/adr/0165-样板游戏C缺口-相机地形避让滚轮输入与空间索引过期条目.md)）

`IInput` 追加**默认接口成员** `double ConsumeScrollNotches()`（缺省 0；只加不改，既有实现无需改动、二进制兼容）：取走并清零自上次调用以来累积的滚轮格数，一格 = 1.0，向前（远离使用者）为正；实现负责把硬件单位换算成格数。滚轮是连续累积量，不进 `PollEvents` 的离散事件列表（精细滚动设备给小数，进事件列表会丢精度或刷屏）。`UnityInput` 绑定 `<Mouse>/scroll`（绝对值达 20 视为像素单位，按 120 一格换算），`StubInput.ScrollWheel` 供测试。已知限制：只有垂直方向，不区分设备。

## 可选能力接口 `ICameraOrientation`（M4-W4，2026-10-03）

`core/foundation/engine_adapter/contracts/ICameraOrientation.cs`：相机朝向查询，只有 `double YawRadians`（相机在世界平面上的偏航，逆时针为正，0 = 屏幕上方是世界 +Y；右轴 (cos, sin)、上轴 (−sin, cos)）。独立成可选接口（探测写法 `camera is ICameraOrientation`），不给必选的 `ICamera` 加成员，旧相机实现与第三方实现不受影响；用途是输入映射的相机相对控制空间（`core/foundation/input_map/README.md` M4-W4 一节）。`StubCamera`（`YawDegrees` 换算）与 `UnityCamera` 实现它。

## 可选能力接口 `ICameraZoomPunch` 与 `IRumble`（手感落地 M5-S5，2026-10-04，[ADR-0148](../../../architecture/adr/0148-镜头与音画反馈的合成上限玩家强度脚步材质与动画表现标记.md)）

两个新的可选能力，探测写法与 `ICameraImpulse` 相同（`camera is ICameraZoomPunch z && z.SupportsCameraZoomPunch`、`IRumble.SupportsRumble`），不给必选接口加成员，旧实现与第三方实现不受影响。`ICameraZoomPunch.ZoomPunch(magnitude, decayMs)`：可视范围瞬间收窄 `magnitude`（比例）再线性回落，多次叠加合计收窄不超过 0.5；`IRumble.Rumble(strength, durationMs)`：0..1 强度，后到的取强度较大者、时长取剩余较长者。`UnityCamera` 实现缩放脉冲（恒声明支持）；`UnityRumble` 经 Input System 手柄马达输出（低频全强度、高频 0.6 倍），可注入马达输出以便无设备测试，`UnityEngineHost` 逐帧推进并在退出时归零。
