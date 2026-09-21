# L5 表现层 · UI 框架（presentation/ui）

对应 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `ui` 行、
[09_表现层.md](../../architecture/09_表现层.md) 第 7 节。职责：界面数据绑定（查询 + 订阅刷新）、
用户操作到意图请求的转译、十个视图模型。

## 铁律落实

- **P1 只读逻辑状态**：全部数据经 `IUiDataSource.Query`（路径小语法，见下）或直接持有某个只读
  宿主接口（如 `IQuestHost.GetLog`）读取，视图模型不持有任何可写字段回写给逻辑层。
- **P2 只订阅事件**：视图模型构造期经 `IUiDataSource.Subscribe`/`IEventBus.Subscribe` 订阅相关
  事件触发 `Refresh()`，不轮询内部结构。维护派生展示状态（订阅事件增量更新，不是每次都重新
  `Query`）的视图模型，还必须额外订阅 `SaveEventKeys.SaveLoaded`（`save.loaded`）——存档系统
  "读档不是业务事件"的抑制作用域会连带丢弃读档期间业务事件本身的派发，只按业务事件订阅刷新的
  视图模型在同图读档场景下会因此收不到任何刷新信号（见下 UI-111-01 根治记录，与
  `presentation/view_binding/core/ViewBinder.cs` 的 `OnSaveLoaded`、
  `presentation/vfx_sfx/core/EquipmentWeaponStyleSource.cs` 同一惯例）。
- **P3 不写回**：一切用户输入经 `UiIntents` 转成 `IWorldSim.SubmitIntent` 或窄契约调用
  （`IEquipmentHost`/`IQuestHost`/`IDialogHost`/`IEconomyHost`/`IInputMapHost`/`IL10nHost`/
  `IAppStateHost`），本模块不直接修改任何逻辑数据。

扫描验收：`grep -rniE "\.SetPosition\(|\.SetAlive\(|\.AddModifier\(|\.ModifyPower\(|\.SetBase\(|WorldState\.Set\(" presentation/ui`
应无匹配；`UiIntents`/视图模型均不出现这些写方法调用。

## 路径小语法与解析策略

`IUiDataSource.Query(path)` 的路径按 `.` 切分成段（`UiPathParser`/`UiPathSegment`），首段路由给
注册的 `IUiPathProvider`（`PlayerPathProvider`/`TargetPathProvider`/`UnitPathProvider`）。逻辑 Id
本身含 `.`，与路径分隔符同一字符——各 Provider 按"固定关键字位置"消歧（见
`core/PathProviders/UnitSubQueries.cs`、`UnitPathProvider` 的判断记录），不是从左到右贪婪匹配。

支持的路径清单（见各 Provider 源码顶部注释的逐条对照）：

```
player.power.<powerType>.current|max
player.stat.<statId>
player.level / player.xp / player.xp_to_next
player.inventory.count
player.inventory[i].template|count|instance
player.equipment.<slot>
player.quest.<questId>.state|objective[i]
player.currency.<id>
player.skills[i]
player.skill.<id>.cooldown
target.power.<powerType>.current|max
target.stat.<statId>
unit.<id>.power.<type>.current|max
unit.<id>.stat.<statId>
```

路径语法非法/引用了格式非法的 Id/首段没有注册 Provider 时返回 `null` 并记一条诊断；路径语法
合法但当前无值（未选中目标、槽位为空、下标越界）同样返回 `null`，但不记诊断。

## 十一个视图模型

`HudViewModel`、`ActionBarViewModel`、`InventoryViewModel`、`QuestLogViewModel`、
`DialogViewModel`、`SkillBookViewModel`、`CharacterStatsViewModel`、`SettingsViewModel`、
`SaveSlotsViewModel`、`PauseMenuViewModel`、`ShopViewModel`（`core/ViewModels/`），与
`schema/UiLayoutSchema.cs` 的 `UiPanel` 十一个枚举值一一对应（拍板 7 补 `Shop`，见该类型注释）。
均实现 `IDisposable`，构造期完成一次 `Refresh()`（`ShopViewModel` 例外——未 `OpenVendor` 前没有
货架可刷新，见其类型注释）并订阅相关事件触发后续自动刷新。

`ShopViewModel` 与其余十个不同：不经 `IUiPathProvider` 路径查询，直接持有
`Core.Gameplay.Economy.EconomyHost`（具体类型，见其类型注释判断记录）只读查询商人出售清单/库存/
价格，买卖仍走既有 `UiIntents.Buy`/`UiIntents.Sell`。T-N4-11 起单价改经
`IEconomyHost.TryGetSellItemPrice(vendorId, itemId)` 读取，不再直接读
`VendorSellItem.PriceAmount`（`sell_items[].price_amount` 未填时该字段恒为占位 0，见
`core/gameplay/economy/README.md` 判断记录 15）——与 `EconomyHost.Buy` 实际扣款出自同一条价格
解析路径。

## 已知契约缺口

（已解决，ADR-0050，2026-09-20）此前"`SkillHost.GetKnownSkills` 不在 `ISkillHost` 契约上（是具体类
`Core.Rules.Skill.SkillHost` 的公开方法）"——`ISkillHost` 现已补 `GetKnownSkills`/`Knows`/
`LearnSkill`/`LearnFromBook` 四个默认接口成员（见 `core/rules/common/README.md` 判断记录 11）；
`SkillHostSkillBookQuery` 已新增接受 `ISkillHost` 的构造函数重载。本模块仍保留窄接口
`ISkillBookQuery` + 适配器 `SkillHostSkillBookQuery`（UI 侧仍不需要 `ISkillHost` 其余大部分成员，
窄接口收敛依赖的价值不因宿主契约补齐而消失），测试仍可用内存 Fake 替代。

（已解决，缺口 4）此前"没有宿主契约暴露'动作条槽位 → 技能 id'绑定查询"——G1 补了
`Core.Carriers.Unit.ISkillBindingHost`（`player.skill_bindings` 运行期查询/写入），
`ActionBarViewModel` 已改用它替代 `Func<int, Id?>` 注入委托；`UiIntents` 新增
`BindActionBarSlot`/`UnbindActionBarSlot` 作为技能书面板拖放/点击绑定的意图入口。

（已解决，缺口 12）此前"分层音效音量没有专门的音量宿主契约"——`presentation/vfx_sfx` 已补
`IAudioLayerVolumeHost`（层清单 = `sfx.def.layer` 去重 + `music`，读写经 `IAudio`/`ISfxPlayer`
落地并经 `ISettingsStore` 持久化），`SettingsViewModel`/`UiIntents` 已改用它，删除此前的
`Func<string, double>`/`Action<string, double>` 注入回调。

（ADR-0019 F1c 判断记录）`ui_layout_definition.fields` 不登记子结构：不是简单的 Map（键为
`stat_id`/锚点名一类同构键值），而是"具体布局参数，结构由引擎适配层/具体游戏约定"的完全不透明
JSON blob（见 `UiLayoutDefinition.Fields` 类型注释"本模块只透传"）——本模块自己唯一会读的逻辑
相关项是顶层 `slots`（已登记为 `FieldKind.Int`），`fields` 保持"存在且是对象"。

（P4-2 已修补，不再是契约缺口）`IDialogHost` 原先没有对称于 `GetStoryView` 的 `GetGossipView` 只读
查询，`DialogViewModel` 的 gossip 视图需要打开菜单的调用方手动灌入；`IDialogHost.GetGossipView`
现已补上，`DialogViewModel.Refresh` 与 `Story` 同一惯例直接查询，`SetGossipView` 仅保留供尚未升级
的既有调用方兼容使用（见 `DialogViewModel` 类型注释）。

（已解决，拍板 7）此前"商店 UI 单元无视图模型/面板，只有 `UiIntents.Buy`/`UiIntents.Sell`"——补
`ShopViewModel`（见上"十一个视图模型"一节）+ `UiPanel.Shop` 第 11 值 + `ui_layout_definition`
schema/示例行同步扩枚举；09 第 7.1 节勘误已补 changelog 说明技能书/角色属性/存档槽/暂停菜单为
实现级单元（无专属数据表，纯查询/意图组合，见该文档）。

（已解决，技术债 17，2026-09-06 收口）回合顺序条/行动点显示/"结束回合"按钮三个离散模式界面单元
（09 第 7.1 节）此前只有引擎侧 `TurnStatusPanel` 一份实现（不经 `UiPanelHost` 登记，直接读
`GameplayAssembly`、经 `UiIntents` 转发意图），本模块十个视图模型均不认识 `TurnScheduler`。现已
迁入 `HudViewModel`（可选注入 `Core.Foundation.SimLoop.TurnScheduler`/`IAppStateHost`/等待输入
子态，见该类型判断记录），引擎侧 `HudPanel` 只做渲染与键盘轮询，`TurnStatusPanel` 已删除；
01 L5 `ui` 行与 09 第 7.1 节的例外注记已同步撤销；台账见落地计划"已知未解决缺口"第 17 条。

**UI-111-01 根治（第十三轮审核 6739f50，2026-09-09）**：
`architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md`，复现见
`core/logs/followup-core-probe.log` `INVENTORY-VM-SAME-MAP-LOAD` 小节。此前 `InventoryViewModel`
只订阅 `item.added`/`item.removed`/`item.equipped`/`item.unequipped` 四个背包/装备业务事件，
同图 `RestoreFromSlot` 读档时这四个业务事件本身的派发被 `SaveSystem.Load` 的抑制作用域连带压住，
`InventoryViewModel` 因此错过刷新时机，`Slots`/`EquippedSlots` 停留在读档前的 A 快照，与已经是 B
的 `IUiDataSource` 实时查询结果不一致（手动调用 `Refresh()` 才会恢复正确）。根治：逐个核对全部
十一个视图模型，凡是维护"经事件订阅增量更新的派生展示状态"（不是每次都重新 `Query`/查询宿主）
的，一律额外订阅 `SaveEventKeys.SaveLoaded` 并整体重建——`InventoryViewModel`、
`ActionBarViewModel`、`CharacterStatsViewModel`、`DialogViewModel`、`HudViewModel`、
`QuestLogViewModel`、`ShopViewModel`、`SkillBookViewModel` 八个补齐了这个订阅；`SaveSlotsViewModel`
此前已经订阅（本就依赖 `ISaveSystem.ListSlots()` 反映存档槽变化）；`PauseMenuViewModel`（状态源自
`IAppStateHost`，与存档数据无关）、`SettingsViewModel`（本地化/按键绑定/音频分层音量是设备级用户
偏好，不随存档槽切换，见 `core/foundation` 对应模块——均未提供 `IPersistable` 实现）两个确认与存档
数据无关，不需要订阅，逐个核对后排除。重复收到 `save.loaded`（如迁移紧接读档两次派发）只是多刷新
一次，幂等无副作用，构造函数只调用一次 `Subscribe`，不重复订阅。

对应测试：`presentation/ui/tests/ViewModelTests.cs` 新增 `UI111_01_*` 系列（`InventoryViewModel`
两条：同图读档后无需手动 `Refresh` 即与宿主当前状态一致、连续两次 `save.loaded` 均正确刷新且
`Dispose` 后不再响应；`ActionBarViewModel`/`CharacterStatsViewModel`/`DialogViewModel`/
`HudViewModel`/`QuestLogViewModel`/`SkillBookViewModel` 各一条），`presentation/assembly/tests/
PresentationAssemblyTests.cs` 新增 `Shop_SaveLoadedEvent_RefreshesOpenShelf_NoManualRefresh`
（`ShopViewModel` 走真实 `EconomyHost`，需要装配根环境，故放在该测试文件而非本模块）。

## 判断记录（诊断契约统一转发机制，2026-09-19，architecture/adr/0042-诊断契约统一转发到宿主控制台.md）

`UiDataSource` 新增只读属性 `Diagnostics`（返回 `IUiDiagnostics`，ABI 只新增只读属性，不改动任何既有公开签名）：
全仓普查发现本模块的诊断契约同仓库另外 20 余个 `I*Diagnostics` 契约一样，此前只记内存
（`UiDataSource` 构造函数未注入自定义实现时默认 `new InMemoryUiDiagnostics()`），从不外发到引擎控制台——真实
游戏里出现对应告警时控制台一行输出都没有。本轮由 `adapters/unity` 侧新增的
`Adapter.Unity.Diagnostics.DiagnosticsHub`（注册制轮询集线器，见其类型注释）通过本属性拿到默认
实例引用，登记进 `DiagnosticsHubComposition.RegisterCoreSources`，三个生产装配入口
（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.GameBootstrap`）每帧轮询转发
一次（恒映射为控制台 Warning，不产生 Error，硬约束见该 ADR）。本模块自身逻辑不变，只是多了一个
对外只读出口。

## 判断记录（QuestLogViewModel 新增 GetObjectiveRequiredCounts 转发，2026-09-20，消费方反馈第 2 条）

`QuestLogPanel`（`adapters/unity`）此前只展示任务 id 与状态，不展示 `QuestProgress.ObjectiveCounts`
——核实结论是数据链路本身是通的，缺口纯粹是参考 UI 没有读取，不是本模块的契约缺口。但 UI 要拼
"当前/需求"文案还差一个"需求数量"，`QuestLogViewModel` 此前只转发 `IQuestHost.GetLog`/
`GetActiveObjectives`，没有转发口子能拿到任务定义的目标需求数。新增只读方法
`GetObjectiveRequiredCounts(Id questId) => _quest.GetObjectiveRequiredCounts(questId)`——纯转发，
不新增订阅、不缓存快照（需求数量来自内容定义，不随事件变化，不需要像 `Log` 那样经 `Refresh` 缓存，
因此不影响本文件"UI-111-01 根治"那一节梳理的"经事件订阅增量更新的派生展示状态需要订阅
`save.loaded`"结论——本方法每次都直接查询，不是缓存字段）。ABI 判断：`IQuestHost` 侧新增的是默认
接口成员（见 `core/gameplay/quest/README.md` 判断记录 19），本模块这条纯转发是新增方法，同样不改
动任何既有公开签名，不需要 ADR。测试见 `presentation/ui/tests/ViewModelTests.cs`
`QuestLogViewModel_GetObjectiveRequiredCounts_ForwardsQuestHostAndDegradesWhenMissing`。

## 判断记录（HudViewModel.TargetId / ActionBarViewModel 技能名称，2026-09-20，消费方反馈第 3 条，[ADR-0048](../../architecture/adr/0048-任务起始方式补与场景物件交互取值.md)）

背景：`HudViewModel` 此前没有任何字段能表达"当前目标是谁"，目标框只能展示资源条数字；
`ActionBarViewModel` 的槽位快照也没有技能名称，动作条只能展示技能引用的短串。两者迫使
`adapters/unity` 参考界面只能硬编码/展示内部引用短串。

**`HudViewModel.TargetId`**：新增只读属性 `Id? TargetId`，经新增的 `TargetPathProvider` 叶子
路径 `target.id` 解析（`ExprValue.OfId`），无目标时为 `null`。判断记录（转发原始引用，不是解析
好的显示文本）：见 ADR-0048 决策 4——本模块视图模型一贯只转发原始 `Id`，把"展示成什么"交给
消费端；本字段延续这一惯例，不新增名称解析服务。ABI：`TargetPathProvider.Resolve` 新增一个
`switch` 分支，`HudViewModel` 不新增构造参数，均不改动既有公开签名。

**`ActionBarViewModel`/`ActionBarSlotSnapshot.NameKey`**：`ActionBarSlotSnapshot` 新增只读属性
`Id? NameKey` + 配套三参数构造函数重载（既有两参数构造函数不变）；`ActionBarViewModel` 新增携带
`ISkillBookQuery` 的五参数构造函数重载（既有四参数构造函数不变），`Refresh()` 在提供了
`ISkillBookQuery` 时经 `GetNameKey(skillId)` 解析技能名称写入快照。`ISkillBookQuery` 新增默认
接口成员 `Id? GetNameKey(Id skillId) => null`（同 `IQuestHost.GetObjectiveRequiredCounts` 既有
默认接口成员惯例），`SkillHostSkillBookQuery` 覆盖转发到新增的 `SkillHost.GetSkillNameKey`。
`name_key` 字段本身命名/类型见 `core/rules/skill/README.md` 同名判断记录（TextKey 惯例）。

**消费端**：`adapters/unity` 的 `HudPanel.RefreshUi` 目标标签补身份短串（`ShortId`，同文件既有
`CurrentActorId`/`TurnOrder` 展示手法）；`ActionBarPanel.Construct` **新增**携带 `IL10nHost` 的
构造函数重载（既有三参数签名原样保留，行为不变——取不到 `IL10nHost` 时回退既有 `ShortId`），
生产调用方 `UiPanelHost.Initialize` 改走新重载。判断记录：设计层复审否决了"直接给既有签名加
必填参数"的初版方案——AGENTS.md §3 的 ABI 纯新增纪律不因 `adapters/unity` 不在 ABI 探针锁定的
六个核心程序集范围内而失效（探针 `breaks=0` 只说明未扫描该程序集，不等于无破坏），且消费方
第 3 条反馈原话表明其已持有 `ActionBarPanel`，必填参数会在这次修复硬编码的发布里反而破坏其
构建；改为加性重载后与 `IQuestHost.GetObjectiveRequiredCounts`（默认接口成员）、
`DataHotReload.Initialize` 四参数版（新增重载）同一手法。`RefreshUi` 在新重载下优先渲染
`_l10n.Text(slot.NameKey.Value)`，未声明该字段或走旧签名（`_l10n` 为 `null`）时回退既有
`ShortId`。

测试见 `presentation/ui/tests/UiDataSourceTests.cs`（`Query_target_id_*` 两条，新增 `target.id`
叶子路径有/无目标两态）与 `presentation/ui/tests/ViewModelTests.cs`（`HudViewModel_TargetId_*`；
`ActionBarViewModel_WithSkillCatalog_ResolvesNameKey_*`/`ActionBarViewModel_WithoutSkillCatalog_NameKeyIsAlwaysNull`，
覆盖 `NameKey` 经 `ISkillBookQuery` 解析、未提供该依赖或技能未声明字段时为 `null`）；
`adapters/unity` 侧新增 PlayMode 测试
`ActionBarPanel_LegacyThreeArgConstruct_DoesNotThrow_AndFallsBackToShortId`
（`DiscreteCombatTests.cs`），覆盖走旧三参数 `Construct` 签名不抛异常、且回退展示技能引用短串
（不解析 `name_key`）。

## 判断记录（HudViewModel.TargetName / TargetFaction，2026-09-21，消费方反馈第 3 条续，沿用 [ADR-0048](../../architecture/adr/0048-任务起始方式补与场景物件交互取值.md) 口径）

背景：上一条判断记录落地的 `HudViewModel.TargetId` 只转发目标原始 `Id`，接入方要做目标框（展示
被选中目标的名字与阵营）仍拿不到数据，只能自行查表硬编码。本条补这个缺口，只加两条叶子路径，
不引入新机制。

**`TargetPathProvider.target.faction`**：新增构造函数重载，携带 `Core.Rules.Common.IUnitAccess`
（既有三参数构造函数原样保留）；`target.faction` 经 `IUnitAccess.GetFaction` 转发目标阵营原始
`Id`，同 `target.id` 口径不解析显示文本。判断记录（无诊断分支）：`Unit.factionId`（05 第 1.2 节）
是必填运行期字段，单位一旦存在就恒有合法值，唯一"查不到"的情形是目标当前不在世界模拟中
（`IUnitAccess.Exists` 为 `false`），按 `power`/`stat` 子路径对未注册单位的既有处理同一惯例静默
返回 `null`，不是内容配置问题，不需要诊断。

**`TargetPathProvider.target.name`**：同一构造函数重载再携带
`Core.Carriers.Creature.ICreatureTemplateQuery`；`target.name` 先经 `IUnitAccess.GetTemplateId`
取目标的内容模板 id，再经 `ICreatureTemplateQuery.Get(...).NameKey` 取显示名文本键——口径与
`skill.def.name_key`/`ActionBarSlotSnapshot.NameKey` 一致：文本键，不是已本地化文本，本模块不做
本地化。判断记录（诊断分支）：目标存在但取不到内容模板（`GetTemplateId` 为空——手工放置对象没有
模板引用）或模板 id 未在 `ICreatureTemplateQuery` 登记，属于内容配置问题，记一条诊断后返回
`null`（AGENTS.md §3"运行时路径不静默降级"），不回退成占位文案。

**`HudViewModel`**：新增只读属性 `Id? TargetName`/`Id? TargetFaction`，`Refresh()` 分别经
`target.name`/`target.faction` 查询转发，无目标时为 `null`（与 `TargetId` 既有口径逐字一致）。
不新增构造参数，不改动既有公开签名。

**生产装配**：`PresentationAssembly` 构造 `TargetPathProvider` 改走新的五参数构造函数重载，传入
`gameplay.Carriers.Units`（`IUnitAccess`）与 `gameplay.Carriers.Creatures`
（`CreatureFactory` 兼实现 `ICreatureTemplateQuery`，同 `CreatureInteractionHost` 既有取用方式）。

ABI：纯加法——`TargetPathProvider` 新增一个构造函数重载与两个 `private` 解析方法、`Resolve`
的 `switch` 新增两个分支；`HudViewModel` 新增两个只读属性；全部既有公开签名与全部
`creature.template` 数据行零改动仍合法。

测试见 `presentation/assembly/tests/PresentationAssemblyTests.cs`
（`HudViewModel_TargetNameAndFaction_ReflectRegisteredTemplate_NullWhenNoTarget`/
`HudViewModel_TargetName_TemplateNotRegistered_ReturnsNull_AndRecordsDiagnostic`）——选择装配级
测试而不是 `presentation/ui/tests` 惯用的 Fake 夹具，因为本条需要一条真实登记的 `creature.template`
记录（`name_key`/`faction_id` 具体取值）与真实 `CreatureFactory.Spawn` 产生的单位，才能验证
"取到的值就是模板声明的那两个值"这一装配级行为，而不只是 Provider 内部转发逻辑本身。

## 判断记录（ActionBarViewModel 冷却总时长/充能/结构化不可用原因，2026-09-21，消费方反馈
第三批第 2 条，[ADR-0057](../../architecture/adr/0057-动作条槽位补冷却总时长充能与结构化不可用原因.md)）

`ISkillBookQuery` 新增默认接口成员 `GetSkillReadiness(Id unitId, Id skillId)`，转发规则层
`ISkillHost.GetSkillReadiness`/`SkillReadiness` 既有的完整就绪数据（技能自身/分类/公共冷却、
充能、修饰后总时长、六种阻塞位）——未显式覆盖时按"只看冷却剩余"的降级算法就地计算（与规则层
同名成员的默认降级算法逐字一致，不是另起一套）；生产适配器 `SkillHostSkillBookQuery` 显式覆盖，
直接转发。不新增第三个成员之外的窄接口改动，也不改 `GetKnownSkills`/`GetCooldown` 既有两个成员。

`ActionBarSlotSnapshot` 新增四个只读字段（`EffectiveCooldownDuration: double?`、
`MaxCharges: int?`、`CurrentCharges: int?`、`BlockReason: ActionBarSlotBlockReason`），既有
两个构造函数（两参数、三参数）字节级不变、缺省填 `null`/`null`/`null`/`None`；新增一个七参数
构造函数。新枚举 `ActionBarSlotBlockReason`（`None`/`ConditionNotMet`/`SkillCooldown`/
`CategoryCooldown`/`NoCharges`/`GlobalCooldown`/`ActionLocked`/`Unknown`）取值一一对应规则层
`SkillReadinessBlockers` 的六个阻塞位加"无阻塞"，外加一个"未知"（数据不可用，见下）；多个阻塞
位同时成立时的裁决顺序是写死的显式 if 链（`ResolveBlockReason`），不依赖位值大小或枚举/字典
迭代顺序，顺序依据见 ADR-0057 决策 3。

既有 `Available` 布尔字段的窄口径原样保留（"有绑定技能且自身冷却已就绪"），不回头用新的
`BlockReason` 重新定义它——需要完整判定的接入方改用"`BlockReason == None`"，`Available` 留作
向后兼容的窄口径入口，理由见 ADR-0057 决策 5。

`ActionBarViewModel` 新增一个携带 `IUiDiagnostics` 的六参数构造函数重载：只有传入诊断出口时
才启用新字段的完整解析路径；`GetSkillReadiness` 判定为"数据不可用"（`EffectiveCooldownDuration`
为 `null`，即窄接口落到降级算法）时，经该诊断出口告警一次，槽位标记 `BlockReason.Unknown`，
不当作 `None` 处理（AGENTS.md §3"运行时路径不静默降级"）；未使用新构造函数重载的既有调用方
（含既有测试）不触发这条诊断路径，新四个字段恒为缺省值，行为逐位不变。`PresentationAssembly`
的生产装配改用该重载，复用既有的 `UiDiagnostics` 实例，不新增第二套诊断出口。

## 判断记录（施法条 `casting.*` / 增益减益列表 `auras.*`，2026-09-21，消费方反馈第 1/4 条，[ADR-0056](../../architecture/adr/0056-施法条与光环列表数据补全.md)）

背景：本项目 HUD 只能展示生命/资源条与目标身份，缺"谁在读条、还剩多久、总共多久"（施法条）与
"目标身上有哪些增益/减益、层数、剩余时长"（增益减益列表），消费方只能自建平行机制直接读
`ISkillHost`/`IAuraQuery` 拼装。沿用本模块一贯机制（既有路径小语法 + `HudViewModel` 便利属性），
不另起一套。

**共享解析逻辑**：`UnitSubQueries` 新增 `Casting`/`Auras` 两个静态方法，供 `PlayerPathProvider`/
`TargetPathProvider` 复用（`UnitPathProvider` 面向路径显式携带的任意单位 id，不装配这两项依赖，
不复用）——`casting.skill|remaining|total`（施法条）、`auras.count`/`auras[i].def|stacks|
remaining|total|name_key|polarity|icon_ref`（增益减益列表，形状同既有 `inventory[i].<field>`；
`polarity`/`icon_ref` 为 [ADR-0060](../../architecture/adr/0060-光环极性与图标引用字段补全.md)
一个发现的交付缺口新增，惯例同 `name_key`——字段未声明时静默返回"无"，不记诊断）。

**诊断分支（AGENTS.md §3"运行时路径不静默降级"）**：`casting.remaining`/`casting.total` 区分两种
`null`——"当前无人读条"（`GetCastingSkillId` 为空，合法查询无值，同 `target.id` 无目标口径，不
记诊断）与"确认在读条但取不到剩余/总时长"（`GetCastingSkillId` 非空但对应查询仍为空，数据不一致，
记一条诊断）。`auras.*` 的诊断分支下沉在规则层 `AuraHost.GetActiveAuraSnapshots` 内部（光环定义
理论上必然已登记，防御性诊断，见 `core/rules/skill/README.md` 对应判断记录），本模块不重复诊断。

**依赖装配**：`casting.*` 复用两个 Provider 既有必填的 `ISkillBookQuery`（新增
`GetCastingSkillId`/`GetCastingRemaining`/`GetCastingTotal` 三个默认接口成员，默认降级为 `null`；
这三个成员随后已提升进 `Core.Rules.Common.ISkillHost` 契约本身，`SkillHostSkillBookQuery` 直接
经接口引用转发，不再向下转型到具体类 `Core.Rules.Skill.SkillHost`——见
`core/rules/common/README.md` 判断记录 15 收口记录），`PlayerPathProvider` 不需要新增构造函数即可
解答。
`auras.*` 需要新增的 `IAuraQuery` 依赖：`PlayerPathProvider` 新增十参数构造函数重载（追加
`IAuraQuery auraQuery`），`TargetPathProvider` 新增七参数构造函数重载（在既有五参数重载后追加
`ISkillBookQuery skillBook, IAuraQuery auraQuery`，两项一并新增而不是拆成两次重载——本次改动
同批交付，同 `target.name`/`target.faction` 当初合并进同一个五参数重载的既有先例）。均纯加法，
未装配新重载的既有调用方两条新路径恒返回"无"（不记诊断，同 `_unitAccess` 既有惯例）。

**`HudViewModel`**：新增只读标量属性 `CastingSkillId`/`CastingRemaining`/`CastingTotal`（玩家自身）
与 `TargetCastingSkillId`/`TargetCastingRemaining`/`TargetCastingTotal`（目标），及只读列表属性
`Auras`/`TargetAuras`（`IReadOnlyList<Core.Rules.Common.AuraSnapshot>`）。列表属性经 `count` +
`[i].<field>` 路径逐条查询重建（惯例同既有 `PowerBars` 按 `_powerTypes` 逐个查询重建），顺序原样
转发查询结果——权威排序（按光环创建顺序）已在规则层完成，本视图模型不重新排序。不改动任何既有
公开签名。

**生产装配**：`PresentationAssembly` 新增局部变量 `auraQuery = gameplay.Carriers.Rules.Skill.
AuraQuery`（`SkillHost` 早已暴露的公开属性，不新增依赖边界），`PlayerPathProvider`/
`TargetPathProvider` 改走新增的重载传入 `skillBookQuery`/`auraQuery`。

ABI：纯加法——两个 Provider 各新增一个构造函数重载，`ISkillBookQuery` 新增三个默认接口成员，
`HudViewModel` 新增八个只读属性；全部既有公开签名与全部既有 `skill.aura_def`/`skill.def` 数据行
零改动仍合法。

测试见 `presentation/ui/tests/UiDataSourceTests.cs`/`ViewModelTests.cs`（Fake `ISkillBookQuery`/
`IAuraQuery` 夹具，覆盖路径解析、诊断计数、`HudViewModel` 属性刷新）与
`core/rules/skill/tests/CastingSnapshotTests.cs`/`AuraSnapshotTests.cs`（规则层真实读条/施加光环，
详见该模块 README）。

## 判断记录（缺陷修复：`HudViewModel.Auras`/`TargetAuras` 的 `Polarity`/`IconRef` 恒为默认值，2026-09-21，消费方反馈第四批第 3 条）

背景：[ADR-0060](../../architecture/adr/0060-光环极性与图标引用字段补全.md) 给 `skill.aura_def`
新增 `polarity`/`icon_ref` 两个可选字段，并给 `AuraSnapshot` 新增携带这两个字段的七参数构造函数
重载，`core/rules/skill/core/AuraHost.GetActiveAuraSnapshots` 正确转发。但消费方反馈
`HudViewModel.Auras`/`TargetAuras` 里每一条快照的 `Polarity` 恒为 `Undeclared`、`IconRef` 恒为
`null`，即使对应光环定义确实声明了这两个字段。

**根因（`presentation/ui/core/ViewModels/HudViewModel.cs`，`RefreshAuras` 方法）**：本视图模型的
`Auras`/`TargetAuras` 不是直接持有规则层返回的 `AuraSnapshot`，而是逐字段经路径查询
（`{root}[i].<field>`）重新组装一份新的 `AuraSnapshot`（见上一条判断记录"列表属性经 `count` +
`[i].<field>` 路径逐条查询重建"）。ADR-0060 落地时只新增了路径层的
`auras[i].polarity`/`auras[i].icon_ref` 两个叶子（`UnitSubQueries.Auras` 正确输出），但
`RefreshAuras` 重新组装快照的那几行查询语句本身没有同步补上这两个字段的查询，仍在调用五参数的
旧 `AuraSnapshot` 构造函数——即"转发链路上游（规则层→路径层）正确，最后一环（路径层→视图模型）
逐字段重新拼装时漏了新加的两个字段"，不是任何转发环节把值搞错，是这一环根本没读。

**全仓 `new AuraSnapshot(` 调用点复查**（confirm 本次缺陷是唯一的生产代码问题点）：
- `core/rules/skill/core/AuraHost.cs`（`GetActiveAuraSnapshots` 内部）：已使用七参数构造函数，
  正确转发 `polarity`/`iconRef`，非缺陷点。
- `core/rules/assembly/RulesAssembly.cs`（`DeferredAuraQuery.GetActiveAuraSnapshots`）：显式转发
  到 `Real.GetActiveAuraSnapshots(unitId)`，代码注释明确说明这是为了避免落到 `IAuraQuery` 接口
  默认方法的降级实现，非缺陷点。
- `core/rules/common/contracts/IAuraQuery.cs`（接口默认方法内的降级实现）：按设计返回一份没有
  `polarity`/`iconRef` 的退化快照——该抽象层级本就拿不到 `skill.aura_def` 登记表，是有文档说明
  的故意降级，不是缺陷。
- `presentation/ui/core/ViewModels/HudViewModel.cs`（`RefreshAuras`）：**本次缺陷点**，五参数
  构造调用漏读 `polarity`/`icon_ref` 两条路径。已修复为读取
  `{root}[i].polarity`/`{root}[i].icon_ref` 并改走七参数构造函数，未声明字段的查询结果为"无"时
  分别回落到 `AuraPolarity.Undeclared`/`null`（与路径层未声明时的"无"语义一致，不是新造一个默认
  值）。
- `presentation/ui/tests/UiDataSourceTests.cs`/`ViewModelTests.cs` 内的五参数 `AuraSnapshot(` 调用：
  均为测试夹具，构造与本缺陷无关的其它测试场景数据，非缺陷点。

**为什么 ADR-0060 落地时的验收测试没有拦住这个缺陷**：ADR-0060 落地时新增的测试
（`core/rules/skill/tests/AuraPolarityIconRefTests.cs`）只验证到规则层 `AuraHost` 这一级——断言
`AuraHost.GetActiveAuraSnapshots` 返回的快照带有正确的 `Polarity`/`IconRef`；表现层路径查询
（`presentation/ui/tests/UiDataSourceTests.cs`）也只验证到 `IUiDataSource.Query("auras[0].polarity")`
这一级路径求值本身正确。两组测试都没有再往下验证"`HudViewModel.Auras` 这个字段列表属性读出来
的值"——而 `RefreshAuras` 恰恰是在路径查询结果之上又重新组装了一次快照，这一步组装逻辑本身没有
被任何测试覆盖到。判断记录（对未来同类"字段穿线"改动的验收深度要求）：任何一次给某个数据结构
新增字段、且该结构在链路上存在"规则层产出 → 路径查询转发 → 视图模型重新组装成同类型对象"这种
三段式转发时，验收测试必须贯穿全部三段、以生产装配入口（`PresentationAssembly`）为准，断言到
最终暴露给消费端的那个属性（本例即 `HudViewModel.Auras[i].Polarity`），不能止步于中间任何一段
转发正确就视为验收通过——中间某一段正确不代表最后一段的"重新组装"逻辑同步跟上了新字段。

测试见 `presentation/assembly/tests/PresentationAssemblyTests.cs`
（`HudViewModel_Auras_ReflectPolarityAndIconRef_DeclaredValues_UndeclaredWhenNotDeclared`）——
选择装配级测试而不是 `presentation/ui/tests` 惯用的 Fake 夹具，理由同 `TargetName`/`TargetFaction`
判断记录：需要贯穿真实的 `skill.aura_def` 登记、真实的 `EffectSink.ApplyAura` 施加与真实的
`PresentationAssembly` 装配，才能验证到"最终读出来的属性值就是登记表声明的那两个值"这一装配级
行为，而不只是某一段转发逻辑本身。

## 判断记录（`player.auto_attack.*`/`target.auto_attack.*`、`player.alive`/`target.alive`，2026-09-21，消费方反馈第四批第 1/2 条，[ADR-0061](../../architecture/adr/0061-表现层补普通攻击状态与存活状态转发.md)）

背景：[ADR-0059](../../architecture/adr/0059-普通攻击的框架原生执行机制.md) 把普通攻击落地为
`Core.Rules.Combat.AutoAttackHost`，但表现层没有任何路径能读到它的状态；表现层同样没有任何路径
能回答"这个单位是否存活"。两者均是"接入方拿不到就只能自己猜或自己判断血量阈值"的纯加法缺口，
详细决策见 ADR-0061，本条只记落地要点。

**`PlayerPathProvider.player.auto_attack.state`/`TargetPathProvider.target.auto_attack.state`**：
新增子路径，形如 `auto_attack.state`（不接受下标/其它子字段，非法形状记一条诊断，见
`UnitSubQueries.AutoAttack`），经 `AutoAttackHost.GetState(unitId)` 取值后由新增的
`Core.Rules.Combat.AutoAttackStateNames.ToText` 转成固定小写文本（`off`/`no_target`/`attacking`）
装入 `ExprValue.OfString`。`PlayerPathProvider` 新增十二参数构造函数重载（追加
`IUnitAccess unitAccess, AutoAttackHost autoAttackHost`），`TargetPathProvider` 新增八参数构造
函数重载（追加 `AutoAttackHost autoAttackHost`）。

**`PlayerPathProvider.player.alive`/`TargetPathProvider.target.alive`**：新增子路径（不接受
下标/子字段），经 `IUnitAccess.Exists(unitId)`×`IsAlive(unitId)` 得出（见 `UnitSubQueries.Alive`）；
`target.alive` 无当前目标时求值结果为"无"，与 `target.id` 既有"无目标即为空"口径逐字一致，不
新造判定。

**`HudViewModel`**：新增只读属性 `PlayerAlive`（`bool`）、`TargetAlive`（`bool?`）、
`AutoAttackState`（`Core.Rules.Combat.AutoAttackState`，玩家自身）、`TargetAutoAttackState`
（同类型，目标）。四者均经 `IUiDataSource.Query` 转发对应新增路径，`AutoAttackState`/
`TargetAutoAttackState` 读到路径输出的文本后经 `AutoAttackStateNames.Parse` 转回枚举——协议层
文本化只发生在路径查询这一层边界，属性本身仍是类型安全的枚举，不对消费端暴露裸字符串（同
`AuraPolarity` 既有做法）。不改动任何既有公开签名。

**生产装配**：`PresentationAssembly` 新增局部变量 `autoAttackHost = gameplay.Carriers.Rules.
AutoAttack`（`RulesAssembly` 已有的公开只读属性，不新增依赖边界），两个 Provider 均改走新增的
重载传入。

ABI：纯加法——两个 Provider 各新增一个构造函数重载，`HudViewModel` 新增四个只读属性，
`Core.Rules.Combat` 新增 `AutoAttackStateNames` 一个新类型；全部既有公开签名不改动。

测试见 `presentation/assembly/tests/PresentationAssemblyTests.cs`
（`HudViewModel_AutoAttackStateAndTargetAlive_ReflectRealCombatAndDeathPath`）——经真实武器装备、
真实 `AutoAttackHost.SetTarget`/`SetEnabled` 与真实 `WorldSim.Tick` 驱动结算致死目标，断言
`AutoAttackState` 从 `Off`→`Attacking`、`TargetAlive` 从 `true`→`false`、目标死亡到
`AutoAttackState` 回落 `NoTarget` 之间存在的一拍延迟（`AutoAttackHost.Update` 既有惯例，见
`core/sim/tests/AutoAttackHostIntegrationTests.cs` 对应判断记录），而不只是验证某一段转发逻辑
本身正确。

## 判断记录（`InteractPathProvider`、`interact.nearest.*`，2026-09-21，消费方反馈第五批第 1 条，[ADR-0062](../../architecture/adr/0062-地面掉落物原生交互与统一最近可交互目标查询.md)）

新增 `Presentation.Ui.InteractPathProvider`，`Root => "interact"`，解答 `interact.nearest.
id|kind|distance` 三个叶子字段。核对现状后确认表现层此前对 gobj/creature 同样从未提供过"附近是否
有可交互目标"查询——不是"另两类已有、只差 loot"，而是三类都没有；既然要新开路径，直接设计成三类
统一：`interact.nearest.kind` 返回 `EntityKinds` 既有的三个字符串常量之一（`"gobj"`/`"creature"`/
`"loot"`，复用既有 token，不新造一套字符串），接入方判断"附近是否有可拾取掉落物"只需要
`interact.nearest.kind == "loot"`，不需要框架为掉落物单独开一条 `interact.nearest_loot.*`。附近
无候选时三个叶子均返回 `null`，不记诊断——同 `target.*` 既有惯例（正常查询结果，不是路径错误）；
段数/关键字不符时才记诊断。

构造函数 `InteractPathProvider(Id unitId, IInteractionTargetRegistry registry, double? maxRange =
null)`：`unitId` 是查询锚点（同 `PlayerPathProvider` 惯例，只服务"玩家自己按键时附近有什么"这一
最常见场景，不支持查任意单位）；`maxRange` 默认 `null`（不限距离）——三类目标各自的 `interact`
意图分流已经在判距（`GobjOptions.InteractRange`/`CreatureInteractOptions.InteractRange`/
`LootOptions.PickupRange`），本查询只是提示玩家附近有什么，不重复实现一套独立的范围策略。
`PresentationAssembly` 接线：`new InteractPathProvider(_playerId, gameplay.Carriers.
InteractionTargets)`（`CarriersAssembly` 新增的只读属性，见该模块 README 对应判断记录）。

验收贯通到生产装配入口：`presentation/assembly/tests/PresentationAssemblyTests.cs` 新增
`InteractPathProvider_NearestTarget_PrefersCloserLoot_OverFartherGobj_AndFallsBackAfterPickup`
——经真实 `PresentationAssembly.Build` 装配、真实掉落物生成与真实 `LootHost.PickUp` 拾取（拾取后
需推进一次 `world.Tick` 让 `MarkForDestruction` 完成生命周期清理，`IInteractionTargetRegistry`
现场查询才会反映最新状态，见 `core/carriers/assembly/README.md` 对应判断记录），断言路径查询结果
从"命中较近的掉落物"正确回退到"命中较远的场景物件"，覆盖到最外层（视图模型/路径层），不停在
装配代码中间层。

## 判断记录（`player.equipment.<slot>.template`/`.instance`、`InventoryViewModel.EquippedSlotIdentities`，2026-09-21，消费方反馈——游戏接入方第五批第 2 条，[ADR-0063](../../architecture/adr/0063-装备宿主契约补模板id查询.md)）

背景：装备面板要显示"槽位名 + 已装备物品名"，需要已装备物品的模板 id；既有
`player.equipment.<slot>` 裸路径只返回实例 id，`IEquipmentHost` 契约同样拿不到模板 id（见
ADR-0063）。本条只记表现层落地要点，契约层新增成员见 `core/carriers/item/README.md`/
`core/carriers/common` 对应判断记录。

**`PlayerPathProvider.ResolveEquipment`**：新增 `.template`/`.instance` 两个子路径。装备槽 id
允许多段（namespaced，如 `equip.main_hand`，见 `UiDataSourceTests.Query_equipment_slot`），沿用
`ResolveQuest`/`ResolveSkillCooldown` 已有的"末段保留关键字，其余段拼装 id"惯例：
`remaining.Count >= 3` 且末段不带下标、字面量恰为 `"template"`/`"instance"` 时按子路径解析；否则
落到既有裸查询分支，行为逐字节不变。`.template` 经新增的 `IEquipmentHost.GetEquippedTemplateId`
取值，`.instance` 经既有 `IEquipmentHost.GetEquipped` 取值（与裸路径同一数据源，只是显式给出
子路径入口）。不新增 `.count`（装备 `stack_size` 恒为 1，无信息量）/`.name_key`/`.quality`
（需要另查模板/词缀表，背包侧 `inventory[i].*` 同样不给，两侧口径一致，详见 ADR-0063）。

**`InventoryViewModel`**：新增只读属性 `EquippedSlotIdentities`
（`IReadOnlyDictionary<Id, EquippedItemIdentity>`，槽位 id → 实例 id + 模板 id），比照 `Slots`
（`InventorySlotSnapshot` 携带模板 id）。判断记录（新增属性而不是改写既有
`EquippedSlots` 的元素类型）：`EquippedSlots`（槽位 id → 裸实例 id）是已发布的公开只读属性，
ABI 只允许新增，改写其元素类型会破坏既有调用方编译；新增并行属性，两者互不影响，仍只需要裸实例
id 的既有消费方继续读 `EquippedSlots`。`Refresh()` 同一次遍历内先查裸 `player.equipment.<slot>`
判断该槽是否已装备，已装备才追加查一次 `.template`（未装备槽本就没有必要查一次必然落空的子
路径）。

ABI：纯加法——`PlayerPathProvider` 无新增公开签名（`ResolveEquipment` 是私有方法，扩展的是它能
识别的路径形状，不是新增重载）；`InventoryViewModel` 新增一个只读属性；载体层新增内容见
`core/carriers/common`/`core/carriers/item` 对应判断记录。全部既有公开签名不改动。

测试见 `presentation/ui/tests/UiDataSourceTests.cs`
（`Query_equipment_slot_template_and_instance_subpaths`、
`Query_equipment_slot_template_and_instance_subpaths_AreNull_WhenEmptyOrUnequipped`）与
`presentation/ui/tests/ViewModelTests.cs`（`InventoryViewModel_reads_slots_and_equipped_items`
补充断言）——经 `FakeEquipmentHost`（新增 `TemplatesByInstance` 登记表，见该类型判断记录）驱动，
覆盖已装备/空槽/卸下三种状态。

## 判断记录（`player.quest.<questId>.title_key`/`.objective_description_key[i]`、
`QuestLogViewModel.GetQuestTitleKey`/`GetObjectiveDescriptionKey`，2026-09-22，消费方反馈——
游戏接入方第六批（阻塞），[ADR-0064](../../architecture/adr/0064-任务宿主契约补标题与目标描述文本键查询.md)）

背景：任务追踪 HUD 要显示"任务名 + 目标描述 + 进度 + 可交付状态"，`QuestLogViewModel` 此前只转发
`IQuestHost.GetLog`/`GetActiveObjectives`，拿不到标题/目标描述两项。本条只记表现层落地要点，
契约层新增成员（`IQuestHost.GetQuestTitleKey`/`GetObjectiveDescriptionKey`）见
`core/gameplay/quest/README.md` 对应判断记录。

**`QuestLogViewModel`**：新增两个纯只读转发方法 `GetQuestTitleKey(Id questId)`/
`GetObjectiveDescriptionKey(Id questId, int objectiveIndex)`，比照既有
`GetObjectiveRequiredCounts` 同一处理口径——不新增订阅、不缓存快照（文本键来自内容定义，不随
运行期事件变化）。不改写 `Log`（`IReadOnlyList<QuestProgress>`）/`ActiveObjectives`
（`(Id QuestId, int ObjectiveIndex, Id TargetRef)` 值元组）两个既有公开成员的元素形状——前者是
已发布公开类型，后者是值元组，两者都无法在 ABI 只加不改的约束下塞入新字段。

**`PlayerPathProvider.ResolveQuest`**：新增 `title_key`（不带下标）/`objective_description_key[i]`
（带下标，下标与既有 `objective[i]` 对齐同一份 `QuestObjective` 数组）两个子路径，沿用
`ResolveQuest`/`ResolveEquipment`/`ResolveSkillCooldown` 已有的"末段保留关键字"惯例
（`remaining.Count >= 3`，末段字面量匹配则按子路径解析，其余落到既有 `state`/`objective[i]`
分支，行为逐字节不变）。两条新路径均转发到新增的 `IQuestHost` 成员，未知任务/越界下标/数据未填
统一返回"无"（`ExprValue?` 为 `null`），与既有 `objective[9]` 越界口径一致，宿主层已降级，本层
不重复判断。

ABI：纯加法——`PlayerPathProvider` 无新增公开签名（`ResolveQuest` 是私有方法，扩展的是它能识别的
路径形状）；`QuestLogViewModel` 新增两个公开方法；契约层新增内容见
`core/gameplay/quest/README.md` 对应判断记录。全部既有公开签名不改动。

**参考面板 `Adapter.Unity.Ui.Panels.QuestLogPanel`**（`adapters/unity/Packages/
com.gamefoundation.adapter.unity/Runtime/Ui/Panels/GameplayPanels.cs`）：`RefreshUi` 此前只显示
`QuestProgress.QuestId` 裸 id，顺手改为有标题键时优先显示标题。比照 `ActionBarPanel` 同一手法
（见该类型判断记录）：不给已发布的两参数 `Construct(parent, vm)` 加必填参数（消费方已在用这个
签名，加参数会导致下一次升级直接编译不过），新增携带 `IL10nHost` 的三参数加性重载——提供了才会
优先 `_l10n.Text(titleKey.Value)` 渲染真名；未提供时（既有两参数签名）有标题键则退化显示键本身
（键串本身有信息量，不是编造的占位文案，同 `ActionBarPanel` 的 `ShortId` 兜底判断记录）；完全
没有标题键时回退显示 `QuestId`，保持本字段落地前的行为。`Adapter.Unity.Ui.UiPanelHost.Initialize`
改走新重载（传 `presentation.L10n`，与 `ActionBar`/`Dialog` 两处既有接线同一份 `IL10nHost` 实例）。

测试见 `presentation/ui/tests/UiDataSourceTests.cs`
（`Query_quest_title_key_and_objective_description_key`）与
`core/gameplay/quest/tests/QuestHostTests.cs`（宿主层四例，见该文件判断记录）——经
`FakeQuestHost`（新增 `SeedTitleKeyForTest`/`SeedObjectiveDescriptionKeyForTest` 登记表，见该类型
判断记录）与真实 `QuestHost` 两条路径分别驱动。经 `PresentationAssembly` 构建出的 `QuestLog`
视图模型最外层验收（接一个真实数据定义的任务，标题键/目标描述键与数据定义一致，未填描述的目标
返回"无"）见 `presentation/assembly/tests/PresentationAssemblyTests.cs`
（`QuestLog_ThroughPresentationAssembly_ExposesTitleAndObjectiveDescriptionKeys`）。
