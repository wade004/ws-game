# L5 表现层 · UI 框架（presentation/ui）

对应 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `ui` 行、
[09_表现层.md](../../architecture/09_表现层.md) 第 7 节。职责：界面数据绑定（查询 + 订阅刷新）、
用户操作到意图请求的转译、十个视图模型。

## 铁律落实

- **P1 只读逻辑状态**：全部数据经 `IUiDataSource.Query`（路径小语法，见下）或直接持有某个只读
  宿主接口（如 `IQuestHost.GetLog`）读取，视图模型不持有任何可写字段回写给逻辑层。
- **P2 只订阅事件**：视图模型构造期经 `IUiDataSource.Subscribe`/`IEventBus.Subscribe` 订阅相关
  事件触发 `Refresh()`，不轮询内部结构。
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

## 十个视图模型

`HudViewModel`、`ActionBarViewModel`、`InventoryViewModel`、`QuestLogViewModel`、
`DialogViewModel`、`SkillBookViewModel`、`CharacterStatsViewModel`、`SettingsViewModel`、
`SaveSlotsViewModel`、`PauseMenuViewModel`（`core/ViewModels/`），与 `schema/UiLayoutSchema.cs`
的 `UiPanel` 十个枚举值一一对应。均实现 `IDisposable`，构造期完成一次 `Refresh()` 并订阅相关
事件触发后续自动刷新。

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

（已解决，技术债 17，2026-09-06 收口）回合顺序条/行动点显示/"结束回合"按钮三个离散模式界面单元
（09 第 7.1 节）此前只有引擎侧 `TurnStatusPanel` 一份实现（不经 `UiPanelHost` 登记，直接读
`GameplayAssembly`、经 `UiIntents` 转发意图），本模块十个视图模型均不认识 `TurnScheduler`。现已
迁入 `HudViewModel`（可选注入 `Core.Foundation.SimLoop.TurnScheduler`/`IAppStateHost`/等待输入
子态，见该类型判断记录），引擎侧 `HudPanel` 只做渲染与键盘轮询，`TurnStatusPanel` 已删除；
01 L5 `ui` 行与 09 第 7.1 节的例外注记已同步撤销；台账见落地计划"已知未解决缺口"第 17 条。
