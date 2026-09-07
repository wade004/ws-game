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
  `IWeaponStyleResolver`（09 §4.4）、`IPresentationDiagnostics`（本模块与 `feedback_binder` 共用的
  表现层诊断出口，语义同 `Core.Foundation.Expr.IExprDiagnostics`）。
- `core/`：`VfxPlayer`/`SfxPlayer`（默认实现）、`VfxPool`（对象池）、`DisplayInfoResolver`（09 §5.6）、
  `WeaponStyleResolver`（09 §4.4 最小实现）。
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
   `VfxPlayer` 经 `Update(dt)`（`IVfxPlayer` 契约本就有该方法）累计倒计时，`SfxPlayer` 因
   `ISfxPlayer` 契约没有 `Update` 方法（09 原文未定义，不新增契约方法）改为在下一次任意 `Play`
   调用开头惰性扫过期项。

8. **GP-03 收口（第四方深度审核）：三处生产引导现在每帧真正调用 `VfxPlayer.Update`**——
   `Update(dt)` 承担两件事：对象池 lifetime 到期回收（判断记录 4）与首次异步加载的 pending 超时
   扫描（判断记录 7），两者都依赖被生产代码逐帧调用才会推进；原实现只有测试直接调用过
   `Update`，`adapters/unity/.../GameFoundationBootstrap.cs`、
   `adapters/unity/.../FrameworkResidentHost.cs`、`games/_template/Runtime/GameBootstrap.cs` 三处
   生产帧循环都从未调用，导致循环特效不按 `lifetime` 停、迟迟不回调的加载请求永久积压（不会
   触发超时诊断，也不会被清理）。现在三处 Bootstrap 的每帧推进链路里都加入 `Vfx.Update(dt)`
   调用，与既有的 `Sfx` 惰性扫过期项（判断记录 7）一起构成完整的逐帧生命周期推进。见
   `VfxPlayerTests.cs`。

## 不负责什么

- 不接入 `data/_sample/`：本任务不新增示例数据文件，`schema/VfxSfxSchemas` 只声明表结构，测试用
  `InMemoryDataSource` 内联 JSON 验证 `FromRecord` 的解析正确性。
- 不实现 `CharacterRig`、纸娃娃层、序列帧动画播放器（09 第 4 节，另一任务范围）。
- 不实现 `presentation/common`（`IView`/`PresentationEventKeys` 等）——若集成时该模块已提供
  `AnchorResolver` 或等价类型，以 `presentation/common` 为准，本模块的 `AnchorResolver`/
  `EntityPositionResolver` 委托类型可直接替换为对其类型的适配。
