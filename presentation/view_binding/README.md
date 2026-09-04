# L5 表现层 · view_binding（View 绑定协议）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `view_binding` 行
（契约接口名 `ViewBinder`）、[03_运行时骨架.md](../../architecture/03_运行时骨架.md) 第 5、9 节、
[09_表现层.md](../../architecture/09_表现层.md) 第 2 节：订阅 `entity.created`/`entity.destroyed`
创建/销毁 View、维护"实体 id → View"绑定表、驱动位置插值、把相关事件转发给对应 View。

依赖：`Presentation.Common.csproj`（同解决方案下引用 `Core.Gameplay` 传递链）。

## 目录

```
view_binding/
  README.md
  contracts/
    IViewBinder.cs           03 §9 ViewBinder 接口的最小签名（OnEntityCreated/OnEntityDestroyed/GetInterpolatedPosition）
    ViewBinderOptions.cs      转发事件键集合 + 默认方向档位数
  core/
    ViewBinder.cs             IViewBinder 默认实现
  tests/
    ViewBindingTestSupport.cs  FakeView/FakeViewFactory/FakeDisplayInfoRegistry/TestEntity
    ViewBinderTests.cs         18 个用例
```

## `ViewBinder.SyncAll`

```csharp
public void SyncAll(double alpha)
{
    foreach (var pair in _views)
    {
        var entityId = pair.Key;
        var view = pair.Value;

        var pos = GetInterpolatedPosition(entityId, alpha);
        var facing = _snapshot.Exists(entityId) ? _snapshot.GetFacing(entityId) : 0.0;
        var height = _snapshot.Exists(entityId) ? _snapshot.GetHeight(entityId) : 0.0;
        var direction = ResolveDirection(facing, entityId);

        view.SyncPose(pos, direction, height);
    }
}
```

其中 `GetInterpolatedPosition` 用 `prev + (curr - prev) × alpha`；`prev`/`curr` 两份快照只在
`sim.tick_finished` 时更新一次（见 `OnTickFinished`），高度直接读当前 `ISimSnapshot.GetHeight`
（不插值，见任务书拍板"高度取 HeightOffset"）。

## 谁实现 / 谁调用

| 类型/成员 | 谁实现 | 谁调用 |
|---|---|---|
| `IViewBinder`（`ViewBinder`） | 本模块 | 主循环组装代码（持有具体 `ViewBinder` 以调用 `SyncAll`）；`IEventBus` 自动回调 `OnEntityCreated`/`OnEntityDestroyed`/转发方法 |
| `IViewFactory` | 引擎侧（游戏/引擎适配层组装代码） | `ViewBinder` 内部 |
| `ISimSnapshot`（`WorldSimSnapshot`） | `presentation/common` | `ViewBinder` |
| `IDisplayInfoRegistry` | `core/foundation/display_info` | `ViewBinder`（解析方向量化档位） |

## 判断记录

1. **位置插值不依赖 `unit.moved` 事件**：`ViewBinder` 只在每次 `sim.tick_finished` 读一次
   `ISimSnapshot.GetPosition`，与是否发过 `unit.moved` 无关——`WorldSim` 只在 `Tick` 内部改变
   实体状态，两次 tick 之间状态稳定，这样比"订阅 `unit.moved` 累积位置"更简单、也不遗漏"实体被
   非 `unit.moved` 路径移动"（例如传送）的情形。
2. **转发键集合默认排除 `unit.moved`/`entity.created`/`entity.destroyed`/`sim.*`**：前者已被
   "位置插值"专门处理，不需要再走通用 `OnEvent` 转发；后两组由 `ViewBinder` 内部专门订阅处理，
   不走通用转发路径（否则会出现"创建事件转发给了尚未创建完成的 View"之类的时序怪问题）。
3. **`SyncAll`/`Count`/`TryGetView` 不在 `IViewBinder` 接口上**：03 第 9 节给出的 `ViewBinder`
   接口签名只有三个方法，本模块把 `IViewBinder` 严格对齐文档最小契约，三个补充成员只作为具体类
   `ViewBinder` 的公开成员，供持有具体类型的组装代码调用。
4. **事件转发候选字段用固定六选一，不做"事件类型 → 应转发字段"的可配置表**：任务书拍板明确列出
   `unitId`/`targetId`/`sourceId`/`entityId`/`casterId`/`gobjInstanceId` 六个候选名，用
   `IExprReadableEvent.TryGetField` 统一尝试，命中即转发；同一事件可以命中多个候选字段并转发给
   多个不同 View（如 `combat.damage_dealt` 的 `sourceId`/`targetId`），同一 View 不会重复收到同一
   事件两次。

## 契约缺口

- 见 `presentation/common/README.md`"契约缺口"一节（`Entity.Kind` 词汇表未统一、
  `IRenderer2D.SetTransform` 无高度参数）；`ViewBinder` 依赖前者做 kind 映射，依赖 `presentation/render`
  的工作绕方案间接受后者影响。
