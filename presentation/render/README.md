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
    RenderConventionHostTests.cs   12 个用例
    SpriteViewBaseTests.cs         10 个用例
```

方向档位命名/镜像回退的量化索引对照表现在唯一来源于 `presentation/common/contracts/DirectionSlots.cs`
（见 `presentation/common/README.md`），对应测试 `presentation/common/tests/DirectionSlotsTests.cs`
（42 个用例，含 4/8/16 三档的完整索引→档位对照）。

## `RenderConventionHost.ResolveDirectionSlot`

```csharp
public (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo)
{
    var canonical = DirectionSlots.FromQuantized(direction.Index, direction.DirectionCount);

    for (var i = 0; i < spriteInfo.MirrorPairs.Count; i++)
    {
        var pair = spriteInfo.MirrorPairs[i];
        if (pair.DirectionSlot.Equals(canonical))
        {
            return (pair.MirrorOf, pair.FlipX);
        }
    }

    var defaultMirror = DirectionSlots.MirrorSourceOf(canonical);
    if (defaultMirror.HasValue)
    {
        return (defaultMirror.Value.MirrorOf, defaultMirror.Value.FlipX);
    }

    return (canonical, false);
}
```

`Presentation.Common.DirectionSlots.FromQuantized`（P4-2 恢复，取代 P4-1 自造的罗盘命名
`"dir."` 前缀 + e/ne/n/nw/w/sw/s/se）把量化索引换算成
[14_资产规格书模板.md](../../architecture/14_资产规格书模板.md) 第 2.1 节固定的档位族命名：8 方向
`front`/`front_side_r`/`front_side_l`/`side_r`/`side_l`/`back_side_r`/`back_side_l`/`back`，4 方向
`front`/`side_r`/`side_l`/`back`，16 方向按 14 的延伸规则扩展（`_a`/`_b` 过渡档位，与
`toolchain/asset_import/directions.py` 的 `_CANONICAL_NAMES[16]` 逐字一致）；`Id` 前缀判断记录见
`DirectionSlots` 类型注释。`display.map.mirror_pairs` 未登记某个 `_l` 档位时，
`DirectionSlots.MirrorSourceOf` 给出 14 固定的默认镜像来源（`_l` 镜像自同族 `_r`）作为回退。

### 索引 → 档位对应表（8 方向，判断记录见下）

| 量化索引 | 角度 | 罗盘方位（仅供人工核对） | 档位 id |
|---|---|---|---|
| 0 | 0° | e（+X） | `dir.side_l` |
| 1 | 45° | ne | `dir.front_side_l` |
| 2 | 90° | n（+Y，面朝观察者） | `dir.front` |
| 3 | 135° | nw | `dir.front_side_r` |
| 4 | 180° | w（-X） | `dir.side_r` |
| 5 | 225° | sw | `dir.back_side_r` |
| 6 | 270° | s（-Y，背对观察者） | `dir.back` |
| 7 | 315° | se | `dir.back_side_l` |

4 方向：索引 0=`dir.side_l`、1=`dir.front`、2=`dir.side_r`、3=`dir.back`（同一推导，步进
`directionCount / 4`）。16 方向的完整对照表见
`presentation/common/tests/DirectionSlotsTests.cs`（`FromQuantized_SixteenDirections_MatchesNamingTable`）。

## 谁实现 / 谁调用

| 类型 | 谁实现 | 谁调用 |
|---|---|---|
| `IRenderConventionHost`（`RenderConventionHost`） | 本模块 | `SpriteViewBase`、后续 `presentation/vfx_sfx`（特效挂点排序）、具体游戏的 View 实现 |
| `SpriteViewBase` | 本模块提供骨架，具体游戏/引擎适配层继承 | `presentation/view_binding` 的 `ViewBinder`（经 `IViewFactory` 产出的具体子类） |

## 判断记录

1. **方向槽位命名改用 14 §2.1 固定命名，索引→档位对应关系仍是本模块的判断记录**：14 只固定了
   "档位族叫什么名字、哪些是原创绘制、哪些是镜像"，没有规定"量化索引 0 对应哪个具体档位"——这天然
   是"逻辑坐标系朝向与游戏镜头朝向如何对应"的问题，架构文档不预先拍板。见
   `Presentation.Common.DirectionSlots.FromQuantized` 类型注释的完整推导：依据 05 第 3.1 节
   "sortY 越大越靠前，即 +y 朝向观察者"确定 `front` = 角度 90°（+Y 轴），配合
   `Core.Carriers.Unit.DirectionQuantizer` 的既有约定（index 0 = 角度 0 = +X 轴，按角度递增/逆时针
   编号）反推出上方对照表。具体游戏镜头朝向如与此不一致，只需在游戏层加一层索引重映射（不改动本
   模块任何签名）。
2. **`Id` 前缀判断记录（契约缺口，见任务书要求核对并汇报）**：14 §2.1、
   `toolchain/asset_import/directions.py`、`assets/_placeholder/sprites/*/anchors.json` 三处一致
   使用不带前缀的裸档位名（如 `front_side_r`），但 `Core.Foundation.Common.Id` 的格式要求"至少一个
   点分段"，裸名字不是合法 `Id`。`DirectionSlots` 沿用 `core/foundation/display_info` 既有测试夹具
   （`DisplayInfoTestSupport.PlayerHeroSpriteRow`）的 `"dir."` 前缀惯例把裸名字包装成合法 `Id`
   （如 `"dir.front_side_r"`），只是把该夹具原有的罗盘缩写换成 14 的档位族命名；`StripPrefix` 提供
   反向转换，供需要裸名字的场景（文件名/资源 id 拼接）使用。**这不是 14/工具链的错误**——那两处的
   裸名字本就不经过 `Id` 类型，只有真正写入 `display.map.mirror_pairs`（字段类型是 `Id`）时才需要
   加前缀；具体游戏的资产导入工具接入阶段需要在这一步统一处理，见 `DirectionSlots` 类型注释详细
   说明。
3. **`SpriteViewBase.ResolveLayerResourceId` 改用 14 §1.2 命名模板**（取代 P4-1 的
   `"layer.<layerName>"` 占位）：`DisplayInfo.Sprite.PaperdollLayers` 类型是
   `IReadOnlyList<string>`，`IRenderer2D.SetLayers` 需要 `IReadOnlyList<Id>`；04/09/14 均未给出
   运行期资源 `Id` 的专用格式规定，但 14 §1.2 给出了"文件名"拼接模板
   （`<资源引用id去掉类别前缀，点号换下划线>__<方向档位id>__<层id>`），本方法让资源 Id 的名字部分
   直接复用这套文件名模板（同一套命名，避免"文件叫什么"与"运行期 Id 叫什么"各自发明一套），只在
   外面包一层 `"layer."` 域前缀满足 `Id` 格式要求；方向档位段经 `DirectionSlots.StripPrefix` 还原
   成裸名字。`protected virtual`，具体游戏按自己的资源命名规则重写。
4. **`SpriteViewBase.OnEvent` 默认空实现**：装备变化（`item.equipped`/`item.unequipped`）要更新
   纸娃娃层，需要"已装备物品的 DisplayInfo"（尚待 L3 `item` 模块提供的查询能力，不在本任务范围），
   本类型只提供 `SetPaperdollLayers` 这个底层原语，`OnEvent` 留给具体游戏 View 子类或后续
   `presentation/feedback_binder` 重写实现"收到什么事件时传什么层名列表"这一策略。
5. **高度偏移已由 ADR-0016 解决**：`IRenderer2D.SetTransform` 增加了 `height` 参数
   （与 `IRenderer3D.SetPlacement` 对齐），`SyncPose` 现直接把换算出的像素高度经这个正式参数传递，
   不再借用 `SetShaderParam` 通道，`HeightOffsetShaderParam` 常量已删除。
6. **资源首次加载责任已由 ADR-0016 解决**：`SpriteViewBase` 可选注入 `IResourceLoader`，构造期对
   `sprite_set_id`、`SetPaperdollLayers` 期间对每个新解析出的纸娃娃层资源 id，均以
   `ResourceKind.Image` 触发一次 `LoadAsync`（同一 id 只触发一次，见
   `Presentation.Common.ResourceReferenceTracker`）。

## 契约缺口

- 方向槽位到具体量化索引的对应关系是本模块的默认约定，非拍板内容，见判断记录 1。
- 裸档位名与 `Id` 格式之间需要一道前缀转换，14/工具链/占位资产与 `Id` 类型本身三处未统一约定该
  转换应该发生在哪一步，见判断记录 2。
