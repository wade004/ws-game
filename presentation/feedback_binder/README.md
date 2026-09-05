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

- `contracts/`：`FeedbackAction`（六个 sealed 子类判别联合）、`FeedbackRule`、`TextSource`、
  `FromDisplaySource`、`FeedbackAttachTarget`/`FeedbackAttachSpec`、`IFeedbackSink`、
  `FeedbackOptions`、`MergeMode`/`QueueMode`、`FloatingTextStyleDef`、
  `PlaybackFinishedEvent`（见下"并行协调"）、`EntityLogicalIdResolver`（可选扩展点，见"契约缺口"）。
- `core/`：`FeedbackBinder`（主体）、`PlaybackQueue`、`FloatingTextMerger`、
  `CompositeFeedbackSink`（默认 `IFeedbackSink` 实现，vfx/sfx 转给 `Presentation.VfxSfx`）、
  `FeedbackRuleValidator`。
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
6. **`text_source: literal` 的本地化解析**：`l10n.text` 查表（04 第 7.2 节）不在本模块契约范围
   内，当前实现直接把文本键原文（如 `"l10n.combat.dodge"`）当作展示文本传给
   `IFeedbackSink.FloatingText`，只是一个占位；集成阶段需要注入一个 `Func<Id,string>` 本地化
   解析委托替换掉这一处（预留在 `FeedbackBinder.DispatchFloatingTextAction` 的 `Literal` 分支，
   已用注释标出）。
7. **`sfx.def`/`play_sfx` 没有挂接坐标**：09 第 6.1 节 `PlaySfx(sfxId)` 伪代码本就没有 `attach`
   字段，`CompositeFeedbackSink.PlaySfx` 统一传 `at: null`（非定位音效）——这是本模块按伪代码字面
   拍板的设计选择，不是契约缺口：`IAudio.PlaySfx` 已由 ADR-0016 补上 `position` 参数，`at: null`
   经 `SfxPlayer.Play` 透传后就是"按无空间衰减方式播放"，行为与此前一致；`play_sfx` 动作本身若要
   携带挂接坐标，需要先在 `feedback.binding` 数据层给该动作类型补字段（不在本模块契约范围）。
8. **`FeedbackRuleValidator` 的校验范围**：`FeedbackAction` 是强类型判别联合（见该类型注释），
   "action kind 合法""params 必填"两项在 `FeedbackRule.FromRecord` 解析期已经由类型系统/构造函数
   强制满足——一条规则能被构造出来就已经通过这两项；`FeedbackRuleValidator` 只补运行期/跨记录才能
   判断的三项：`event` 已登记、`id` 落在 `feedback` domain、`id` 在规则集内不重复。

## 不负责什么

- 不实现 `presentation/common`（`IView`/`PresentationEventKeys`/`ISimSnapshot` 等）——见上"并行
  协调"。
- 不实现 `l10n.text` 查表（判断记录 6）——显式声明的契约缺口，不是遗漏；`from_display: source/target`
  的实体 → 模板 id 映射已在 P4-2 修补（判断记录 5），不再是契约缺口。
- 不接入 `data/_sample/`：`schema/FeedbackSchemas` 只声明表结构，测试用 `InMemoryDataSource`
  内联 JSON 验证 `FromRecord` 的解析正确性。
- 顿帧（`Freeze`）、震屏（`ShakeCamera`）、闪白（`Flash`）的具体落地（tick 节奏/镜头/材质参数）
  留给 `IFeedbackSink` 实现方注入的委托，本模块只发指令。
