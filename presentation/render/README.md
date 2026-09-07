# L5 表现层 · render（2.5D 渲染约定 + 纸娃娃/动画）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `render` 行（契约接口名
`RenderConventionHost`）、[09_表现层.md](../../architecture/09_表现层.md) 第 3.1～3.4、4 节：统一
`sortY` 排序、方向量化到方向槽位的镜像回退解析、纸娃娃层合成顺序、影子锚点、高度像素换算；
`sprite` 型 `IView` 的引擎无关骨架 `SpriteViewBase`；`CharacterRig`（09 §4.1）、`AnimState` 七态
状态机（09 §4.2）、命中帧同步（09 §4.3）、序列帧播放器预留接口（09 §4.5）、程序动画八原语
（09 §4.1）——拍板 6（W3a）落地 sprite 型全套引擎无关部分；[ADR-0017](../../architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)（W6 表现能力补齐 A 部分）把 `model` 型
`CharacterRig` 从占位收口为真实实现（层/槽位管理、锚点/挂点查询、八原语映射），`HitFrameReached`
提升进 `ICharacterRig` 契约本身，两种外形类型均已提供；引擎侧真正的三维渲染管线实现（`IRenderer3D`
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
  core/
    RenderLayers.cs            六层的整数层号常量
    RenderOptions.cs             口味配置项（方向档位数、PixelsPerUnit、HitFrameSync 策略）
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
   对应行时保持不变（等价于此前的默认空实现）。`mesh_ref` 不经方向档位换算，是已知简化（见类型
   注释判断记录）。
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
    `HitFrameReached`——`HitFrameReached` 已从 `SpriteCharacterRig` 专属成员提升进 `ICharacterRig`
    契约本身（见判断记录 16），供命中反馈的落地代码（`presentation/feedback_binder` 的
    `HitFrameSyncPolicy`）延迟到这一刻才播放；`LogicDriven`（默认）策略下两种实现均不触发，命中
    反馈沿用 `FeedbackBinder` 收到 `combat.damage_dealt` 立即派发的既有行为。

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
    `HitFrameReached` 从 `SpriteCharacterRig` 专属成员提升进 `ICharacterRig` 接口本身（`event
    Action<Id>? HitFrameReached`），两套实现均提供。`AnimStateMachine` 新增 `StateChangedWithSkill`
    事件（`(entityId, from, to, triggerSkillId: Optional<Id>)`），与既有 `StateChanged` 三元组事件
    同一次切换背靠背触发，`triggerSkillId` 只在 `skill.cast_start` 驱动的切换（进入
    `attack`/`cast`）时非空，支持 09 §4.4 `cast_anim_override` 按技能覆盖查表；既有 `StateChanged`
    事件签名不变，不影响既有订阅方。真正把这些能力接到具体三维渲染引擎（`IRenderer3D` 真实实现）
    与默认装配（`PresentationAssembly` 自动注册 rig 进 `IHitFrameSource`、自动把
    `IWeaponStyleSource` 接进 `AnimClipResolver` 等）仍留给 W6-B，见
    `architecture/落地计划/落地方案与分阶段计划.md`"W6 表现能力补齐"小节。

## 契约缺口

- 方向槽位到具体量化索引的对应关系是本模块的默认约定，非拍板内容，见判断记录 1。
（原"裸档位名与 `Id` 格式之间需要一道前缀转换"契约缺口已解决，见判断记录 2。）
- `AnimStateMachine` 的 `jump` 状态没有事件驱动来源（06 事件词汇表当前无 `unit.jumped`/
  `unit.landed`），只能靠 `RequestOverride` 手工触发，见判断记录 10、类型注释"jump 判断记录"。
- `FlashParams` 的具体数值（强度/时长）没有对应的 `flash_profile` 登记表（09 §6.1 只拍板了
  `profileId: Id`，未定义该表结构），`PresentationAssemblyOptions.FlashProfileResolver` 默认恒
  返回 `FlashParams.Default`，见 `presentation/assembly/README.md`、`feedback_binder/README.md`
  判断记录 9。
