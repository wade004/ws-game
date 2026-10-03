# L5 表现层 · render（2.5D 渲染约定 + 纸娃娃/动画）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `render` 行（契约接口名
`RenderConventionHost`）、[09_表现层.md](../../architecture/09_表现层.md) 第 3.1～3.4、4 节：统一
`sortY` 排序、方向量化到方向槽位的镜像回退解析、纸娃娃层合成顺序、影子锚点、高度像素换算；
`sprite` 型 `IView` 的引擎无关骨架 `SpriteViewBase`；`CharacterRig`（09 §4.1）、`AnimState` 七态
状态机（09 §4.2）、命中帧同步（09 §4.3）、序列帧播放器预留接口（09 §4.5）、程序动画八原语
（09 §4.1）——拍板 6（W3a）落地 sprite 型全套引擎无关部分；[ADR-0017](../../architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)（W6 表现能力补齐 A 部分）把 `model` 型
`CharacterRig` 从占位收口为真实实现（层/槽位管理、锚点/挂点查询、八原语映射），命中帧统一事件
`HitFrameReached` 经可选接口 `IHitFrameEmitter` 提供（PJ130-04 勘误：不是 `ICharacterRig` 本身的
强制成员，见 ADR-0017"修订记录"），两种外形类型均已提供；引擎侧真正的三维渲染管线实现（`IRenderer3D`
真实实现）仍待 W6-B，不在本模块范围内。

依赖：`Presentation.Common.csproj`。

## 目录

```
render/
  README.md
  contracts/
    IRenderConventionHost.cs
    AnimState.cs                动画状态七态枚举（09 §4.2）
    ICharacterRig.cs             CharacterRig 契约（09 §4.1）：层管理/锚点查询/剪辑播放/程序动画入口
    IHasCharacterRig.cs          能力接口：IView 实现可选暴露内部持有的 ICharacterRig
    IProceduralAnim.cs           程序动画八原语契约 + 各原语参数结构（09 §4.1）
    IFrameAnimPlayer.cs          序列帧播放器契约（09 §4.5，勘误扩展 onAnimEvent）
    FrameAnimClip.cs             引擎无关序列帧剪辑元数据（帧数/帧率/关键帧标记，09 勘误新增字段）
    PoseContext.cs               LocomotionGait 步态枚举 + PoseContext（步态/武器族/变体，手感设计/04 第 2 节）
    IPoseContextSource.cs        姿势上下文来源契约（只读查询 + ContextChanged 通知）
  core/
    GaitDeriver.cs              GaitThresholds（读呈现型手感视图的步态阈值）+ GaitDeriver（带滞回的步态派生，手感设计/02 第 7 节）
    PoseSelector.cs             IPoseContextSource 的框架实现：按实体喂速度/武器族/变体，上下文变化时通知
    MapLayerHost.cs             ADR-0080：地图分层图（ground/decal/overlay）建/销驱动，接入 PresentationAssembly.MapLayers
    RenderLayers.cs            六层的整数层号常量
    RenderOptions.cs             口味配置项（方向档位数、PixelsPerUnit、HitFrameSync 策略、
                                   MirrorFacingY/FacingAngleOffsetRadians 朝向约定，ADR-0094）
    SpriteLayerPlacement.cs      ComposeSpriteLayers 产出的单条层放置信息
    ShadowSpec.cs                 影子锚点 + ShadowMode 数据侧→引擎侧转换
    RenderConventionHost.cs       IRenderConventionHost 默认实现
    SpriteViewBase.cs             sprite 型 IView 骨架，持有并委托 SpriteCharacterRig
    SpriteCharacterRig.cs         ICharacterRig 的 sprite 型实现
    ModelCharacterRig.cs          ICharacterRig 的 model 型真实实现（ADR-0017 决策 b）：装备外观、挂点查询、
                                   八原语 → SetPlacement/SetMaterialParam、命中帧事件
    AnimStateMachine.cs           AnimState 七态状态机的引擎无关实现（订阅 06 事件词汇表驱动切换，
                                   ADR-0017 决策 f 新增 StateChangedWithSkill 事件）
    ProceduralAnimSequencer.cs    IProceduralAnim 默认实现：纯时间推进 + 曲线求值，不调用任何 L-1 接口
    FrameAnimPlayer.cs            IFrameAnimPlayer 参考实现：帧号推进 + 关键帧/播放完成事件派发
  tests/
    MapLayerHostTests.cs           ADR-0080：建层/decal 缺失降级/image_transform 缺失降级/切图不泄漏/Dispose 清理/默认接口成员用例
    RenderConventionHostTests.cs   12 个用例
    SpriteViewBaseTests.cs         10 个用例
    AnimStateMachineTests.cs       逐条转移 + 优先级用例 + StateChangedWithSkill 用例
    SpriteCharacterRigTests.cs     层合成/锚点/程序动画/命中帧同步策略用例
    ModelCharacterRigTests.cs      真实实现用例：放置合成、装备外观、挂点查询、命中帧事件（用 StubRenderer3D 驱动）
    ProceduralAnimSequencerTests.cs  八原语时序 + 结束回调 + 同类型替换用例
    FrameAnimPlayerTests.cs        帧推进/关键帧/循环/播放完成用例
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

### 朝向约定口味项（`RenderOptions.MirrorFacingY`/`FacingAngleOffsetRadians`，ADR-0094）

下表"角度 0 = e（+X）、90° = n（+Y，面朝观察者）"是本模块默认的朝向约定，具体游戏的镜头朝向若与
之不一致（角度增长方向相反，和/或"角度 0"对应的屏幕朝向整体旋转了一个固定角度），不必再去改
方向档位数或量化规则本身去绕，配 `RenderOptions` 的这两个口味项即可：`MirrorFacingY`（镜像开关，
角度取负，处理角度增长方向相反的情况）先生效，`FacingAngleOffsetRadians`（弧度偏移量）后叠加，
变换后的角度才进入下表的量化——即"先镜像、再偏移"，量化本身不变。两项默认值（`false`/`0`）下
变换前后逐字节相同，未配置的游戏不受影响。推荐配置方式：先用默认值跑一遍，肉眼核对"角度 0 对应的
朝向是否符合预期"，对不上再按需要配置这两项，不必逐档位试错。完整推导见
[ADR-0094](../../architecture/adr/0094-朝向约定口味项与档位数无关.md)、判断记录 24。

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
| `ICharacterRig`（`SpriteCharacterRig`/`ModelCharacterRig`） | 本模块 | `SpriteViewBase`（持有并委托，经 `IHasCharacterRig` 对外暴露）、`PresentationAssembly`（Flash 原语默认接线） |
| `AnimStateMachine` | 本模块 | 具体游戏/装配层订阅 `StateChanged` 后据此调用 `ICharacterRig.SetAnimState`/`PlayClip`（本轮未在 `PresentationAssembly` 强制接线，留给游戏层按自己的武器表现档案查表逻辑接入，见判断记录 8） |
| `IProceduralAnim`（`ProceduralAnimSequencer`） | 本模块（`SpriteCharacterRig`/`ModelCharacterRig` 各自持有一份） | `FeedbackBinder`（经 `PresentationAssembly` 默认 Flash 接线）、W3b 引擎适配层（其余七个原语的 `onSample` 落地） |
| `IFrameAnimPlayer`（`FrameAnimPlayer`） | 本模块提供引擎无关参考实现，W3b 可整体替换/包装 | `ICharacterRig.PlayClip` 转发目标；`SpriteCharacterRig` 订阅其 `OnAnimEvent` 服务命中帧同步 |

## 判断记录

1. **方向槽位命名改用 14 §2.1 固定命名，索引→档位对应关系仍是本模块的判断记录**：14 只固定了
   "档位族叫什么名字、哪些是原创绘制、哪些是镜像"，没有规定"量化索引 0 对应哪个具体档位"——这天然
   是"逻辑坐标系朝向与游戏镜头朝向如何对应"的问题，架构文档不预先拍板。见
   `Presentation.Common.DirectionSlots.FromQuantized` 类型注释的完整推导：依据 05 第 3.1 节
   "sortY 越大越靠前，即 +y 朝向观察者"确定 `front` = 角度 90°（+Y 轴），配合
   `Core.Carriers.Unit.DirectionQuantizer` 的既有约定（index 0 = 角度 0 = +X 轴，按角度递增/逆时针
   编号）反推出上方对照表。**"游戏层加一层索引重映射"已解决（缺口 8，见
   `RenderConventionHost` 构造函数）**：`RenderOptions.DirectionIndexRemap`（长度须等于当前方向档位
   数，默认 null 恒等映射）在 `ResolveDirectionSlot` 换算规范档位之前生效，不改本模块任何签名。
2. **`Id` 前缀判断记录（已由设计层拍板落地，不再是待核对的契约缺口）**：14 §2.1、
   `toolchain/asset_import/directions.py`、`assets/_placeholder/sprites/*/anchors.json` 三处一致
   使用不带前缀的裸档位名（如 `front_side_r`），但 `Core.Foundation.Common.Id` 的格式要求"至少一个
   点分段"，裸名字不是合法 `Id`。`DirectionSlots` 沿用 `core/foundation/display_info` 既有测试夹具
   （`DisplayInfoTestSupport.PlayerHeroSpriteRow`）的 `"dir."` 前缀惯例把裸名字包装成合法 `Id`
   （如 `"dir.front_side_r"`），只是把该夹具原有的罗盘缩写换成 14 的档位族命名；`StripPrefix` 提供
   反向转换，供需要裸名字的场景（文件名/资源 id 拼接）使用。**这不是 14/工具链的错误**——那两处的
   裸名字本就不经过 `Id` 类型，只有真正写入 `display.map.mirror_pairs`（字段类型是 `Id`）时才需要
   加前缀。设计层已拍板并落地为 14 第 2.1 节 2026-09-05 勘误："运行期方向档位 Id 固定为
   `dir.<裸档位名>`，文件名/标注文件/工具链一律用裸档位名"，与 `common/README.md` 判断记录同一条
   结论，见 `DirectionSlots` 类型注释"Id 前缀已拍板结论"。
3. **`SpriteViewBase.ResolveLayerResourceId` 改用 14 §1.2 命名模板**（取代 P4-1 的
   `"layer.<layerName>"` 占位）：`DisplayInfo.Sprite.PaperdollLayers` 类型是
   `IReadOnlyList<string>`，`IRenderer2D.SetLayers` 需要 `IReadOnlyList<Id>`；04/09/14 均未给出
   运行期资源 `Id` 的专用格式规定，但 14 §1.2 给出了"文件名"拼接模板
   （`<资源引用id去掉类别前缀，点号换下划线>__<方向档位id>__<层id>`），本方法让资源 Id 的名字部分
   直接复用这套文件名模板（同一套命名，避免"文件叫什么"与"运行期 Id 叫什么"各自发明一套），只在
   外面包一层 `"layer."` 域前缀满足 `Id` 格式要求；方向档位段经 `DirectionSlots.StripPrefix` 还原
   成裸名字。`protected virtual`，具体游戏按自己的资源命名规则重写。
4. **`SpriteViewBase.OnEvent` 已默认处理装备变化（缺口 10）**：新增
   `presentation/render/contracts/EquipVisualDef.cs`（`display.equip_visual` 强类型视图）+ 构造期
   可选注入 `IReadOnlyDictionary<Id, EquipVisualDef>? equipVisualByItemInstanceId`（按物品实例 id
   索引，见判断记录"为何按实例 id 而不是 item_id"）；`OnEvent` 收到 `item.equipped`/
   `item.unequipped` 时按该表重新解析对应槽位的纸娃娃层资源并调用 `SetLayers`，未注入该表或查不到
   对应行时保持不变（等价于此前的默认空实现）。`mesh_ref` 的方向档位换算规则见判断记录 22
   （ADR-0071 已取代此前"不经方向档位换算，是已知简化"的旧表述）。
5. **高度偏移已由 ADR-0016 解决**：`IRenderer2D.SetTransform` 增加了 `height` 参数
   （与 `IRenderer3D.SetPlacement` 对齐），`SyncPose` 现直接把换算出的像素高度经这个正式参数传递，
   不再借用 `SetShaderParam` 通道，`HeightOffsetShaderParam` 常量已删除。
6. **资源首次加载责任已由 ADR-0016 解决**：`SpriteViewBase` 可选注入 `IResourceLoader`，构造期对
   `sprite_set_id`、`SetPaperdollLayers` 期间对每个新解析出的纸娃娃层资源 id，均以
   `ResourceKind.Image` 触发一次 `LoadAsync`（同一 id 只触发一次，见
   `Presentation.Common.ResourceReferenceTracker`）。
7. **`IAnchorQuery` 锚点查询已解决（缺口 6）**：`contracts/IAnchorQuery.cs` 声明
   `Vec2? GetAnchorWorldPosition(Id entityId, Id anchorId)`，由 `presentation/view_binding` 的
   `ViewBinder` 实现（谁持有 View 绑定表与 `ISimSnapshot` 谁实现，`RenderConventionHost` 本身无
   状态不适合持有，见该接口类型注释判断记录）；镜像下锚点偏移的水平分量换算复用
   `IRenderConventionHost.ResolveDirectionSlot` 同一套镜像判定，不重复实现。`PresentationAssembly`
   把它接到 `vfx_sfx` 的 `AnchorResolver`。`ICharacterRig.ResolveAnchorLocalOffset`（新增）是同一套
   镜像判定的另一份独立实现（不加实体世界坐标，只算局部偏移），两处刻意不合并——见判断记录 8。
8. **`CharacterRig` 归并任务（拍板 6）：`SpriteViewBase` 委托而非直接继承 `ICharacterRig`**：09
   §4.1 CharacterRig 职责表要求归并层/槽位管理、锚点查询、动画状态机驱动、程序动画原语四项；
   `SpriteViewBase` 已有的装备覆盖合并逻辑（`RebuildEquippedLayers`）比"给定层名顺序直接合成"复杂
   得多，本次不推翻既有实现——`SpriteCharacterRig` 只归并两段全部 sprite 型角色都需要的通用步骤
   （`ComposeAndApplyLayers`/`ApplyLayers`），`SpriteViewBase` 持有一个 `SpriteCharacterRig` 实例并
   委托，经 `IHasCharacterRig.Rig` 对外暴露；`ICharacterRig.ResolveAnchorLocalOffset` 是新增能力
   （不是从 `ViewBinder.GetAnchorWorldPosition` 抽取——那份实现测试已充分覆盖、风险高于收益，见上一条
   "两处刻意不合并"）。
9. **"动画状态机驱动"职责拆成"记账"与"播放"两个独立方法**：`AnimStateMachine`（引擎无关）只回答
   "当前该处于哪个 `AnimState`"，不知道具体该播哪个剪辑——那依赖 09 第 4.4 节武器表现档案查表
   （同一 `cast` 状态装备双手剑和法杖播不同美术），是内容相关逻辑，不适合固化进本模块契约。
   `ICharacterRig.SetAnimState`（记账）与 `PlayClip`（转发到已解析好的具体 clip id）因此是两个独立
   方法，中间的"状态 + 武器 → clip id"查表环节留给具体游戏的组装代码，`PresentationAssembly` 本轮
   未强制接线（见上表"谁实现/谁调用"）。
10. **`AnimStateMachine` 事件→状态映射与优先级表是本模块的判断记录，非 09 拍板内容**：09 只给出
    `AnimState` 七态集合本身，未给事件词汇表对照与优先级表；完整推导（为何用 `unit.state_changed`
    而非 `unit.moved`、为何 `jump` 走手工 `RequestOverride` 而非事件驱动、优先级数值表）见
    `AnimStateMachine` 类型注释。
11. **`IProceduralAnim` 八原语的曲线与叠加/互斥规则是本模块的判断记录**：09 只拍板了原语名称与
    "参数留白由具体引擎适配层解释"，未给缓动曲线与多原语叠加规则；`ProceduralAnimSequencer` 选择
    "同类型互相替换（旧实例的 `onComplete` 立即以'被替换'方式触发）、跨类型互相独立"的简单规则 +
    线性/对称三角波两种曲线，完整推导见该类型注释。
12. **`SpriteCharacterRig.ProceduralAnim.Flash` 是八原语里唯一有开箱即用落地的一个**：受击/无敌帧
    闪白是最高频场景，固定接 `IRenderer2D.SetShaderParam` 的 `"flash_intensity"` 参数；其余七个
    原语只转发到 `ProceduralAnimSequencer`，合理默认表现留给调用方（通常是 W3b）在 `onSample` 里
    决定，本模块不代为拍板，见 `SpriteCharacterRig` 类型注释。
13. **命中帧同步策略切换（`RenderOptions.HitFrameSync`）现两种外形类型均已接线**（ADR-0017 决策 c，
    2026-09-08 更新，取代本条原先"目前只有 sprite 型接线"的表述）：`SpriteCharacterRig` 在
    `AnimKeyframeDriven` 策略下把 `FrameAnimClip.HitFrameMarker` 对应的 `IFrameAnimPlayer.OnAnimEvent`
    重新广播为 `HitFrameReached` 事件（携带实体 id）；`ModelCharacterRig` 同一策略下把
    `IRenderer3D.OnAnimEvent` 命中 `ModelCharacterRig.HitFrameEventId` 时同样广播
    `HitFrameReached`——`HitFrameReached` 经可选接口 `IHitFrameEmitter` 提供（见判断记录 16；
    PJ130-04 勘误：不是 `ICharacterRig` 本身的强制成员），两套实现均同时实现该接口，供命中反馈的
    落地代码（`presentation/feedback_binder` 的 `HitFrameSyncPolicy`）延迟到这一刻才播放；
    `LogicDriven`（默认）策略下两种实现均不触发，命中反馈沿用 `FeedbackBinder` 收到
    `combat.damage_dealt` 立即派发的既有行为。

14. **N18 收口（外部审核 68c9bed）：`FrameAnimPlayer.Update` 每次都重新从 `_clips` 查一次当前
    clipId，不缓存 `Play` 那一刻的剪辑对象引用**——原实现 `_current` 只在 `Play` 时从共享的可写
    字典 `_clips` 查一次，此后整段播放期间缓存同一个对象引用；若调用方在播放中途用同一个
    `clipId` 重新登记了一份新剪辑（`_clips[clipId] = new FrameAnimClip(...)`，典型场景是
    `Adapter.Unity.Presentation.UnityFrameAnimPlayer.RegisterClipFromEffect` 冷序列帧资源加载
    完成后原地升级），本类型对此一无所知，仍按开始播放那一刻的旧帧数/帧率/关键帧继续推进，
    视觉上永远停在旧内容，必须调用方手工重新 `Play` 才会用上新剪辑。现在每次 `Update` 开头都
    重新查一次最新版本——`_elapsedSeconds` 是纯时间累加量，天然实现"保留播放进度、按新剪辑重新
    解释这段时间对应第几帧"；剪辑对象引用确实变化时即便算出来的帧下标数值没变，也强制重新触发
    `FrameChanged`（同一帧下标在新剪辑里可能对应完全不同的贴图）。`UnityFrameAnimPlayer.
    OnFrameChanged` 本身已经是按 clipId+帧下标现查表，不需要改动即可受益。见 `FrameAnimPlayer.cs`
    （`Update`/`SetFrame`）、`FrameAnimPlayerTests.cs`。

15. **N19 收口（外部审核 68c9bed）：瞬发（`AnimState.Attack`）的施法收尾事件不再驱动回落，只有
    `NotifyTransientStateFinished` 能让它回落**——判断记录 10 的事件→状态映射原文写"三个收尾事件
    （只要 `casterId` 匹配、当前状态仍是 Attack/Cast）一律驱动回落到当前运动状态"，这条描述本身
    是缺陷：瞬发（覆盖普攻与瞬发技能）的 `skill.cast_start`（进入 Attack）与 `skill.cast_success`
    在逻辑层同一次派发批次内背靠背发出，若靠事件收尾会让 Attack 播放形态在同一帧内被切回
    Idle/Move，Attack 动画剪辑根本没有机会真正播出（09 表现层"逻辑结算与动画播放时长相互独立"
    这一原则要求二者不能靠同一个逻辑事件同步收尾）。现在只有 `AnimState.Cast`（真正的读条/引导，
    视觉时长本就等于逻辑层的 `cast_time`/`channel_time`，收尾事件到达时天然已经播完）继续在
    `OnSkillCastEnd` 里回落；`AnimState.Attack` 一律改由 `NotifyTransientStateFinished` 独家负责
    （`Adapter.Unity.Presentation.UnityViewFactory.AttachDefaultAnimation` 早已把
    `IFrameAnimPlayer.OnComplete` 接回本状态机，见 `adapters/unity/.../README.md`"瞬态完成通知"，
    不需要新增接线）。见 `AnimStateMachine.cs`（`OnSkillCastEnd`）、`AnimStateMachineTests.cs`。

16. **ADR-0017（W6 表现能力补齐 A 部分，2026-09-08）：`model` 型 `CharacterRig` 从占位收口为真实
    实现**：`ModelCharacterRig` 不再对层管理、锚点/挂点查询抛 `NotSupportedException`——
    `ComposeAndApplyLayers`/`ApplyLayers`（纸娃娃层专属方法，model 型不适用）改为 no-op；新增
    `ApplyEquipVisual(EquipVisualDef)` 是 model 型真正的装备外观应用入口，按 `mode` 做
    `IRenderer3D.SetSlotMesh`（槽位换装）或 `CreateModelInstance` + `AttachToSocket`（挂点挂接，
    自动 `Detach`/`DestroyModelInstance` 旧挂接，见类型判断记录）；`ResolveAnchorLocalOffset` 按
    `ModelInfo.Sockets` 是否声明该挂点返回声明性结果（存在返回 `Vec2.Zero` 占位、不存在返回
    null——model 型没有可精确计算的挂点偏移数据，真实世界坐标由具体 `IRenderer3D` 实现的骨骼系统
    决定，本方法只能回答"是否声明"）；八项程序动画原语中 move/rotate/scale/stagger/topple 五项
    经 `SyncPlacement` 缓存的基准姿态叠加后调用 `IRenderer3D.SetPlacement`，flash/trail/fade 三项
    固定调用 `IRenderer3D.SetMaterialParam`（参数名 `flash_intensity`/`trail_intensity`/
    `fade_alpha`，与 `SpriteCharacterRig.Flash` 接 `flash_intensity` 同一惯例的自然扩展）。
    `HitFrameReached` 经可选接口 `IHitFrameEmitter` 提供（`event Action<Id>? HitFrameReached`，
    PJ130-04 勘误：不是 `ICharacterRig` 接口本身的强制成员，见 `presentation/render/contracts/
    IHitFrameEmitter.cs`），两套实现均同时实现该接口。`AnimStateMachine` 新增 `StateChangedWithSkill`
    事件（`(entityId, from, to, triggerSkillId: Optional<Id>)`），与既有 `StateChanged` 三元组事件
    同一次切换背靠背触发，`triggerSkillId` 只在 `skill.cast_start` 驱动的切换（进入
    `attack`/`cast`）时非空，支持 09 §4.4 `cast_anim_override` 按技能覆盖查表；既有 `StateChanged`
    事件签名不变，不影响既有订阅方。真正把这些能力接到具体三维渲染引擎（`IRenderer3D` 真实实现）
    与默认装配（`PresentationAssembly` 自动注册 rig 进 `IHitFrameSource`、自动把
    `IWeaponStyleSource` 接进 `AnimClipResolver` 等）仍留给 W6-B，见
    `architecture/落地计划/落地方案与分阶段计划.md`"W6 表现能力补齐"小节。

17. **PJ130-04 根治（第六轮文档—代码深度审计，`architecture/落地计划/audit-5c444f1-20260908/`）：
    `HitFrameReached` 从 `ICharacterRig` 强制成员改回可选接口 `IHitFrameEmitter`**——判断记录 16 记录
    的"提升进 `ICharacterRig` 契约本身"随 1.3.0（次版本号）发布给该接口新增了一个强制事件成员，属于
    `architecture/11_工程规范与测试.md` 第 156 行定义的 MAJOR 级契约签名变化，与次版本号不应破坏既有
    编译这一既定语义冲突（继续实现旧版 `ICharacterRig` 形状的外部代码升级到 1.3.0 会直接编译失败）。
    新增 `Presentation.Render.IHitFrameEmitter`（`event Action<Id>? HitFrameReached`），
    `SpriteCharacterRig`/`ModelCharacterRig` 同时实现 `ICharacterRig` 与 `IHitFrameEmitter`，事件触发
    时机/语义完全不变；`Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource.RegisterRig` 按
    `rig is IHitFrameEmitter` 探测，未实现该接口的 `ICharacterRig`（不参与命中帧同步）仍能正常登记
    （`HasRig` 仍返回 true），不抛异常。详见 [ADR-0017](../../architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)"修订记录"。

18. **PR130-07 根治新增：`EquipmentVisualSource`（默认的 装备实例 -&gt; `EquipVisualDef` 供给入口）**
    ——doc-code-matrix 此前记录的能力边界"`UnityViewFactory` 构造函数没有 `equipVisual` 参数……默认
    factory 仍缺入口"在本轮补上：新增 `Presentation.Render.EquipmentVisualSource`，订阅
    `item.added`/`item.equipped`/`item.unequipped`，按 `item.added` 携带的 `ItemTemplateId` 累积
    "实例 id -&gt; 模板 id"表，`item.equipped` 时反查按 `display.equip_visual.item_id` 建的目录得到
    对应 `EquipVisualDef`，暴露一个随事件实时增删的 `VisualByItemInstanceId` 只读字典；三处生产装配根
    （`games/_template.GameBootstrap`/`GameFoundationBootstrap`/`FrameworkResidentHost`）与
    `EquipmentWeaponStyleSource` 同批构造/`Dispose`，把该字典传给 `UnityViewFactory` 新增的
    `equipVisualByItemInstanceId` 构造参数（见 `adapters/unity/.../README.md`"equipVisual 接线步骤"）。
    同批根治 `UnityModelView` 卸装路径按 `ItemInstanceId` 反查实际应用过的 `EquipVisualDef`（见该类型
    `_appliedEquipVisualsByItemInstanceId` 判断记录），取代此前直接把事件携带的逻辑 `item.slot` 同时
    当 slot_mesh 槽位 id 与 socket_attach 挂点 id 使用、导致 socket 子模型实例卸装后残留不清理的问题。

19. **P2-08 根治新增：`IEquipmentVisualResettable`（同图已有 View 的 SaveLoaded 装备外观对账）**——
    判断记录 18 只解决了 View 创建/装备变化事件两条路径的外观应用，同图内继续存活的既有 View 在
    读档后没有任何机制刷新外观（读档期间 `item.equipped`/`item.unequipped` 本就被存档系统的抑制
    作用域丢弃，见 `presentation/view_binding/README.md` 判断记录 6）。新增
    `presentation/render/contracts/IEquipmentVisualResettable.cs`：
    `ResetEquipmentVisuals(IReadOnlyList<EquippedItemRef> equipped)`——清空当前已应用的全部装备
    外观，再按真实快照重新应用，幂等。`SpriteViewBase` 实现为清空并重建
    `_equipOverridesBySlot`（按槽位索引，天然不受"读档后实例 id 是否还对应同一物品"影响）；
    model 型实现见 `adapters/unity` 包 `UnityModelView`（直接遍历自己的
    `_appliedEquipVisualsByItemInstanceId` 逐条精确清理，不依赖 `ItemUnequippedEvent` 反查）。
    `ViewBinder`（`view_binding` 模块）持有可选的 `EquipmentVisualSource` 引用并驱动这一步，见
    `presentation/view_binding/README.md` 判断记录 6——本模块（`render`）只提供"如何清空/重建自己
    的外观状态"这一具体能力，不知道、也不需要知道自己何时被谁调用。

20. **资源加载完成后回填已渲染层**（诊断记录
    `docs/复盘/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`"教训四"根治，取代
    此前"首次引用未加载完成的资源→落地占位方块→此后再没有机制刷新"的结构性缺口）：
    `SpriteCharacterRig.ComposeAndApplyLayers`/`SpriteViewBase.RebuildEquippedLayers` 调用
    `ResourceReferenceTracker.EnsureLoading` 时统一传入 `SpriteCharacterRig.
    HandleResourceLoadCompleted` 作为 `onComplete`（`common/README.md` 判断记录 7 新增的重载）：
    加载成功时用 `SpriteCharacterRig` 记住的"最近一次已知完整层列表"（`ApplyLayers` 每次调用都
    会更新这份记录，天然反映最新期望状态而不是请求发起时刻的旧快照）重新调用一次
    `ApplyLayers`，让占位方块有机会被迟到的真实资源替换；加载失败时不重新应用，改用新增的
    `SpriteCharacterRig.Diagnostics`（复用 `Presentation.VfxSfx.Contracts.IPresentationDiagnostics`，
    不新造 render 专属诊断契约，同判断记录 1"不新造 IRenderSurface"一贯做法）记一条警告，不静默；
    `SpriteViewBase.Destroy` 调用新增的 `SpriteCharacterRig.MarkDestroyed` 使此后迟到的回调直接
    跳过，不触碰已销毁的 `SpriteHandle`。幂等设计：同一资源 id 因 `ResourceReferenceTracker` 自身
    去重只会回调一次；多个层引用同一资源 id 时那一次回调会把"整份当前层列表"重新应用一次，天然
    覆盖全部引用它的层，不需要按层分别处理；重新应用本身开销可忽略——`IRenderer2D.SetLayers`
    按索引复用既有 `SpriteRenderer`，不销毁重建任何引擎对象。未走 `VfxPlayer`/`SfxPlayer` 的
    构造期诊断注入模式：`SpriteCharacterRig`/`SpriteViewBase` 现有构造函数不允许加参数（AGENTS.md
    "ABI 只新增"），改为固定内部实例、经只读属性暴露，是比新增构造重载更小的改动面。

21. **ADR-0070（消费方反馈第十七批，2026-09-23）：`AnimStateMachine` 新增订阅
    `combat.auto_attack_swing`，攻击者进入 `AnimState.Attack`；`RequestOverride` 收紧适用边界为
    "游戏专属触发"**——普通攻击（`Core.Rules.Combat.AutoAttackHost`）结算前广播的新事件只携带
    `sourceId`/`targetId`，构造函数新增订阅并调用既有 `TryEnter(evt.SourceId, AnimState.Attack)`
    （不新增状态、不新增优先级层级，与既有 `skill.cast_start` 走同一套仲裁：优先级 2，不覆盖受击/
    死亡，覆盖待机/移动），每次挥击广播都重新调用一次，天然支持连续挥击重复进入攻击态（同状态重入
    触发既有 `StateRetriggered`，不是 `StateChanged`）。同时更新 `RequestOverride` 的类型/方法文档：
    明确它只用于框架不掌握权威判定时点的游戏专属动作（如跳跃，见判断记录 10）；凡是框架自身能算出
    精确触发时机的动作（普通攻击挥击、技能施法开始/成功/失败/被打断、受击、死亡）必须由框架自己
    经事件驱动，不应反过来靠游戏侧调用 `RequestOverride` 模拟——此前这条边界只隐含在优先级模型里
    （框架事件驱动的状态优先级均 ≥ 1，`RequestOverride` 写入的跳跃态占用优先级 1），本次只是写清楚，
    不改变优先级模型本身。配套：`IViewFactory` 的引擎适配层实现新增一个公开只读属性转发已持有的
    `AnimStateMachine` 实例（既有仅供框架自测使用的受限出口原样保留）——此前该实例只经这一受限出口
    暴露，游戏侧程序集不在可见范围内，`RequestOverride` 文档所称"供游戏调用"在生产环境从未真正
    可达，本次一并根治。ABI：`AnimStateMachine` 新增一个事件订阅（构造期内部行为，不改变构造签名），
    视图工厂新增一个公开只读属性，不改动任何既有公开签名。

22. **ADR-0071 决策 1（消费方反馈第十八批）：`EquipVisualDef.MeshRef` sprite 型下改为"装备层资源集
    引用"，经与身体层同一套方向档位换算解析**——取代判断记录 3/4 里"`mesh_ref` 不经方向档位换算，
    是已知简化"的旧结论（一件装备此前只能给一张图，无法随朝向切换素材）。新增
    `SpriteViewBase.ResolveEquipLayerResourceId(SpriteLayerPlacement, Id equipLayerSetRef)`
    （`protected virtual`，ABI 加法，不改 `ResolveLayerResourceId` 既有签名），与后者共用私有静态
    `ComposeLayerResourceId` 拼接同一套 14 §1.2 命名模板公式，唯一差异是"资源集名字"来源
    （身体层用 `DisplayInfo.Sprite.SpriteSetId`，装备层用 `EquipVisualDef.MeshRef`）——保证两条路径
    永远同一套方向档位规则，不会出现两边分叉。`_equipOverridesBySlot` 存的从"该层最终资源 Id"改为
    "装备层资源集引用"，`RebuildEquippedLayers` 改为：先把默认层名与装备新增层名合并成一份完整列表，
    统一调一次 `ComposeSpriteLayers` 按当前朝向解析方向槽位，再逐层按是否命中覆盖分派到
    `ResolveEquipLayerResourceId`/`ResolveLayerResourceId`——装备新增的层（此前直接使用覆盖值、不经
    方向解析）现在也纳入同一次方向解析，行为更一致。**破坏性数据变更**：升级前的单值 `mesh_ref`
    数据在新语义下会解析到不存在的资源（原值本身此后被当"资源集名字前缀"而非"资源 Id"使用），
    见 `architecture/adr/0071-*.md`"升级影响"一节与消费方通知稿。

23. **ADR-0080（地图分层图接入运行期渲染）：`MapLayerHost` 是本模块新增的场景加载驱动方，不是
    `ICharacterRig`/`RenderConventionHost` 体系的一部分**——世界矩形唯一由 `world.map.image_transform`
    决定（复用 `MapImageTransform.WorldBounds`，不自造第二套换算），三层是否真的建出（尤其可选层
    `decal`）由具体 `IRenderer2D` 实现按加载结果自行决定，本类型只按返回的 `MapLayerHandle.IsValid`
    决定是否纳入销毁清单，避免驱动方与渲染实现各自维护一份"哪些层文件存在"的判断，见类型注释判断
    记录。**`Dispose` 主动销毁仍存活的层（2026-09-23 补充，超出 ADR-0080 原始决策范围的本模块自主
    判断）**：`OnPreUnload` 只在真的经 `ISceneRouter` 走一次卸载/切图时才触发，调用方在地图仍加载
    中直接 `Dispose`（进程退出、测试夹具收尾、未来允许的其它场景）不会经过那条路径；同
    `ViewFactory.DestroyAllCreatedViews` 一类"自己负责清理自己建出的引擎侧对象"惯例补齐，避免同一
    进程内反复构造/销毁 `PresentationAssembly` 时前一份实例的地图分层图 GameObject 一直挂在引擎
    适配层常驻根节点下不被回收（Unity PlayMode 测试套件里多个测试夹具共享同一个 DontDestroyOnLoad
    宿主单例，正是这条路径会被触发的真实场景，见 `adapters/unity/.../Tests/Runtime/
    MapLayerHostPlayModeTests.cs` 判断记录）。

24. **ADR-0094（消费方反馈第三十九批）：朝向约定新增 `MirrorFacingY`/`FacingAngleOffsetRadians`
    两个口味项，与方向档位数无关**——事先核实过 `presentation/view_binding` 的 `ViewBinder` 在调用
    `Direction.FromQuantized` 量化之前手上就有未量化的原始朝向弧度角，因此选择在这一层做统一变换
    （`RenderConventionHost.ApplyFacingConvention`：先按 `MirrorFacingY` 镜像、再加
    `FacingAngleOffsetRadians`），而不是另一个曾设想的"按方向档位数各配一张索引重映射表"方案——
    原始角度层面的变换与档位数无关，配一次对任意档位数生效，索引重映射表则要求每种档位数各自配
    一份。两项默认值下逐字节不变。既有的 `RenderOptions.DirectionIndexRemap`（判断记录 1"缺口 8"）
    是另一套独立机制，作用在量化之后的索引上，顺序固定为"先角度层面镜像/偏移、量化、再索引重映射"，
    不可调换；该配置项长度与当前方向档位数不一致时，此前是纯静默按恒等映射处理，现按
    (档位数, 表长度) 组合去重记一条一次性 Warn 诊断（经 `RenderConventionHost` 新增的可选诊断出口
    构造重载，未注入诊断出口时仍纯静默，处理结果本身不变，不抛异常）。完整推导见
    [ADR-0094](../../architecture/adr/0094-朝向约定口味项与档位数无关.md)。

25. **ADR-0093（消费方反馈第三十九批）：默认挂接的动画剪辑随朝向变化重新探测，取代判断记录（见
    [ADR-0072](../../architecture/adr/0072-纸娃娃层逐层播放剪辑.md)）里"只在实体挂接时刻按当时
    朝向探测一次，运行期朝向改变不会重新探测"这条边界**——`SpriteViewBase.SyncPose` 解析出的
    方向槽位与上一次不同时，新增受保护可覆写方法 `OnDirectionSlotChanged(Id newSlotId)`（默认空
    实现，ABI 加法，早于本次改动的子类不受影响）同步触发一次；引擎适配层的具体视图实现把它转发为
    对外事件，供负责挂接默认动画的一方订阅并按新方向裸档位名重新走一遍 ADR-0072 决策 1 既有的候选
    探测规则（逐层与整身两条路线待遇一致，整身路线在本次之前不支持按方向探测）。换向后同一状态内
    保留已播放的时长，复用判断记录 14（N18 收口）"按原剪辑标识原地覆盖、播放器自己重新换算当前
    帧"这一既有热替换机制，未新增任何进度记账代码。探测结果按(实体, 方向, 状态/层)缓存，只在方向
    真正变化时触发一次。完整推导见
    [ADR-0093](../../architecture/adr/0093-动画剪辑随朝向档位切换.md)。

26. **ADR-0099（消费方反馈第四十四批，阻塞）：纸娃娃层写入渲染器之后新增一个通知出口，三条路径
    共用**——`SpriteCharacterRig.ApplyLayers` 是本模块把纸娃娃层写入渲染器的唯一落点
    （`ComposeAndApplyLayers`/`ComposeAndApplyEquipAwareLayers`——即 `RebuildEquippedLayers`/
    `SetPaperdollLayers` 共用的实现——与 `HandleResourceLoadCompleted` 的迟到回填最终都委托本方法），
    新增 `LayersApplied` 事件在写入渲染器之后触发一次；`SpriteViewBase` 构造期订阅该事件转发到
    新增的受保护可覆写方法 `OnLayersComposed(IReadOnlyList<Id> layerResourceIds)`（默认空实现，
    ABI 加法）。引擎适配层的具体视图实现把它转发为对外事件，供负责逐层动画写回的一方订阅：命中
    逐层动画的层立即按播放器当前帧号重新写回一次，不必等下一次自然推进——修复"重合成把当前播放
    帧临时覆盖成静态图，要等下一帧才纠正回来"的可见闪回，覆盖方向变化、装备变化、首次引用的
    资源异步加载完成三条路径。
    完整推导见 [ADR-0099](../../architecture/adr/0099-纸娃娃层只在方向槽位变化时重合成.md)。

27. **ADR-0104（消费方反馈第五十二批）：`TieBreakComparer` 由"建议"改为规则，`IRenderer2D` 新增
    登记/读回两个默认接口成员，接入引擎适配层真正确定同层同 `sortY` 的绘制先后**——
    `IRenderConventionHost.TieBreakComparer` 契约文档措辞收紧：比较结果靠前 = 先绘制 = 位于画面更
    后方，同 `sortY` 时 `Id` 更小者在后、更大者在前；`SpriteViewBase.Bind` 在实体身份就绪的这一刻
    调用新增的 `IRenderer2D.SetSortIdentity`（默认空实现，ABI 加法）登记该句柄参与平局比较的稳定
    id——冷加载（资源尚未加载完成，渲染占位方块）与热路径共用同一个调用点，不分别接线；新增
    `IRenderer2D.CompareDrawOrder`（默认恒返回 0）供读回验证。本模块只负责比较器语义与登记调用点，
    引擎适配层如何把比较器全序落到具体渲染管线的绘制顺序不在本模块职责内（见
    `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md`"判断记录索引"
    `UnityRenderer2D.cs` 条目 ADR-0104 跟进）。完整推导见
    [ADR-0104](../../architecture/adr/0104-渲染平局规则接入与绘制顺序可读回.md)。
28. **ADR-0111（消费方反馈第六十一批"战斗待机"）：`AnimStateMachine` 新增与 `AnimState` 正交的战斗姿态记账**——
    不新增 `AnimState` 成员。订阅 `combat.entered`/`combat.left`（逻辑层每单位在真实迁移时各入队一次；读档、销毁不发），维护每实体一个布尔；新增只读 `IsInCombatStance`、变化事件 `CombatStanceChanged`（姿态没变不触发）、显式 `Track`，构造新增重载接受初始姿态探针 `Func<Id,bool>`（首次跟踪某实体时调用一次；旧构造保留，初始恒非战斗），`Forget` 清姿态。
    取舍：①由战斗事件首次创建记录的实体**不问探针**——迁移前姿态已知是事件值的反面，探针此刻读到的往往已等于事件值，拿它当基线会把这次迁移判成"没变"而吞掉（`CombatProbe_DeterminesInitialStanceOnce_EventCreatedEntriesSkipProbe_ForgetClears` 锁定）；②姿态变化**不触发** `StateChanged`/`StateRetriggered`，本类型不选剪辑、不打断瞬态，回落到运动态时读到的是当时姿态（`StanceChange_DoesNotInterruptTransientState_RevertSeesCurrentStance_DeathUnaffected`）；③`Death` 终态记录不受姿态变化影响。"该状态该播哪个剪辑、变体键回落"是解析层（适配层 `AnimClipResolver`）的事，见适配层 README "判断记录索引" ADR-0111 条目。剪辑键前缀 `combat_` 只在契约层 `AnimSetDef.CombatClipKeyPrefix` 定义一处。
    测试：`presentation/render/tests/AnimStateMachineCombatStanceTests.cs`（3 例，通过：订阅+去重+与状态正交、瞬态不打断、探针语义）；完整推导见 [ADR-0111](../../architecture/adr/0111-战斗姿态动画变体.md)。

29. **ADR-0112（消费方反馈第六十三批）：方向切换原子化**——`SpriteViewBase` 把"期望方向"与"已显示方向"分开：
    期望方向由 `SyncPose` 每帧算出，槽位相对已显示方向变化时视图每帧调用新增的受保护虚方法
    `PrepareDirection(Direction, Id, bool)`（默认恒真）询问"新方向是否准备好（当前显示所需的资源都有结论）"，准备好才当帧提交（静态层、
    装备层、逐层剪辑、整身/覆盖剪辑、按方向的锚点一起换）；准备期间全部视觉内容停在已显示方向，模拟朝向不受
    影响。仅镜像变化（同槽位、翻转不同）与热转向当帧提交。**`OnDirectionSlotChanged` 改为提交时触发**（此前是期望
    方向变化的当帧），提交后再触发新增的 `OnDirectionCommitted`。契约面纯加法：只读 `DesiredDirection`、
    `DisplayedDirection`、`HasDisplayedDirection`、`HasPendingDirectionSwitch`、`DisplayedFacing`；纯计算
    `ComposeLayersForDirection(Direction)`（与应用路径共用实现）、`GetDistinctDirectionSlots()`、
    `EnsureLayerImage(Id, bool)`（经追踪器预取静态层图并记录结论）。层静态图的加载完成回调统一记录结论，
    只有属于当前已显示层集合的资源才转发给 rig 重新应用。
    测试：`adapters/unity/.../Tests/Runtime/DirectionSwitchAtomic{Repro,Invariant}Tests.cs`。

30. **手感设计/02 第 7 节 + 04 第 2 节（手感落地第 1 波 S5）：步态派生与姿势上下文**——`GaitDeriver`/`GaitThresholds`/`PoseSelector`/`PoseContext`/`IPoseContextSource`。
    - **纯呈现**：只读速度与 `PresentingFeelView`（呈现型视图，读不到判定型字段），不回写判定；步态阈值读**标定前的相对值**（`GetRaw`，`idle_max_ratio` 等是"基础移速倍数"，标定后的绝对值是世界速度），字段未设置/没有手感数据时用 02 第 7 节缺省（0.05 / 0.6 / 无冲刺 / 滞回 0.05）。速度比 = `|velocity| / 该实体的基础移速属性`，由调用方给（运动切片 S2 导出档案后，调用方换成档案值即可，本类型不依赖其导出形式）。
    - **滞回口径（02 没规定落在阈值哪一侧，本版拍板）**：升档在阈值处（与 02 表格无滞回条件逐字一致），降档在"阈值 减 滞回宽度"处。从静止起步/匀速运动与无滞回结果相同，只在阈值附近来回抖动时不闪；一次越过多个阈值（急加速）在一次 `Update` 内逐档收敛；首次观测用无滞回的 `Seed` 定档；手感重算（视图版本号变）后下一次观测读新阈值，冲刺阈值被撤销时正在冲刺的实体落回 run。
    - **`move` 状态下步态为 Idle**（动画状态机判定在动、速度却低于 `idle_max_ratio`，如被推着蹭动）按最慢的 `walk` 取姿势，不用无步态的基础 `move`；其它状态忽略步态。
    - **`AnimClipResolver`（adapters/unity）接入**：新增可选构造重载参数 `IPoseContextSource`；默认剪辑解析改走 `PoseResolver`（回落链 去变体→去武器族→去姿态→去步态→基础键），**没有来源时请求只有"状态 + 战斗姿态"两维，回落链恰好是 `[<state>.combat, <state>]`，与改动前的两级查表逐位一致**（核心层有随机子集的等价用例）；就绪探针只对非基础键咨询、已在播的剪辑视为就绪（同 ADR-0111）。来源的 `ContextChanged` 与姿态变化走同一个 `Refresh` 出口。武器风格/技能覆盖剪辑（`AutoAttackAnim`/`CastAnimOverride`）优先级仍最高，不经姿势解析——"武器覆盖收编进 family 维"的含义是：武器族补齐覆盖剪辑够不到的状态（待机/移动/受击），不取代覆盖剪辑。`UnityViewFactory.AnimStateKeysFor` 把外形声明的、首段为七个基础状态键之一的维度键（如 `move.run`、`idle.combat.2h`）纳入登记/探测键表，`IsCommitCriticalStateKey` 把首段为 `idle/move` 的维度键算作换向提交所需键；没有维度键的外形键表与改动前逐项一致。
    - **本版没有接到引擎的帧循环**：`PoseSelector.Observe` 需要每实体每帧的速度比，视图层目前没有这路输入（视图只有 `SyncPose(pos, facing, height)`，速度需要差分或运动状态的只读速度查询，后者是运动切片 S2 的导出），因此框架装配（`UnityViewFactory`）本版不构造 `PoseSelector`；游戏或后续切片构造它并传给 `AnimClipResolver` 即可生效。

31. **顿帧表现冻结：rig 的可选能力 `IPresentationFreezable`（2026-10-02，手感落地 M2-A，[手感设计/07](../../architecture/手感设计/07_镜头与音画反馈.md) 第 5 节）**：
    - **契约增量（ABI 只加法）**：新增可选接口 `IPresentationFreezable`（`IsPresentationFrozen`、`FreezePresentation(freezeTrail)`、`UnfreezePresentation()`），`SpriteCharacterRig`/`ModelCharacterRig` 实现；`IFrameAnimPlayer` 新增带缺省实现的 `IsPaused`/`SetPaused`（旧实现不实现也能编译，等价于不暂停）。`ICharacterRig` 一个成员都没加。
    - **冻结是幂等的布尔开关**：重复冻结/解冻无副作用，与 `feel.hitstop_started/ended` 的集合语义同口径（同一单位被多次命中延长时没有"冻结计数"要配平）。冻结时：序列帧播放器暂停（帧下标、关键帧、完成回调都不推进，恢复后从暂停点继续）；`ProceduralAnimSequencer` 的位移/缩放/回弹时间轴停住，**闪白不冻**（命中反馈要在顿帧里亮着并按自己的时间衰减），拖尾按 `freezeTrail`（即 `ImpactFreezeLayers.Trail`）；model rig 把动画速率写 0、解冻时写回最近一次请求的速率（冻结期间 `PlayClip` 换剪辑，剪辑照换、速率仍为 0，解冻后用新剪辑自己请求的速率）。
    - **冷路径同热路径**：冻结期间才附上的序列帧播放器（`AttachFrameAnimPlayer`）按当前冻结状态启动；适配层 `UnityFrameAnimPlayer` 的暂停标志存在组件上（内部播放器懒创建，暂停期间才首次 `Play` 的剪辑也按暂停启动）；`UnityRenderer3D` 记 `AnimSpeedOverride`，资源后到时的原地替换视觉内容重放 `PlayAnim` 也保持冻结速率，新的 `PlayAnim` 自带速率并清掉它。
    - **接线**：`PresentationAssembly` 默认把 `OnFreezePresentation/OnReleasePresentation` 落到被击/攻击单位视图的 rig（见 `presentation/assembly/README.md`）；不在名单里的单位不受影响。
    - **范围**：本接口只管 rig 自己持有的时间轴（动画、程序动画原语）；没有实现 `IPresentationFreezable` 的自定义 rig 被静默跳过。粒子/特效不在 rig 里，由特效播放器按宿主单位承接（手感落地 M3-C 已解除原"粒子宿主不随顿帧冻结"的限制，见 `presentation/vfx_sfx/README.md` 判断记录 28）。
    - 复现/不变量：`tests/PresentationFreezeTests.cs`（暂停与恢复的帧下标等价于从未暂停的参考播放器、冻结期间关键帧/完成回调推迟、序列器冻结后闪白照常衰减、sprite/model rig 冻结/解冻幂等与冷路径、无暂停能力的旧播放器静默跳过）；引擎侧 `UnityRenderer3DTests.SetAnimSpeed_Zero_FreezesAnimator_ColdSwapKeepsItFrozen_...`。

- 方向槽位到具体量化索引的对应关系是本模块的默认约定，非拍板内容，见判断记录 1。
（原"裸档位名与 `Id` 格式之间需要一道前缀转换"契约缺口已解决，见判断记录 2。）
- `AnimStateMachine` 的 `jump` 状态没有事件驱动来源（06 事件词汇表当前无 `unit.jumped`/
  `unit.landed`），只能靠 `RequestOverride` 手工触发，见判断记录 10、类型注释"jump 判断记录"。
- `FlashParams` 的具体数值（强度/时长）没有对应的 `flash_profile` 登记表（09 §6.1 只拍板了
  `profileId: Id`，未定义该表结构），`PresentationAssemblyOptions.FlashProfileResolver` 默认恒
  返回 `FlashParams.Default`，见 `presentation/assembly/README.md`、`feedback_binder/README.md`
  判断记录 9。

- **换装姿势桥 `EquipmentPoseBridge`（2026-10-02，手感设计/08 第 1 节）**：订阅 `feel.weapon_changed`，把事件携带的武器族设进 `PoseSelector.SetFamily`（空手事件携带 null 即清除）。桥而不是让 `PoseSelector` 自己订阅：`PoseSelector` 的契约是"不订阅事件、不持有逻辑层写入能力"，事件到武器族这一步单独放在装配层可选择性接入的小类里；桥只读事件，不回头查装备宿主或手感表。`PresentationAssembly` 目前没有装出 `PoseSelector`/`EquipmentPoseBridge`（生产装配接线留给后续装配切片，实验室装置自己装）。

- **步态喂入 `PoseGaitFeeder` 与单位销毁清理（M2-B，2026-10-02）**：见 `presentation/assembly/README.md` M2-B 节。上文"`PresentationAssembly` 目前没有装出 `PoseSelector`/`EquipmentPoseBridge`"在 M1 收口时已不成立（`PresentationAssembly.Pose` 在手感启用时装配）；`EquipmentPoseBridge` 现在也订阅 `entity.destroyed` 清理选择器记账。

- **空中姿势 `AirPhase` 与 `AirPoseFeeder`（M4-V，2026-10-03，ADR-0130 追加决定）**：`PoseContext` 新增 `Air`（`AirPhase`：None/Rise/Fall/Land，缺省 None 时相等性、哈希、`ToString` 与改动前一致）、`IsAirborne` 与 `TryGetAirRequest(stateKey, out AirPoseRequest)`（`jump` 状态取阶段键，`hit`/`attack` 状态在空中时取 `hit.air`/`attack.air[.<family>]`）；`PoseSelector.SetAirPhase` 发布阶段。`AirPoseFeeder`（纯呈现，只读 `IVerticalMotion.AirborneUnits()`）每个 tick 结束时发布：腾空且向上 = Rise，向下或顶点 = Fall，刚落地保持 8 个 tick 的 Land（呈现常量，不进手感档案），之后清回 None。`AnimStateMachine.AttachAirPhaseSource`：腾空时 Idle/Move 进 Jump，阶段清除时 Jump 回运动态，临时状态（受击/攻击）结束时若仍在空中回到 Jump。`PresentationAssembly` 在手感开启且世界装配了竖直轴时创建喂入器（否则空中阶段恒 None）。原已知局限（空中姿势不带步态/变体维度；落地保持窗口不可配）已由 M4-W1b 解除，见下一条。测试：`tests/AirPoseTests.cs`、`presentation/assembly/tests/AirPoseProductionTests.cs`。
- **空中姿势的变体维度与可配落地保持（M4-W1b，2026-10-03，ADR-0130 追加决定）**：(1) **变体维度**：`AirPoseRequest` 增加姿态（只有 `combat` 生成段）、武器族、变体三个可选维度，键形如 `jump.rise.combat`、`hit.air.wounded`、`attack.air.combat.sword`；回落链 = 空中键的维度前缀逐段去尾（先去变体、再去武器族、再去姿态，规则同 `PoseRequest.Chain`）后接固定尾链（`jump`→`idle`、`hit.launch`→`hit`、`attack.<族>`→`attack`）；不带维度的请求链与改动前逐位一致。`PoseContext.TryGetAirRequest(stateKey, inCombat, out)` 新重载对跳跃与受击同样带武器族（两参数重载保持旧行为）；`AnimClipResolver`（Unity）改用新重载并以 `AnimStateMachine.IsInCombatStance` 供姿态。判断：空中专用剪辑缺失时落到既有的通用键，这些通用键自己的战斗姿态/变体细分由地面解析负责，不在空中链里重复。(2) **落地保持可配**：呈现型可选字段 `land_hold_ms`（0..2000 毫秒，缺省无值 = 沿用 8 个 tick，0 = 不播落地姿势）；`AirPoseFeeder` 新构造（带 `IFeelPresentingSource` 与步长秒）按单位解析并经 `FeelCalibration.MillisecondsToTicks` 换算，`PresentationAssembly` 在手感开启且步长可得时接线。测试：`tests/AirPoseTests.cs`（带维度的上下文请求、无维度与两参数重载一致）、`presentation/assembly/tests/AirPoseProductionTests.cs`、`core/foundation/display_info/tests/AirPoseKeyTests.cs`、Unity `AirPoseAnimPlayModeTests`（带维度剪辑的解析）；实验室 `space.air_pose_real`（读真实姿势集行 `display.anim_set.std_dummy_biped`）。
