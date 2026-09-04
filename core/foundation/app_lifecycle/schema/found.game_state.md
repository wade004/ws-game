# `found.game_state` 字段表

对应 `01_分层与依赖.md` L0 模块表 `app_lifecycle` 行的主要数据表、`04_数据与内容管线.md`
第 1.1 节表清单里的 `found.game_state`（"游戏状态机的状态与合法迁移定义"）。

本表描述的是**转移定义**（一条主状态转移，或一条 InWorld 子状态转移），不是"状态"本身
的登记表——状态集合本身固定为 `contracts/AppState.cs`（主状态）与
`contracts/InWorldSubState.cs`（内置子状态）两个枚举加游戏层可扩展的自定义子状态名
（见 `AppStateMachineConfig.AddCustomSubState`），不随数据行变化；本表的每一行是"允许
从 X 转移到 Y"这条边本身，供游戏层在 03 第 2 节默认表之外扩展自定义转移。

本模块（T1-7a）只提供从内存转移定义构造配置的入口
（`AppStateMachineConfig.Default()` 等价于本表默认数据、`AllowTransition`/
`AllowSubTransition` 是运行期追加入口），不做 JSON 读取；从数据文件读取并转换成配置是
数据注册表（`core/foundation/data_registry`，T1-4）的职责，与 `event_bus` 处理
`found.event_catalog`、`hook_registry` 处理 `found.hook` 同一惯例。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `found.state.<from>_to_<to>`（例如 `found.state.boot_to_main_menu`），满足 04 第 2.1 节 id 规范。 |
| `from` | string | 是 | 转移起点状态名；`kind: main` 时取 `AppState` 枚举名（`Boot`/`MainMenu`/`Loading`/`InWorld`/`Pause`），`kind: sub` 时取 `SubStateId` 名（内置 `InWorldSubState` 枚举名或游戏层自定义子状态名）。 |
| `to` | string | 是 | 转移终点状态名，取值规则同 `from`。 |
| `kind` | `main` \| `sub` | 是 | 区分本行是主状态转移还是 InWorld 子状态转移。 |
| `description` | string \| null | 否 | 说明该转移由谁驱动、进入条件（可照抄 03 第 2 节状态机表对应列）。 |

## 默认表等价于 03 第 2 节

`AppStateMachineConfig.Default()` 等价的默认数据行（`kind: main`，共 8 条，见
03_运行时骨架.md 第 2 节状态机表）：

| id | from | to |
|---|---|---|
| `found.state.boot_to_main_menu` | Boot | MainMenu |
| `found.state.main_menu_to_loading` | MainMenu | Loading |
| `found.state.loading_to_in_world` | Loading | InWorld |
| `found.state.in_world_to_pause` | InWorld | Pause |
| `found.state.in_world_to_main_menu` | InWorld | MainMenu |
| `found.state.in_world_to_loading` | InWorld | Loading |
| `found.state.pause_to_in_world` | Pause | InWorld |
| `found.state.pause_to_main_menu` | Pause | MainMenu |

"MainMenu → 退出应用"不在本表内，退出应用不是一个 `AppState`，由
`IAppStateHost.RequestExit()` 单独表达（见 03 第 2 节 MainMenu 一行"允许转移到"列
"退出应用"、任务书拍板）。

`kind: sub` 默认行（共 8 条，见本模块 `contracts/AppStateMachineConfig.cs`
`Default()` 判断记录——`Combat → MenuOverlay` 一条是本模块对 03 原文"叠加规则由游戏层
配置，例如 Combat 中打开 MenuOverlay"的显式落地，03 第 2 节状态机表本身未把它列进
"允许转移到"列，属于本模块的判断记录，已在 `AppStateMachineConfig.cs` 与 README 中详述）：

| id | from | to |
|---|---|---|
| `found.state.explore_to_combat` | Explore | Combat |
| `found.state.explore_to_dialog` | Explore | Dialog |
| `found.state.explore_to_menu_overlay` | Explore | MenuOverlay |
| `found.state.explore_to_cutscene` | Explore | Cutscene |
| `found.state.combat_to_explore` | Combat | Explore |
| `found.state.dialog_to_explore` | Dialog | Explore |
| `found.state.cutscene_to_explore` | Cutscene | Explore |
| `found.state.combat_to_menu_overlay` | Combat | MenuOverlay |

`MenuOverlay` 本身没有"到 X"的转移数据行——它"回落到触发前的子状态"是栈结构本身的
弹出语义（`IAppStateHost.PopSubState`），不是一条可枚举的固定目标转移。

## `AppStateMachineConfig.FromDefinitions` 入口

`AppStateMachineConfig.FromDefinitions(IEnumerable<GameStateTransitionDefinition>)`
供数据注册表加载完 `found.game_state` 表后转换成配置对象：从空配置起步，逐行按 `kind`
分派到 `AllowTransition`（`kind: main`，`from`/`to` 按 `AppState` 枚举名解析）或
`AllowSubTransition`（`kind: sub`，`from`/`to` 按 `SubStateId` 名构造）。它不是"在默认表
基础上叠加"——若需要"03 默认表 + 自定义扩展"，调用方应把 `Default()` 的等价定义行与自定义
行一起传入，或直接对 `Default()` 返回值调用 `AllowTransition`/`AllowSubTransition` 追加。
`GameStateTransitionDefinition`（`contracts/GameStateTransitionDefinition.cs`）是对应的单行
定义类型，字段与上表一一对应；从 JSON 读取并转换成该类型列表是数据注册表
（`core/foundation/data_registry`，T1-4）的职责，本模块不实现 JSON 反序列化。

## 本模块不做什么

- 不读取 `found.game_state.json`（或具体游戏的对应数据文件）；只提供
  `AppStateMachineConfig.Default()`/`AllowTransition`/`AllowSubTransition` 的内存构造入口。
- 不校验 `id` 字段格式与 `from`/`to` 是否指向存在的状态名——这属于数据注册表引用完整性
  校验器（04 第 5 节）未来对接本表时的职责。
