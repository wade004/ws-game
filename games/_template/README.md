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
    TemplateSmokeRunner.cs            无人值守冒烟入口（"-gf-smoke-template"，见下"无人值守冒烟"一节）
  Editor/
    Game.Template.Editor.asmdef       编辑器程序集（改名见下）
    GameSceneBuilder.cs               一键生成 Shell + 首张地图两个场景并加入 Build Settings
  Tests/Runtime/
    Game.Template.Tests.asmdef        PlayMode 测试程序集（改名见下）
    GameTemplateSmokeTests.cs         最小验收：默认 GameOptions 启动 → 数据零阻断错误 → 主菜单可见；
                                      编辑器内验证 "-gf-smoke-template" 冒烟序列可跑通（不退出进程）
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
| 7. 跑验收 | `validate.ps1`（数据校验）+ `Tests/Runtime/GameTemplateSmokeTests.cs`（模块/集成测试的最小子集：装配+主菜单）+ `Runtime/TemplateSmokeRunner.cs`（`-gf-smoke-template` 命令行无人值守冒烟：主菜单→新游戏→进图→移动→存档→读档→退出） | 第 7 节七项验收关卡（本模板覆盖第 1 项"数据校验通过"、第 6 项"端到端可玩性验收"的起步部分——本模板无战斗/技能/任务内容，`-gf-smoke-template` 只验证进图/移动/存档读档这一段不阻断） |

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
5. 先按目标版本号取得一份本框架的发布产物（而不是直接引用框架的开发目录）：`toolchain/
   get_framework.ps1 -Version <version> -Target packages` 从框架的 Release 拉取
   `ws-game-<version>.zip` 与配套的锁文件，校验六个核心 DLL 的哈希与锁文件一致后解压到
   `packages/ws-game-<version>/`，并在游戏仓库根写入/校验 `ws-game.lock`（记录当前引用的版本号
   与六个核心 DLL 的 sha256，供后续升级或核对内容完整性；离线场景可传 `-FromLocalDist <本机
   zip 路径>`，或在框架仓库对应标签上本机跑 `build.ps1 -Dist <version>` 后直接使用产出的
   `dist/<version>/`，两条路径产出的目录结构一致，都可以直接被下一步的 `file:` 引用）。
6. 在新游戏的 Unity 工程 `Packages/manifest.json` 中用 `file:` 相对路径引用两个包：本框架的引擎
   适配层包 `com.gamefoundation.adapter.unity`（`packages/ws-game-<version>/adapters/unity/
   Packages/com.gamefoundation.adapter.unity/`）与复制改名后的游戏层包，例如：
   ```json
   {
     "dependencies": {
       "com.gamefoundation.adapter.unity": "file:../../packages/ws-game-1.0.0/adapters/unity/Packages/com.gamefoundation.adapter.unity",
       "com.<studio>.game-<name>": "file:../../packages/ws-game-1.0.0/games/_template"
     },
     "testables": [
       "com.<studio>.game-<name>"
     ]
   }
   ```
   （具体相对路径层级、版本号以实际情况为准；见落地计划 3.5 节"新游戏如何消费本框架"。）
7. 依赖方向单向：新游戏的程序集引用 `Adapter.Unity`，`Adapter.Unity` 不反向引用任何游戏层程序集；
   `Adapter.Unity` 也不出现任何具体游戏代号——本模板同样遵守这条规则，`Runtime`/`Editor` 两个
   asmdef 都只引用 `Adapter.Unity`，不引用工作台任何专属脚本（见 `Editor/GameSceneBuilder.cs`
   顶部判断记录"为什么复制而不是引用工作台的 GreyBoxSceneBuilder/ShellSceneBuilder"）。
8. **补齐 TextMeshPro 运行期资源与占位字体**（消费方演练任务实跑发现的必需步骤，缺了会在主菜单
   渲染文字这一步抛异常）：把发布产物内 `assets/textmesh_pro_essentials/`（即第 5 步落地的
   `packages/ws-game-<version>/assets/textmesh_pro_essentials/`，或本机直接用
   `dist/<version>/assets/textmesh_pro_essentials/`）整份拷进新工程的 `Assets/TextMesh Pro/`
   （TMP_Settings 单例、SDF 着色器等，Unity 批处理环境下无法可靠地靠"导入 TMP Essential
   Resources"这条异步菜单流程自动补齐，只能整份提交为普通 Assets，见 `adapters/unity/Packages/
   com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityUISurface.cs` 顶部"判断记录（TMP
   运行期依赖）"）；再把 `assets/_placeholder/fonts/*.otf`/`*.ttf`（同一份发布产物或
   `dist/<version>/` 下）拷进 `Assets/Framework/Resources/Fonts/`（`UnityUISurface`/
   `UnityResourceLoader` 按 `font.<name>` -> `Resources/Fonts/<name>` 规则解析，见包 README"资源
   id → 路径规则"）。两步都是一次性的文件拷贝，不需要打开 Unity 编辑器操作。
   **已自动化的部分（W3b 收边核实）**：`build.ps1 -Dist` 已把 `adapters/unity/Assets/TextMesh Pro/`
   整份打进分发包 `dist/<version>/assets/textmesh_pro_essentials/`（见 `build.ps1` "assets/
   textmesh_pro_essentials" 步骤）；`toolchain/consumer_smoke.ps1`（消费方演练脚本，模拟"新游戏
   工程从分发包搭建"全流程）"同步内容数据集 + TextMeshPro 运行期资源到消费方工程"一步已经把它与
   占位字体自动镜像拷贝进消费方工程——**用该脚本搭建/验证消费方工程时，本步骤不需要再手工执行**，
   上面两段描述的是"手工新建 Unity 工程、不经 consumer_smoke.ps1"这条路径仍然需要的一次性拷贝
   操作（没有对应的自动化入口，Unity 批处理环境不提供"新建工程时自动拉取分发包"这一步）。

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

## 无人值守冒烟

构建独立版后，用命令行标志 `-gf-smoke-template` 驱动 `Runtime/TemplateSmokeRunner.cs` 跑一遍
"数据零阻断 → 主菜单 → 新游戏（`SampleNewGameStarter`）→ 进入地图 → 向右移动 1 秒 → 存档到
`slot.smoke` → 读档 → 退出"，不需要人工点击：

```
<独立版可执行文件> -batchmode -gf-smoke-template -logFile <日志路径>
```

日志格式与退出码约定沿用工作台 `Adapter.Unity.Shell.SmokeRunner`（见该类型头注释）：每步成功打印
`[GF-SMOKE] step=<name> ok`；全部步骤跑完打印 `[GF-SMOKE] RESULT=OK` 并以退出码 `0` 结束；任一步
失败打印 `[GF-SMOKE] RESULT=FAIL reason=...` 并以退出码 `2` 结束；总耗时超过 60 秒以退出码 `3`
结束（看门狗兜底）。判定脚本只需要检查退出码与日志里是否出现 `RESULT=OK`，见
`toolchain/consumer_smoke.ps1` 第 10 步的用法。

判断记录（为什么命令行标志不是工作台同款的 `-gf-smoke`）：见 `Runtime/TemplateSmokeRunner.cs`
类型头注释——`Game.Template.asmdef` 引用了 `Adapter.Unity`，任何链接了本模板的独立版都会把
`Adapter.Unity.Shell.SmokeRunner` 一并打进去，沿用同一个标志字符串会导致两个互不相干的
`SmokeRunner` 同时响应同一条命令行参数、互相撞车（工作台那个找不到 `ShellRoot` 会提前把整个进程
终止掉）。复制本模板为新游戏时这条限制依然成立，不建议把标志改回 `-gf-smoke`。

## 已知限制（本模板"最小闭环"故意不覆盖的部分）

- 不含任何战斗/技能/物品/任务内容——`GameOptions` 声明了对应口味配置项字段（见该文件注释），但
  `data/game/` 只填了让 `LoadAll` 与 `ShellHost.NewGame` 跑通所需的最少表；本模板没有任何
  `item.template` 行（没有物品内容），`item.budget_curve` 登记的默认曲线一条只是留作示例——
  `ItemBudgetValidationRule` 只在数据集确实登记了 `item.template` 时才要求该曲线存在，见
  `data/README.md`"与校验器的关系"一节判断记录。
- 玩家进图后没有可见外观（未配 `creature.template`/`display.map`），见 `data/README.md`"判断
  记录"一节；这是 13 §6"配表现"的工作，不在本模板范围。
- `TemplateShellUi` 只做数据驱动的"新游戏"一种入口，没有存档槽选择/难度选择/暂停菜单/设置面板
  UI（`ShellHost`/`SaveSystem`/`IDifficultyHost` 等底层能力都已装配好，只是本模板没有把它们全部
  接出 UI）；需要更完整菜单可参照 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
  Runtime/Shell/ShellRoot.cs` 的实现扩展。
