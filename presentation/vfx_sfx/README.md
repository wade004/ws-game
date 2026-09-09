# Presentation.VfxSfx（presentation/vfx_sfx）

职责：VFX/SFX 播放体系（见 [09_表现层.md](../../architecture/09_表现层.md) 第 5 节）——`vfx.def`/
`sfx.def`/`display.weapon_style` 三张表的 schema、`IVfxPlayer`/`ISfxPlayer` 及其默认实现、对象池、
经 DisplayInfo 把逻辑 id 映射到具体 vfx_id/sfx_id、武器表现档案的命中特效覆盖查询。

铁律遵守（09 第 1 节）：本模块只经 `Core.Foundation.EngineAdapter.IRenderer2D`/`IRenderer3D`/`ICamera`/
`IAudio` 播放（P4），不持有任何被误认为权威的逻辑字段（P1），不订阅事件、不写回任何逻辑数据（P3）——
`VfxPlayer`/`SfxPlayer` 是纯粹的"调用方给一个逻辑意图（vfx_id/sfx_id + 挂接目标），本模块查表后经
L-1 播放"的无状态服务，事件订阅与"哪个事件触发哪个播放"的翻译是 `feedback_binder` 的职责。

## 目录

- `contracts/`：`VfxAttachMode`/`VfxAttach`（判别联合）、`VfxDef`/`SfxDef`（09 §5.1/5.2 字段）、
  `IVfxPlayer`/`ISfxPlayer`、`VfxOptions`/`SfxOptions`（池容量/并发上限等策略配置项）、
  `AnchorResolver`/`EntityPositionResolver`（注入的窄契约，见下"契约缺口"）、`WeaponStyleDef`/
  `IWeaponStyleResolver`（09 §4.4）、`IWeaponStyleSource`/`MainHandWeaponTemplateResolver`（ADR-0017
  决策 e，"实体 → 武器风格引用"前置查询，见判断记录 12）、`IPresentationDiagnostics`（本模块与
  `feedback_binder` 共用的表现层诊断出口，语义同 `Core.Foundation.Expr.IExprDiagnostics`）。
- `core/`：`VfxPlayer`/`SfxPlayer`（默认实现）、`VfxPool`（对象池）、`DisplayInfoResolver`（09 §5.6）、
  `WeaponStyleResolver`（09 §4.4 最小实现）、`EquipmentWeaponStyleSource`（`IWeaponStyleSource` 默认
  实现，ADR-0017 决策 e，见判断记录 12）。
- `schema/`：`VfxSfxSchemas`——`vfx.def`/`sfx.def`/`display.weapon_style` 三张表的 `TableSchema`
  登记（与 `Core.Foundation.DisplayInfo.DisplaySchemas` 同一惯例：只登记 schema，不接入
  `data/_sample/` 真实数据集，见下"不负责什么"）。
- `tests/`：见验收测试列表。

## 判断记录

1. **`socket` 挂接已解决（缺口 13）**：`presentation/common/contracts/IModelHandleProvider.cs`
   （`IView` 的可选能力接口，`TryGetModelHandle()`）+ `ModelHandleResolver` 委托补齐了此前缺的
   "host 实体自己的 `ModelHandle`"这一信息——`PresentationAssembly` 用 `ViewBinder.TryGetView` +
   `is IModelHandleProvider` 判定接线。`VfxPlayer` 新增可选构造参数 `renderer3D`/
   `modelHandleResolver`：两者均注入且该实体的 View 返回非空句柄时，`Spawn` 改经
   `IRenderer3D.CreateModelInstance` 创建子模型并 `AttachToSocket` 真挂接（`Stop` 对称走
   `Detach`/`DestroyModelInstance`）；任一未注入或该实体查不到句柄时，才退回下方"降级为
   world"路径（诊断文案不变）。`SpriteViewBase`（sprite 型外形）不实现
   `IModelHandleProvider`——sprite 型没有模型实例，这类实体仍走 world 降级，符合预期，不是遗留
   缺口。
2. **`ISfxPlayer.Play` 的 `at` 参数已由 ADR-0016 解决**：`IAudio.PlaySfx` 增加了
   `position: Optional<Vec2>` 参数（决策 3），`SfxPlayer.Play` 现把 `at` 直接透传给
   `IAudio.PlaySfx(position)`；不支持空间音频的引擎侧实现可以忽略该参数按无空间方式播放，但不得
   因该参数报错或拒绝播放。
3. **`sfx.def.priority` 的大小方向**：09 原文未定义，本实现采用"数值越大优先级越高"，未声明
   （`Priority` 为 `null`）按 `int.MinValue`（最低优先级）处理。
4. **对象池"超容量按 lifetime 最旧回收"的判定标准**：解释为"剩余存活时间最短"而非"最早插入"
   （提前打断一个反正很快会自然到期的实例，观感损失更小）；未声明 `lifetime` 的实例视为剩余时间
   无穷大，只在全部实例都未声明 lifetime 时才按插入顺序退化淘汰。
5. **`DisplayInfoResolver.ResolveVfx/ResolveSfx` 的 `slot` 参数**：任务书拍板签名
   `ResolveVfx(Id logicalId, string slot = "default")`，但 `display.map`（04 第 7.1 节）的
   `vfx_id`/`sfx_id` 是单值字段，不是"槽位 → id"映射；非 `"default"` 槽位一律返回 null（不是
   错误，是为未来 `display.map` 扩展多槽位 vfx/sfx 预留调用方签名）。
6. **`IWeaponStyleResolver` 只覆盖 vfx 相关查询**：`auto_attack_anim`/`cast_anim_override` 指向
   动作剪辑，属于 CharacterRig/动画状态机（09 第 4 节）职责范围，不在本模块（vfx_sfx）接口内；
   `WeaponStyleDef` 仍如实携带这两个字段供上游模块使用。
7. **资源首次加载责任已由 ADR-0016 解决，首次引用时排队等待加载完成才播放（外部审核阻塞项 4
   收口，2026-09-07）**：`VfxPlayer`/`SfxPlayer` 均可选注入 `IResourceLoader`，`Spawn`/`Play` 首次
   引用某个 `resource_ref`（含 `sfx.def.variants` 命中的具体变体）时触发一次
   `ResourceKind.Effect`/`ResourceKind.Audio` 的 `LoadAsync`（同一 id 此后永远不再重复触发，含加载
   失败的情形——失败不重试）；未注入时保持不主动触发任何加载的既有行为。**此前**这一步只是
   fire-and-forget（不管加载成功与否都在同一次调用内立即 `EmitParticle`/`PlaySfx`），首次引用一个
   真正异步加载的资源会在资源就绪前就播放（复现为"首次施法命中特效/音效不播放/播放通用退化效果"）。
   **现在**资源尚未加载完成时不立即播放，改为排队（`VfxPlayer.PendingSpawn`/`SfxPlayer.PendingPlay`）
   等待 `LoadAsync` 回调补播放；同步加载器（测试桩/引擎缓存命中）在同一次调用栈内完成时，
   `Spawn`/`Play` 仍能同步返回真实句柄，不退化调用方体验。排队等待有超时（`VfxOptions`/
   `SfxOptions.FirstLoadTimeoutSeconds`，默认 5 秒），超时丢弃并记一条诊断，不是无限期等待——
   `VfxPlayer`/`SfxPlayer` 均经各自的 `Update(dt)` 累计倒计时（`IVfxPlayer` 契约本就有该方法；
   `ISfxPlayer` 起初没有，本条落地时改为在下一次任意 `Play` 调用开头惰性扫过期项，判断记录 10
   之后新增了独立的 `Update` 方法，见该条——本条这里不重复描述，避免与判断记录 10 各自维护一份
   过期或最新的说法）。

8. **GP-03 收口（第四方深度审核）：三处生产引导现在每帧真正调用 `VfxPlayer.Update`**——
   `Update(dt)` 承担两件事：对象池 lifetime 到期回收（判断记录 4）与首次异步加载的 pending 超时
   扫描（判断记录 7），两者都依赖被生产代码逐帧调用才会推进；原实现只有测试直接调用过
   `Update`，`adapters/unity/.../GameFoundationBootstrap.cs`、
   `adapters/unity/.../FrameworkResidentHost.cs`、`games/_template/Runtime/GameBootstrap.cs` 三处
   生产帧循环都从未调用，导致循环特效不按 `lifetime` 停、迟迟不回调的加载请求永久积压（不会
   触发超时诊断，也不会被清理）。现在三处 Bootstrap 的每帧推进链路里都加入 `Vfx.Update(dt)`
   调用，与既有的 `Sfx` 惰性扫过期项（判断记录 7）一起构成完整的逐帧生命周期推进。见
   `VfxPlayerTests.cs`。

9. **N17 收口（外部审核 68c9bed）：`IVfxPlayer`/`ISfxPlayer` 新增 `PendingSpawnCountChanged`/
   `PendingPlayCountChanged` 事件**——判断记录 7 的首次加载排队只提供了 `PendingSpawnCount`/
   `PendingPlayCount` 两个纯轮询属性，`presentation/feedback_binder.FeedbackBinder` 需要在"资源
   真正加载完成那一刻"补一次完成检查才能正确发出 `PlaybackFinishedEvent`（见
   `feedback_binder/README.md` 判断记录 12），纯轮询属性没有任何"变化时机"可以驱动这个检查。
   `VfxPlayer.OnResourceLoadCompleted`/`SfxPlayer.OnResourceLoadCompleted`（含加载失败分支）与
   `SfxPlayer.SweepTimedOutPendingPlays`（超时清理）现在都会在对应 pending 集合发生变化时触发
   各自的事件；事件只是"提示重新读取 `PendingSpawnCount`/`PendingPlayCount`"，不携带具体数值、
   不保证触发时已经归零，调用方（`CompositeFeedbackSink`）必须自行重新读取判断真实状态。见
   `IVfxPlayer.cs`/`ISfxPlayer.cs`/`VfxPlayer.cs`/`SfxPlayer.cs`。

10. **C07 收口（外部审核 7e63d66）：补上判断记录 9 遗漏的"超时"这一路径，`ISfxPlayer` 新增独立
    `Update` 时钟入口**——判断记录 9 只覆盖了"加载成功/失败"两种结局触发事件，`VfxPlayer.Update`
    自己的首次加载超时清理分支（判断记录 7 的 `FirstLoadTimeoutSeconds` 到期，典型场景：
    `resource_ref` 拼写错误或资源确实缺失，加载请求永远不会回调）只从 `_pendingSpawns` 移除、记
    诊断，从未触发 `PendingSpawnCountChanged`——`PendingSpawnCount` 由非零变零/减少却没有对应
    信号，下游（`CompositeFeedbackSink`/`FeedbackBinder`）收不到"该重新检查一次是否已经播完"的
    通知，`wait_for_playback` 链在这一分支下会永久卡住。`SfxPlayer` 一侧问题更深：判断记录 7 提到
    的"惰性扫过期项"（`SweepTimedOutPendingPlays` 只在下一次任意 `Play` 调用开头被调用）意味着
    `ISfxPlayer` 完全没有独立于 `Play` 的时钟入口——若节奏门已经关闭、此后没有任何新的 `Play`
    调用（这一步唯一的音效就是这条冷资源，没有后续动作），卡死的加载请求永远不会被扫到，
    `PendingPlayCountChanged` 永远不会因超时而触发。现在：`VfxPlayer.Update` 的超时分支补发一次
    `PendingSpawnCountChanged`（同 `OnResourceLoadCompleted` 一致的"只要本次调用确实摘除过任何一
    项就触发一次"）；`ISfxPlayer` 新增 `Update(double dt)`（同 `IVfxPlayer.Update` 签名，供引擎侧
    统一逐帧驱动——生产接线见 `adapters/unity/.../FrameworkResidentHost.cs` 新增的
    `Presentation.Sfx.Update(dt)` 调用，与既有的 `Presentation.Vfx.Update(dt)` 各自独立、同一惯例），
    `SfxPlayer.Update` 直接复用既有 `SweepTimedOutPendingPlays`（内部已经在真正摘除任何一项时触发
    事件），不依赖下一次 `Play` 调用。见 `IVfxPlayer.cs`（`Update` 判断记录补充）/`ISfxPlayer.cs`
    （新增 `Update`）/`VfxPlayer.cs`/`SfxPlayer.cs`，`VfxPlayerTests.cs`
    （`Update_TimesOut_TriggersPendingSpawnCountChanged_Once`）、`SfxPlayerTests.cs`
    （`Update_TimesOut_TriggersPendingPlayCountChanged_WithoutAnyFurtherPlayCall`）、
    `presentation/feedback_binder/README.md` 判断记录 13（端到端真实链路用例）。

12. **ADR-0017（W6 表现能力补齐 A 部分，2026-09-08）：新增 `IWeaponStyleSource` 补齐"实体 → 武器
    风格引用"前置查询**——判断记录 6 指出 `IWeaponStyleResolver` 已知 `weaponStyleRef` 之后能查覆盖
    表，但"给定一个实体，它现在算哪一个 `weaponStyleRef`"这一前置查询此前没有任何实现；新增
    `contracts/IWeaponStyleSource.cs`（接口 + `MainHandWeaponTemplateResolver` 委托）与
    `core/EquipmentWeaponStyleSource.cs`（默认实现）：订阅 `item.equipped`/`item.unequipped` 事件
    失效按实体缓存的结果，取该实体主手武器物品模板 id 后经既有 `display.map.logical_id`（物品模板
    对应的 `display.map` 行，其 `logical_id` 就是模板 id 本身）查 `IDisplayInfoRegistry.Lookup` 拿
    `WeaponStyleRef`；"实体现在装备的主手武器物品模板 id"本身按窄契约委托
    `MainHandWeaponTemplateResolver` 注入，本模块不直接依赖 `core/carriers/item` 的具体装备宿主
    实现（避免表现层反向耦合到某一种装备存储形状），由装配层按具体游戏使用的装备宿主实现构造并
    注入。未新增任何数据表字段——`display.map.weapon_style_ref` 已足以承载这条关联。

13. **PRES-110-01 根治（第十二轮审核 ac3b622，2026-09-09）：`EquipmentWeaponStyleSource` 新增订阅
    `save.loaded`，读档后整表清空缓存**——判断记录 12 的缓存只按 `item.equipped`/`item.unequipped`
    两个事件失效，但 `Core.Foundation.SaveSystem.SaveSystem.Load` 把"逐段 Load + 失败回滚"整段包在
    `IEventBus.SuppressDispatch` 抑制作用域内（读档不是业务事件），`EquipmentPersistable.Load` 在此
    作用域内调用真实 `EquipmentHost.Equip`/`Unequip` 重放装备联动时正常派发的这两个事件被直接丢弃，
    本类型两个失效订阅永远收不到——同图读档（同一批实体 id 延续、不经过 View 重建）后缓存仍保留
    读档前的旧值，与已经真实变化的 `EquipmentHost` 装备状态不一致（真实探针：
    `architecture/落地计划/audit-ac3b622-20260909/core/repro/FollowupCoreProbe.cs`
    `RunWeaponStyleCacheAfterLoad`／`followup-core-probe.log`
    `WEAPON-STYLE-CACHE-SAME-MAP-LOAD`）。`SaveSystem.Load` 在该抑制作用域<b>外</b>正常派发的
    `SaveLoadedEvent`（`save.loaded`）不受影响——对照 `presentation/view_binding/core/ViewBinder.cs`
    的 `OnSaveLoaded` 做法，本类型额外订阅 `save.loaded` 并整表清空缓存（不按实体逐个失效：读档时
    哪些实体的装备发生了变化对本类型不可见，清空后下一次 `GetWeaponStyleRef` 对该实体重新经
    `MainHandWeaponTemplateResolver` 查一次真实状态即可）。见
    `core/EquipmentWeaponStyleSource.cs`、`tests/EquipmentWeaponStyleSourceTests.cs`
    （`PRES110_01_SaveLoadedEvent_ClearsCache_NextQueryReResolvesRealState`/
    `PRES110_01_RepeatedSaveLoadedEvents_NeverReuseStaleValueAcrossReloads`）。
    <br/>判断记录（`presentation/render/core/EquipmentVisualSource.cs` 未同批处理，不算"同类"一并
    根治）：审核任务书要求核对本模块外"其它按装备事件缓存的来源"是否同类——`EquipmentVisualSource`
    同样按 `item.added`/`item.equipped`/`item.unequipped` 三个事件维护
    `_visualByItemInstanceId`，在同一次 `save.loaded` 抑制作用域问题下也会失去更新，但它与
    `EquipmentWeaponStyleSource` 不是同一种缺陷形状：后者是一个纯粹的"无状态查询结果缓存"（下一次
    调用只要重新查一遍真实状态即可自愈，`GetWeaponStyleRef` 本身不产生任何外部可观察副作用），清空
    缓存就是完整根治；前者是一份已经"应用"到具体 `IView` 实例（经 `view.OnEvent(ItemEquippedEvent)`
    调用把外观贴到 Mesh/Sprite 插槽上）的状态日志——真正的根治需要让已绑定的 View 重新走一次
    `EquipmentVisualSource.ReplayEquippedForUnit` + `view.OnEvent` 才能让 Unity 侧的挂点/外观贴图
    与新装备状态对齐，这属于视图重放（Mesh 冷加载/挂点外观），本审核任务书边界条款（本报告"结论与
    边界"一节）明确排除在 PRES-110-01 范围外——"该结论只覆盖风格缓存与真实装备状态不一致，不外推为
    Mesh 冷加载、HUD 刷新或 Unity 屏幕外观缺陷；这些链路仍需各自端到端证据"。本轮已核对
    `EquipmentVisualSource` 存在同源风险但未展开 Unity 端到端证据，因此不在本轮改动，留给后续以该
    模块自己的复现证据立项（不是遗漏，是刻意维持审核范围边界）。

14. **ADR-0019 F1c 判断记录（`display.weapon_style.cast_anim_override`/`impact_vfx_override`
    不登记子结构）**：两者均为 `Id → Id` 动态键映射（按技能 id 覆盖动作剪辑/命中特效），是真正的
    Map（键为任意 `skillId`，值同构），按 ADR-0019 通用规则 5 保持"存在且是对象"，不为此扩展
    契约到新的 `FieldKind`。

## 不负责什么

- 不接入 `data/_sample/`：本任务不新增示例数据文件，`schema/VfxSfxSchemas` 只声明表结构，测试用
  `InMemoryDataSource` 内联 JSON 验证 `FromRecord` 的解析正确性。
- 不实现 `CharacterRig`、纸娃娃层、序列帧动画播放器（09 第 4 节，另一任务范围）。
- 不实现 `presentation/common`（`IView`/`PresentationEventKeys` 等）——若集成时该模块已提供
  `AnchorResolver` 或等价类型，以 `presentation/common` 为准，本模块的 `AnchorResolver`/
  `EntityPositionResolver` 委托类型可直接替换为对其类型的适配。
