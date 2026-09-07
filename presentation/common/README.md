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
    DirectionSlots.cs        方向档位命名与镜像回退的唯一来源（14 §2.1；P4-2 新增，见下判断记录）
    IView.cs                 View 绑定协议（09 §2）
    IViewFactory.cs          View 工厂（09 §2，由引擎侧实现）
    ViewContext.cs           传给 IViewFactory 的绘制能力集合（IRenderer2D/IRenderer3D?/ICamera/DisplayInfo）
    ISimSnapshot.cs           只读快照门面（铁律 P1）
    EntityKindMapping.cs     Entity.Kind 字符串 → ViewKind 映射：player/creature/gobj/loot/projectile/
                              area_trigger 六类已全部改用 Core.Foundation.SimLoop.EntityKinds 常量，
                              不再有占位裸字符串（见下"判断记录"）
    PresentationEventKeys.cs  presentation.playback_finished key 常量（PlaybackFinishedEvent 事件类
                              已删除，见下"判断记录"去重一条；实际发布用
                              presentation/feedback_binder/contracts/PlaybackFinishedEvent.cs）
  core/
    WorldSimSnapshot.cs       ISimSnapshot 基于 IWorldSim 的只读实现
  tests/
    ...
```

## 表现层铁律（09 第 1 节）与本模块的落实

| 铁律 | 本模块如何满足 |
|---|---|
| P1 只读逻辑状态 | `ISimSnapshot`/`WorldSimSnapshot` 只暴露只读查询方法（`Get*`/`Exists`），不提供任何写入方法；`WorldSimSnapshot` 不缓存字段，每次调用直接查 `IWorldSim.GetEntity` |
| P2 只订阅事件 | 本模块不订阅任何事件（订阅逻辑在 `view_binding`）；只登记 `presentation.playback_finished` 这一个 key 常量（表现层唯一允许发出的事件），事件类本体在 `presentation/feedback_binder`，见下"判断记录"去重一条 |
| P3 不写回 | 本模块不提供任何"提交意图"的接口（意图提交属于 UI 框架，见 09 第 7.2 节，不在本模块范围） |
| P4 只经 L-1 绘制 | `ViewContext` 只打包 `IRenderer2D`/`IRenderer3D?`/`ICamera`（均为 L-1 接口），不新增任何绕过 L-1 的绘制通道 |

## 谁实现 / 谁调用

| 类型 | 谁实现 | 谁调用 |
|---|---|---|
| `IView` | 引擎侧（具体游戏/引擎适配实现，`presentation/render` 提供 `SpriteViewBase` 骨架供 sprite 型继承） | `presentation/view_binding` 的 `ViewBinder` |
| `IViewFactory` | 引擎侧 | `ViewBinder` |
| `ViewContext` | `presentation/view_binding` 或游戏层组装代码构造后传给 `IViewFactory.CreateView` 的实现方使用 | `IViewFactory` 实现内部 |
| `ISimSnapshot`（`WorldSimSnapshot`） | 本模块 | `ViewBinder`、`presentation/camera` 的 `CameraHost` |
| `PresentationEventKeys.PlaybackFinished`（key 常量） | 本模块 | `Presentation.FeedbackBinder.Contracts.PlaybackFinishedEvent`（实际发布用的事件类，见 `feedback_binder/README.md`）间接引用同一字符串值的生成物常量 |

## 判断记录

1. **不新造 `IRenderSurface`**：任务书拍板——View 实现直接持有 `IRenderer2D`/`IRenderer3D`，`common`
   只提供 `ViewContext` 打包传递，避免多一层无谓抽象。
2. **`Direction` 同时携带量化索引与原始弧度**：09 第 3.2 节要求"仅 sprite 型量化，model 型用连续
   朝向"，为避免定义两套朝向类型，单一 `Direction` 结构体两种消费方各取所需字段（见类型注释）。
3. **`ISimSnapshot` 未命中/不存在实体的行为不统一**：`GetPosition`/`GetFacing`/`GetHeight` 返回值
   类型不可空，选择"不存在则抛异常"（调用前应先 `Exists`）；`GetDisplayId`/`GetKind` 返回值本就是
   `Optional`，选择"不存在则返回 null"，与"实体不存在"和"实体存在但映射不出结果"两种情形统一为
   同一个 null（`GetKind` 在 `Entity.Kind` 无法映射时也返回 null，见 `EntityKindMapping`）。
4. **`DirectionSlots` 集中"量化索引 → 档位 id"与"档位 id → 默认镜像来源"两条规则**（P4-2 新增，取代
   `presentation/render` P4-1 自造的罗盘命名）：档位族命名本身取自 14 第 2.1 节固定表，但"量化索引
   0 对应哪个具体档位"仍是本类型的判断记录（游戏镜头朝向问题，架构文档不预先拍板）——依据 05 第
   3.1 节"sortY 越大越靠前，即 +y 朝向观察者"确定 `front` = 角度 90°（+Y 轴），配合
   `Core.Carriers.Unit.DirectionQuantizer` 的既有约定（index 0 = 角度 0 = +X 轴，逆时针编号）反推出
   完整对照表，见该类型 `FromQuantized` 方法注释与 `presentation/render/README.md`"索引→档位对应
   表"一节。
5. **去重：删除 `PlaybackFinishedEvent` 事件类、`IPresentationClock` 契约（09 勘误）**：
   `PresentationEventKeys.cs` 此前额外声明了一个 `PlaybackFinishedEvent` 事件类，与
   `presentation/feedback_binder/contracts/PlaybackFinishedEvent.cs`（`FeedbackBinder` 实际
   `PublishImmediate` 使用的那一个）同名重复，且前者从未被任何生产代码引用——已删除，只保留 key
   常量本身（供测试核对与生成物 `EventKeys.PresentationPlaybackFinished` 字符串值一致，见
   `PresentationEventKeysTests`）。`IPresentationClock.cs` 是一个零实现、零调用点的悬空契约——09
   全文未定义"表现时钟/插值 alpha"这一具体契约名（只泛泛提到"插值仍用于固定步长模拟与渲染帧率
   解耦，见 03"），真正产出 alpha 的 `Core.Foundation.SimLoop.ISimClockHost.Advance` 的返回值目前
   被 `Core.Gameplay.Assembly.GameplayAssembly.Advance` 丢弃、也没有对外暴露的读取点——已删除本
   契约及其在 `ViewBinder.SyncAll` 文档注释里的引用，`alpha` 参数改由调用方自行传入，真正接通
   "alpha 从哪来"留给 W2/W3b，见下"契约缺口"。

## 契约缺口

- **`Entity.Kind` 字符串词汇表已完整登记（G1 + 收边任务 + 加固任务）**：`Core.Foundation.SimLoop.EntityKinds`
  现收齐六个常量（`Player`/`Creature`/`Gobj`/`Loot`/`Projectile`/`AreaTrigger`，加固任务补齐
  `core/gameplay/area_trigger` 落地 `AreaTriggerEntity` 后的最后一个），`EntityKindMapping` 全部改用
  常量引用，不再有裸字符串占位；本条缺口已完全解决，仅 `summon` 一个模块（尚未落地 `Entity` 子类）
  按"未使用的不发明"暂不登记，留待该模块落地后再补。
- **`IRenderer2D.SetTransform` 没有高度参数——已由 ADR-0016 解决**：`SetTransform` 现增加了
  `height` 参数（与 `IRenderer3D.SetPlacement` 对齐），`presentation/render` 的 `SpriteViewBase`
  已改为经这个正式参数传递高度，不再借用 `SetShaderParam` 通道，详见
  `presentation/render/README.md`"契约缺口"一节。
- **表现层插值 alpha 的产出与消费已接通（W2b/W3b 收边，勘误：以下不再是"尚未接通"）**：
  `GameplayAssembly` 新增只读属性 `InterpolationAlpha`（连续模式下等于本次 `Advance` 内部调用
  `ISimClockHost.Advance` 返回的 alpha；离散模式/未装配 `clockHost` 时恒为 `1.0`，见该属性判断
  记录），`Adapter.Unity.Bootstrap.GameFoundationBootstrap`/`Adapter.Unity.Shell.
  FrameworkResidentHost` 两处生产帧循环都已读取 `Gameplay.InterpolationAlpha` 传给
  `ViewBinder.SyncAll(alpha)`/`CameraHost.Update(alpha)`（拍板 9）。`ViewBinder.SyncAll`/
  `CameraHost.Update` 本身接受 alpha 参数这一部分判断记录 5 描述依然成立，不变的只是"产出方
  是否暴露给消费方"这一点。
（原"裸档位名与 `Id` 格式的前缀不一致"契约缺口已由设计层拍板并落地为 14 第 2.1 节 2026-09-05 勘误：
运行期方向档位 Id 固定为 `"dir.<裸档位名>"`，文件名/标注文件/工具链一律用裸档位名，不再是待核对的
契约缺口，见 `DirectionSlots` 类型注释"Id 前缀已拍板结论"。）
