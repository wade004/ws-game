# Presentation.FeedbackBinder（presentation/feedback_binder）

职责：把"逻辑事件"翻译为"一组具体表现动作"的规则引擎（见
[09_表现层.md](../../architecture/09_表现层.md) 第 6 节）——`feedback.binding`/
`feedback.floating_text_style` 两张表的 schema、`FeedbackBinder`、飘字合并、播放队列与
`presentation.playback_finished`。

铁律遵守（09 第 1 节）：`FeedbackBinder` 只经 `IEventBus.Subscribe` 订阅事件（P2），只经
`IFeedbackSink` 这一扇窄门下达动作指令、自身不直接调用任何 L-1 接口（P4）；唯一允许发出的事件是
`PlaybackFinishedEvent`（`presentation.playback_finished`，见下），且经 `PlaybackQueue.Finished`
联动，只在 `QueueMode.Sequential` 下才可能发生，不改变任何逻辑数据、不构成 P2 的例外。

## 目录

- `contracts/`：`FeedbackAction`（六个 sealed 子类判别联合）、`FeedbackRule`（含 `FeedbackSyncMode`/
  `Sync` 字段，ADR-0017 决策 d）、`TextSource`、`FromDisplaySource`、`FeedbackAttachTarget`/
  `FeedbackAttachSpec`、`IFeedbackSink`、`FeedbackOptions`（新增 `HitFrameSync`/
  `HitFrameSyncTimeoutSeconds`）、`MergeMode`/`QueueMode`、`FloatingTextStyleDef`、
  `PlaybackFinishedEvent`（见下"并行协调"）、`EntityLogicalIdResolver`（可选扩展点，见"契约缺口"）、
  `IHitFrameSource`（ADR-0017 决策 d，见判断记录 14）。
- `core/`：`FeedbackBinder`（主体）、`PlaybackQueue`、`FloatingTextMerger`、
  `CompositeFeedbackSink`（默认 `IFeedbackSink` 实现，vfx/sfx 转给 `Presentation.VfxSfx`）、
  `FeedbackRuleValidator`、`HitFrameSyncPolicy`（命中帧等待队列）、`CharacterRigHitFrameSource`
  （`IHitFrameSource` 默认实现，见判断记录 14）。
- `schema/`：`FeedbackSchemas`——`feedback.binding`/`feedback.floating_text_style` 的
  `TableSchema` 登记（不接入 `data/_sample/`，同 `vfx_sfx` 模块惯例）。
- `tests/`：见验收测试列表。

## 并行协调：`presentation.playback_finished`

`Core.Foundation.EventBus.EventKeys.PresentationPlaybackFinished` 已经由 `found.event_catalog`/
生成脚本登记（`core/foundation/event_bus/generated/EventKeys.g.cs` 第 169 行），说明事件 key 本身
已经落地；但截至本模块开发时 `presentation/common` 仍只有一个占位 `LayerMarker.cs`，没有提供强类型
`IEvent` 事件类。按任务书指示，本模块在 `contracts/PlaybackFinishedEvent.cs` 落地一个最小事件类，
直接复用已登记的 `EventKeys.PresentationPlaybackFinished` 常量（不重复定义 key）。**集成阶段**：若
`presentation/common` 提供了同名/等价类型，删除本类型、改用其类型即可，`FeedbackBinder` 内部只在
一处（构造函数里 `_queue.Finished += () => _bus.PublishImmediate(new PlaybackFinishedEvent());`）
引用它，替换成本低。

## 判断记录

1. **`FeedbackAttachSpec` 命名偏离文档伪代码**：09 第 6.1 节伪代码把"挂接参数"写作 `AttachSpec`，
   但本模块（经 `Presentation.VfxSfx`）已有 `VfxAttach` 表达"L-1 引擎侧挂接形状"
   （world/anchor/socket/screen）；`FeedbackAttachSpec` 表达的是不同的轴——"以事件哪一方实体为
   挂接基准"（source/target/world），沿用同名容易让读者误以为是同一个类型，因此改名，并在
   `CompositeFeedbackSink` 里做一次显式换算。
2. **飘字挂在哪个实体上**：09 第 6.1 节 `FloatingText(styleId, textSource)` 伪代码没有携带挂接
   实体，但 `IFeedbackSink.FloatingText(Id entityId, ...)` 需要一个具体实体。本实现：优先挂
   `targetId`（承受效果的一方，飘字最常见的展示位置），缺 `targetId` 时退回 `selfId`。
3. **飘字合并只对 `text_source: amount` 生效**：`MergeMode.Sum` 语义（求和）天然要求文本是数值；
   `field`/`literal` 类文本（如"闪避"提示语）一律立即派发，不进入合并窗口——09 原文举的合并例子
   本就是"多段伤害"数值场景。`FloatingTextMerger` 类型注释有完整判断记录。
4. **合并窗口不因追加命中而延长**：从该 `(entityId, styleId)` 组合第一次出现起固定计时，行为对
   测试/回放更容易预测；09 原文未规定这一细节。
5. **`from_display: source/target` 契约缺口已修补（P4-2）**：09 第 5.6 节"逻辑 id"特指技能/光环/
   物品/生物模板 id，不是运行期实体 id；`from_display: skill` 经事件的 `skillId`/`auraDefId` 字段
   直接就是这种逻辑 id，可以完整实现。`source`/`target` 需要"实体 id → 模板 id"这层映射——原判断
   记录称"本任务契约清单没有提供（`IUnitAccess` 没有模板 id 访问器）"，但 `IUnitAccess` 契约本身
   已经补上 `GetTemplateId(Id unitId): Id?`（05 第 1.1 节 `Entity.templateId`），这一契约缺口已不
   存在。`FeedbackBinder.ResolveEntityLogicalId` 现按"先看可选注入的 `EntityLogicalIdResolver`（仍
   保留，供需要与模板 id 不同映射规则的具体游戏覆盖），没有注入时默认经 `IUnitAccess.GetTemplateId`
   取模板 id"的顺序解析；两者都未注入时仍记一条诊断并跳过该动作，不崩溃、不影响规则里的其它动作。
6. **`text_source: literal` 的本地化解析——已解决（缺口 7）**：`FeedbackBinder` 构造函数新增可选
   `Func<Id, string>? textResolver` 参数；未注入时保留此前"直接把文本键原文当展示文本"的退化占位
   行为（不阻断装配），注入后经它解析出真正文案。`PresentationAssembly` 默认接
   `key => L10n.Text(key)`（`IL10nHost.Text`，见 09 第 7.3 节"文案一律经本地化表用 key 间接引用"），
   `L10nHost` 因此提前到 `feedback_binder` 小节构造（原在 `ui` 小节，见 `assembly/README.md` 判断
   记录）。
7. **`sfx.def`/`play_sfx` 没有挂接坐标**：09 第 6.1 节 `PlaySfx(sfxId)` 伪代码本就没有 `attach`
   字段，`CompositeFeedbackSink.PlaySfx` 统一传 `at: null`（非定位音效）——这是本模块按伪代码字面
   拍板的设计选择，不是契约缺口：`IAudio.PlaySfx` 已由 ADR-0016 补上 `position` 参数，`at: null`
   经 `SfxPlayer.Play` 透传后就是"按无空间衰减方式播放"，行为与此前一致；`play_sfx` 动作本身若要
   携带挂接坐标，需要先在 `feedback.binding` 数据层给该动作类型补字段（不在本模块契约范围）。
8. **`FeedbackRuleValidator` 的校验范围**：`FeedbackAction` 是强类型判别联合（见该类型注释），
   "action kind 合法""params 必填"两项在 `FeedbackRule.FromRecord` 解析期已经由类型系统/构造函数
   强制满足——一条规则能被构造出来就已经通过这两项；`FeedbackRuleValidator` 只补运行期/跨记录才能
   判断的三项：`event` 已登记、`id` 落在 `feedback` domain、`id` 在规则集内不重复。
9. **`FeedbackOptions.QueueMode` 默认 `Immediate`，离散模式下的切换不由本模块自己决定（拍板 5）**：
   `PlaybackQueue.Mode` 是公开可写属性，支持运行期切换；但"什么时候该切"依赖 03/ADR-0013 的时间
   模型状态（`core/gameplay/assembly.TimeModelSwitch`），不是 `feedback_binder`/`vfx_sfx` 两个模块
   自己能判断的信息——切换时机的决策与接线放在装配根，见 `presentation/assembly/README.md`
   `PresentationAssemblyOptions.FeedbackQueueMode` 判断记录；此前 Unity 引导侧"靠零事件兜底短路"
   的临时手法已随本次收口废弃。
10. **`HasPendingPlayback` 统一查询与 `playback_finished` 推迟发出（GP-PRES-03 跟进）**：
    `FeedbackBinder.HasPendingPlayback`（`Queue.PendingCount > 0 || FloatingTextMerger.
    HasPendingMerges`）是"当前离散步是否还有未回放完的表现"这一问题的唯一正确答案——
    `MergeWindow > 0` 时数值飘字先暂存在 `FloatingTextMerger` 内部，窗口到期前既不派发也不进入
    `PlaybackQueue`，只看 `Queue.PendingCount` 会漏看这部分表现；`presentation/assembly.
    PresentationAssembly` 装配根接 `GameplayAssembly.SetPendingPlaybackProbe` 时改用本属性，不再
    直接用 `Queue.PendingCount`（见 `assembly/README.md` 判断记录 9 跟进）。配套地，构造函数里
    `_queue.Finished` 订阅也收紧为"仅当 `!_merger.HasPendingMerges` 时才发布
    `PlaybackFinishedEvent`"——飘字还停留在合并窗口内时，队列因"这一步只有非飘字动作"而先播空一次
    不代表本步表现已经结束；窗口到期后合并结果经 `DispatchFloatingText` 入队、再次播空时
    `Finished` 会第二次触发，那一次才真正发布事件，与 `HasPendingPlayback` 的语义保持一致。
11. **GP-09 收口（第四方深度审核）：`HasPendingPlayback` 补上 `IFeedbackSink` 侧的冷资源 pending
    信号，不再只看 `Queue`/`FloatingTextMerger` 两处**——`play_vfx`/`play_sfx` 首次引用尚未加载
    完成的资源时会在 `VfxPlayer`/`SfxPlayer` 内部排队等待（见 `presentation/vfx_sfx/README.md`
    判断记录 7），`IFeedbackSink.PlayVfx`/`PlaySfx` 调用本身只是"发指令"，不等播完就立即返回，
    不产生任何"这一步已经播完"的信号；此前 `FeedbackBinder.HasPendingPlayback` 只看
    `Queue.PendingCount`/`FloatingTextMerger.HasPendingMerges`，冷资源的 vfx/sfx 因此会被误判为
    "这一步没有待回放内容"而提前放行离散步的表现完成门，真正的播放效果可能在下一步甚至后续
    vfx/sfx 之间乱序才姗姗来迟。`IFeedbackSink` 新增只读属性 `HasPendingPlayback`
    （`CompositeFeedbackSink` 直接转发 `IVfxPlayer.PendingSpawnCount`/`ISfxPlayer.
    PendingPlayCount` 是否大于 0），`FeedbackBinder.HasPendingPlayback` 改为
    `Queue.PendingCount > 0 || Merger.HasPendingMerges || Sink.HasPendingPlayback` 三者取或。见
    `IFeedbackSink.cs`/`CompositeFeedbackSink.cs`/`FeedbackBinder.cs`，
    `CompositeFeedbackSinkTests.cs`（新增）。
12. **N17 收口（外部审核 68c9bed）：`PlaybackFinishedEvent` 的发出条件补上 sink 侧冷资源，且新增
    第二条驱动路径**——判断记录 10 描述的 `_queue.Finished` 收紧（"仅当 `!_merger.HasPendingMerges`
    时才发布"）只覆盖了 merger，没有覆盖判断记录 11 补上的 sink 侧 pending：`PlaySfx`/`PlayVfx`
    命中冷资源时立即返回、不阻塞队列，Sequential 模式下队列因此可能在冷资源仍在加载时就已经清空，
    `_queue.Finished` 触发时只看 merger、门提前打开；Immediate 模式下动作同步执行、队列永远为空，
    `_queue.Finished` 从不触发，冷资源真正加载完成那一刻完全没有信号能补发
    `PlaybackFinishedEvent`（09 表现层"immediate 模式不发"的既有描述只适用于"从未有过冷资源
    pending"的常见情形，不适用于确实命中过冷资源、节奏门确实被关闭过的这一分支）。现在改为
    `TryPublishFinished`（队列空 && `!Merger.HasPendingMerges` && `!Sink.HasPendingPlayback` 三者
    同时成立才发出），同时挂在两条独立路径上：`_queue.Finished`（队列由非空变空那一刻，覆盖
    Sequential 常见情形）与新增的 `IFeedbackSink.PendingPlaybackChanged`（sink 侧 pending 计数
    可能变化那一刻，由 `IVfxPlayer.PendingSpawnCountChanged`/`ISfxPlayer.PendingPlayCountChanged`
    经 `CompositeFeedbackSink` 汇聚转发，见 `presentation/vfx_sfx/README.md` 对应条目——覆盖
    "队列早已清空、只等冷资源"与 Immediate 模式两种此前的信号缺口）。`TryPublishFinished`
    本身无状态（每次调用只检查"当下是否三个条件同时满足"），不会因为挂在两条路径上而重复发出——
    只有真正从"有 pending"变成"全部清空"的那一次调用会通过全部条件。见 `FeedbackBinder.cs`
    （`TryPublishFinished`）、`FeedbackBinderTests.cs`
    （`QueueMode_Sequential_ColdSfxPending_DoesNotFirePlaybackFinished_UntilSinkResolves`、
    `QueueMode_Immediate_ColdSfxPending_FiresPlaybackFinished_WhenSinkResolves`）。

13. **C07 收口（外部审核 7e63d66）：冷资源超时终止路径此前没有接上判断记录 12 的完成信号链**——
    判断记录 12 覆盖了"加载成功/失败"两种结局，但 `VfxPlayer`/`SfxPlayer` 各自的首次加载超时清理
    （`VfxOptions.FirstLoadTimeoutSeconds`/`SfxOptions.FirstLoadTimeoutSeconds` 到期、加载请求迟迟
    不回调，如 `resource_ref` 拼写错误或资源确实缺失这类真实会发生的情形）没有触发对应的
    `PendingSpawnCountChanged`/`PendingPlayCountChanged`（`VfxPlayer` 一侧）与"完全没有独立于
    `Play` 的时钟入口"（`SfxPlayer` 一侧，超时清理此前只能在下一次任意 `Play` 调用开头被惰性扫到，
    若节奏门已关闭、此后没有新的 `Play` 调用，卡死的加载请求永远不会被扫到）——两处都会让本模块
    的 `wait_for_playback` 链在真实冷资源永久加载失败场景下永久卡住，`PlaybackFinishedEvent` 永远
    等不到。根治见 `presentation/vfx_sfx/README.md` 对应条目（`VfxPlayer.Update` 超时分支补发
    信号、`ISfxPlayer` 新增独立 `Update` 时钟入口）；本模块不需要改动——`CompositeFeedbackSink`
    早已订阅两者的 `PendingSpawnCountChanged`/`PendingPlayCountChanged` 并转发为
    `PendingPlaybackChanged`（判断记录 11），`TryPublishFinished` 早已挂在这条路径上（本条判断
    记录），信号一旦补上就自动接通，只是此前信号从未在超时这一分支发出过。见
    `FeedbackBinderTests.cs`
    （`QueueMode_Sequential_RealSfxPlayerColdResourceTimesOut_FiresPlaybackFinishedExactlyOnce_ViaUpdateOnly`
    ——不用手工 stub 的 `RecordingFeedbackSink` 模拟 pending，而是串联真实 `SfxPlayer`/
    `CompositeFeedbackSink`/`FeedbackBinder`，只靠 `SfxPlayer.Update`（不调用第二次 `Play`）驱动
    超时，验证完整真实链路恰好发布一次完成事件）。

14. **ADR-0017（W6 表现能力补齐 A 部分，2026-09-08）：命中帧同步（`anim_keyframe_driven`）从"没有
    任何订阅方"收口为真实接线**——`presentation/render/README.md` 判断记录 13/16 提到
    `HitFrameReached`（经可选接口 `IHitFrameEmitter` 提供，见该 README 判断记录 17"PJ130-04 勘误"）
    此前只在 `SpriteCharacterRig` 一侧有实现且全仓无人订阅，
    `RenderOptions.HitFrameSync` 切到 `AnimKeyframeDriven` 因此没有可观察效果；本模块新增
    `IHitFrameSource`（按实体注册/注销 rig 的命中帧事件订阅，汇聚成按实体 id 广播的聚合事件，默认
    实现 `CharacterRigHitFrameSource`）与 `HitFrameSyncPolicy`（等待队列：按时释放、超时兜底默认
    0.5 秒并记诊断、攻击方无 rig 时立即释放、多次攻击各自入队不串扰）；`FeedbackRule` 新增
    `Sync: FeedbackSyncMode`（对应 `feedback.binding.sync: "hit_frame"` 字段，未提供时按 `event`
    是否为 `combat.damage_dealt` 决定默认值）；`FeedbackBinder` 新增可选构造参数
    `hitFrameSource`，只有 `FeedbackOptions.HitFrameSync == AnimKeyframeDriven` 且确实注入了
    `IHitFrameSource` 时才构造内部的 `HitFrameSyncPolicy`——两个条件缺一则完全回退为改动前的行为
    （规则的 `Sync` 字段被忽略，全部动作立即派发），保证未升级的既有调用方/测试不受影响。
    `HitFrameSyncPolicy.PendingCount` 并入 `HasPendingPlayback`，`PendingChanged` 事件接入既有
    `TryPublishFinished` 完成信号链（同判断记录 11/13 的既有接线惯例，不新增一条独立的完成信号
    通路）。**仍是契约缺口的部分（留给 W6-B/具体游戏装配代码）**：默认装配
    （`presentation/assembly/PresentationAssembly`）当前不会自动把 View 创建期产生的
    `ICharacterRig` 注册进 `IHitFrameSource`、不会自动构造并注入 `hitFrameSource`、也不会默认把
    `HitFrameSync` 切到 `AnimKeyframeDriven`——这是"框架提供机制，游戏层按需接线启用"的既有模式
    （同 `flash_profile`/`FlashProfileResolver` 一类判断记录），不接线时行为等同本条修复之前。

15. **PR130-04 根治（第六轮文档—代码深度审计，`architecture/落地计划/audit-5c444f1-20260908/`）：
    同一逻辑事件命中多条 `sync: hit_frame` 规则时，此前每条规则各自调用一次
    `HitFrameSyncPolicy.WaitForHitFrame`，各自登记成一个独立的等待项**——`HitFrameSyncPolicy` 每次
    命中帧只释放同一实体最早入队的那一条（`OnHitFrameReached` 的既有 FIFO 单条释放语义，见其类型
    判断记录"多次攻击不串扰"，本条修复没有改动这一点，仍由
    `HitFrameSyncPolicyTests.MultipleAttacks_SameEntity_DoNotCrossTalk` 锁定），第二条及之后的规则
    因此要么错过这一次命中帧、串到下一次攻击的命中帧才播放，要么等到超时兜底（默认 0.5 秒）才播放，
    与"同一事件的全部动作应当作为一个批次同时释放"的预期不符。改法在调用方（`FeedbackBinder.OnEvent`）
    一侧：同一次 `OnEvent`（同一个逻辑事件）触发的全部 `sync: hit_frame` 规则的动作先合并进一个
    列表，循环结束后只调用一次 `WaitForHitFrame`，使它们登记成同一个 `PendingEntry`、随同一次命中帧
    整体释放；`HitFrameSyncPolicy` 本身的释放逻辑未改动一行——批次的边界完全由调用方划定，策略只需要
    保证"一次 `WaitForHitFrame` 调用＝一次命中帧时的一次完整 `release()` 调用"这一基本原子性。范围
    攻击对多个目标各自产生独立的 `combat.damage_dealt` 事件，各自经独立的一次 `OnEvent` 调用登记为
    各自独立的批次，仍按既有 FIFO 顺序逐批释放，不会被本次改动误合并成一批。

## 不负责什么

- 不实现 `presentation/common`（`IView`/`PresentationEventKeys`/`ISimSnapshot` 等）——见上"并行
  协调"。
- 不自己实现 `l10n.text` 查表逻辑（那是 `core/foundation/localization` 的事）——本模块只暴露一个
  可选 `textResolver` 注入点（判断记录 6），`l10n.text` 不再是未接线的契约缺口；`from_display:
  source/target` 的实体 → 模板 id 映射已在 P4-2 修补（判断记录 5），同样不再是契约缺口。
- 不接入 `data/_sample/`：`schema/FeedbackSchemas` 只声明表结构，测试用 `InMemoryDataSource`
  内联 JSON 验证 `FromRecord` 的解析正确性。
- 顿帧（`Freeze`）、震屏（`ShakeCamera`）的具体落地（tick 节奏/镜头）仍留给 `IFeedbackSink`
  实现方注入的委托，本模块只发指令；闪白（`Flash`）已由 `PresentationAssembly` 默认接到
  `presentation/render` 的 `ICharacterRig.ProceduralAnim.Flash` 原语（见拍板 6/09 第 4.1 节），
  不再是未接线的空回调——`OnFlash` 选项仍保留供调用方完全覆盖默认行为。09 未定义
  `flash_profile` 登记表，`profileId → FlashParams`（强度/时长）的解析仍是契约缺口（默认恒返回
  `FlashParams.Default`，见 `PresentationAssemblyOptions.FlashProfileResolver`）。
