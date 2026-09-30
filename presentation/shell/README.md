# L5 表现层 · 游戏外壳（presentation/shell）

对应 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `shell` 行、
[09_表现层.md](../../architecture/09_表现层.md) 第 9 节。职责：主菜单、存档槽、新游戏与难度、
加载画面、设置，五个子模块的编排入口 `ShellHost`。

## 页面模型

`ShellPage`：`MainMenu → SaveSlots/NewGameSetup/Settings`（应用级主状态仍是 `AppState.MainMenu`
期间的 Shell 内部子页面）、`Loading`/`InWorld`/`Paused`（直接反映应用级主状态，见
`ShellHost.Page` 判断记录）。

## 编排流程

- `Start()`：`Boot → MainMenu`（`IAppStateHost.RequestTransition`）。
- `NewGame(slotId, difficultyId, archetypeId?)`：`IDifficultyHost.Apply` → 经注入的
  `NewGameStarter` 委托由游戏层创建初始玩家状态、返回起始地图 id → `ISaveSystem.Save` 写入
  新槽的初始存档 → `ISceneRouter.LoadScene(起始地图)`（该调用内部把应用状态机转入 `Loading`）。
- `LoadGame(slotId)`：`ISaveSystem.Load` → 优先取 `LoadResult.CurrentMapId`（G1 新增字段，缺口
  11 已解决，见下），为 null 时才回退可选注入的 `LoadedMapIdResolver` 委托 → `ISceneRouter.LoadScene`。
- `ReturnToMainMenu()`：InWorld/Pause → MainMenu（`IAppStateHost.RequestTransition`）。
- `Quit()`：`IAppStateHost.RequestExit`（仅 MainMenu 下允许）。
- 设置：`SaveSettings` 把调用方传入的字段与 `IInputMapHost.ExportBindings()` 一并写入
  `ISettingsStore`；`LoadSettings` 读回后把其中的 `input_bindings` 经 `IInputMapHost.ImportBindings`
  重新应用。

## 已知契约缺口

（已解决，缺口 11）10_存档与持久化.md 第 3 节步骤 7 提到读档后应"触发场景路由加载对应地图"，
此前 `ISaveSystem.Load` 的返回值 `LoadResult` 只携带 meta 段、不携带 world 段的 `current_map_id`，
`LoadGame` 因此把这一步表达为构造期注入的 `LoadedMapIdResolver` 委托。G1 给 `LoadResult` 补了
`CurrentMapId`/`CurrentPosition` 两个字段（读自 `world.current_map_id`/`world.current_position`
段），`LoadGame` 现优先用 `CurrentMapId`；`LoadedMapIdResolver` 降级为该字段为 null 时才生效的可选
覆盖（默认 `null`，见 `ShellHost` 构造函数）。`CurrentPosition` 不在本模块内消费——`ShellHost`
不持有玩家实体引用（铁律 P1/P3），原样保留在 `LoadGame` 返回值里，由拿到 `LoadResult` 的调用方
（游戏层/表现层装配代码）在场景加载完成后自行落位玩家。
- `NewGameStarter` 委托同理承担"创建初始玩家状态"这一游戏层专属步骤，返回起始地图 id 后由
  `ShellHost` 统一调用 `LoadScene`，见该委托类型注释判断记录。

## ADR-0019 F1c 子结构登记

`shell_menu_definition.entries[]`（`ShellSchemas.EntriesItemSchema`）：`{id:Id, text_key:TextKey,
action:Enum(6值), target_panel:Reference(ui_layout_definition)?}`，按 `ShellMenuEntry.ParseEntry`
权威解析登记。`target_panel` 登记为 Reference——`Presentation.Shell`/`Presentation.Ui` 同属
`Presentation.Common` 程序集（同层）。`text_key` 补齐文本键存在性校验（原运行时只做 `Id.TryParse`
格式检查）。

## 验收

- `grep -rniE "\.SetPosition\(|\.SetAlive\(|\.AddModifier\(|\.ModifyPower\(|\.SetBase\(|WorldState\.Set\(" presentation/shell`
  应无匹配。
- `dotnet test` 覆盖：状态流转与 `IAppStateHost` 转移调用、新游戏/读档流程的委托与场景路由调用、
  设置保存往返、加载进度事件、退出。

## 判断记录（`LoadGame` 场景路由失败显式暴露，2026-10-01，[ADR-0121](../../architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 10 条，D10）

`LoadGame` 读档成功后按存档地图 id 切场景，场景路由抛 `ArgumentException`（未知场景 id）或
`InvalidOperationException`（当前应用状态不允许转入 Loading）时，此前两个空 catch 静默吞掉且仍返回
`Loaded`（违反运行时路径不静默降级）。现在：读档**仍算成功**（存档已加载，`Status` 不变），但返回值
`LoadResult` 新增只增字段 `SceneRouteFailed`（bool，默认 false）与 `SceneRouteError`（string?，含目标地图 id
与异常消息），并向 `ShellHost.Diagnostics` 记一条警告；两条 catch 分支各一条用例，另有"正常路由时两字段为
默认值"的不变量用例（`tests/ShellHostTests.cs`）。`LoadResult` 不可变，新增 `WithSceneRouteFailure(string)`
返回置位副本。ABI 只新增：`ShellHost` 旧构造函数签名原样保留并转调新增的十一参重载（多一个
`IPresentationDiagnostics`），旧构造函数的诊断默认自建一份 recorder。`NewGame` 的场景路由失败仍按原约定返回
`false`，不在本条范围。
