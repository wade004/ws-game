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
  `IWeaponStyleResolver`（09 §4.4）、`SfxFeelLayer`/`SfxFeelLayers`（手感音效层，判断记录 27）、`IWeaponStyleSource`/`MainHandWeaponTemplateResolver`（ADR-0017
  决策 e，"实体 → 武器风格引用"前置查询，见判断记录 12）、`IPresentationDiagnostics`（本模块与
  `feedback_binder` 共用的表现层诊断出口，语义同 `Core.Foundation.Expr.IExprDiagnostics`）。
- `core/`：`VfxPlayer`/`SfxPlayer`（默认实现）、`VfxPool`（对象池）、`DisplayInfoResolver`（09 §5.6）、
  `WeaponStyleResolver`（09 §4.4 最小实现）、`EquipmentWeaponStyleSource`（`IWeaponStyleSource` 默认
  实现，ADR-0017 决策 e，见判断记录 12）、`SfxLayerIndex`（手感音效分层索引，判断记录 27）。
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

14. **ADR-0024 第二批登记（`display.weapon_style.cast_anim_override`/`impact_vfx_override`，取代
    下方已废止的 ADR-0019 F1c 判断记录）**：两者均为 `Id → Id` 动态键映射（按技能 id 覆盖动作剪辑/
    命中特效），现登记为 `MapSchema.FreeKeyed`（键为技能 id，本模块不静态耦合 `skill.def` 表结构；
    值为 `FieldKind.Id`，与本表 `auto_attack_anim`/`swing_vfx` 两个同类字段一致，只做格式校验，不做
    引用完整性检查），与 `WeaponStyleDef.ParseIdMap` 对键值均 `Id.TryParse`、非法即抛
    `DataFieldException` 的解析代码逐字段核对一致。
15. **VFX anchor/socket 持续跟随根治（第十四轮审核 c9ff301，2026-09-10）**：此前 `Spawn` 对
    `attach_mode: anchor` 只在生成那一刻解析一次挂接目标的世界坐标就 `EmitParticle`，`Update`
    只推进对象池与首次加载超时，不会随挂接目标继续移动而重新定位——本模块契约面（`IVfxPlayer`）
    不变，改为新增一个引擎适配层按需实现的可选能力 `IParticleRepositioner`
    （`SetParticlePosition(ParticleHandle, Vec2)`，见 `contracts/IParticleRepositioner.cs`），
    `VfxPlayer` 构造期以 `(_renderer2D as IParticleRepositioner)` 探测，未实现时保持改动前"生成后
    静止"的行为。`Update` 新增 `UpdateFollowTargets`：对 `attach_mode: anchor` 与
    `attach_mode: socket` 因缺可用模型句柄而降级为 world 的两类活动实例，每帧重新解析一次位置
    （前者经 `AnchorResolver`，查不到退回 `EntityPositionResolver`；后者直接经
    `EntityPositionResolver` 跟随宿主实体本身）并经 `IParticleRepositioner` 移动过去；两个解析器都
    查不到（通常是实体已销毁）时结束该特效（`Stop`），不留悬空静止实例。**真正挂接成功的
    `socket`**（判断记录 1，提供了 `renderer3D`/`modelHandleResolver` 且该实体查到句柄）不受影响
    也不需要接入本机制——`IRenderer3D.AttachToSocket` 做的是引擎侧真实父子挂接，子实例的变换随
    父挂点持续变化，天然持续跟随。对应单测：`tests/VfxPlayerFollowTests.cs`
    （`FollowCapableStubRenderer2D` 验证跟随/降级路径/实体销毁三类场景）；真实引擎实现见
    `adapters/unity` 包 README"VFX anchor/socket 持续跟随"一节。

16. **PRES-118-SFX 勘误（第十八轮审核）：判断记录 10 提到的"生产接线"此前只在 `FrameworkResidentHost`
    一处真正落地，`GameFoundationBootstrap`/`games/_template` `GameBootstrap` 两处的 `OnFrameTick`
    从未调用过 `Presentation.Sfx.Update(dt)`**——判断记录 10 的原文只举了
    `FrameworkResidentHost.cs` 一个例子作为"生产接线"的代表，未逐一核对全部三个生产装配入口，
    实际上另外两处始终缺失这一步：连续两次播放同一个缺失音效资源时，第二次请求没有任何未来回调可
    等（`SfxPlayer._pendingResourceLoads` 已记录过该资源 id，不会再次触发加载），只能靠
    `ISfxPlayer.Update` 的超时扫描清理，两处入口没有任何代码驱动它时，该请求永久卡在 pending。
    根治不改本模块任何契约/实现，改为在 `presentation/assembly` 新增
    `PresentationAssembly.UpdatePlaybackMaintenance` 统一逐帧维护入口（`Feedback.Update`/
    `Vfx.Update`/`Sfx.Update` 三步收敛为一处），三个生产装配入口现全部改为调用该方法，不再各自
    罗列这份清单，避免未来再次出现"举一个例子当作全部已接线"的文档漂移。详见
    `presentation/assembly/README.md` 判断记录"PRES-118-SFX 根治"。

17. **ADR-0074 粒子发射混合模式**：`vfx.def` 新增可选字段 `blend_mode`（`alpha`/`additive`，缺省
    `alpha`），`VfxDef` 强类型同步新增 `BlendMode` 属性（新增 6 参构造函数，旧 5 参构造函数转发，
    行为不变）；`Spawn`/`QueuePendingSpawn`/`OnResourceLoadCompleted` 三处 `EmitParticle` 调用点
    统一改走 `Core.Foundation.EngineAdapter.IRenderer2D` 新增的带混合模式参数重载（该重载带默认
    实现，转发到既有 3 参重载并固定按 `Alpha` 处理，未实现该重载的既有 `IRenderer2D` 实现方不受
    影响）。混合模式是发射时固定属性，不经 `SetShaderParam` 或播放期间动态修改，详见
    [ADR-0074](../../architecture/adr/0074-粒子发射混合模式.md)。
18. **ADR-0075 `StopInternal` 幂等修复**：为配合 `feedback_binder` 新增的 `stop_vfx` 动作（该动作
    对"查不到在播实例"要求静默无操作，见 ADR-0075），发现并修复 `VfxPlayer.StopInternal` 既有的
    非幂等缺陷——此前对已经不在 `_handleCategory` 中的句柄（已被对象池自然回收/已被重复停止）仍
    无条件调用 `IRenderer2D.StopParticle`，命中 `StubRenderer2D`/`UnityRenderer2D` 等实现"句柄已
    销毁或不存在"的防御性异常。现改为先查 `_handleCategory.Remove(handle)` 的返回值
    （`wasAlive`），只在确实移除成功时才转发 `StopParticle`；`_followTargets.Remove` 与 socket
    型模型句柄的清理路径不受影响（无论 `wasAlive` 与否都执行，因为它们各自已有独立的存在性判断）。
    这一修复使本模块自身的 `Stop(handle)`（面向已知句柄的既有公开入口）与新增的"按
    `(vfx_id, entity)` 键查找句柄后转发 `Stop`"路径（`CompositeFeedbackSink`，见
    `feedback_binder` README）在"目标已经自然过期"场景下行为一致（都不抛异常），回归用例见
    `tests/VfxPlayerTests.cs`（新增 blend_mode 两条）与 `presentation/feedback_binder/tests/
    CompositeFeedbackSinkTests.cs`（`StopVfx_AfterNaturalLifetimeExpiry_*`，用真实
    `vfx.Update(dt)` 触发自然回收后再调用停止路径）。

19. **ADR-0083：`SfxPlayer` 新增单调累计播放诊断 `ISfxPlaybackDiagnostics`，定位消费方第
    二十五/二十六批"`play_sfx` 确认已派发但轮询不到播放"**——沿 1.67.0 CHANGELOG 记录的两轮
    未复现继续排查，本轮实测（`tests/SfxColdLoadTimingTests.cs`）+ 通读 `Play`/
    `OnResourceLoadCompleted`/`SweepTimedOutPendingPlays` 三处代码路径，结论：
    - `sfx.def` 占位音效本身极短（`assets/_placeholder/sfx/ui_click_01.wav` 实际 PCM 数据
      2205 帧/44.1kHz=0.05 秒，`ui_open_01.wav` 6615 帧=0.15 秒，见
      `toolchain/gen_placeholder_assets.py` `synth_sweep`/`normalize_peak`），"正在播放"这一
      引擎侧瞬时状态本就只能被观测这么短的一段时间——`adapters/unity` 侧
      `UnityAudio.ReclaimFinishedSfxSlots` 只在 `AudioSource.isPlaying` 变 `false`（即 clip
      自然播完）时才回收池位，可观测窗口长度就等于音效本身时长，不存在额外的"提前结束"。
    - 冷资源首次引用到真正调用 `IAudio.PlaySfx` 之间的墙钟延迟，实测（同构复刻
      `UnityResourceLoader.LoadAsync` 的"后台线程 `Task.Run` 读字节 + 并发队列 + 按帧 `Tick`
      消费"架构，见 `tests/SfxColdLoadTimingTests.cs` 判断记录）单次运行 19.08ms（frames=1），
      量级远小于消费方 0.4 秒轮询窗口——该架构模式本身不会系统性地突破这个窗口。
    - 冷加载完成后是**补播放**（`OnResourceLoadCompleted` 成功分支恰好调用一次
      `IAudio.PlaySfx`，用例 `Play_ResourceNotYetLoaded_DoesNotPlayImmediately_
      PlaysExactlyOnceAfterLoadCompletes` 钉死），只有加载失败/`FirstLoadTimeoutSeconds`
      到期两种结局才丢弃且不重试；三条丢弃路径（`sfx.def` 未登记、加载失败、加载超时）均已各自
      伴随一条 `IPresentationDiagnostics.Warn` 文本，逐处核对后**没有发现任何静默吞掉播放请求、
      不留诊断的分支**——`MakeRoomIfNeeded` 的同层抢占停止是一个相邻但不同的既有行为（提前结束
      一次已经成功开始、已经计入播放的音效，不是"从未播放"），该分支同样不记诊断，本轮一并记录
      在案，不在本次范围内改动（不改变既有播放语义，见 ADR-0083"不解决的问题"）。
    - **综合结论**：现有证据更支持"确实播放了，只是轮询窗口太短/轮询节奏抓不到瞬态"（选项 b），
      不支持"播放压根没发生"（选项 a，被本模块既有确定性用例与两轮 PlayMode 复现共同排除）；
      "冷加载本身把开始时间推迟到超出消费方轮询窗口之外"（选项 c）在本轮实测的延迟量级下不成立，
      但不能排除消费方真实机器/工程存在本轮未接触到的额外延迟来源——**证据不足以完全排除 c 与
      其它未知因素，如实记录为未能完全定论**，见 ADR-0083。
    - 新增 `Presentation.VfxSfx.Contracts.ISfxPlaybackDiagnostics`（播放请求/开始/丢弃三个单调
      计数 + 最近一次播放记录），`SfxPlayer` 新增只读属性 `PlaybackDiagnostics`（构造期无条件
      自建，不改变现有 `diagnostics`/`resourceLoader` 等既有可选构造参数），经
      `PresentationAssembly.SfxPlaybackDiagnostics` 转发到装配根——契约形状、是否接入
      ADR-0042 统一转发集线器（本次刻意不接入，结构性排除）等取舍见该接口类型注释与
      `presentation/assembly/README.md` 对应判断记录。纯加法，不改变任何既有播放语义与既有
      公开方法签名行为。回归用例：`tests/SfxPlayerTests.cs`
      `PlaybackDiagnostics_ResourceAlreadyLoaded_*`/`PlaybackDiagnostics_UnknownSfxId_*`/
      `PlaybackDiagnostics_ColdResource_*`（三条丢弃路径 + 一条冷加载补播放路径）。

20. **[ADR-0089](../../architecture/adr/0089-循环音效与stop_sfx动作.md)：`sfx.def` 新增可选
    `loop: bool`（缺省 false），`ISfxPlayer` 新增 `PlayAttached`/`StopAttached` 按
    (sfxId, entityId) 跟踪循环音效实例**：`SfxDef` 新增 `Loop` 属性 + 带该参数的构造重载（旧五参
    构造转调，`FromRecord` 未提供该字段时缺省 false）；`Core.Foundation.EngineAdapter.IAudio.PlaySfx`
    新增带 `loop` 的默认接口成员重载（默认体转调旧四参签名、忽略 `loop`），`SfxPlayer.Play` 改为
    按 `def.Loop` 传参；`Adapters.Stub.StubAudio`（`SfxPlayback` 新增 `Loop` 只读字段）与
    `Adapter.Unity.EngineAdapter.UnityAudio`（`AudioSource.loop = true`，每次 `RentSlot` 复用旧
    池位都会显式重新赋值，避免循环标志跨播放串位）均已覆写为真正落地。`ISfxPlayer` 新增默认接口
    成员 `PlayAttached(sfxId, entityId, at)`/`StopAttached(sfxId, entityId)`（默认体分别退化为
    普通 `Play`/空操作，ABI 只加法），`SfxPlayer` 显式覆盖：只有 `def.Loop=true` 才登记
    `(sfxId, entityId) -> SfxHandle` 键（一次性音效退化为普通 `Play`，不建键、不影响既有行为）；
    同键已在播时 `PlayAttached` 幂等返回已登记句柄，不叠播第二个实例；`StopAttached` 查到即
    `Stop` 并摘除，查不到静默忽略、不写诊断（同判断记录 18"目标已经自然过期"/ADR-0075 `stop_vfx`
    "没有在播实例是正常时序"同一惯例）。实体销毁（NF2 补齐，取代原"循环音效必须靠内容侧显式 `stop_sfx` 终止"的边界）：
    `SfxPlayer` 新增 7 参构造（末位 `EntityPositionResolver? entityPositionResolver`，原 6 参构造保留并等价传 null），
    `PresentationAssembly` 把与 `VfxPlayer` 同一个解析器接进来；`Update(dt)` 每帧对仍登记着的
    `(sfxId, entityId)` 键询问解析器——返回 null（实体已销毁）就走 `StopAttached` 同一出口停音并摘键。
    没有注入解析器（null）时行为与此前逐字节相同（只靠显式 `stop_sfx`）。不新建"实体销毁"监听机制，
    复用 `VfxPlayer.UpdateFollowTargets` 同一个存活信号。回归用例：
    `tests/SfxAttachEntityLifetimeTests.cs`（实体销毁后循环音效在下一次 `Update` 停止、实体仍在时不停、
    同 sfx 不同实体互不影响、无解析器时不变）；其余回归用例：`tests/SfxPlayerTests.cs`
    `Play_LoopDef_PassesLoopTrueToAudio`/`PlayAttached_LoopDef_RegistersKey_AndStopAttached_
    StopsIt`/`PlayAttached_LoopDef_SameKeyAlreadyPlaying_IsIdempotent_DoesNotStackNewInstance`/
    `PlayAttached_NonLoopDef_DoesNotRegisterKey_StopAttachedIsNoOp`、`tests/
    VfxSfxFromRecordTests.cs` `SfxDef_FromRecord_ParsesExplicitLoopTrue`、
    `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UnityAudioTests.cs`
    `PlaySfx_LoopTrue_SetsAudioSourceLoopTrue`（PlayMode，`AudioSource.loop == true`）。

21. **缺陷修复（2026-09-26，消费方第三十五批阻塞项）：`PlayAttached` 冷加载路径不登记 attach
    键**——判断记录 20 落地时，`_activeByAttachKey` 只在 `Play` 同步返回真实句柄那一刻才写入；引擎
    适配层的音频解码永远异步（判断记录 17"外部审核阻塞项 4"同款机制），循环音效在进程内第一次
    `PlayAttached` 必然先走"排队等待首次加载完成"这条路径，起播成功但键从未登记过，`StopAttached`/
    `stop_sfx` 因此永远查不到、循环音效停不下来，再次触发还会叠播第二个实例。改为在排队那一刻
    （早于任何 `IResourceLoader.LoadAsync` 回调）就把键登记为"排队中"态，真正播放成功后升级为
    "已在播"态；`StopAttached` 对"排队中"态改为取消这次排队（加载完成后不再补播放）并立即摘键，
    对"已在播"态仍是 `Stop` 并摘键；同键幂等覆盖两态（排队中/已在播都不会叠播或重复排队）；任何
    路径停止一个被跟踪的句柄（`Stop(handle)`、同层抢占）都经反向索引一并摘键，避免幂等检查返回
    一个已经停止播放的死句柄。契约签名、既有行为（非循环退化为普通 `Play`、查不到静默忽略）不变。
    回归用例：`tests/SfxPlayerTests.cs`
    `PlayAttached_ColdLoad_QueuesAttachKey_StopAttached_StopsPlaybackAfterLoadCompletes`（复现，
    修复前先证真红）/`PlayAttached_ColdLoad_StopAttachedBeforeLoadCompletes_CancelsQueuedPlay_
    AndIsIdempotentWhilePending`（不变量，含阳性对照）；
    `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/
    SfxAttachColdLoadPlayModeTests.cs`
    `PlayAttached_ColdLoopSfx_ThenStopAttached_StopsRealAudioSource`（PlayMode，真实
    `UnityResourceLoader`/`UnityAudio` 上验证，含阳性对照）。见
    [ADR-0089](../../architecture/adr/0089-循环音效与stop_sfx动作.md)"后果"节
    2026-09-26 追加。

22. **[ADR-0105](../../architecture/adr/0105-一次性音效播完即释放同层并发名额.md)（消费方第五十四批）：
    一次性音效播完即释放同层并发名额**——根因：`IAudio` 没有"句柄是否已播完"查询，`_activeByLayer`
    只在 `Stop`/同层抢占时释放，已自然播完的一次性音效永久占名额；层累计播满 `DefaultMaxConcurrent`
    后"永远满员"，同帧先挥击（低优先级）后命中（高优先级）时挥击声被 `MakeRoomIfNeeded` 选为被抢占者
    当场 `StopSfx`（`PlayStartedCount` 照加、`PlayDroppedCount` 不加、`LastPlay` 同帧被覆盖，与消费方
    全部黑盒读数吻合）。修法：`IAudio.IsSfxPlaying`（默认接口成员，默认 null）；`Update`/
    `MakeRoomIfNeeded` 计数前经唯一出口 `ReleaseFinishedOneShots` 对非循环记账逐个询问——false 释放、
    true 保留、null 才退回 `SfxOptions.OneShotLayerSlotHoldSeconds`（默认 2 秒，`Update(dt)` 累加的
    表现时钟，不读系统时间）；只摘记账、不调用 `StopSfx`；冷加载补播放与热路径同一套记账，
    `StartedAt` 取真正 `PlaySfx` 那一刻；循环音效不释放。设计决定（后端回报 null 时的推定）：`IsSfxPlaying` 回报 null（后端
    答不出"是否已播完"）时只能按 `OneShotLayerSlotHoldSeconds` 推定——更长的一次性音效尾段不计数
    （上限变松、绝不会被提前停止），更短的音效播完后到期前仍占名额，从不驱动 `Update` 时不会到期；
    理由：推定的误差方向是"宁松勿紧"，不会误停还在响的音效，而真正需要精确的后端（Unity、桩）都已回报
    true/false，推定只是第三方后端的兜底，没有比"后端自己回报"更精确的来源可补。回归：`presentation/assembly/tests/AutoAttackSwingSfxPreemptionTests.cs`
    （生产装配级复现，引擎回报/回退两支，修复前均第 4 轮红）、`tests/SfxLayerSlotHoldTests.cs`（9 例：
    引擎回报 false 即释放且不等保留时长、回报 true 压过保留时长、N+1 超限仍抢占、回退到期不
    `StopSfx` 不计丢弃、循环不释放、`≤0` 关闭、冷加载三支）；引擎侧
    `adapters/unity/.../Tests/Runtime/SfxEngineReportedFinishPlayModeTests.cs`（真实 AudioSource）。

23. **[ADR-0121](../../architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 决策 1～3（测试覆盖梳理
    D1/D2/D3，见 `docs/复盘/测试覆盖梳理-2026-10-01.md` 第 2.1 节）**：
    - **D1 VFX 冷加载中可 Stop**：`VfxPlayer.Spawn` 在资源未就绪而排队时也返回可用句柄（与热路径一致）。
      排队时分配占位句柄（`int.MinValue` 起递增，与后端正值句柄、socket 合成负值句柄都不重叠），
      `Stop(占位句柄)` 取消排队（资源到达后不再补发，并触发 `PendingSpawnCountChanged`）；补发之后占位
      句柄映射到真实句柄，`Stop` 仍能停到那个粒子；加载失败/超时/已取消后再 `Stop` 安全忽略。同步加载器
      （加载在 `Spawn` 调用栈内完成）仍直接返回真实句柄。`CompositeFeedbackSink` 因此无论冷热都能登记
      句柄，`stop_vfx` 冷加载中到达也生效。旧用例 `Spawn_ResourceNotYetLoaded_...` 与 Unity PlayMode 用例
      里"冷加载 `Spawn` 返回 null"的断言是在给旧行为拍照，改为 `NotNull`。
    - **D2 SFX 冷加载超时改 dt 累计**：`PendingPlay.Deadline` 由 `DateTime.UtcNow` 改为
      `_clockSeconds + FirstLoadTimeoutSeconds`（`_clockSeconds` 只由 `Update(dt)` 推进，与 `VfxPlayer`
      同口径）；`Play` 开头的惰性扫描保留，只按当前时钟判断、不推进时间；累计 dt 等于阈值即到期。
      `FirstLoadTimeoutSeconds=0` 语义不变（下一次扫描即到期）。依赖墙钟的
      `SfxColdLoadTimingTests` 改为 dt 驱动的确定性用例（按帧 `Update` + 同构加载器 `Tick`，断言累计 dt
      而非 `Stopwatch`）；`FeedbackBinderTests` 里与代码不符的 `0.05` 注释改为代码也取 0.05 并补"未到期
      仍 pending"一支。
    - **D3 层满时低优先级新来者被拒绝**：`MakeRoomIfNeeded` 改为 `TryMakeRoom`——新来者优先级严格
      低于该层全部在播实例（低于在播最低优先级）时不淘汰任何实例：不播、返回 null、记一条诊断、计入
      `PlayDroppedCount`；相等或更高仍淘汰"最低优先级中最老"。未声明 priority 按 `int.MinValue`，对已声明
      者总被拒绝、与同为未声明者相等。冷加载补播放（`OnResourceLoadCompleted`）走同一出口，拒绝时同样丢弃
      并摘掉排队时登记的 attach 键。`SfxLayerSlotHoldTests` 里一条用更低优先级 `Mid` 抢占 `High` 的用例
      是在给旧行为拍照，改用同优先级 `High`（仍验证"保留时长内 high 占着唯一名额"）。
    - 回归/不变量用例：`tests/VfxPlayerTests.cs` `Spawn_ColdLoad_StopBeforeLoadCompletes_ReturnsUsableHandle_
      AndNeverEmitsAfterLoad`（D1 复现）等 8 例（含热/冷两支最终状态一致的 Theory）、
      `feedback_binder/tests/CompositeFeedbackSinkTests.cs` `StopVfx_ColdLoad_StopBeforeLoadCompletes_NeverEmits`
      与冷热一致 Theory；`tests/SfxPlayerTests.cs` `Update_ColdLoadTimeout_IsDrivenByAccumulatedDt_NotWallClock`
      （D2 复现）+ 未到期/恰到期两向；`Priority_LowerArrivalOnFullLayer_IsRejected_ActiveSetUnchanged`
      （D3 复现）+ 规则 Theory、未声明优先级、冷加载路径、循环 attach 不留键。

24. **[ADR-0125](../../architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 第 D21 条：音量入口拒绝非有限值、未知层抛异常、持久化失败记诊断**：
    - `AudioLayerVolumeHost.SetVolume` / `SfxPlayer.SetLayerVolume` 对 `NaN`/`±Infinity` 抛
      `ArgumentOutOfRangeException`（形参名 `volume`）；有限值裁剪到 `[0,1]`，**存下来并持久化的是裁剪后的值**
      （此前入口不做有限性校验，非有限值会进入状态与持久化）。共享规则在内部 `LayerVolume.Normalize`/`Clamp`。
    - 未知层名抛 `ArgumentException`（形参名 `layer`）且不写入新键（此前静默新增一个无人使用的层键）；`null` 层名
      抛 `ArgumentNullException`。`SfxPlayer` 的"已知层"= 目录里任一 `sfx.def.layer`。
    - `ISettingsStore.Save` 返回 `false` 时，`AudioLayerVolumeHost.Diagnostics` 记一条警告（层名与音量），
      **不回滚内存值**（本次会话内有效、重启不恢复）。读档恢复时持久化的有限数值同样裁剪，非有限值忽略（取默认 1.0）。
    - ABI 只新增：`AudioLayerVolumeHost` 新增五参构造（多一个 `IPresentationDiagnostics?`）与 `Diagnostics` 属性，
      原四参构造转调新构造并自建 recorder；`PresentationAssembly` 改用五参并共享 `SfxPlayer.Diagnostics`。
      复现/不变量：`tests/AudioLayerVolumeHostTests.cs`、`tests/SfxPlayerTests.cs` 的 NaN/Infinity、裁剪 Theory、
      未知层无残留键、`Save` 失败诊断各例。

25. **测试覆盖剩余项第四批（2026-10-01）：生产工程文件加 `InternalsVisibleTo`，只向测试程序集开放；冷加载 3D socket 路径如实登记；冷加载排队 VFX 的发射位置口径见本条第三项（M4 清扫改为与热路径同位）**：
    - `presentation/Presentation.Common.csproj` 新增 `<InternalsVisibleTo Include="Tests.PresentationCommon" />`，
      原因：`VfxPool`（`internal sealed`）的"超容量淘汰剩余时间最短者、并列取插入最早、永久项视为无穷、容量 ≤ 0 不限、
      剩余 ≤ 0 到期"这些规则此前只能经 `VfxPlayer` 间接覆盖，无法对池本身做边界断言（T-M34）。没有任何公开签名变化，
      不进 ABI 探针；复现/不变量：`tests/VfxPoolTests.cs`。
    - **冷加载与热路径同位发射（M4 清扫，取代"冷加载排队的 VFX 最多滞后一帧"这条已知限制）**：冷加载排队的 VFX 在加载完成那一刻重新取 anchor/socket 宿主的**当前**位置发射，与热路径（资源已缓存、`Spawn` 当场发射）同一口径；
      宿主已不在（单位销毁、锚点缺失）时回落到 `Spawn` 时刻的位置；没有 `IParticleRepositioner` 的适配层不登记跟随，行为不变；排队期间 `Stop(占位句柄)` 取消排队、从不发射、不登记跟随。
      理由：此前冷路径用 `Spawn` 时刻位置、热路径用当前位置，加载耗时越长两者差越大，要到下一次 `Update` 才追上；`VfxPlayer` 的冷/热两条路径必须逐位同口径（AGENTS 硬约束）。
      复现/不变量：`tests/VfxPlayerColdLoadFollowTests.cs`（宿主在排队期间移动：发射位置由 `Spawn` 时刻位置变为加载完成时的宿主位置；宿主已销毁回落 `Spawn` 位置；socket 降级路径、Screen 路径、anchor/socket × 热/同步冷/异步冷收敛 Theory）。
    - `SfxPlayer` 变体选择走 `options.RngStream`（默认 `presentation.sfx`），不碰模拟流；`min==max` 不消耗随机数、不创建流
      （`tests/SfxVariantSelectionTests.cs`）。

26. **收口遗留修复（2026-10-01，测试覆盖第四批收口）：真 3D socket 挂接路径补上冷加载排队，与其它三条路径"冷 = 热"一致（行为收紧，无公开签名变化）**：
    `VfxPlayer.TrySpawnAttachedToSocket`（`renderer3D` + `modelHandleResolver` 均注入且宿主有 `ModelHandle`）此前不检查资源加载状态，
    直接 `CreateModelInstance` + `AttachToSocket`——引擎侧资源尚未就绪（判断记录 25 曾把它记为"待设计层确认"，现已拍板修）。
    现在：`resourceLoader` 已注入且资源未加载完成时，走与 world/anchor/降级 socket 同一条 `QueuePendingSpawn` 排队出口——返回占位句柄
    （`PendingSpawnCount` +1，`Stop(占位句柄)` 可取消、取消后加载完成也不再补挂）、加载完成回调里才创建子模型并挂到挂点
    （创建/挂接/合成句柄/生命周期登记与热路径共用 `AttachModelToSocket` 一个出口）；加载失败/超时与其它路径同口径丢弃并记诊断。
    补挂时宿主 `ModelHandle` 按实体 id 重新经 `modelHandleResolver` 解析（排队期间宿主模型可能已销毁/重建，缓存旧句柄不可靠），
    解析不到（实体已离场）记一条诊断并丢弃，不抛。同步加载器场景（加载在 `LoadAsync` 调用栈内完成）直接返回真实合成句柄，与热路径一致。
    未注入 `resourceLoader` 或资源已加载时行为不变。复现/不变量：`tests/VfxPlayerSocketColdLoadTests.cs`
    （排队/补挂/占位句柄 Stop、Stop 后永不挂接、加载失败、超时、宿主消失、冷热收敛、到期回收）。

27. **手感音效分层（2026-10-02，手感落地第 1 波 S4；设计见 `architecture/手感设计/07` 第 3 节）：`sfx.def` 的 `feel_layer`/`feel_tier`/`feel_material` + `SfxLayerIndex`**：
    - **契约增量（ABI 只加法）**：`SfxFeelLayer`（Swing/Whiff/Impact/Sweetener/Voice/Footstep）与 `SfxFeelLayers`（名字互转）；`SfxDef` 新增 `FeelLayer`/`FeelTier`/`FeelMaterial` 与九参构造重载（旧六参构造原样）；
      `sfx.def` 表新增三个可选字段（缺省不影响旧数据，`FromRecord` 缺字段时仍走旧构造）；未知 `feel_layer` 名抛 `DataFieldException`。`feel_layer`（手感层）与既有 `layer`（混音分组，自由文本）正交。
    - **`SfxLayerIndex`**：把声明了 `feel_layer` 的行按 `(层, 档, 材质)` 建索引；反馈包只引用"层 + 档"，材质来自武器的 `sfx_material`。回落顺序：请求档 t 从高到低，每一档先材质精确行、再同档 `generic` 行，第一个命中即返回；全部落空返回 false 并记一条去重诊断（同一 `(层, 档, 材质)` 只记一次），不抛、不静音前先找近似；请求档 <= 0 表示该层关闭，无诊断。
      判断记录：先降材质、后降档——材质只是音色差异，档位决定"这一下打得多重"；不向高档回落，避免资产缺失时反而放大动静。同一 `(层, 档, 材质)` 多行时取 id 序第一行并记诊断。
    - **样例数据与并发限制的归属（M4 清扫，设计决定）**：带 `feel_*` 字段的 `sfx.def` 样例行与手感反馈包样例集中在实验室样例根 `data/_lab_action`（与场景脚本、基线成套核对），通用样例根 `data/_sample` 保持不带手感字段——不开手感的消费方看到的样例与手感无关；`sfx_max_concurrent` 由反馈包流水线按批限制（见 `feedback_binder/README.md` 判断记录 23），不在本索引内处理，因为索引只回答"层 + 档 + 材质 → 哪一行"，并发是出批时的运行态量。
    用例：`tests/SfxLayerIndexTests.cs`（精确命中、缺材质回落到同档通用、只向低档回落、全缺返回 false 且诊断去重、关闭层、重复行、非手感行不入索引、`FromRecord` 字段解析）。

28. **顿帧期间按宿主单位暂停特效（2026-10-02，手感落地 M3-C；设计见 `architecture/手感设计/07` 第 5 节）**：
    - **契约增量（ABI 只加法）**：`IVfxFreezable`（`SetOwnerFrozen(ownerEntityId, frozen)` / `IsOwnerFrozen`，框架自带 `VfxPlayer` 实现；独立成可选能力接口，不给 `IVfxPlayer` 加成员，探测写法 `vfx is IVfxFreezable`）与 `IParticleFreezer`（引擎适配层的 `IRenderer2D` 实现可选实现的 `SetParticlePaused(handle, paused)`，同 `IParticleRepositioner` 的做法；未实现的适配层含既有 `StubRenderer2D` 只是没有视觉暂停，`lifetime` 倒计时照样停住，见第 29 条）。`VfxPool.SetHeld` 内部加法。
    - **"属于某单位"的判据**：特效以 `anchor`/`socket` 挂接到某实体才有宿主单位（`VfxAttach.EntityId`）；`world`/`screen` 挂接没有，永不随顿帧暂停。因此命中闪光这类以接触点/世界坐标播放的一次性特效天然不冻，挂在单位身上的持续特效（拖尾、光环、灼烧）才随单位冻结——**是否冻结由特效怎么挂接决定，不新增特效表字段**；整层开关由反馈包 `freeze_layers.particles` 决定（装配根只在其为真时才调用本接口，见 `presentation/assembly/README.md`）。取舍：不为"这个特效冻不冻"再加一套逐特效声明——07 的设计粒度是反馈包的 `freeze_layers`，逐特效例外靠挂接方式表达已足够。
    - **状态型，不是一次性快照**：冻结标记挂在宿主单位上（`_frozenOwners`）；冻结期间新播放的该单位特效、以及资源冷加载排队后在冻结期间补发的特效，都在登记宿主时按当前冻结状态从暂停起播——热路径与冷加载补发走同一个登记出口（`RegisterOwner`），不另写一套。socket 真挂接的子模型走 `IRenderer3D.SetAnimSpeed`（0 / 恢复为 1，特效子模型由本类新建、从不被设过别的速率）。
    - **`lifetime` 倒计时随暂停停住**：被暂停实例在 `VfxPool` 里的剩余存活时间同时停住，恢复后从暂停点继续——否则冻结中途因 `lifetime` 到期被回收，"结束恢复"恢复的是一个已经消失的特效。无论引擎适配层是否实现 `IParticleFreezer`，`lifetime` 倒计时都随暂停停住（见第 29 条）。
    - 复现/不变量：`tests/VfxPlayerFreezeTests.cs`（被冻结单位的特效暂停、旁观单位的不暂停、解冻恢复；world 挂接的命中闪光永不暂停；冻结幂等不计数；`lifetime` 倒计时随暂停停住、从冻结点继续；冻结期间新播放与冷加载补发从暂停起播；已停止的句柄不残留宿主登记；socket 3D 子模型速率 0 → 1；无暂停能力的适配层 `lifetime` 同样停住、解冻后从冻结点继续）；引擎侧见 `adapters/unity` 包 README"手感落地 M3-C"节。

29. **没有 `IParticleFreezer` 的适配层也停住特效的存活倒计时（2026-10-03，手感落地 M4-G）**：`freeze_layers.particles` 为真时，`VfxPlayer` 对被冻结单位的特效恒定停住 `lifetime` 时钟（`VfxPool.SetHeld`），不再取决于适配层是否实现 `IParticleFreezer`；实现了的适配层额外获得 `SetParticlePaused`，socket 子模型照旧走 `SetAnimSpeed`。**视觉上无法暂停的部分**：没有 `IParticleFreezer` 的适配层里，已发射的 2D 粒子仍按引擎自己的时间轴继续播放——框架没有"暂停一个在播粒子实例"的通用原语（`IRenderer2D` 只有发射与停止），所以画面上粒子会继续飘、只有"到期被回收的时刻"被推迟到解冻之后；要画面也停住，适配层需实现 `IParticleFreezer`，或经第 30 条的通用兜底装饰器获得（M4-W3）。`freeze_layers.particles` 为假（缺省）时一切不变，装配根不调用 `IVfxFreezable`。用例：`tests/VfxPlayerFreezeTests.cs`（无暂停能力的适配层 `lifetime` 停住并从冻结点继续、旁观与未冻结单位照常到期、冻结期间新播放的特效从暂停起播）。

## 不负责什么

- `data/_sample/` 现已有真实示例数据（`data/_sample/vfx/vfx.def.json`，含 ADR-0074 的
  `blend_mode: additive` 示例行 `vfx.sample_aura_glow`），本条注记因此已过时——原表述"本任务不
  新增示例数据文件"只反映本模块最初落地时的范围，如实保留在下方旧记录之外单独更正，不删旧记录本身
  （见 CLAUDE.md"有分歧的设计直接删掉"以外的场景——这不是分歧，是范围随后续任务扩大，旧记录仍是
  当时任务范围的真实描述）。`schema/VfxSfxSchemas` 本身不变，仍然只声明表结构；测试用
  `InMemoryDataSource` 内联 JSON 的既有验证方式也不变，两者并不互斥——示例数据供 Unity 侧真实
  资源加载/播放路径与数据校验工具链验证，单测内联 JSON 供 `FromRecord` 解析逻辑的快速回归。
- 不实现 `CharacterRig`、纸娃娃层、序列帧动画播放器（09 第 4 节，另一任务范围）。
- 不实现 `presentation/common`（`IView`/`PresentationEventKeys` 等）——若集成时该模块已提供
  `AnchorResolver` 或等价类型，以 `presentation/common` 为准，本模块的 `AnchorResolver`/
  `EntityPositionResolver` 委托类型可直接替换为对其类型的适配。

30. **没有 `IParticleFreezer` 的适配层的通用兜底：`ParticleFreezeFallbackRenderer2D`（2026-10-03，手感落地 M4-W3，ADR-0129 增补）**：第 28/29 条留下的"视觉上无法暂停"不再是实现缺口，而是只剩一条由契约原语集合决定的边界（见下）。
    - **随附适配层**：Unity 适配层 `UnityRenderer2D` 实现 `IParticleFreezer`（M3-C）。无头桩 `StubRenderer2D` 在 `adapters/stub`，该程序集对 `presentation` 层零依赖，不能直接实现表现层的可选接口（层次约束，不是遗漏）；无头/测试组合改用 `ParticleFreezeFallbackRenderer2D.Wrap(new StubRenderer2D())`，得到的就是一个实现了 `IParticleFreezer` 的无头渲染器。
    - **兜底装饰器**：`ParticleFreezeFallbackRenderer2D`（`Presentation.VfxSfx.Core`）是一个 `IRenderer2D` 装饰器，只用契约里已有的 `EmitParticle`/`StopParticle`，自己实现 `IParticleFreezer`。`ParticleFreezeFallbackRenderer2D.Wrap(inner)` 返回：`inner` 已实现 `IParticleFreezer` 时原样返回（不叠第二层）；否则返回装饰器，`inner` 实现 `IParticleRepositioner` 时返回同时实现它的子类 `RepositioningParticleFreezeFallbackRenderer2D`（位置更新透传，暂停期间只记录、恢复时按最新位置重发），使 `VfxPlayer` 对"可选能力"的探测结果与不包装时一致。装饰器给 `VfxPlayer` 的是它自己分配的虚拟句柄（暂停再恢复会换真实句柄，虚拟句柄不变；对不认识的句柄静默忽略，不转发给被包装者）。
    - **两种暂停方式**：宿主在 `ParticleFreezeFallbackOptions.SetTimeScale`（暂停以 0、恢复以 1 调用，参数是被包装适配层自己的句柄）里接上自己的时间缩放口（例如引擎粒子系统的模拟速度）时优先用它——粒子留在画面上、停在原处，恢复后从停住的那一帧继续，不消失、不重播；没有给出时用"停止并在恢复时按原特效、原参数、原混合模式在最近已知位置重新发射"。
    - **兜底暂停的精度边界（设计决定，M4 清扫由"已知限制"改写）**：兜底装饰器不追求画面精确停在原处——没有时间缩放口时，暂停 = 停止真实粒子（画面上消失），恢复 = 重新发射。循环类特效（拖尾、光环、灼烧：挂在单位身上的持续特效，这正是冻结的主要对象）恢复后无缝接上，只是顿帧期间看不见；非循环特效恢复后从头重播一遍（存活计时仍从冻结点继续，所以只播剩余时长）。理由：契约原语（发射/停止）里没有"读出已播进度"，为一个只服务无头/无冻结口适配层的兜底去扩 `IRenderer2D` 契约不划算；要画面精确停在原处，适配层实现 `IParticleFreezer`（Unity 适配层已实现），或给 `SetTimeScale`。
    - 用例：`tests/ParticleFreezeFallbackRenderer2DTests.cs`——复现：既有 `StubRenderer2D` 上冻结单位的特效对画面毫无影响（冻结期间画面上粒子数 1 -> 1），包一层之后冻结期间 1 -> 0、解冻后 0 -> 1（同特效、同参数、同位置）；时间缩放口路径粒子不消失（1 -> 1，口收到 0 与 1）；虚拟句柄跨暂停稳定、存活计时仍从冻结点继续；已实现 `IParticleFreezer` 的适配层不叠第二层；`IParticleRepositioner` 探测结果与不包装时一致、暂停期间的位置更新在恢复时生效；已停止/未知句柄静默忽略且不误停其它粒子。

31. **`vfx.def.sort_order`：贴地特效不被地图地面层盖住（2026-10-07，消费方反馈样板游戏 A 第三阶段缺口 G2）**：序列帧特效的渲染器排序序号恒为 0，与地图地面层的排序序号（0）并列，并列时引擎按世界 Y 次级排序——特效位置比地图中心更靠"后"（Y 更大）时被地面整张盖住，贴地的范围预警圈（首领碎地重击的判定圆）实测完全不可见。新增可选字段 `vfx.def.sort_order`（整数，缺省 0，与改动前逐字一致）：`VfxDef.SortOrder` 同步新增（新增 7 参构造函数，旧 6 参转发），`VfxPlayer.Spawn` 在非 0 时把它以保留键 `sort_order`（`VfxDef.SortOrderParameterKey`）拷贝并入 `EmitParticle` 的 `parameters`（不改调用方传入的字典，不动 `IRenderer2D` 签名，ABI 只加法），`UnityRenderer2D` 的序列帧路径把它落成渲染器序号（池复用每次显式落地，不继承上一次的值）。0 与地面层并列（先后由引擎决定，不保证），正值保证画在地面层之上；地图装饰层/遮罩层与精灵的序号更大，因此正的小值（如 1）正好是"贴地、在地面之上、在角色与装饰之下"。**不覆盖**：只对序列帧路径生效（回退通用粒子路径忽略该键）；本字段不提供"画在角色之上"的语义（要盖过角色需要远大于精灵序号的值，数轴由适配层的排序约定决定，本字段不对该数轴作契约）。用例：`tests/VfxSfxFromRecordTests.cs`（解析与缺省）、`tests/VfxPlayerTests.cs`（透传、缺省不加键、不改调用方字典，修复前红）、适配层 PlayMode `UnityRenderer2DTests.EmitParticle_SortOrderParameter_SetsSortingOrder_AndPooledPlayerDoesNotInheritIt`。

32. **背景音乐播放器与按地图切曲（2026-10-07，消费方反馈样板游戏 A 备忘 2）**：`IMusicPlayer`/`MusicPlayer`——`Play(trackRef)` 在资源未加载时先 `LoadAsync(Audio)`、加载完成后再 `IAudio.PlayMusic`（ADR-0016 决策 6：`IAudio` 实现不隐式加载，首次引用方负责加载；音乐此前没有对应播放器，漏了就静音）；最新请求为准，同曲幂等，加载失败记诊断不重试。
    `MapMusicHost`——场景加载完成时读 `world.map.music_ref` 经播放器播放；地图没有 `music_ref` 时不动当前音乐；`PresentationAssemblyOptions.MapMusicEnabled`（默认 true）关闭后不构造。`PresentationAssembly.Music`/`MapMusic`。用例：`tests/MusicPlayerTests.cs`（10 条）。
