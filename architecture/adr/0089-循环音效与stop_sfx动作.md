# ADR-0089 循环音效与 stop_sfx 动作

状态：已拍板 2026-09-25

## 背景

消费方反馈第三十四批（阻塞项）：`sfx.def` 无 `loop` 字段，`ISfxPlayer.Play`/`IAudio.PlaySfx` 只支持
一次性播放；`feedback.binding` 的 `play_vfx`/`stop_vfx`（ADR-0075）已有"播放/停止"配对动作，`play_sfx`
没有对应的 `stop_sfx`。区域触发进入/持续状态施加一类"进入态起播、离开态停播"的循环音效
（`<game>` 内容示例：光环嗡鸣声、区域环境音）在当前契约下无法表达。

## 决策

1. **`sfx.def` 新增可选字段 `loop: bool`，缺省 `false`**：`SfxDef` 新增 `Loop` 属性 + 带该参数的新
   构造重载（旧五参构造转调，物理签名不变）；`FromRecord` 未提供该字段时按缺省值解析，与新增前
   行为逐字一致。
2. **循环标志经新增的默认接口成员一路传到引擎适配层**：`IAudio.PlaySfx` 新增
   `PlaySfx(soundId, volume, pitch, position, loop)` 重载（默认体转调旧四参签名、忽略 `loop`，
   ABI 只加法）；测试桩适配层实现与已落地引擎适配层实现均已覆写为真正落地（后者把该参数如实设置到
   对应播放单元的循环播放开关上）。`Presentation.VfxSfx.Core.SfxPlayer.Play` 改为按 `sfx.def.Loop`
   传参，旧 `ISfxPlayer.Play/Stop` 签名不变。
3. **`play_sfx` 动作新增可选 `attach: source|target|world`，缺省 `world`**：`PlaySfxAction` 新增带
   `attach` 的构造重载（旧二参构造转调，固定传 `world`）；缺省值下行为与新增前完全一致（不跟踪、
   不建键）。
4. **`feedback.binding` 新增 `stop_sfx` 动作**：`FeedbackActionKind` 新增第八项 `StopSfx`；
   `StopSfxAction` 形状镜像 `StopVfxAction`——`sfx_id?|from_display` 二选一 + `attach: source|target`
   （`world` 构造期拒绝，理由同 `stop_vfx`：没有实体可作为定位键）。`IFeedbackSink` 新增默认接口
   成员 `PlaySfx(sfxId, at, attach)`（默认体转调旧 `PlaySfx(sfxId, at)`）与 `StopSfx(sfxId, attach)`
   （默认空实现，同 `StopVfx` 惯例），`CompositeFeedbackSink` 显式覆盖两者。
5. **按 (sfx_id, 附着实体) 跟踪的职责放在 `SfxPlayer`，不放在 `CompositeFeedbackSink`**：与
   `stop_vfx`（跟踪表 `_activeVfxByKey` 建在 `CompositeFeedbackSink`）的差异在于 vfx.def 没有
   `loop` 概念、任何 attach 到实体的播放都无条件跟踪；sfx 是否需要跟踪取决于 `sfx.def.loop`，这份
   判断只有持有 `sfx.def` 目录的 `SfxPlayer` 能做，不应该让 `feedback_binder` 反过来耦合
   `sfx.def` 字段形状。`ISfxPlayer` 新增默认接口成员 `PlayAttached(sfxId, entityId, at)`/
   `StopAttached(sfxId, entityId)`（默认体分别退化为普通 `Play`/空操作），`SfxPlayer` 显式覆盖：
   只有 `Loop=true` 才登记键（一次性音效退化为普通 `Play`，不建键）；同键已在播时 `PlayAttached`
   幂等返回已登记句柄，不叠播；`StopAttached` 查到即 `Stop` 并摘除，查不到静默忽略、不写诊断
   （同 `stop_vfx` 判断"没有在播实例是正常时序，不是缺陷信号"）。
6. **不做**：不改 `ISfxPlayer.Play/Stop`/`IFeedbackSink.PlaySfx(Id, Vec2?)` 旧签名；不做淡入淡出；
   不做多实例计数/列表（同 `stop_vfx` 覆盖式单句柄惯例）。

## 后果

### 正面

- 循环音效有了数据驱动的"进入态起播、离开态停播"正规出口，不再需要靠 `sfx.def` 之外的手段模拟
  持续音效。
- 全部改动走 ABI 只加法（新增可选字段/新增重载/默认接口成员），既有消费方代码不需要改一行即可
  继续编译、运行。

### 负面（已知限制）

- **实体销毁时不会自动停止循环音效**：`Presentation.VfxSfx.Core.VfxPlayer` 对 `anchor`/降级
  `socket` 附着的跟随实例是靠每帧 `Update` 里重新解析实体位置、解析失败（实体已销毁/离场）才顺带
  结束特效（`UpdateFollowTargets`），本身不是一个显式的"实体销毁"事件钩子；`SfxPlayer` 没有等价的
  逐帧位置解析机制，因此没有可镜像的现成钩子。本决策不为此新建一套独立的"实体销毁监听"机制——
  循环音效必须靠内容侧显式的 `stop_sfx`（如光环移除事件同时配一条 `stop_sfx` 规则）来终止；若
  实体在移除信号到达前已被销毁，循环音效会持续播放直至游戏对象/场景本身被卸载。真需要这类兜底
  时应另开 ADR，不在本次范围内。
- 同一 (sfx_id, 附着实体) 键的循环实例是覆盖式单句柄（幂等，不叠播）：若确有"同一循环音效在同一
  实体身上允许多份独立实例"的真实场景，当前实现不支持。

## 备选方案与为什么不选

- **把 (sfx_id, 附着实体) 跟踪表放在 `CompositeFeedbackSink`（镜像 `stop_vfx` 的 `_activeVfxByKey`
  完全一致的做法）**：会迫使 `feedback_binder` 模块反查 `sfx.def.loop` 字段（要么注入整份 sfx
  目录，要么新增一个"是否循环"的窄查询接口），耦合了 `vfx_sfx` 模块的内部数据形状；而
  `SfxPlayer` 本身已经持有这份目录，由它就近判断更符合模块边界（`feedback_binder` 只管"是否带
  attach"，"是否真的需要跟踪"交给知道 `loop` 语义的一方决定）。故不选，选择在 `ISfxPlayer` 新增
  `PlayAttached`/`StopAttached`。
