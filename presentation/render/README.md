# L5 表现层 · render（2.5D 渲染约定）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `render` 行（契约接口名
`RenderConventionHost`）、[09_表现层.md](../../architecture/09_表现层.md) 第 3.1～3.4 节：统一
`sortY` 排序、方向量化到方向槽位的镜像回退解析、纸娃娃层合成顺序、影子锚点、高度像素换算，以及
`sprite` 型 `IView` 的引擎无关骨架 `SpriteViewBase`（09 第 4.1 节 CharacterRig 职责的一部分）。

依赖：`Presentation.Common.csproj`。

## 目录

```
render/
  README.md
  contracts/
    IRenderConventionHost.cs
  core/
    RenderLayers.cs            六层的整数层号常量
    RenderOptions.cs             口味配置项（方向档位数、PixelsPerUnit）
    SpriteLayerPlacement.cs      ComposeSpriteLayers 产出的单条层放置信息
    ShadowSpec.cs                 影子锚点 + ShadowMode 数据侧→引擎侧转换
    RenderConventionHost.cs       IRenderConventionHost 默认实现
    SpriteViewBase.cs             sprite 型 IView 骨架
  tests/
    RenderConventionHostTests.cs   14 个用例
    SpriteViewBaseTests.cs         10 个用例
```

## `RenderConventionHost.ResolveDirectionSlot`

```csharp
public (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo)
{
    var canonical = CanonicalDirectionSlotId(direction.Index, direction.DirectionCount);

    for (var i = 0; i < spriteInfo.MirrorPairs.Count; i++)
    {
        var pair = spriteInfo.MirrorPairs[i];
        if (pair.DirectionSlot.Equals(canonical))
        {
            return (pair.MirrorOf, pair.FlipX);
        }
    }

    return (canonical, false);
}
```

`CanonicalDirectionSlotId` 把量化索引换算成罗盘命名（8 档 `dir.e/ne/n/nw/w/sw/s/se`，4 档
`dir.e/n/w/s`，16 档退化为 `dir.slot_<index>`）——这是本模块的判断记录，不是架构拍板，见下方
"契约缺口"。

## 谁实现 / 谁调用

| 类型 | 谁实现 | 谁调用 |
|---|---|---|
| `IRenderConventionHost`（`RenderConventionHost`） | 本模块 | `SpriteViewBase`、后续 `presentation/vfx_sfx`（特效挂点排序）、具体游戏的 View 实现 |
| `SpriteViewBase` | 本模块提供骨架，具体游戏/引擎适配层继承 | `presentation/view_binding` 的 `ViewBinder`（经 `IViewFactory` 产出的具体子类） |

## 判断记录

1. **方向槽位命名约定是本模块的判断记录，不是架构拍板**：见
   `RenderConventionHost.CanonicalDirectionSlotId` 顶部注释——09 只举例、04 测试夹具只给了两个
   `dir.se`/`dir.sw` 样本，没有规定"量化索引 0 对应哪个具体方向"。选用与
   `Core.Carriers.Unit.DirectionQuantizer`（index 0 = 角度 0 = +X 轴）直接对应的罗盘命名，集中在
   一处，具体游戏镜头朝向如与"角度 0 = 东"不一致，只需按此约定反向命名内容或加一层索引重映射。
2. **`SpriteLayerPlacement`/`SpriteViewBase.ResolveLayerResourceId` 不解决"层名 → 引擎资源 Id"的
   完整拼接**：`DisplayInfo.Sprite.PaperdollLayers` 类型是 `IReadOnlyList<string>`，
   `IRenderer2D.SetLayers` 需要 `IReadOnlyList<Id>`，两者之间的資源命名格式架构文档未给出。默认
   `ResolveLayerResourceId` 用 `"layer.<layerName>"` 占位，`protected virtual`，具体游戏按自己的
   资源命名规则重写。
3. **`SpriteViewBase.OnEvent` 默认空实现**：装备变化（`item.equipped`/`item.unequipped`）要更新
   纸娃娃层，需要"已装备物品的 DisplayInfo"（尚待 L3 `item` 模块提供的查询能力，不在 P4-1 范围），
   本类型只提供 `SetPaperdollLayers` 这个底层原语，`OnEvent` 留给具体游戏 View 子类或后续
   `presentation/feedback_binder` 重写实现"收到什么事件时传什么层名列表"这一策略。
4. **高度偏移经 `SetShaderParam` 工作绕**：见 `SpriteViewBase` 类型注释与
   `presentation/common/README.md`"契约缺口"一节。

## 契约缺口

- `IRenderer2D.SetTransform` 缺少高度/像素纵向偏移参数（对比 `IRenderer3D.SetPlacement` 显式携带
  `height`），建议 02 文档评估是否补充。
- `DisplayInfo.Sprite.PaperdollLayers`（`string`）与 `IRenderer2D.SetLayers`（`Id`）之间的资源命名
  拼接格式未拍板，见判断记录 2。
- 方向槽位到罗盘命名的映射是本模块的默认约定，非拍板内容，见判断记录 1。
