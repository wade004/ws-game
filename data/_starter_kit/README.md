# data/_starter_kit：内容起步包（可选框架数据根）

出厂的**通用游戏内容**起点（[ADR-0162](../../architecture/adr/0162-内容起步包与默认中文字体子集工具.md)）：技能、敌人、装备、掉落、任务、对话、地图规则模板，加上通用界面与系统文案、数值曲线。
游戏无关：没有任何具体游戏的剧情、角色名、美术、音频；数值是自拟的试调起点（标准见"限制"），不是已验证值。

**默认不装载。** 它是与 `data/_feel_templates` 同款的可选根：游戏要用，就把它作为额外框架根声明（模板配置容器 `GameOptions.ExtraFrameworkDatasetRoots = new[] { GameOptions.StarterKitDatasetRoot }`，
装载顺序：框架根 → 手感根 → 额外框架根 → 游戏根）；不声明就与它无关，行为与此前逐位一致。
随发布产物进分发目录与框架数据包（`Data~/data/_starter_kit`），内容同步脚本把它镜像进内容根；不进实验室根。

## 内容清单（`data/_starter_kit/<domain>/<table>.json`）

| 家族 | 表 | 内容 |
|---|---|---|
| 技能模板 | `skill.def`、`skill.book`、`target.chain_def`、`skill.base_curve`、`skill.budget_rule` | 三段近战连招 `kit_melee_1~3`（标 `weapon_paced`，节奏随主手武器攻速缩放，后摇起点可被翻滚取消；ADR-0176）、重击 `kit_heavy`、蓄力 `kit_charge`、翻滚 `kit_dodge`（主动键，无敌帧 + 位移；技能 id 沿用 `kit_dodge`，「闪避」一词只指下面的被动判定）、远程射击 `kit_ranged_shot`、带预警的范围大招 `kit_ultimate_aoe`（1.5 秒前摇 + 前摇霸体）、小兵/重型/首领敌方招式、两档治疗药水；6 条目标链（自身、最近敌人、窄/宽扇形、圆形环绕、圆形范围）；起步职业技能书 `skill.book.kit_hero` |
| 敌人原型 | `creature.template`、`creature.tier_definition`、`ai.behavior_profile`、`ai.rotation`、`fac.*` | 近战小兵、远程射手、重型精英、首领骨架，各自带 AI 配置与出招表；普通/精英/首领三档；玩家/敌对/镇民三个阵营与敌我关系；任务发布者、商人、存档点三类 NPC |
| 装备 | `item.slot_definition`、`item.quality_definition`、`item.affix`、`item.template`、`item.*_curve` | 主手/胸甲/饰品/消耗品/材料五个槽位；普通/优秀/精良/史诗四档品质（预算倍率、词缀数递增）；四条词缀结构（预算份额 + 属性混合 + 品质池，含加闪避的 `kit_of_evasion`）；九个通用示例装备与消耗品（含一件加闪避的护符 `kit_charm_evasion`）；预算/护甲/需求等级/武器 DPS 曲线 |
| 掉落 | `loot.table` | 每个敌人一张表：`chance_each`（按概率各掷一次）与 `weighted_pick_one`（加权必掉一件）两种结构的示例 |
| 任务结构 | `quest.def` | 击杀计数 `kit_kill_count`、收集 `kit_collect`、对话交付 `kit_talk`、多阶段（同任务多目标 + 前置任务链）`kit_stage_1/2` |
| 对话结构 | `dialog.gossip_menu`、`dialog.story_tree` | 发布者/商人/存档点菜单（按任务状态显示/隐藏选项）、带条件分支的剧情树（分支按任务完成情况切换） |
| 地图规则 | `world.map`、`spawn.table`、`encounter.def`、`area.trigger_def`、`world.flag_schema` | 两张示例图（出生点）、刷怪点、遭遇区域（进入触发、清场写世界标记）、传送区（带目标出生点）、探索区、存档点 NPC |
| 数值曲线 | `prog.*`、`stat.*`、`econ.*`、`item.budget_curve` | 10 级经验/成长曲线、击杀与任务经验来源、属性定义（力量/体力/攻击强度/移速/伤害加成/受伤调整/治疗加成/护甲/闪避）、金币与物品价值曲线 |
| 被动闪避（ADR-0178） | `stat.definition`/`stat.rating_conversion`/`stat.weight`、`combat.hit_table_config`、`feedback.floating_text_style`、`feedback.binding` | 闪避属性 `stat.dodge_rating`：基础 5 点 + 每级成长 0.5 点 + 装备/词缀，1 点折 1%（`stat.rating.kit_dodge`），封顶 50%；默认命中表启用 `dodge` 分支（受击者的闪避率作概率，所有直接伤害攻击可被闪避，治疗与持续伤害不掷）；敌人各有闪避点数（小兵 3、弓手 8、重型 2、首领 10）；闪避时头顶飘字（`feedback.kit_dodge_text`，样式 `kit_dodge`，需要游戏装配飘字接收器）；不可闪避的技能加框架保留标签 `skill.tag.unavoidable`。不想要闪避：覆盖 `combat.hit_table.default` 把 `dodge.enabled` 改回 false |
| 系统文案 | `l10n.locale`、`l10n.text` | 简体中文（默认）+ 英文：框架自带界面（背包/装备/任务日志/暂停/设置/商店/对话/提示框）用到的全部文案键、属性/槽位/品质名 |
| 输入与界面 | `found.input_action`、`shell_menu_definition`、`ui_layout_definition` | 面板开关热键 `input.action.ui_toggle_*`（背包 I、装备 U、任务 J、技能 K、角色 C、设置 N、存档 L，可在设置里改键）、主菜单、基础界面布局 |
| 表现占位 | `display.map` | 每个技能/物品/生物一行，指向占位精灵 `sprite.kit_placeholder`（框架的 display 覆盖规则要求每个逻辑 id 都有外形行）；游戏用 `"override": true` 换成自己的美术 |

## 命名与冲突规则

- **基础设施 id 不带前缀**（`stat.vitality`、`item.slot.main_hand`、`item.quality.rare`、`fac.player`、`econ.currency.gold`、`found.time_model.*`、`l10n.locale.*`、`prog.xp_source.*`……）：游戏通常直接引用；**内容模板带 `kit_` 前缀**（`skill.kit_*`、`creature.kit_*`、`item.kit_*`、`loot.kit_*`、`quest.kit_*`、`dialog.kit_*`、`world.kit_*`……）：拿来即用或复制改名。
- 游戏自己的内容用游戏前缀，不会与起步包冲突。**同一个 id 不能在起步包与游戏根里各定义一次**（重复 id 是阻断错误）：游戏要么删掉自己那份、直接引用起步包的行，要么在自己那行写 `"override": true`（整行替换，覆盖清单会列出）。
- 起步包对框架默认 `arch.power.health`（固定 100）写了一行 `"override": true`：生命上限取自 `stat.vitality`。游戏要别的口径时，在自己的根里对同一 id 再写一行 `"override": true`（后加载的根覆盖先加载的，链式覆盖会逐条列在覆盖清单里）。
- 起步包不带手感数据、特效/音效定义、动画集、地图场景与导航资源（那些是游戏自己的）；`display.map` 的占位行与 `world.map` 的 `scene_ref`/`nav_ref` 只是占位 id，没有对应资源。

## 默认中文字体与子集

默认字体是 `assets/_placeholder/fonts/noto_sans_cjk_sc.otf`（Noto Sans CJK SC，SIL OFL 1.1，许可证 `LICENSE-OFL.txt` 与字体同目录随发布产物分发，约 15.7 MiB，覆盖整套简体中文）。
发版想缩小体积时用字体子集工具：

```
python toolchain/font_subset.py --data-root data/_starter_kit --data-root <游戏数据根> --out <输出>.otf
```

它扫描全部数据根里 `l10n.text` 的用字，加 GB2312 一级常用字表（`--no-common` 可关）与你给的额外字符，输出子集字体；实测起步包文案 + 常用字表约 1.8 MB（整套的 11%），只留数据用字约 0.15 MB。
详见 `toolchain/README.md`。

## 校验与限制

- 门禁步骤 `validate_starter_kit_data`：`validate_data.py --strict --framework-root data/_framework --data-root data/_starter_kit`，须 0 error 0 warning；`toolchain/tests/test_starter_kit_data.py` 另断言：游戏无关、中英文各有一份、框架界面用到的文案键齐全、各内容家族都在。
- 数值（伤害、经验、价格、预算）按框架的数值规则自洽（预算利用率、价格偏离公式 20% 内），但是**自拟的试调起点，没有试玩**；游戏按自己的节奏重配。
- 任务/对话文案是中性占位句，仅演示结构；`kit_stage_2` 的"首领"、`kit_talk` 的"商人"等对象就是起步包自己的示例敌人与 NPC，游戏换成自己的内容时同步改引用。
- 默认字体只覆盖其字形集；GB2312 一级字表不含生僻字与繁体，游戏文案若用到，数据里出现的字会被子集工具自动收进（不依赖常用字表），源字体也没有的字符工具报缺字。
