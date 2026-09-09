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
价格，买卖仍走既有 `UiIntents.Buy`/`UiIntents.Sell`。

## 已知契约缺口

- `SkillHost.GetKnownSkills` 不在 `ISkillHost` 契约上（是具体类 `Core.Rules.Skill.SkillHost` 的
  公开方法），本模块定义窄接口 `ISkillBookQuery` + 适配器 `SkillHostSkillBookQuery` 收敛依赖，
  测试用内存 Fake 替代，不必搭建 `SkillHost` 的完整构造依赖链。
（已解决，缺口 4）此前"没有宿主契约暴露'动作条槽位 → 技能 id'绑定查询"——G1 补了
`Core.Carriers.Unit.ISkillBindingHost`（`player.skill_bindings` 运行期查询/写入），
`ActionBarViewModel` 已改用它替代 `Func<int, Id?>` 注入委托；`UiIntents` 新增
`BindActionBarSlot`/`UnbindActionBarSlot` 作为技能书面板拖放/点击绑定的意图入口。

（已解决，缺口 12）此前"分层音效音量没有专门的音量宿主契约"——`presentation/vfx_sfx` 已补
`IAudioLayerVolumeHost`（层清单 = `sfx.def.layer` 去重 + `music`，读写经 `IAudio`/`ISfxPlayer`
落地并经 `ISettingsStore` 持久化），`SettingsViewModel`/`UiIntents` 已改用它，删除此前的
`Func<string, double>`/`Action<string, double>` 注入回调。

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
