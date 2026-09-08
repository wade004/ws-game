# Adapters.Stub 桩适配层

说明：本项目是专供各层测试项目使用的引擎适配层（L-1）桩实现，不对外发布（见
`11_工程规范与测试.md` 第 6 节"桩适配层作为 `adapters/stub/` 长期维护，专供测试与 CI 使用"）。
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
| `IAudio` | `StubAudio` | 记录当前播放的音乐、存活的音效播放、各总线音量 |
| `IInput` | `StubInput` | 可编程输入；`Press`/`Release`/`SetAxis`/`MoveMouse` 供测试驱动 |
| `IFileSystem` | `StubFileSystem` | 内存文件系统；`FailNextWrite()` 模拟原子写入失败且旧内容不变 |
| `IResourceLoader` | `StubResourceLoader` | 同步"异步"；`Register(id)` 登记的资源立即加载成功，未登记的立即失败 |
| `INavigation2D` | `StubNavigation2D` | 直线导航（路径=起点终点两点）；`SetBlocking`/`Clear` 登记按地图分组的阻挡矩形；`GetBlockingVersion` 按地图独立计数（`BuildNavMesh`/`SetBlocking`/`Clear` 均递增） |
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
