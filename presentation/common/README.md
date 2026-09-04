# L5 表现层 · common（跨模块共享契约）

职责：落地 [09_表现层.md](../../architecture/09_表现层.md) 第 1、2 节的表现层铁律与 View 绑定协议，
供 `presentation/view_binding`、`presentation/render`、`presentation/camera` 及后续
`feedback_binder`/`vfx_sfx`/`ui`/`shell` 共用；不含任何具体渲染/绑定实现（那些属于各自的模块）。

依赖：`Core.Gameplay.csproj`（传递引用 `Core.Carriers`/`Core.Rules`/`Core.Numbers`/`Core.Foundation`，
`Core.Foundation` 内含 `engine_adapter`/`sim_loop`/`display_info`/`event_bus`/`common` 等子模块）。

## 目录

```
common/
  README.md
  contracts/
    ViewKind.cs             View 种类枚举（09 §2）
    Direction.cs             量化方向索引 + 档位数 + 原始弧度（09 §2、§3.2）
    IView.cs                 View 绑定协议（09 §2）
    IViewFactory.cs          View 工厂（09 §2，由引擎侧实现）
    ViewContext.cs           传给 IViewFactory 的绘制能力集合（IRenderer2D/IRenderer3D?/ICamera/DisplayInfo）
    IPresentationClock.cs   插值系数来源（03 §3.1、§9）
    ISimSnapshot.cs           只读快照门面（铁律 P1）
    EntityKindMapping.cs     Entity.Kind 字符串 → ViewKind 映射（契约缺口，见下）
    PresentationEventKeys.cs  presentation.playback_finished 常量 + PlaybackFinishedEvent
  core/
    WorldSimSnapshot.cs       ISimSnapshot 基于 IWorldSim 的只读实现
  tests/
    ...
```

## 表现层铁律（09 第 1 节）与本模块的落实

| 铁律 | 本模块如何满足 |
|---|---|
| P1 只读逻辑状态 | `ISimSnapshot`/`WorldSimSnapshot` 只暴露只读查询方法（`Get*`/`Exists`），不提供任何写入方法；`WorldSimSnapshot` 不缓存字段，每次调用直接查 `IWorldSim.GetEntity` |
| P2 只订阅事件 | 本模块不订阅任何事件（订阅逻辑在 `view_binding`），但提供 `PlaybackFinishedEvent`——表现层唯一允许发出的事件，不携带任何逻辑判定数据 |
| P3 不写回 | 本模块不提供任何"提交意图"的接口（意图提交属于 UI 框架，见 09 第 7.2 节，不在本模块范围） |
| P4 只经 L-1 绘制 | `ViewContext` 只打包 `IRenderer2D`/`IRenderer3D?`/`ICamera`（均为 L-1 接口），不新增任何绕过 L-1 的绘制通道 |

## 谁实现 / 谁调用

| 类型 | 谁实现 | 谁调用 |
|---|---|---|
| `IView` | 引擎侧（具体游戏/引擎适配实现，`presentation/render` 提供 `SpriteViewBase` 骨架供 sprite 型继承） | `presentation/view_binding` 的 `ViewBinder` |
| `IViewFactory` | 引擎侧 | `ViewBinder` |
| `ViewContext` | `presentation/view_binding` 或游戏层组装代码构造后传给 `IViewFactory.CreateView` 的实现方使用 | `IViewFactory` 实现内部 |
| `ISimSnapshot`（`WorldSimSnapshot`） | 本模块 | `ViewBinder`、`presentation/camera` 的 `CameraHost` |
| `IPresentationClock` | 主循环组装代码（把 `SimClockHost.Advance` 返回的 alpha 包一层） | `ViewBinder.SyncAll`、`CameraHost.Update` |
| `PlaybackFinishedEvent` | 回放队列实现（09 第 6.4 节，不在 P4-1 范围，本项目当前只启用连续时间模型，暂无发布方） | 主循环的 `PacingPolicy.onPlaybackFinished` 消费方 |

## 判断记录

1. **不新造 `IRenderSurface`**：任务书拍板——View 实现直接持有 `IRenderer2D`/`IRenderer3D`，`common`
   只提供 `ViewContext` 打包传递，避免多一层无谓抽象。
2. **`Direction` 同时携带量化索引与原始弧度**：09 第 3.2 节要求"仅 sprite 型量化，model 型用连续
   朝向"，为避免定义两套朝向类型，单一 `Direction` 结构体两种消费方各取所需字段（见类型注释）。
3. **`ISimSnapshot` 未命中/不存在实体的行为不统一**：`GetPosition`/`GetFacing`/`GetHeight` 返回值
   类型不可空，选择"不存在则抛异常"（调用前应先 `Exists`）；`GetDisplayId`/`GetKind` 返回值本就是
   `Optional`，选择"不存在则返回 null"，与"实体不存在"和"实体存在但映射不出结果"两种情形统一为
   同一个 null（`GetKind` 在 `Entity.Kind` 无法映射时也返回 null，见 `EntityKindMapping`）。

## 契约缺口

- **`Entity.Kind` 字符串词汇表未统一登记**：架构文档只"建议" `ViewKind` 应覆盖的五个分类，未规定
  各 L3/L4 模块 `Entity.Kind` 该写什么字符串。目前代码库只有 `"player"`/`"creature"`/`"loot"` 三个
  已落地取值；`EntityKindMapping` 为尚未落地的 `gobj`/`projectile`/`area_trigger` 三个模块先占位
  `"gobj"`/`"projectile"`/`"area_trigger"`，留待这些模块落地后由设计层核对一致性。
- **`IRenderer2D.SetTransform` 没有高度参数**：09 第 3.4 节要求高度偏移"只平移精灵……的纵向绘制
  位置"，`IRenderer3D.SetPlacement` 有显式 `height` 参数，但 `IRenderer2D.SetTransform` 没有——
  `presentation/render` 的 `SpriteViewBase` 用 `SetShaderParam` 的既有通用参数通道做了一个记录在案
  的工作绕：详见 `presentation/render/README.md`"契约缺口"一节，建议 02 文档评估是否给
  `IRenderer2D` 补一个高度/像素纵向偏移参数。
