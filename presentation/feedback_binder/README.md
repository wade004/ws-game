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
  `IHitFrameSource`（ADR-0017 决策 d，见判断记录 14）；手感打击反馈包（判断记录 23）：`ImpactProfile`（`ImpactProfile.cs`）、
  `ImpactHit`/`ImpactFeel`/`ImpactOptions`/`ImpactPlan`/`ImpactBatch` 等（`ImpactTypes.cs`）。
- `core/`：`FeedbackBinder`（主体）、`PlaybackQueue`、`FloatingTextMerger`、
  `CompositeFeedbackSink`（默认 `IFeedbackSink` 实现，vfx/sfx 转给 `Presentation.VfxSfx`）、
  `FeedbackRuleValidator`、`HitFrameSyncPolicy`（命中帧等待队列）、`CharacterRigHitFrameSource`
  （`IHitFrameSource` 默认实现，见判断记录 14）、`ImpactPipeline`/`ImpactFreezeRegistry`/`PresentingImpactFeelSource`（判断记录 23）。
- `schema/`：`FeedbackSchemas`——`feedback.binding`/`feedback.floating_text_style`/`feedback.impact_profile` 的
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
    通路）。**设计决定（框架提供机制，游戏层按需接线启用）**：默认装配
    （`presentation/assembly/PresentationAssembly`）当前不会自动把 View 创建期产生的
    `ICharacterRig` 注册进 `IHitFrameSource`、不会自动构造并注入 `hitFrameSource`、也不会默认把
    `HitFrameSync` 切到 `AnimKeyframeDriven`——理由：把 View 的 `ICharacterRig` 登记进 `IHitFrameSource` 与切换同步策略会改变所有未选择该策略的游戏的
    默认时序，是否启用只能由游戏按自己的动画数据决定（同 `flash_profile`/`FlashProfileResolver` 一类判断记录），
    不接线时行为等同本条修复之前。

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

16. **ADR-0019 F1c：`actions[]` 按判别字段 `kind` 登记为 `Variants`**（`FeedbackSchemas.
    ActionsItemSchema`），六种动作各自的 `params` 参数表以 `FeedbackRule.ParseAction` 为唯一依据。
    `style_id` 登记为 `Reference(feedback.floating_text_style)`（同表，同层）；`vfx_id`/`sfx_id`/
    `profile_id`（`shake_camera`/`flash`）退回 `Id`——分别指向本任务未定义的 `vfx.def`/`sfx.def`
    与另一个未预设已加载的 `Presentation.Camera` 子模块表，避免虚假引用完整性错误。`play_vfx`/
    `play_sfx` 的 `vfx_id|from_display`/`sfx_id|from_display` 二选一、`flash.target` 不得为
    `world` 三条业务判断登记层表达不了，继续由 `FeedbackAction` 各子类构造函数（抛异常）承担——
    与 `DataRegistry.LoadAll` 的优雅收集是两套独立的失败通道，互不重复。

17. **ADR-0075 `stop_vfx` 动作**：`FeedbackActionKind` 固定枚举新增第七项 `StopVfx`；新增
    `StopVfxAction`（构造函数校验 `vfx_id`/`from_display` 二选一、`attach` 不得为 `world`，同
    `PlayVfxAction`/`FlashAction` 既有校验风格），`FeedbackSchemas.BuildActionVariants` 与
    `FeedbackRule.ParseAction` 同步登记 `stop_vfx` 变体（`attach` 字段登记层仍表达
    `FeedbackAttachTargetValues` 全集，`world` 由 `StopVfxAction` 构造函数在运行期拒绝，与判断
    记录 16 "登记层表达不了的业务判断继续由子类构造函数承担"同一分工）。`IFeedbackSink` 新增
    `StopVfx(vfxId, attach)`，带默认实现（空方法体，未实现的既有 sink 收到 `stop_vfx` 动作时
    静默无操作），`FeedbackBinder.DispatchStopVfx` 解析 `attach` 后入队 `_sink.StopVfx(...)`。
    `CompositeFeedbackSink` 新增 `_activeVfxByKey: Dictionary<(Id VfxId, Id EntityId),
    ParticleHandle>`——`PlayVfx` 成功且 `attach.EntityId` 有值时按该键覆盖写入最新句柄
    （"停最近一个"，不维护列表，见 ADR-0075"决策 3"内存有界性论证）；`StopVfx` 按同一键查表，查到
    即从 `VfxPlayer` 转发 `Stop(handle)` 并移除该键，查不到（特效已自然到期回收/从未播放过/
    `attach` 是 `world` 没有实体可键）静默返回，不产生任何诊断。详见
    [ADR-0075](../../architecture/adr/0075-停止特效反馈动作.md)、`presentation/vfx_sfx/README.md`
    判断记录 18（`VfxPlayer.StopInternal` 幂等修复，`stop_vfx` 命中"已自然过期"场景的前置条件）。

18. **`OnEvent` 单次调用内的异常隔离修复（2026-09-23，由
    [ADR-0077](../../architecture/adr/0077-ui交互域事件.md)"落地缺陷与修复"一节的排查引出，
    但缺陷本身不限于 UI 域事件——任何逻辑
    事件命中多条 `feedback.binding` 规则时都适用，故记在本模块而不是并入 ADR-0077 正文）：改动前，
    `OnEvent` 处理同一个事件命中的多条规则时，一条规则的 `condition` 求值抛异常，或某条规则内某个
    `action` 派发（`Dispatch`）抛异常，会中止本次 `OnEvent` 调用剩余的全部处理——同一事件命中的
    其它规则、同一规则内排在后面的其它 `action` 一律被跳过；这个异常还会一路冒泡到
    `IEventBus` 的订阅回调这一层（`FeedbackBinder` 每个事件 key 只登记一个订阅者，异常在那里被
    总线按订阅者粒度兜住、记一条诊断，但"这一次 `OnEvent` 内部还没处理完的规则/动作"已经回不来了）。
    `condition` 求值本身另有一层独立兜底：`Core.Foundation.Expr.ExprEvaluator.Evaluate` 早已把
    "宿主 `Query` 抛异常"整体 try/catch，收敛为"整个表达式判定为 false"、不向外抛出（04 第 6.4
    节），所以真正会以异常形式冒出来的只有 `action` 派发这一条路径，且只能由 `IFeedbackSink`
    具体实现抛出——通读当前所有真实接线的 sink（`CompositeFeedbackSink`/`VfxPlayer`/`SfxPlayer`）
    发现它们的实体解析统一走 `snapshot.Exists(id) ? ... : null` 这类防御式写法，不会在当前代码状态
    下真的抛出，因此这条缺陷此前从未在真实装配根下被触发过，只能用故障注入的测试替身
    （`FeedbackBinderTests.ThrowingFeedbackSink`）复现——不代表它不是真缺陷：任何一个未来接入的
    `IFeedbackSink` 具体实现（尤其是直接调用宿主平台 API 的那种）都可能在某些运行期条件下抛出，
    不能假定"当前没人抛"等于"以后也不会有人抛"。修法：`condition` 求值与每条 `action` 派发
    （含 `sync: hit_frame` 批次延迟释放那条路径）分别包一层独立的 try/catch，抛出时记一条诊断
    （带规则 id/事件 key/动作种类，`sync: hit_frame` 批次因为拿不到触发它的具体规则 id，诊断里
    注明"命中帧同步批次"代替），跳过这一条规则/这一个动作，不影响同一事件的其它规则、其它动作，
    也不影响后续任何事件——不改变 `PublishImmediate`/`Enqueue` 既有的立即派发语义（未发现该语义
    本身与本缺陷有关，故未改动 `Core.Foundation.EventBus.EventBus`）。回归证据：
    `FeedbackBinderTests.OnEvent_OneRuleActionThrows_OtherRuleForSameEvent_StillExecutes`/
    `OnEvent_OneActionThrows_LaterActionInSameRule_StillExecutes` 改动前实测为红（`Assert.Single()`
    断言集合为空），改动后为绿；另两条既有事件 key 互相隔离、`PublishImmediate` 重入安全的性质经
    `OnEvent_ActionThrowsForOneEvent_SubsequentDifferentEvent_StillDispatchesNormally`/
    `PublishImmediate_ReentrantPublishFromSubscriberCallback_BothEventsReachAllSubscribers`/
    `PublishImmediate_ReentrantPublishOfSameKey_BothDispatchesReachAllSubscribers` 验证为改动前后
    均已正确，本次修复未涉及、也不需要改动事件总线。

19. **[ADR-0089](../../architecture/adr/0089-循环音效与stop_sfx动作.md) `stop_sfx` 动作 + `play_sfx`
    新增 `attach`**：`FeedbackActionKind` 新增第八项 `StopSfx`；`PlaySfxAction` 新增可选
    `Attach`（缺省 `World`，旧二参构造转调新增三参构造并固定传 `World`，行为与新增前逐字一致）；
    新增 `StopSfxAction`（构造函数校验 `sfx_id`/`from_display` 二选一、`attach` 不得为
    `world`，同判断记录 17 `StopVfxAction` 同款校验风格）。`FeedbackSchemas.ActionKindValues`/
    `BuildActionVariants` 与 `FeedbackRule.ParseAction` 同步登记 `stop_sfx` 变体、`play_sfx` 变体
    追加可选 `attach` 字段。`IFeedbackSink` 新增 `PlaySfx(sfxId, at, attach)`（默认体转调旧
    `PlaySfx(sfxId, at)`）与 `StopSfx(sfxId, attach)`（默认空操作，同判断记录 17 `StopVfx` 惯例），
    `FeedbackBinder.DispatchPlaySfx`/新增 `DispatchStopSfx` 解析 `attach` 后入队对应 `_sink` 调用。
    `CompositeFeedbackSink` 的新增实现不像判断记录 17 `_activeVfxByKey` 那样自建按键跟踪表，而是
    直接转发给 `Presentation.VfxSfx.Contracts.ISfxPlayer.PlayAttached`/`StopAttached`——是否真正
    需要按 (sfx_id, 附着实体) 跟踪取决于 `sfx.def.loop`，这份判断只有持有 `sfx.def` 目录的
    `SfxPlayer` 能做，故跟踪表建在 `presentation/vfx_sfx` 一侧，理由与备选方案见 ADR-0089。
    `attach` 为 world（含 `PlaySfx(Id, Vec2?)` 旧两参重载）时行为与新增前完全一致（不跟踪、不
    建键）。详见 `presentation/vfx_sfx/README.md` 判断记录 20。

20. **[ADR-0125](../../architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 第 D24 条：`FeedbackRule` 可选 Id/枚举字段非法值不再静默吞成 `null`**：
    `OptionalId`/`OptionalEnum<T>` 对"字段缺失或 JSON `null`"仍返回 `null`（合法的"未指定"），但字段存在且取值
    非法（非字符串、非法 Id 格式、未知枚举值、数字字符串如 `"3"`）时抛 `DataFieldException`（`Field="actions"`，
    消息带第几个元素与字段名）——此前这类数据错误悄悄退化为"未指定"，动作带着错误含义执行。`RequireEnum` 与
    `OptionalEnum` 共用 `TryParseEnum`（`Enum.TryParse` 忽略大小写且 `Enum.IsDefined`，拒绝数字字符串）。
    `tests/FeedbackFromRecordTests.cs` 补全：顶层字段、动作元素（必填/类型/非法 Id/非法枚举）、D24 可选字段非法
    （正反例）、构造器不变量违反经 `FromRecord` 以 `ArgumentException` 暴露（**已知不一致**：构造器校验抛
    `ArgumentException` 而 schema 解析抛 `DataFieldException`，本条不统一，见汇报）；`vfx_sfx` 的
    `VfxSfxFromRecordTests.cs` 补 `vfx.def`/`sfx.def`/`weapon_style` 的缺字段/类型不符/非法枚举。

21. **测试覆盖剩余项第四批（2026-10-01）：边界与异常路径补测（T-M33/T-M35 等）**：`PlaybackQueue`（非正参数、负 dt、step 抛异常、
    step 内重入 `Enqueue`）、`FeedbackBinder` 五种 `attach=target` 缺 `targetId` 的告警、`FloatingTextMerger`（同键混投、`FlushAll`、
    窗口边界、构造守卫）的用例见 `tests/PlaybackQueueEdgeTests.cs`、`tests/FeedbackBinderDispatchEdgeTests.cs`、`tests/FloatingTextMergerTests.cs`。
    该批探出的三处现状不一致已在收口时修掉，见判断记录 22。

22. **收口遗留修复（2026-10-01，测试覆盖第四批收口）：三处现状不一致根治**：
    - **`PlaybackQueue` 步骤抛异常**（`core/PlaybackQueue.cs`，新增构造重载 `PlaybackQueue(double, IPresentationDiagnostics?)` 与只读属性 `Diagnostics`，
      旧构造签名原样保留并转调；行为收紧）：`Sequential` 下某一步抛异常不再中断本次 `Update`/`Skip`，也不再让该批次吞掉 `Finished`——
      此前最后一步抛异常时 `Finished` 永不触发，`PlaybackFinishedEvent` 因此不发出、节奏门卡死。现在异常记一条诊断
      （含异常类型与消息）后继续后续步骤，队列清空时照常触发 `Finished`；`FeedbackBinder` 把自己的诊断出口传给队列。
      `Immediate` 下步骤在 `Enqueue` 调用栈内同步执行、没有批次与 `Finished`，异常仍直接抛给调用方（不变）；`Finished` 订阅者抛异常仍原样外抛（不变）。
      原来钉住"异常外抛、其余步骤留到下次"的两条用例改成新行为的断言。
    - **`flash` 动作缺 `targetId` 的警告文本补事件键**（`FeedbackBinder.DispatchFlash` 私有方法加 `evt` 参数，无公开签名变化）：与
      `play_vfx`/`stop_vfx`/`play_sfx`/`stop_sfx` 四处一致含事件键；用例对五种动作一律断言文本含事件键。
    - **`FloatingTextMerger.Update` 同一次调用里多个到期窗口按插入顺序派发**（与 `FlushAll` 一致；此前倒序遍历导致反序派发；行为收紧，无签名变化）：
      先统一推进全部窗口并挑出到期项（保持插入顺序）、从待合并列表摘除，再逐个结算。
    复现/不变量：`tests/PlaybackQueueEdgeTests.cs`、`tests/FeedbackBinderDispatchEdgeTests.cs`、`tests/FloatingTextMergerTests.cs`
    （`Update_MultipleWindowsDueInSameCall_DispatchInInsertionOrder_SameAsFlushAll` 等）。

23. **手感打击反馈包（2026-10-02，手感落地第 1 波 S4；设计见 `architecture/手感设计/07`、ADR-0113/0117）：`play_impact` 动作 + `ImpactPipeline`**：
    - **契约增量（ABI 只加法）**：`FeedbackActionKind.PlayImpact`（枚举末尾追加）+ `PlayImpactAction(Id? ProfileId)`；`feedback.binding` 的 `play_impact` 变体（`profile_id` 可选，引用 `feedback.impact_profile`）；
      新表 `feedback.impact_profile`（`ImpactProfile`，行内 `variants[]`，键为 `(class, outcome)`）；`IFeedbackSink` 新增四个默认接口成员（`PlayVfx(..., parameters)`、`ImpactCamera`、`FreezePresentation`、`ReleasePresentation`，旧 sink 不改也能编译）；
      `FeedbackBinder` 新增十三参构造重载（`impactPipeline`），旧构造转调且 `impactPipeline: null` 时行为与改动前逐位一致；`CompositeFeedbackSink` 加三个可设回调属性。
    - **字段名**：表里用 `class`/`outcome`，不用判定型字段名（`impact_class` 等）——`FeelHalfIsolationRule` 会把判定型字段名当作 `feedback.*` 表键报错。`impact_class` 本身是判定型字段，表现层读不到，由事件（`combat.hit_confirmed`/伤害/回避事件）携带，经 `ImpactHit` 传入。
    - **数据流**：`combat.hit_confirmed` 经 `ObserveHit`（挥空窗口记接触）；即时模式下沿用既有伤害/回避事件触发 `Offer`（同一命中不重复入组）；`sim.tick_finished` 触发 `Flush` 并给流水线时钟；`action.marker`/`action.phase_changed` 驱动挥空窗口；`feel.hitstop_started/ended` 驱动顿帧表现。
      逐目标闪白/粒子/音效/飘字走既有 `PlaybackQueue`；顿帧与镜头冲击不入队，直接下发 sink。
    - **同 tick 组合并**：同一 tick 内多目标命中先逐个解析，镜头冲击幅度取各命中（已乘玩家强度）的最大值，再按镜头拥有者的 `camera_shake_cap` 截断：`Magnitude = min(max, cap)`（`cap = 0` 即无冲击，与 `rpg_classic` 中性预设一致）；方向取幅度加权和后归一；`impulse_min_interval_ms` 内的后续批整批丢弃镜头内容。
      幅度基数取攻击方（武器）的 `camera_impulse_gain`，上限/最小间隔/玩家强度取镜头拥有者（`CameraOwnerResolver`，缺省是镜头跟随的实体）的手感。
    - **强度与限数**：`ImpactIntensity` 的比例系数 × 结局系数（暴击/击杀放大）乘到镜头幅度与粒子缩放，未声明时全为 1。音效限数：本批前 `min(MaxImpactsPerTick, 攻击方 sfx_max_concurrent)` 个命中播音效（`sfx_max_concurrent` 在此是"每批并发上限"，不是跨 tick 活跃声部计数，同层活跃并发仍由 `SfxPlayer` 层名额负责）；`MaxVfxPerTick` 缺省 0 = 不限粒子数。回避/挥空结局强制不播 `impact` 音效层。
    - **呈现型字段读取**：`PresentingImpactFeelSource` 只读呈现型视图；`ScreenHeightRatio` 字段取 `GetRaw`（不乘参考镜头高度，比例随缩放由镜头实现换算），`BodyHeights` 字段取 `GetNumber`；震屏回落用 `FeelCalibration.ReferenceCameraHeight`。
    - **挥空**：`WhiffFeedback`（缺省开）。攻击者的 `active_start`..`active_end`（或判定相进出）窗口内没有任何接触（含被回避），窗口结束时发挥空反馈（`whiff` 变体，缺省内置计划只播挥空层 sfx）。窗口按 (行动者, 动作实例) 配对（手感落地 S3b）：`combat.hit_confirmed.castInstanceId` 与 `action.marker`/`action.phase_changed` 的施法实例 id 是同一个值，带动作实例 id 的命中只计入同一动作实例的窗口（上一段连招的迟到命中、动作结束后才命中的投射物不会污染别的窗口）；没有动作实例 id 的命中（instant 路径的 `combat.damage_dealt`/`combat.attack_avoided`）计入该行动者全部打开的窗口。决定：窗口按动作实例而不按段——同一动作内多段判定（多个 `hit` 标记）合成一个窗口，窗口内只要有任何一次接触就不算挥空（理由见下方"设计决定"）。
    - **顿帧表现**：`ImpactFreezeRegistry` 记录被冻结单位与冻结层（`Freezes`，可查询）；`feel.hitstop_started` 经 `FreezePresentation` 通知 sink，`feel.hitstop_ended` 经 `ReleasePresentation` 解冻。`PresentationAssemblyOptions.OnFreezePresentation/OnReleasePresentation` 是渲染 rig/粒子宿主的接入点，缺省忽略（冻结状态仍可经 `Feedback.Impact.Freezes` 查询）。
    - **装配**：`PresentationAssemblyOptions.FeelResolver` 为 null（缺省）时全部手感呈现关闭，装配与改动前逐位一致；非 null 时构造流水线、`SfxLayerIndex`（见 `vfx_sfx/README.md` 判断记录 27）并启用手感镜头（见 `camera/README.md` 判断记录 8）。调用方显式给的 `ImpactOptions` 字段原样保留，装配只补空缺。
    - **不变量**：呈现型反馈与镜头档案不改判定型视图/逻辑指纹（`ImpactBinderTests.PresentingProfiles_NeverChangeJudgingFeel_...`；端到端由 feellab 套件 54/54 无差异背书）。
    - **设计决定（M4 清扫，取代本条原"已知局限"三项）**：
      - 挥空窗口按动作实例、不按段：同一动作内多段判定（多个 `hit` 标记）合成一个窗口，窗口内只要有任何一次接触就不算挥空。理由：手感设计/07 第 6 节把挥空定义为"本攻击实例命中数为零"，它回答"这一挥是否完全打空"；按段拆会让命中了前两段的连击在第三段落空时报"打空"，与该定义不符。
      - 手感呈现缺省关闭、生产入口自动接线：`PresentationAssemblyOptions.FeelResolver` 为 null 时全部关闭；`GameplayAssembly` 开手感时 `PresentationAssembly` 自动取 `gameplay.Feel.Resolver`，引擎侧引导见适配层 README 手感落地 M2-A 一节。`feedback.impact_profile` 的示例行在实验室样例根 `data/_lab_action/feedback/`，不进通用 `data/_sample`（理由：通用样例不带手感字段，不开手感的消费方看到的样例与手感无关）。
      - 顿帧表现经 `OnFreezePresentation`/`OnReleasePresentation` 下发：渲染 rig 的暂停见判断记录 25，粒子的暂停由 `IParticleFreezer` 或兜底装饰器承担（`vfx_sfx/README.md` 判断记录 30）；本流水线只下发"冻结/解冻"通知与查询状态，不直接操作渲染对象（表现域的出口都经 sink，保持无引擎依赖）。
    复现/不变量：`tests/ImpactPipelineTests.cs`（单次命中的震屏幅度/时长 tick/音效层 id、5 目标同 tick 合并 `min(max, cap)`、材质层缺失回落、挥空、顿帧、限频）、`tests/ImpactBinderTests.cs`（binder 端到端、`feedback.impact_profile` 解析、`play_impact` 规则解析、呈现/判定隔离）。

24. **挥空窗口：非攻击动作不开窗、投射物等结局再定（2026-10-02，手感落地 S12，[手感设计/07](../../architecture/手感设计/07_镜头与音画反馈.md) 第 6 节）**：
    - **非攻击动作**：`FeedbackBinder` 订阅 `action.started`，`isAttack` 为假（闪避、纯位移、纯增益）的动作实例登记进 `ImpactPipeline._nonAttackCasts`，`active_start`/Active 相不为它开窗，所以没有 `whiff` 层；`action.finished`/`action.cancelled` 清登记。没收到过 `action.started` 的动作实例按带攻击处理（旧调用方式不变）。
    - **投射物**：`action.projectile_launched` 登记飞行中弹数（按 (行动者, 动作实例)，独立于窗口登记，release 标记可能先于相位事件）；判定相结束时若还有弹在飞，窗口只标记 `Closed` 并保留，之后到达的命中仍计入；`action.projectile_ended` 把弹数减一，最后一发结束且窗口已关、全程零接触才补播挥空（落在那一刻）；结局原因为 `Cleared` 只放弃等待、不挥空；窗口关闭之前弹就没了（极近距离撞墙）则不影响，仍在 `active_end` 正常结算。
    - **设计决定**：多段技能同一动作实例发射的多发弹合并等待、不按段拆（同上"挥空窗口按动作实例"，理由同）；一发弹"命中但伤害为零/被闪避"也算接触（和既有规则一致：命中确认含回避类结局，"被闪避"与"打空"是两种不同的可感知信号）。
    - 复现/不变量：`tests/ImpactPipelineTests.cs`（非攻击不开窗、旧调用方式按攻击处理、弹在飞时推迟、命中取消、多发弹等最后一发、弹先于窗口关闭而结束、清场不挥空）；`tests/ImpactBinderTests.cs`（经总线的非攻击与投射物两条）；实验室 `feel_projectile`/`feel_projectile_miss`/`feel_dash` 基线。

25. **顿帧冻结落到渲染 rig（2026-10-02，手感落地 M2-A，手感设计/07 第 5 节）**：
    - `PresentationAssembly` 在装配 `CompositeFeedbackSink` 时把 `OnFreezePresentation/OnReleasePresentation` 默认接到 `ViewBinder` 里被冻结单位的视图 rig（`IHasCharacterRig.Rig` 实现 `IPresentationFreezable` 才冻，见 `presentation/render/README.md` 判断记录 31），冻结层里的 `Trail` 决定拖尾是否一并冻；调用方显式给的回调在 rig 冻结之后照常调用（粒子宿主等额外接入点不受影响）。
    - **名单语义**：只冻 `feel.hitstop_started` 名单里的单位（命中的攻击方与被击方），其余单位不受影响；解冻按 `feel.hitstop_ended` 名单。冻结是幂等开关，同一单位被延长的顿帧不需要配平冻结计数。
    - 取舍：接线放在 `PresentationAssembly`（宿主无关）而不是 Unity 适配层——无头宿主与引擎宿主共享同一条路径，引擎侧只需让自己的 rig 实现能力接口。
    - 复现/不变量：`presentation/assembly/tests/FeelRigFreezeWiringTests.cs`（经生产装配：攻击方/被击方 rig 冻结的 tick 数 = 顿帧档案毫秒按标定步长换算值、旁观单位 0、冻结为连续一段且结束后无残留；不开手感时同场景任何 rig 都不冻结；命中 → 镜头冲量幅度来自反馈包与手感解析）。

26. **顿帧的层声明取最近出批命中的 `freeze_layers`（2026-10-02，手感落地 M3-C，缺陷修复）**：`feel.hitstop_started` 由判定型宿主在 tick 末落地并以排队事件发出，到达 `ImpactPipeline` 时命中自己的打击计划通常已在**前一次出批**里下发过；原 `LayersFor` 只在同批计划里找相关命中，生产链路里恒找不到，`freeze_layers.particles/trail` 一直是缺省假（判断记录 25 的单测把命中与顿帧塞进同一批，没暴露）。修法：流水线保留最近 3 次出批的命中（`_recentHits`，按出批代数淘汰，更老的丢弃——不无限保留，免得把很久以前的反馈包套到无关的顿帧上），`LayersFor` 在"最近出批命中 + 本批计划"里按原相关性规则（同攻击实例，或命中的攻击方/被击方在顿帧名单里）取并集。行为收紧：此前声明了 `freeze_layers` 的反馈包在生产里不起作用，现在起作用；没声明的反馈包（含框架全部缺省数据）行为不变。复现/不变量：`tests/ImpactPipelineTests.cs`（`HitstopArrivingInLaterBatch_UsesFreezeLayersOfTheEarlierFlushedHit`、`HitstopFarAfterTheHit_DoesNotInheritStaleFreezeLayers`）与 `presentation/assembly/tests/FeelRigFreezeWiringTests.cs` 的经生产装配用例。

27. **镜头与音画反馈：缺省规则、距离衰减、动画表现标记与可选出口（2026-10-04，手感落地 M5-S5，[ADR-0148](../../architecture/adr/0148-镜头与音画反馈的合成上限玩家强度脚步材质与动画表现标记.md)）**：
   - **缺省规则**：装配根在游戏没有任何含 `PlayImpact` 的规则时补 `combat.hit_confirmed → PlayImpact(from_feel)`（`feedback.binding.framework_default_impact`）；游戏规则优先、不叠加；`DefaultImpactRule=false` 关闭。
   - **距离衰减**（`ImpactPipeline` 算冲击幅度时）：`camera_distance_attenuation` 取 `none`（缺省）/`linear:<跨度>`（系数 = `1 − 距离/跨度` 夹 [0,1]，距离按受击点到镜头所有者的身高倍数）/曲线 id；裸 `linear` 行为保持不衰减，运行期提示一次迁移（ADR-0039）；系数同乘到冲击幅度与缩放脉冲。
   - **标记链路**：rig（`IAnimMarkerEmitter`）→ `CharacterRigHitFrameSource`（同时是 `IAnimMarkerSource`，与命中帧同步策略无关）→ `AnimMarkerDirector`。同名重复标记在关键帧索引里以"名#序号"登记、触发时去后缀（`AnimMarkerNames.RepeatKey/StripRepeat`）；模型事件 `anim_event.fx.<id>` 标准化为 `fx:<id>`。`footstep` 取手感字段 `sfx_footstep_tier`（角色主导，0 = 关）与材质经 `SfxLayerIndex` 选行；`trail_start/trail_end` 需 `trail_enabled` 且 `trail_ref` 齐备才播特效（锚点挂接取 `anchor.hand_main`，世界挂接取开始时刻位置），`afterimage_enabled` 同步开关残影；`fx:<id>` 在单位位置播；`impact` 经 `IAnimMarkerGate` 放行推迟的闪白（`flash.sync = impact_marker`，超时没有标记照常闪）。
   - **流水线侧不变**：批内取最大合并与最小间隔限频保持；`UserIntensity` 在读到保留前缀 `feel.intensity.` 的名字时视为 1（出口统一乘，不重复乘）。变体新增 `camera.zoom_punch` 与 `rumble`（`IFeedbackSink` 新增缺省空实现的 `Rumble` 与带缩放脉冲的重载，ABI 只加法），`CompositeFeedbackSink` 转发。
   - 复现/不变量：`tests/ImpactAudioVisualTests.cs`（含距离衰减三种写法与旧写法提示）、`tests/AnimMarkerDirectorTests.cs`（脚步档位/材质回落/档位 0、拖尾开关与残影、`fx:`、`impact` 门放行与超时）、`tests/CharacterRigHitFrameSourceTests.cs` 两例标记聚合；`presentation/assembly/tests/DefaultImpactRuleWiringTests.cs`。
28. **实体 id 取值链补召唤事件字段（2026-10-08，样板游戏 B 缺口，ADR-0164 决策 8）**：self 取值链 `sourceId`/`casterId`/`unitId` 末尾补 `ownerId`，
   target 取值链 `targetId` 末尾补 `entityId`，使 `summon.created` 上的 `play_vfx`(attach=source/target) 可解析（召唤者/被召唤实体）；链尾补充，既有事件取值不变。
   已知边界：升级事件在数值层、不是表达式可读事件，取不到单位 id（ADR-0164 限制 5）。用例 `FeedbackBinderSummonIdFieldTests`（先红后绿）。

## 不负责什么

- 不实现 `presentation/common`（`IView`/`PresentationEventKeys`/`ISimSnapshot` 等）——见上"并行
  协调"。
- 不自己实现 `l10n.text` 查表逻辑（那是 `core/foundation/localization` 的事）——本模块只暴露一个
  可选 `textResolver` 注入点（判断记录 6），`l10n.text` 不再是未接线的契约缺口；`from_display:
  source/target` 的实体 → 模板 id 映射已在 P4-2 修补（判断记录 5），同样不再是契约缺口。
- `data/_sample/` 现已有真实示例数据（`data/_sample/feedback/feedback.binding.json`，含 ADR-0075
  的 `stop_vfx` 示例行 `feedback.sample_aura_removed`），原表述"不接入 `data/_sample/`"已过时，
  按同 `vfx_sfx` README 判断记录 17 附注一样的方式如实更正而不删旧记录：`schema/FeedbackSchemas`
  本身仍然只声明表结构，测试用 `InMemoryDataSource` 内联 JSON 的既有验证方式不变，两者并不互斥。
- 顿帧（`Freeze`）、震屏（`ShakeCamera`）的具体落地（tick 节奏/镜头）仍留给 `IFeedbackSink`
  实现方注入的委托，本模块只发指令；闪白（`Flash`）已由 `PresentationAssembly` 默认接到
  `presentation/render` 的 `ICharacterRig.ProceduralAnim.Flash` 原语（见拍板 6/09 第 4.1 节），
  不再是未接线的空回调——`OnFlash` 选项仍保留供调用方完全覆盖默认行为。09 未定义
  `flash_profile` 登记表，`profileId → FlashParams`（强度/时长）的解析由调用方经
  `PresentationAssemblyOptions.FlashProfileResolver` 注入，缺省恒返回 `FlashParams.Default`（设计决定：闪白强度/
  时长是游戏风格数据，框架没有数据表可读，只提供注入点）。
