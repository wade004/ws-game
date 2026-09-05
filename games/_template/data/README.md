# 模板数据目录（`games/_template/data/game/`）

本目录是"最小可玩闭环"（主菜单 → 新游戏 → 进到一张地图）所需的最少数据行，按
[`../../../data/README.md`](../../../data/README.md) 的目录/信封/主键约定组织。复制模板为新游戏后：

1. 把 `data/game/` 整个目录改名为你游戏的目录名（例如 `data/mygame`），并同步改
   `Runtime/GameBootstrap.cs` 的 `_gameDatasetRoot` 默认值、`validate.ps1` 的 `-DataRoot` 默认值。
2. 把本目录下每张表里的 `template_*` id 替换成你自己的内容 id（保留信封结构与字段形状不变）。
3. 按下表继续把"游戏必填"的其余表补齐（本目录只含跑通最小闭环所必需的最少表；`architecture/
   13_新游戏接入指南.md` 第 5 节"填数据"、`architecture/04_数据与内容管线.md` 总索引里列出的
   其余表——技能、物品、生物、光环等——按你的实际内容量逐步补充，不在本最小闭环范围内）。

## 与框架级数据表（`data/_framework/`）的关系

框架分发包自带 `data/_framework/`（`found.event_catalog`/`found.input_action` 等，见
[`../../../data/README.md`](../../../data/README.md)"两类目录"一节），与本目录按
`DataRegistry.LoadAll(IReadOnlyList<IDataSource>)` 合并加载（见 `Runtime/GameBootstrap.cs`）；
本目录不需要、也不应该重复登记 `data/_framework/` 已有的表。

## 表清单（框架级已提供 / 游戏必填）

| 表 | 提供方 | 说明 |
|---|---|---|
| `found.event_catalog` | 框架级（`data/_framework`） | 事件词汇登记表，游戏不需要提供 |
| `found.input_action` | 框架级（`data/_framework`） | 输入动作声明，Shell/UI 导航依赖，游戏不需要提供；如需新增自定义动作，在自己的数据目录里对该表补充新行即可（多根合并，见框架 `data/README.md`） |
| `l10n.locale` | 游戏必填 | 至少一条默认语言（`l10n/l10n.locale.json`），`DataRegistryOptions.DefaultLocale` 默认取 `l10n.locale.zh_cn` |
| `l10n.text` | 游戏必填 | 本目录其余表里出现的每个 `text_key` 都要有对应行（`l10n/l10n.text.json`），否则 `text_key_exists` 校验报错 |
| `diff.tier` | 游戏必填 | 至少一档难度（`diff/diff.tier.json`），`Presentation.Shell.ShellHost.NewGame` 需要一个存在的难度 id 才能开局 |
| `shell_menu_definition` | 游戏必填 | 主菜单入口（`shell/shell_menu_definition.json`），本模板的 `TemplateShellUi` 只处理 `action: "new_game"` 的入口；数据驱动，不改代码即可增删入口 |
| `stat.definition` | 游戏必填 | 至少玩家职业的主属性一条（`stat/stat.definition.json`） |
| `arch.class` | 游戏必填 | 至少玩家职业一条（`arch/arch.class.json`），`primary_stat` 必须指向 `stat.definition` 里存在的行；`power_types` 不能为空数组（`PowerHost.RegisterUnit` 要求至少一种资源类型，默认至少含生命值） |
| `arch.power_type` | 游戏必填 | 至少 `arch.power.health` 一条（`arch/arch.power_type.json`）——`Core.Rules.Common.WellKnownPowers.Health` 硬编码这个固定 id（不是可配置默认值），`PresentationAssemblyOptions.HudPowerTypes` 也默认只展示它；skill/combat 对生命值的一切读写都经这个资源类型，见该常量类型注释 |
| `prog.level_curve` | 游戏必填 | 至少一条（`prog/prog.level_curve.json`），`arch.class.level_curve_ref` 指向它——`RulesAssembly.RegisterUnit` 只在职业记录带 `level_curve_ref` 时才会顺带 `ProgressionHost.RegisterUnit`；玩家一旦不经这条路径注册，`HudViewModel`（经 `player.level` 路径查询）构造期就会因 `ProgressionHost.GetLevel` 抛异常整体装配失败——即便游戏暂时不关心升级成长，也必须给玩家职业挂一条最小曲线（可以只有 1 级、`growth` 为空） |
| `combat.hit_table_config` | 游戏必填 | 至少 `combat.hit_table.default` 一条（`combat/combat.hit_table_config.json`），`CombatOptions.HitTableConfigId` 默认指向它——只要装配了 `CombatHost`（`GameOptions.BuildCombatOptions()` 默认总是装配），构造期就无条件要求该表存在 |
| `combat.resist_curve` | 游戏必填 | 至少一条（`combat/combat.resist_curve.json`），同上——`CombatDataLoader` 构造期无条件要求该表存在，即便暂时没有任何需要走抗性结算的技能/物品 |
| `fac.faction` | 游戏必填 | 至少玩家阵营一条（`fac/fac.faction.json`） |
| `world.map` | 游戏必填 | 至少起始地图一条（`world/world.map.json`），`scene_ref`/`nav_ref` 指向的场景/导航资源需要引擎适配层能读到（Unity 侧见 `Editor/GameSceneBuilder.cs`/`build.ps1` 判断记录：本仓库工作台自测用途，用同名占位资源；正式游戏接入自己的资产管线） |
| `item.budget_curve` | 游戏必填 | 至少 `item.budget.default` 一条（`item/item.budget_curve.json`），`ItemOptions.BudgetCurveId` 默认指向它——即便游戏暂时还没有任何 `item.template` 行，框架 `ItemBudgetValidationRule` 也会无条件检查该曲线是否存在 |
| `found.time_model` | 游戏必填 | 探索/战斗各一条（`found/found.time_model.json`），`Core.Gameplay.Assembly.TimeModelSwitch` 构造期无条件要求至少存在 `scope: exploration` 一条；本模板两条都给 `mode: continuous`（探索/战斗都走连续模式，架构文档 `architecture/13_新游戏接入指南.md` 第 4 节"口味项"里的默认选择），游戏若要接入回合制战斗，把 `found.time_model.combat` 一行改成 `mode: discrete` 并按 04 第 3.1 节补齐 `seconds_per_turn`/`initiative_policy`/`movement_budget_rule` 等字段 |

## 判断记录：为什么没有 `creature.template`/`display.map`

本模板的 `GameOptions.PlayerTemplateId` 只是一个不透明的 id，赋给玩家实体的 `TemplateId` 字段供
表现层（`display.map` 按 `logical_id` 匹配外形）使用；`DisplayMapCoverageRule`（"外形映射存在"
校验项）只检查调用方注入的内容表（如 `creature.template`）里出现的 id 是否在 `display.map` 里有
对应行，不会因为运行期一个未登记进任何数据表的 id 缺外形映射而报错。因此最小闭环阶段可以不填
`creature.template`/`display.map`——玩家能正确进图、移动，只是暂时没有可见外观，这正是
`architecture/13_新游戏接入指南.md` 第 6 节"配表现"要做的下一步，不属于本模板"进到一张地图"这一
最小验收范围。

## 校验

```powershell
# 单独校验本目录（骨架检查，不含跨表/跨模块字段级校验）：
python <框架仓库>\toolchain\validate_data.py --data-root data\game --skip-dotnet

# 本目录 + 框架级数据表合并校验（完整字段级/引用完整性校验，见 validate.ps1）：
.\validate.ps1
```
