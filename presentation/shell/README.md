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
- `LoadGame(slotId)`：`ISaveSystem.Load` → 经注入的 `LoadedMapIdResolver` 委托取得读档后的
  地图 id → `ISceneRouter.LoadScene`。
- `ReturnToMainMenu()`：InWorld/Pause → MainMenu（`IAppStateHost.RequestTransition`）。
- `Quit()`：`IAppStateHost.RequestExit`（仅 MainMenu 下允许）。
- 设置：`SaveSettings` 把调用方传入的字段与 `IInputMapHost.ExportBindings()` 一并写入
  `ISettingsStore`；`LoadSettings` 读回后把其中的 `input_bindings` 经 `IInputMapHost.ImportBindings`
  重新应用。

## 已知契约缺口

- 10_存档与持久化.md 第 3 节步骤 7 提到读档后应"触发场景路由加载对应地图"，但
  `ISaveSystem.Load` 的返回值 `LoadResult` 只携带 meta 段，不携带 world 段的
  `current_map_id`；本模块把这一步表达为构造期注入的 `LoadedMapIdResolver` 委托，由持有该
  world 段 `IPersistable` 引用的组装层提供。建议后续给 world 段的宿主补一个只读属性，届时可以
  去掉本委托直接查询。
- `NewGameStarter` 委托同理承担"创建初始玩家状态"这一游戏层专属步骤，返回起始地图 id 后由
  `ShellHost` 统一调用 `LoadScene`，见该委托类型注释判断记录。

## 验收

- `grep -rniE "\.SetPosition\(|\.SetAlive\(|\.AddModifier\(|\.ModifyPower\(|\.SetBase\(|WorldState\.Set\(" presentation/shell`
  应无匹配。
- `dotnet test` 覆盖：状态流转与 `IAppStateHost` 转移调用、新游戏/读档流程的委托与场景路由调用、
  设置保存往返、加载进度事件、退出。
