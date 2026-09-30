# Adapters.Stub 桩适配层（无头适配层）

说明：本项目原为"专供各层测试项目使用、不对外发布"的引擎适配层（L-1）桩实现；[ADR-0018](../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md) 决策第 3 条起，本程序集同时列为框架正式交付物之一——对外以"无头适配层"称呼，随 `build.ps1 -Dist`/`-Release` 进入 zip 快照（`dist/<ver>/adapters/headless/Adapters.Stub.dll`）与私服第四个包 `com.gamefoundation.adapter.headless`，供测试/CI 与内容编辑器等无头宿主（不接引擎、不需要渲染/输入）使用。

判断记录（程序集名不改）：对外称呼改为"无头适配层"，但程序集名仍是 `Adapters.Stub`（改名会牵动全部依赖它的测试工程 `ProjectReference`），代码内类型名与本目录路径均不变；`11_工程规范与测试.md` 第 6 节措辞同步勘误为"同时是框架交付物（ADR-0018）"，不再是"专供测试与 CI"。

本阶段实现了 `core/foundation/engine_adapter/contracts` 定义的全部 13 个接口的最小可用桩：
确定性、无引擎依赖、无线程、不读取任何系统挂钟时间、不调用任何系统伪随机数生成器、
不使用任何基于线程池的异步任务库。

依赖：仅引用 `core/foundation/Core.Foundation.csproj`（含 `common` 与 `engine_adapter` 两个
子模块）。

## 桩清单

| 接口 | 桩类型 | 关键行为 |
|---|---|---|
| `IWindow` | `StubWindow` | 内存状态记录；`RequestCloseForTest()` 触发 `OnCloseRequested` |
| `IClock` | `StubClock` | 手动时钟，`Now()` 从 0 开始；`Advance(seconds)` 推进并触发固定步/帧回调 |
| `IRenderer2D` | `StubRenderer2D` | 句柄自增分配；记录已创建句柄与最近的分层/变换/着色器参数；销毁后复用抛异常 |
| `IAudio` | `StubAudio` | 记录当前播放的音乐、存活的音效播放、各总线音量；`IsSfxPlaying`（ADR-0105）按"未停止、未被 `CompleteSfx` 标记自然播完"回报，`ReportsPlaybackState=false` 时恒回报 null（模拟不支持回报的后端） |
| `IInput` | `StubInput` | 可编程输入；`Press`/`Release`/`SetAxis`/`MoveMouse` 供测试驱动 |
| `IFileSystem` | `StubFileSystem` | 内存文件系统；`FailNextWrite()` 模拟原子写入失败且旧内容不变 |
| `IResourceLoader` | `StubResourceLoader` | 同步"异步"；`Register(id)` 登记的资源立即加载成功，未登记的立即失败 |
| `INavigation2D` | `StubNavigation2D` | 直线导航（路径=起点终点两点）；`SetBlocking`/`Clear` 登记按地图分组的阻挡矩形；`GetBlockingVersion` 按地图独立计数（`BuildNavMesh`/`SetBlocking`/`Clear` 均递增）；ADR-0110 最近可走点：按与网格实现同一份 `NavGridLayout` 虚拟格子，`NearestWalkableSearch.CollectOnGrid` 排序（两个实现结果一致）；可达最近点 `TryFindNearestReachable`：桩的寻路只有直线，"连通"= `FindPath(from, 候选) != null`（两端可走且线段不受阻），依赖 `from` 的位置、不是等价类关系，没有可缓存的分量标号，逐候选直接用与 `FindPath` 同一个判定（谓词只在候选刷新当前最优时才调用）；桩没有绕障，需要绕墙才能到的点在桩上算不可达，比网格实现保守 |
| `ISpatialQuery` | `StubSpatialQuery` | 对 `Register(id, position, radius, tags)` 登记的对象做暴力遍历查询，结果按 `Id` 排序 |
| `IUISurface` | `StubUISurface` | 记录已创建 surface、布局、DrawText 调用、当前焦点元素 |
| `IPlatform` | `StubPlatform` | 崩溃日志写入内存列表 `CrashLog`；剪贴板为内存字符串；语言可用 `SetLanguage` 设置 |
| `IRenderer3D` | `StubRenderer3D` | 句柄自增分配；记录放置/动画/槽位换装/挂点；`FireAnimEventForTest` 模拟关键帧事件 |
| `ICamera` | `StubCamera` | 记录 Configure/Follow/Zoom/Shake 的最近参数 |

## 测试用法

```csharp
using Adapters.Stub;

var engine = new StubEngine(); // 一次性构造全部 13 个桩实例
engine.Clock.Advance(1.0 / 60.0);
engine.FileSystem.WriteTextAtomic("save/slot1.json", "{}");
```

被测对象按需注入 `engine.Clock`、`engine.FileSystem` 等具体桩实例（各桩类型上还暴露了
接口之外的测试专用方法，如 `StubClock.Advance`、`StubInput.Press`，直接使用具体类型即可
调用）。

## 判断记录（`StubSpatialQuery.QueryShape(Line)` 改为矩形带口径，2026-10-01，行为收紧，[ADR-0125](../../architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 第三批探针缺陷修复）

此前按"点到线段距离 <= width/2 + r"判定（胶囊，端点处是圆头），与 05 §3.5 及 `ShapeGeometry.Contains` 的矩形带不一致：
零半径实体落在端面外 <= width/2 处被误命中，带半径实体在端面外 gap > r 处也被误命中。现为：实体中心落在带内（直接复用
`ShapeGeometry.Contains`，闭区间）即命中；带半径实体另按"圆与矩形带相交"（局部坐标下圆心到带的最近点距离 <= r，同本桩
`Rect` 口径）判定。跨实现一致性用例 `T_H12_ShapeGeometryBoundaryConsistencyTests.QueryShape_ForPointEntities_*`
新增 `line` 一支并改为每个点对着自己的形状比对；端面用例 `ADR0125_StubSpatialQueryLineBandTests`。
