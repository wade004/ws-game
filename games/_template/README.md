# 游戏层组装模板

复制本目录即可起步：`Runtime/GameBootstrap.cs` 组合 `Adapter.Unity` 的常驻宿主装配全部逻辑层，
`GameOptions` 收敛口味配置，`data/game/` 是一份能跑通"主菜单 → 新游戏 → 进到一张地图"的最小数据
集。改三处名字、把 `data/<game>/` 填上你自己的内容，即可在自己的 Unity 工程里从主菜单进到一张
地图。

## 目录结构

```
games/_template/
  package.json                        UPM 包清单（改名见下）
  README.md                           本文件
  Runtime/
    Game.Template.asmdef              运行期程序集（改名见下）
    GameOptions.cs                    口味配置容器（13 §4 逐行对应）
    GameBootstrap.cs                  游戏层组合根（数据根、GameplayAssembly/PresentationAssembly 装配）
    SampleNewGameStarter.cs           可替换的 NewGameStarter 示例
    TemplateShellUi.cs                最小主菜单（数据驱动，读 shell_menu_definition 表）
    TemplateAutoStart.cs              跳过主菜单直接开局，供"首张地图"场景快速验证
  Editor/
    Game.Template.Editor.asmdef       编辑器程序集（改名见下）
    GameSceneBuilder.cs               一键生成 Shell + 首张地图两个场景并加入 Build Settings
  Tests/Runtime/
    Game.Template.Tests.asmdef        PlayMode 测试程序集（改名见下）
    GameTemplateSmokeTests.cs         最小验收：默认 GameOptions 启动 → 数据零阻断错误 → 主菜单可见
  data/game/                          最小可玩闭环数据集（改名见下），见 data/README.md
  validate.ps1                        校验 data/game/ + 框架级数据表（data/_framework）合并 0 错误
```

## 七步立项对照表

对照 [`architecture/13_新游戏接入指南.md`](../../architecture/13_新游戏接入指南.md) 第 1 节"立项
步骤总览"逐步落到本模板的具体文件/字段，以及第 8 节检查表对应项：

| 13 §1 步骤 | 本模板对应的文件/字段 | 13 §8 检查表对应项 |
|---|---|---|
| 1. 选引擎并实现适配层（L-1） | 已由框架 `adapters/unity` 提供（Unity 实现）+ `adapters/stub`（桩实现）；本模板不重复 | "已完成引擎评估打分并选定引擎"、"L-1 全部接口已有对应实现" |
| 2. 组装模块与策略配置 | `Runtime/GameBootstrap.cs`（`GameplayAssembly`/`PresentationAssembly` 构造参数）；启用哪些模块 = 传了哪些 `xxxOptions` | "已确定启用哪些 L0~L4 模块" |
| 3. 确定口味配置项清单 | `Runtime/GameOptions.cs`（逐字段对应 13 §4 表格每一行，见该文件注释） | "第 4 节口味配置项清单已逐行填写为本游戏基线值" |
| 4. 定资产规格并接通导入工具 | 本模板未覆盖（属"配表现"第 2 步，见下），参照 [`14_资产规格书模板.md`](../../architecture/14_资产规格书模板.md) | "资产规格书已填"、"导入工具已接通" |
| 5. 填数据 | `data/game/`（见 `data/README.md`"表清单"一节：框架级已提供 / 游戏必填） | "最小可玩闭环所需数据已填齐并通过校验" |
| 6. 配表现 | 本模板故意不覆盖（`GameOptions.PlayerTemplateId` 默认无 `display.map` 映射，玩家进图后暂时不可见，见 `data/README.md`"判断记录"）；`Editor/GameSceneBuilder.cs` 只生成 Shell/首张地图两个场景骨架 | "DisplayInfo 映射已配齐"、"UI 框架实例已接入主题" |
| 7. 跑验收 | `validate.ps1`（数据校验）+ `Tests/Runtime/GameTemplateSmokeTests.cs`（模块/集成测试的最小子集：装配+主菜单） | 第 7 节七项验收关卡（本模板只覆盖第 1 项"数据校验通过"与第 6 项的起步部分） |

## 复制为新游戏：改哪几处

1. **`package.json`**：`name` 改成新游戏专属包名（如 `com.<studio>.game-<name>`），按需改
   `displayName`/`description`。
2. **三个 asmdef**（`Runtime/Game.Template.asmdef`、`Editor/Game.Template.Editor.asmdef`、
   `Tests/Runtime/Game.Template.Tests.asmdef`）：
   - 文件名与 JSON 里的 `"name"` 都从 `Game.Template`/`Game.Template.Editor`/`Game.Template.Tests`
     改成 `Game.<Game>`/`Game.<Game>.Editor`/`Game.<Game>.Tests`（`<Game>` 替换为新游戏代号）。
   - `Editor`/`Tests` 两个 asmdef 的 `references` 数组里对 `"Game.Template"` 的引用要同步改成新
     名字（`"Game.<Game>"`），否则编辑器/测试程序集会引用不到运行期程序集。
   - `rootNamespace` 建议同步改成 `Game.<Game>`（并同步改全部 `.cs` 文件的 `namespace Game.Template`
     为 `namespace Game.<Game>`）；`references` 里对 `"Adapter.Unity"` 的引用保留不变。
3. **`GameOptions` 默认 id**（`Runtime/GameOptions.cs` "起始状态"一组字段）：`PlayerUnitId`/
   `PlayerFactionId`/`PlayerClassId`/`PlayerTemplateId`/`StartMapId`/`DefaultDifficultyId`/`GameId`
   从 `xxx.template_*`/`game.template` 改成你自己的 id（同步改 `data/game/` 里对应的数据行 id）。
4. **数据目录改名**：`data/game/` 改成 `data/<你的游戏目录名>`（如 `data/mygame`），同步改
   `Runtime/GameBootstrap.cs` 的 `_gameDatasetRoot` 默认值（Inspector 字段，也可以留着默认值不改、
   直接在场景里的 `GameBootstrap` 组件上改 Inspector 值）与 `validate.ps1` 的 `-DataRoot` 默认值。
5. 在新游戏的 Unity 工程 `Packages/manifest.json` 中用 `file:` 相对路径引用两个包：本框架的引擎
   适配层包 `com.gamefoundation.adapter.unity`（分发包 `dist/<version>/adapters/unity/Packages/
   com.gamefoundation.adapter.unity/`）与复制改名后的游戏层包，例如：
   ```json
   {
     "dependencies": {
       "com.gamefoundation.adapter.unity": "file:../../dist/0.1.0/adapters/unity/Packages/com.gamefoundation.adapter.unity",
       "com.<studio>.game-<name>": "file:../../dist/0.1.0/games/_template"
     },
     "testables": [
       "com.<studio>.game-<name>"
     ]
   }
   ```
   （具体相对路径层级、分发包版本号以实际情况为准；见落地计划 3.5 节"新游戏如何消费本框架"。）
6. 依赖方向单向：新游戏的程序集引用 `Adapter.Unity`，`Adapter.Unity` 不反向引用任何游戏层程序集；
   `Adapter.Unity` 也不出现任何具体游戏代号——本模板同样遵守这条规则，`Runtime`/`Editor` 两个
   asmdef 都只引用 `Adapter.Unity`，不引用工作台任何专属脚本（见 `Editor/GameSceneBuilder.cs`
   顶部判断记录"为什么复制而不是引用工作台的 GreyBoxSceneBuilder/ShellSceneBuilder"）。

## 生成场景并跑一遍

```
Unity.exe -batchmode -nographics -quit -projectPath <你的 Unity 工程>
  -executeMethod Game.<Game>.EditorTools.GameSceneBuilder.BuildAll
```

会在 `Assets/Framework/Scenes/` 下生成两个场景并加入 Build Settings：`GameTemplateShell.unity`
（挂 `TemplateShellUi`，主菜单，读 `shell_menu_definition` 表驱动入口）与 `GameTemplateMap.unity`
（挂 `GameBootstrap` + `TemplateAutoStart`，跳过主菜单直接开局，供快速验证地图本身）。生成后的
`.unity` 场景文件与工作台 `GreyBox.unity`/`Shell.unity` 一样，是需要提交的源文件，不是构建产物。

## 校验数据

```powershell
.\validate.ps1
```

默认合并本目录 `data/game/` 与框架分发包的 `data/_framework/`（脚本会按几种常见相对位置自动猜测
框架仓库/分发包的位置，找不到时提示显式传 `-FrameworkRoot`/`-ValidateDataScript`），见
`data/README.md`"校验"一节。

## 已知限制（本模板"最小闭环"故意不覆盖的部分）

- 不含任何战斗/技能/物品/任务内容——`GameOptions` 声明了对应口味配置项字段（见该文件注释），但
  `data/game/` 只填了让 `LoadAll` 与 `ShellHost.NewGame` 跑通所需的最少表；`item.budget_curve`
  只登记了 `ItemBudgetValidationRule` 无条件要求存在的默认曲线一条，不代表游戏里已经有物品内容。
- 玩家进图后没有可见外观（未配 `creature.template`/`display.map`），见 `data/README.md`"判断
  记录"一节；这是 13 §6"配表现"的工作，不在本模板范围。
- `TemplateShellUi` 只做数据驱动的"新游戏"一种入口，没有存档槽选择/难度选择/暂停菜单/设置面板
  UI（`ShellHost`/`SaveSystem`/`IDifficultyHost` 等底层能力都已装配好，只是本模板没有把它们全部
  接出 UI）；需要更完整菜单可参照 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
  Runtime/Shell/ShellRoot.cs` 的实现扩展。
