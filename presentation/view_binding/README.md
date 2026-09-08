# L5 表现层 · view_binding（View 绑定协议）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `view_binding` 行
（契约接口名 `ViewBinder`）、[03_运行时骨架.md](../../architecture/03_运行时骨架.md) 第 5、9 节、
[09_表现层.md](../../architecture/09_表现层.md) 第 2 节：订阅 `entity.created`/`entity.destroyed`
创建/销毁 View、维护"实体 id → View"绑定表、驱动位置插值、把相关事件转发给对应 View；额外订阅
`save.loaded`（PRES-180 根治），在读档完成后按 `ISimSnapshot` 当前状态做一次全量对账，补上读档期间
被 `IEventBus.SuppressDispatch` 抑制、从未送达的 `entity.created`/`entity.destroyed`（见判断记录 5）。

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
    PRES180_SaveLoadViewReconciliationTests.cs  4 个用例（save.loaded 对账回归，见判断记录 5）
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
5. **PRES-180 根治：`save.loaded` 触发全量对账，不是"再订阅一遍 `entity.created`/`entity.destroyed`"**
   （见 architecture/落地计划/audit-e070e3f-20260908/presentation/presentation-findings.md"存档
   抑制与掉落物 View 候选"）：`SaveSystem.Load` 把逐段 `Load` 包在 `IEventBus.SuppressDispatch`
   作用域内，作用域内经 `Enqueue`/`PublishImmediate` 提交的事件被直接丢弃、不会补发——本模块原本
   只在构造期订阅两个事件的做法，遇到某段 `Load` 期间往 `IWorldSim` 加/删实体（如同图读档恢复地面
   掉落物，见 `Core.Gameplay.Loot.DroppedLootPersistable.Load`/`LootHost.RestoreDropped`）就会漏掉
   这批变化。`SaveSystem.Load` 在该抑制作用域<b>外</b>正常派发 `SaveLoadedEvent`
   （`Core.Foundation.SaveSystem.SaveEventKeys.SaveLoaded`），本模块订阅它后做一次以
   `ISimSnapshot.GetAllEntityIds`/`Exists` 为准的对账：缺 View 的按与 `OnEntityCreated` 完全一致的
   规则补建（复用同一方法，跳过 `AreaTrigger`、记录未映射分类、幂等去重同一套逻辑），已绑定但对应
   实体已不存在的按 `OnEntityDestroyed` 补销毁——两个方向合起来同时覆盖"`entity.created` 被丢弃"与
   "`entity.destroyed` 被丢弃"两类残留，同步完成，不依赖任何一次后续 `sim.tick_finished` 补发。
   为此在 `ISimSnapshot` 新增两个只读成员：`GetAllEntityIds()`（当前存活实体 id 列表）与
   `GetRawKind(Id)`（映射前的原始 `Entity.Kind` 字符串，供复用 `OnEntityCreated` 的映射/跳过/诊断
   规则），`presentation/common/core/WorldSimSnapshot.cs` 按 `IWorldSim.QueryEntities(default)`
   实现，不引入除现有 `IWorldSim` 之外的新依赖。回归见 `tests/PRES180_SaveLoadViewReconciliationTests.cs`
   （真实 `SaveSystem`/`DroppedLootPersistable`/`LootHost`/`WorldSim`/`ViewBinder`，`IViewFactory`
   用记录型 stub），含跨图读档不回归的用例（真实 `GameplayAssembly` + `SceneRouter`）。

## 契约缺口

- 见 `presentation/common/README.md`"契约缺口"一节（`Entity.Kind` 词汇表未统一、
  `IRenderer2D.SetTransform` 无高度参数）；`ViewBinder` 依赖前者做 kind 映射，依赖 `presentation/render`
  的工作绕方案间接受后者影响。
