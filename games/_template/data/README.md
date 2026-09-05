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
| `found.game_state` | 框架级（`data/_framework`） | 应用状态机的状态与合法迁移定义，游戏不需要提供；`Core.Foundation.AppLifecycle.AppStateMachineConfig.FromRegistry` 缺表时退化为 `Default()`（03 第 2 节默认表），行为等价 |
| `found.hook` | 框架级（`data/_framework`） | 脚本钩子挂载点登记表（`found.hook.scene_pre_unload`/`found.hook.scene_post_load`），游戏不需要提供；`Core.Foundation.HookRegistry.FoundHookSchema.LoadDefinitions` 缺表时退化为 `WellKnownHooks` 两个内置常量，行为等价 |
| `l10n.locale` | 游戏必填 | 至少一条默认语言（`l10n/l10n.locale.json`），`DataRegistryOptions.DefaultLocale` 默认取 `l10n.locale.zh_cn` |
| `l10n.text` | 游戏必填 | 本目录其余表里出现的每个 `text_key` 都要有对应行（`l10n/l10n.text.json`），否则 `text_key_exists` 校验报错 |
| `diff.tier` | 游戏必填 | 至少一档难度（`diff/diff.tier.json`），`Presentation.Shell.ShellHost.NewGame` 需要一个存在的难度 id 才能开局 |
| `shell_menu_definition` | 游戏必填 | 主菜单入口（`shell/shell_menu_definition.json`），本模板的 `TemplateShellUi` 只处理 `action: "new_game"` 的入口；数据驱动，不改代码即可增删入口 |
| `stat.definition` | 游戏必填 | 至少玩家职业的主属性一条（`stat/stat.definition.json`） |
| `arch.class` | 游戏必填 | 至少玩家职业一条（`arch/arch.class.json`），`primary_stat` 必须指向 `stat.definition` 里存在的行；`power_types` 不能为空数组（`PowerHost.RegisterUnit` 要求至少一种资源类型，默认至少含生命值） |
| `arch.power_type` | 框架级（`data/_framework`） | `arch.power.health` 一条（`max_source: {kind: fixed, value: 100}`）已随收边任务迁入框架分发包，游戏不需要提供；`Core.Rules.Common.WellKnownPowers.Health` 硬编码这个固定 id（不是可配置默认值），`PresentationAssemblyOptions.HudPowerTypes` 也默认只展示它；skill/combat 对生命值的一切读写都经这个资源类型，见该常量类型注释。游戏若需要生命值上限跟随某个属性成长，见下"覆盖 `arch.power.health`（可选）"一节 |
| `prog.level_curve` | 游戏必填 | 至少一条（`prog/prog.level_curve.json`），`arch.class.level_curve_ref` 指向它——`RulesAssembly.RegisterUnit` 只在职业记录带 `level_curve_ref` 时才会顺带 `ProgressionHost.RegisterUnit`；玩家一旦不经这条路径注册，`HudViewModel`（经 `player.level` 路径查询）构造期就会因 `ProgressionHost.GetLevel` 抛异常整体装配失败——即便游戏暂时不关心升级成长，也必须给玩家职业挂一条最小曲线（可以只有 1 级、`growth` 为空） |
| `combat.hit_table_config` | 游戏必填 | 至少 `combat.hit_table.default` 一条（`combat/combat.hit_table_config.json`），`CombatOptions.HitTableConfigId` 默认指向它——只要装配了 `CombatHost`（`GameOptions.BuildCombatOptions()` 默认总是装配），构造期就无条件要求该表存在 |
| `combat.resist_curve` | 游戏必填 | 至少一条（`combat/combat.resist_curve.json`），同上——`CombatDataLoader` 构造期无条件要求该表存在，即便暂时没有任何需要走抗性结算的技能/物品 |
| `fac.faction` | 游戏必填 | 至少玩家阵营一条（`fac/fac.faction.json`） |
| `world.map` | 游戏必填 | 至少起始地图一条（`world/world.map.json`），`scene_ref`/`nav_ref` 指向的场景/导航资源需要引擎适配层能读到（Unity 侧见 `Editor/GameSceneBuilder.cs`/`build.ps1` 判断记录：本仓库工作台自测用途，用同名占位资源；正式游戏接入自己的资产管线） |
| `item.budget_curve` | 游戏必填 | 至少 `item.budget.default` 一条（`item/item.budget_curve.json`），`ItemOptions.BudgetCurveId` 默认指向它——即便游戏暂时还没有任何 `item.template` 行，框架 `ItemBudgetValidationRule` 也会无条件检查该曲线是否存在 |
| `found.time_model` | 游戏必填 | 探索/战斗各一条（`found/found.time_model.json`），`Core.Gameplay.Assembly.TimeModelSwitch` 构造期无条件要求至少存在 `scope: exploration` 一条；本模板两条都给 `mode: continuous`（探索/战斗都走连续模式，架构文档 `architecture/13_新游戏接入指南.md` 第 4 节"口味项"里的默认选择），游戏若要接入回合制战斗，把 `found.time_model.combat` 一行改成 `mode: discrete` 并按 04 第 3.1 节补齐 `seconds_per_turn`/`initiative_policy`/`movement_budget_rule` 等字段 |

## 覆盖 `arch.power.health`（可选）

数据行覆盖语义任务新增：`DataRegistry` 多根合并时，游戏根的行可以用行级字段 `"override": true`
整行替换框架根的同 id 行（见 [`../../../data/README.md`](../../../data/README.md)"多根加载与合并
规则"、`core/foundation/data_registry/core/DataRegistry.cs` 类型级判断记录"覆盖语义"）——这条机制
取代了此前"只能新开一个资源类型、不能覆盖框架这一条"的限制。若你的游戏需要生命值上限跟随某个
属性成长，在 `data/game/arch/arch.power_type.json`（本模板默认没有这个文件，需自己新建）里加一条：

```json
{
  "table": "arch.power_type",
  "schema_version": 1,
  "rows": [
    {
      "id": "arch.power.health",
      "override": true,
      "name_key": "l10n.power.health.name",
      "max_source": { "kind": "stat", "stat": "stat.template_power" },
      "start_full": true
    }
  ]
}
```

注意事项：

- `override: true` 是行级字段，只在多根合并且发生同 id 跨根重复时才生效；本模板默认不提供这个
  文件（不覆盖），保留框架默认的固定上限 100——上面这段是"如果你需要"才复制使用的示例，不要
  不假思索地把它加进模板默认数据（那会让每个新游戏一上来就悄悄改掉生命值上限，与"最小闭环"的
  可预测性冲突，这也是没有直接落一个 `arch.power_type.json.example` 文件到本目录的原因：本仓库
  不启动 Unity 校验/生成新文件对应的 `.meta`，手工伪造 Unity `.meta` 有引入不一致资产元数据的
  风险，纯文档示例更安全）。
- `max_source.stat` 必须指向一个真实存在的 `stat.definition` 行（本模板玩家职业的主属性是
  `stat.template_power`，示例直接复用它；换成你自己的属性 id 即可）。
- 想彻底拒绝被覆盖（例如平台方发布的框架分发包不希望任何游戏改动某条数据），在框架那一行加
  `"final": true` 即可，见框架 `data/README.md` 同一节判断记录；本模板未使用这个字段。

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
