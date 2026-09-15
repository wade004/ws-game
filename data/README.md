# 数据目录约定

本目录存放全部内容数据表（"数据即内容"，见 [`../architecture/04_数据与内容管线.md`](../architecture/04_数据与内容管线.md)）。字段规范、id 规范、schema 版本与迁移、校验器检查项均以该文档为唯一权威来源；本文件只约定文件级组织方式，不重复定义字段。

## 两类目录：框架级数据表 vs. 示例/游戏数据

```
data/_framework/<domain>/<table>.json   框架级数据表（随分发包 dist/<version>/data/_framework/ 一起发给游戏）
data/_sample/<domain>/<table>.json      示例数据（仅供本仓库 DataRegistry 冒烟测试与校验器自测使用）
data/<game>/<domain>/<table>.json       具体游戏的数据（放各自游戏仓库自己的 data/<game>/ 下，本框架仓库不含）
```

- **`data/_framework/`**：判定规则——"该表的行由框架代码引用或生成"。逐表核对结果（见
  `core/foundation/data_registry` 任务判断记录）：
  - `found.event_catalog`：行被 `toolchain/gen_event_constants.py` 生成的
    `core/foundation/event_bus/generated/EventKeys.g.cs` 常量硬引用。
  - `found.input_action`：全部行被 `Adapter.Unity` 引导代码（`GameFoundationBootstrap`/
    `FrameworkResidentHost`）无条件整批 `DeclareActionSet`，是 Shell/UI 导航（确认/取消/菜单等）
    正常工作的前提，不是游戏可选内容。
  - `found.hook`/`found.game_state`：架构文档已点名的框架级表（`WellKnownHooks` 硬编码挂载点
    id、`AppStateMachineConfig.Default()` 等价于该表默认数据行）。收边任务已把两张表接成真正从
    数据注册表读取（`AppStateMachineConfig.FromRegistry`/`Core.Foundation.HookRegistry.
    FoundHookSchema.LoadDefinitions`，表缺失时分别退化为 `Default()`/`WellKnownHooks`，行为不变）
    并落地了对应的 `data/_framework/found/found.game_state.json`/`found.hook.json`
    （内容分别等价于 `AppStateMachineConfig.Default()`、`WellKnownHooks` 两个常量，见
    `core/foundation/app_lifecycle/schema/found.game_state.md`/
    `core/foundation/hook_registry/schema/found.hook.md`）。
  - 判断记录（`found.time_model`，本次改动）：`Core.Gameplay.Assembly.TimeModelSwitch` 构造期
    只要求该表存在 `scope: exploration` 一条（缺失时抛异常），不硬编码具体行内容；`scope: combat`
    一条可以完全缺失（`CombatModel` 为空时恒不切换离散模式，见该类型判断记录"缺失战斗时间模型时
    恒不切换"）——两条行本身是 13_新游戏接入指南.md 第 4 节"口味项"里"探索/战斗时间模型"的具体
    取值（`mode: continuous` 或 `discrete`，`discrete` 分支下的 `initiative_stat` 还会引用某个
    具体 `stat.definition` 属性 id），按判定规则属于"可配置默认值"而非"框架代码只认这一个固定
    值"，不应算框架级；此前（阶段落地时）曾放在 `data/_framework/found/`，是一处未经本判定规则
    审核的遗留错放（见 `architecture/落地计划/文档代码一致性审计_2026-09-05.md`），本次改动移到
    `data/_sample/found/`，`games/_template/data/game/found/` 补一份模板默认（探索/战斗都
    `continuous`）供新游戏复制修改；随迁移移除了此前为满足 `initiative_stat` 硬引用而在模板
    `stat.definition` 里补的 `stat.strength` 占位行（模板已改用连续模式，不再需要）。
  - 判断记录（`arch.power_type`，收边任务迁移）：`core/rules/common/contracts/WellKnownPowers.cs`
    硬编码 `arch.power.health` 为固定常量（非可配置默认值，与 `stat.definition` 等表被
    `CombatOptions`/`MovementOptions` 之类"可配置默认值"引用的情况不同——后者游戏层可以整体
    改配置指向别的 id，不构成"框架代码只认这一个固定值"），按判定规则应算框架级。收边任务把
    `arch.power.health` 这一行本身迁到 `data/_framework/arch/arch.power_type.json`（其余行如
    `arch.power.mana` 继续留在 `data/_sample`，多根行合并，见下"多根加载与合并规则"）；
    `games/_template/data/game/` 此前自带的一份 `arch.power.health` 行随之删除
    （多根合并对同一主键在两个根间重复判定为阻断错误，不能与框架这一行共存）。
    **判断记录（`max_source` 改为 `{kind: fixed, value: 100}`，不是原 `_sample` 那份
    `{kind: stat, stat: stat.stamina}`）**：`data/_sample` 原行的上限来源引用 `stat.stamina`——
    这是 `_sample` 自己的示例属性表内容，具体游戏未必定义同名属性（`games/_template`
    就没有），框架级的 `arch.power.health` 行若继续依赖某个具体游戏可能不存在的属性 id，会让
    "游戏不需要提供这张表"这句话变成假话（模板校验会因 `stat.stamina` 不存在而报错）。框架行
    因此改用与 `games/_template` 迁移前自带的那份完全一致的 `{kind: fixed, value: 100}`
    （游戏不需要任何前置属性即可使用），这是本次迁移在"复用 `_sample` 原内容"与"保证任意游戏
    开箱可用"之间的取舍，选择了后者——游戏若需要"生命值上限跟随某个属性成长"，只能另开一个
    新的资源类型 id（如 `arch.power.<game>_health_pool`）自行定义，不能覆盖/改写框架这一条。
    配套地，`core/numbers/tests/L1SampleDataTests.cs` 里原本用 `arch.power.health` 验证
    "`max_source: stat` 端到端算出正确数值、随属性成长 `RecomputeMax` 跟着变"这条测试路径，
    改用 `_sample` 新增的另一个纯测试用途资源类型 `arch.power.sample_vigor`（内容与原
    `arch.power.health` 行完全一致，只是改了 id/文本键）承接，覆盖范围不变；该测试类的数据加载
    方式也从单根 `data/_sample` 改为 `data/_framework` + `data/_sample` 双根合并加载（同
    `core/foundation/data_registry/tests/DataRegistryTests.cs` 的既有多根加载测试手法），
    `arch.power.health`/`arch.power.mana`/`arch.power.sample_vigor` 三者合并后仍是同一份
    `arch.class.sample_a.power_types` 清单能查到的资源集合。
  - 判断记录（收边任务复核："`core/` 内是否还有其它硬编码数据行 id 未迁入 `_framework`"）：
    对全仓库 `core/` 下 `new Id("...")` 字面量做过一轮排查（排除测试、生成物、`obj`/`bin`），
    区分三类：(1) 事件 key 常量（如 `RulesEventKeys.CombatEntered = new Id("combat.entered")`）
    ——不是数据行 id，是事件总线的路由 key，不在本判定规则范围内；(2) `XxxOptions` 类的
    `{ get; set; }` 属性默认值（如 `CombatOptions.HitTableConfigId`/`ItemOptions.BudgetCurveId`/
    `MovementOptions.MoveSpeedStat`）——游戏层可以整体改配置指向别的 id，是"可配置默认值"不是
    "框架代码只认这一个固定值"，不算框架级（同本节判定规则原文）；(3) RNG 流名（如
    `AiOptions.RngStream = new Id("ai.decision")`）、事件字段哨兵值（如
    `DifficultyHost.GlobalScopeId`/`SpawnHost.NoPlayerSentinel`）、运行期生成的实例 id 前缀
    （如 `AreaTriggerHost` 的 `"area.trap_" + seq`）——这些字符串从不作为某张 `TableSchema` 的
    主键去 `IDataRegistryView.Get`/`GetAll` 查询，不是"数据行 id"，只是恰好也用 `Id` 类型/
    `域名.名字` 格式的普通字符串常量。排查结果：全仓库唯二满足"`static readonly`
    + 不可配置 + 确实作为某张内容表主键被查询"的情形就是 `WellKnownHooks`（`found.hook`，
    见上）与 `WellKnownPowers.Health`（`arch.power_type`，本节），均已迁入 `_framework`；
    未发现其它遗漏项。
  - 其余表（`stat.*`/`arch.class`/`arch.race`/`arch.talent_tree`/`prog.*`/`fac.*`/`item.*`/
    `skill.*`/`combat.*`/`ai.*`/`quest.*`/……）经逐表核对，均只被"可配置默认值"（`XxxOptions`
    类的字段默认值，游戏层可整体覆盖）引用或完全不被框架代码引用，属于示例/游戏内容，留在
    `data/_sample`。
- **`data/_sample/`**：仅供本框架仓库 `DataRegistry` 冒烟测试与校验器自测使用，不代表任何真实
  游戏内容，具体游戏不应依赖其中任何具体 id。不随主分发包 `dist/<version>/`/`ws-game-<ver>.zip`
  分发（消费方接入的不是本仓库自身）；`assets/_sample` 同理。消费方反馈 E4 根治（2026-09-10，见
  `architecture/落地计划/消费方反馈-2026-09-10-编辑器.md` E4）：新工程接入后若想拿一份现成的、
  已知合法的样例数据/资源验证框架端到端能不能跑起来，`build.ps1 -Dist`/`-Release` 额外单独打一份
  `dist/ws-game-<ver>-samples.zip`（内含 `data/_sample`、`assets/_sample`），
  `toolchain/get_framework.ps1 -WithSamples` 下载/校验（按锁文件 `samples.sha256` 字段）并合并
  落地到与主 zip 相同的目录下；不传 `-WithSamples` 时行为与改动前完全一致（不下载、不落地这两棵
  目录树）。消费方反馈第 40 条（2026-09-13）：`data/_sample/found/found.migration_sample.json`
  是**迁移链演示表**——`found.migration_sample`（见
  `core/foundation/data_registry/core/BuiltinSchemas.cs`）是框架内唯一真正声明并使用了
  `TableMigration` 的登记表，没有任何运行时消费方，该文件的 `schema_version` 故意写 `1`（落后于
  该表当前登记版本 `2`），供内容工具/编辑器用真实数据验证"迁移链串接 + 整表迁移"复刻实现是否与
  框架行为一致；不要往这张表填真实内容、也不要把它当作任何游戏功能的一部分。
- **`data/<game>/`**：具体游戏的数据放各自游戏仓库自己的 `data/<game>/` 下（游戏代号由游戏仓库
  自己决定），与框架分发包的 `data/_framework/` **并列加载**（见下"多根加载与合并规则"），本框架
  仓库不包含任何具体游戏的数据目录。

## 多根加载与合并规则

`DataRegistry.LoadAll(IReadOnlyList<IDataSource> sources)`（见
`core/foundation/data_registry/core/DataRegistry.cs`）支持同一次加载传入多个数据根：同一张表
可以同时出现在多个根（例如 `data/_framework` 与游戏自己的 `data/<game>`），加载时按表名合并：

- 不同根贡献的记录按主键并集合并；同一主键在两个根间重复，默认判定为阻断错误（消息中点出两个根
  各自的位置）。
- 记录可用行级布尔字段显式改写上一条默认行为（勘误：2026-09-06 起，见
  `core/foundation/data_registry/core/DataRegistry.cs` 类型级判断记录"覆盖语义"、04 第 4 节
  `loadAll` 一行同一次改动）：后层记录声明 `"override": true` 时整行替换前层同主键记录（不做
  字段级合并），改记为一条覆盖诊断（`toolchain/validator` 会打印"覆盖清单"，见其 `Program.cs`），
  不产生校验问题；前层记录声明 `"final": true` 时拒绝被覆盖，仍判定为阻断错误。两个字段仅在确实
  发生跨根同主键重复时才生效，单根场景（该表本次未经历合并）出现值为真的这两个字段判定为警告并
  忽略。用法示例见 `games/_template/data/README.md`"覆盖 `arch.power.health`（可选）"一节。
- 同一张表在不同根的信封 `schema_version` 不一致，判定为阻断错误（消息中点出两个根各自的位置），
  不再合并该表；`override`/`final` 不改变这条规则。
- 单根用法（只传一个数据源）行为与改动前完全一致，`LoadAll()`（无参）等价于单元素多根调用。

`toolchain/validate_data.py`/`toolchain/validator` 已按此规则支持 `--data-root` 重复传入与
`--framework-root` 便捷参数（见该脚本文件头说明）；新游戏典型用法是"框架分发包的
`data/_framework` + 本游戏的 `data/<game>`"两根合并加载/校验。

## 目录结构（单个根内部）

```
data/<game_or_sample>/<domain>/<table>.json
```

- `<domain>`：表名的第一段（见 04 第 2.2 节域名清单），例如 `stat`、`l10n`。
- `<table>.json`：一张表一个文件，文件名（不含扩展名）就是表名，例如 `stat/stat.definition.json` 对应表名 `stat.definition`。

## 由资产导入工具生成/合并的表

`display.map`/`vfx.def`/`sfx.def`/`world.map` 四张表的行通常不手填——由
[`toolchain/import_assets.py`](../toolchain/README.md)"资产导入工具"一节的 `sprite`/`icon`/
`vfx`/`sfx`/`map` 子命令读取出图产物/地图分层图后自动生成，合并写入本目录对应文件（已存在同
主键的行整体替换，其余行原样保留，见该工具"合并写入行为"说明），格式仍遵循本文件"文件顶层
信封"/"编码与格式"两节约定；该工具的 `check` 子命令另外交叉校验这四张表引用的资产文件/目录是否
存在（含 `world.map` 引用的地图分层图，见 `toolchain/README.md`"已知缺口"一节——`data/_sample`
的 `display`/`vfx`/`sfx` 三张表历史遗留未接入该校验，`world.map` 已接入）。

## 文件顶层信封

每个数据文件顶层固定为以下结构：

```json
{
  "table": "stat.definition",
  "schema_version": 1,
  "rows": [ ... ]
}
```

- `table`：字符串，必须等于文件名（不含 `.json`）。
- `schema_version`：正整数，从 1 起，表级统一版本号（04 第 3 节允许表级版本，同一张表内全部记录共享同一个版本号）。
- `rows`：记录数组，每条记录是"字段名 → 值"的对象，字段定义见 04 及 05～09 各文档。
- `migrated_from`（可选）：整数，与 `schema_version` 同级的信封字段（不是逐行字段——04 第 3 节
  "schema 版本与迁移"是表级统一版本号，`migrated_from` 同样按表统一登记，不逐行登记）；若文件经历过
  迁移，记录该表最早的原始版本号，便于排查（04 第 3 节字段表）。取值须严格小于当前 `schema_version`
  （即 `[1, schema_version - 1]` 范围内的整数），否则 `DataRegistry` 加载期判定为 `envelope` 检查项
  错误。消费方反馈第 40 条：内容工具在把迁移结果写回磁盘前，用框架公开的
  `Core.Foundation.DataRegistry.SchemaMigrator.MigrateEnvelope` 生成该字段的取值——多次增量迁移时该
  实现会保留最早一次的原始版本，不会被后续每次迁移逐次覆盖成"上一次迁移前的版本"。

## 记录主键

- 一般表：主键字段是 `id`，取值必须符合 04 第 2.1 节 id 格式 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`，且 domain 前缀必须等于表名的第一段（例如 `stat.definition` 表里的记录 id 必须以 `stat.` 开头）。
- `l10n.text` 表例外：没有 `id` 字段，主键是 `key`（格式 `l10n.<来源域>.<来源记录 name>.<字段名>`，见 04 第 7.2 节）与 `locale` 的复合键。
- 主键规则：内容表用 `id`（domain 前缀 = 表名第一段）；跨 domain 的登记表（`found.event_catalog`、`found.input_action`、`l10n.text`）用 `key`，不做 domain 前缀检查（见 `core/foundation/data_registry/README.md`"主键规则"一节、`event_bus/schema/found.event_catalog.md`"与通用 id 规则的偏差"、`core/foundation/input_map/schema/found.input_action.md`"判断记录：登记表，主键字段名 key"）。`found.input_action` 记录的 id 域名是 `input`（不是表名首段 `found`），与 `found.event_catalog` 同理。

## l10n 示例数据（多语言/回退链/变量缺失演示）

消费方反馈第 41 条（2026-09-14）：`data/_sample/l10n/` 现登记两种语言——`l10n.locale.zh_cn`
（默认语言，`fallback: null`）与 `l10n.locale.en_us`（`fallback: l10n.locale.zh_cn`）；`l10n.text`
里每一个被某张内容表 `TextKey` 字段引用的键都补齐了 `en_us` 一行译文（英文正文与中文含义对应，
含 `{变量}` 占位的行英文保留同名占位符），`python toolchain/validate_data.py --strict` 在该数据集
上仍保持 0 error 0 warning（含消费方反馈第 42 条新增的非默认语言缺翻译 Warning，见下）。

另外补了两个**不被任何表字段引用**的纯演示键（因此不会触发第 42 条的 Warning）：

- `l10n.ui.sample_fallback_demo.text`：只登记了 `zh_cn` 一行，没有 `en_us`——演示语言回退链：
  `en_us` 查询本键时，`L10nHost`/`DataRegistry` 均沿 `en_us` 的 `fallback` 链（这里只有一步，直接
  落到 `zh_cn`）取到中文正文。
- `l10n.ui.sample_variable_demo.text`：`zh_cn`/`en_us` 都有，正文含 `{amount}` 占位——演示变量
  缺失路径：不提供 `amount` 时占位符原样保留，且记一条变量缺失警告（`L10nOptions.WarnOnMissingVar`，
  见 `core/foundation/localization/core/L10nHost.cs` 的 `SubstituteVars`）。

对应的加载/断言用例见 `core/foundation/localization/tests/L10nHostTests.cs`
（`RealSampleL10nFiles_FallbackDemoKey_...`/`RealSampleL10nFiles_VariableDemoKey_...`）。两个演示键
与其余内容表的示例数据一样，仅供本框架仓库自测使用，不代表任何真实游戏内容（同上"两类目录"一节
`data/_sample/` 判定说明）；`sample_*` 前缀、`<game>` 占位规则同样适用——本框架仓库不含任何具体
游戏专属键。

## creature 示例数据（summon_only 正例演示）

消费方反馈第 44 条（2026-09-14）：`data/_sample/creature/creature.template.json` 新增
`creature.sample_summon_totem`——`npc_flags` 含 `npc_flag.summon_only`（见
`core/carriers/creature/contracts/NpcFlag.cs`），是此前两条示例生物（`creature.sample_hero`/
`creature.sample_beast`）都没有覆盖到的取值：`SpawnSummonOnlyCreatureRule`（见
`core/gameplay/spawn/schema/SpawnValidationRules.cs`）的 Error 分支此前只能靠消费方自己在内存里
现改某条生物的 `npc_flags` 才能观测到，示例数据集本身给不出开箱即用的正例样本。配套补了一条
`display.map.sample_summon_totem`（复用 `sprite.creature.sample_beast` 精灵集，不新增素材）满足
`DisplayMapCoverageRule` 的覆盖检查，以及 `l10n.text` 里 `zh_cn`/`en_us` 两行译文满足非默认语言
缺翻译 Warning（消费方反馈第 42 条）。

**判断记录（不进 `spawn.table`）**：`data/_sample/spawn/spawn.table.json` 故意不引用这条生物——
示例数据集的既有基线是"`python toolchain/validate_data.py --strict` 0 error 0 warning"，若
`spawn.table` 真的引用一条 `summon_only` 生物会触发 `spawn_summon_only_creature` Error，破坏这条
基线。想看这条规则真的触发 Error 的"一眼可见的坏例子"，见：

- `core/gameplay/spawn/tests/SpawnSummonOnlyCreatureRealSampleDataTests.cs`：用真实
  `data/_framework` + `data/_sample`（含本条新增生物）经 `Presentation.Assembly.
  ContentValidationAssembly.CreateRegistry`/`LoadAll` 正式装配加载，内存里额外追加一条指向
  `creature.sample_summon_totem` 的 `spawn.table` 行（不落盘），断言产出
  `spawn_summon_only_creature` Error。
- `toolchain/tests/test_validate_data_summon_only_rule_default_wiring.py`：真实调用 `dotnet`
  （经 `validate_data.py` 拉起 `toolchain/validator`），把示例数据集拷到临时目录、往拷贝出的
  `spawn.table.json` 追加同一条违规行后跑 `validate_data.py --strict`，断言退出码非 0、输出含
  `spawn_summon_only_creature`——见下一条判断记录，这条路径现在也真的会拦下。

**判断记录（根治：`SpawnSummonOnlyCreatureRule` 现默认接线，`toolchain/validator` 侧同样生效）**：
此前 `SpawnSummonOnlyCreatureRule` 需要显式提供 `ICreatureTemplateQuery` 才会注册（见
`ContentValidationOptions.CreatureTemplateQuery` 判断记录），而 `toolchain/validator`（一次性命令行
进程）按既有设计从不接线该依赖——`python toolchain/validate_data.py --strict` 这条命令行路径下
`spawn_summon_only_creature` 检查项因此恒不生效，是示例数据门禁的一处真实缺口：`spawn.table` 即便
真的引用一条 `summon_only` 生物，门禁也永远不会拦下。2026-09-14 根治（比照消费方反馈第 34 条
`DisplayMapCoverageRule` 先例）：新增 `Core.Carriers.Creature.RegistryCreatureTemplateQuery`——直接
从已构造的 `IDataRegistryView` 现读现解析 `creature.template` 记录（不像 `CreatureFactory` 那样要求
"registry 必须已加载完成"这一更强前置条件，见该类型判断记录），`ContentValidationAssembly.
CreateRegistryCore` 未提供 `CreatureTemplateQuery` 时默认改用它，规则因此默认启用。`toolchain/
validator/Program.cs` 不需要任何改动——它从未显式设置过 `CreatureTemplateQuery`，走
`ContentValidationAssembly` 的新默认值即可。`--json` 输出里 `SpawnSummonOnlyCreatureRule` 现出现
在 `enabled_optional_rules`/`optional_rules[].enabled == true` 里，`disabled_optional_rules` 恒为
空数组（两条可选规则现均默认启用，同 `DisplayMapCoverageRule` 现状）。**行为变更提醒**：消费方若
自己的 `data/<game>/spawn/spawn.table.json` 引用了任何 `npc_flags` 含 `summon_only` 的生物模板，
升级到本版本后 `validate_data.py --strict`（乃至默认的 `WarningsAllowed` 严格级别，因为
`spawn_summon_only_creature` 是 Error 级，不受严格级别影响）会报错，需要先清理这类引用再升级。

## item N2 示例数据（多等级多品质模板、三条曲线、份额词缀、槽位与品质新列）

T-N2-10（分阶段落地计划第 14 节；ADR-0032）：在 T-N2-1～T-N2-9 已落地的运行时改动基础上，把
`data/_sample/item/**` 从"单一等级/单一品质/仅武器槽"的最小样例，充实为覆盖 N2 新字段与新表的样例
数据集，`games/_template` 空壳表保持不动（见下方判断记录）。

- **`item.slot_definition`**：新增 8 条槽位，与既有 `item.slot.sample_main_hand`（武器位）/
  `item.slot.sample_bag`（非装备分类桶）并存，不删既有行——`item.slot.sample_off_hand`（副手，
  `is_weapon: true`）；五个防具位 `item.slot.sample_head`/`sample_chest`/`sample_legs`/
  `sample_hands`/`sample_feet`（均 `has_armor: true`，T-N2-6 新字段，各自不同的
  `budget_coefficient`/`price_coefficient` 样例，胸甲系数最高 1.25、手/脚较低 0.6，体现"部位越大
  预算占比越高"这一常见取舍，样例非框架默认值）；两个饰品位 `item.slot.sample_ring`/
  `sample_necklace`（`has_armor: false`）。
- **`item.quality_definition`**：新增第三档 `item.quality.sample_epic`（`sort_weight: 3`，
  `budget_multiplier`/`price_multiplier: 2.0`，与既有 `sample_common`（1.0）/`sample_rare`（1.5）
  一起满足 `ItemQualityMultiplierOrderRule` 的非递减顺序）；`sample_common`/`sample_rare` 的
  `affix_count` 由 2/3 改为 0/1，`sample_epic` 补 2，三档呈 0/1/2（任务书字面要求）；
  `grant_budget_share` 三档 0.0/0.2/0.3 递增（`sample_rare` 原值 0.2 未改动，只新增 `sample_epic`
  取比它更大的 0.3，改动面最小）。真实仓库里没有任何运行时代码消费 `item.quality_definition` 的
  `affix_count`/`grant_budget_share` 具体数值（前者只作词缀抽取上限的登记位，随掉落三次掷骰
  T-N2-8 已消费；后者"授予价值超占比"警告尚无对应 `IValidationRule` 实现，见下方判断记录），改动
  这两个样例数值不影响任何既有断言（已核对 `core/carriers/item/tests`、`core/gameplay/loot/tests`
  等全部引用 `item.quality.sample_*` id 的测试均使用各自独立的内联 JSON 夹具，不读取本目录真实
  文件）。
- **`item.affix`**：新增 `item.affix.sample_of_the_titan`（`quality_pool: item.quality.sample_epic`，
  `budget_share: 0.15`，`stat_mix: [{stat: stat.stamina, ratio: 1.0}]`），与既有 4 条（`common`
  池 2 条、`rare` 池 2 条）一起覆盖全部三档 `quality_pool`，满足"≥4 条覆盖三档"。
- **`item.budget_curve`/`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve`**：四张
  曲线表断点均由 3 个扩到 6 个（`x = 1/10/20/30/40/60`，覆盖 07 惯例的 1～60 物品等级区间），全部
  保留原有断点值不变（`item.budget.default` 在 `x=1/10/60` 处仍是 20/200/1200，`item.weapon_dps
  .default` 仍是 4/20/120，`item.req_level.default` 仍是 1/10/60，`item.armor.default` 仍是
  5/50/300）——`item.sample_blade`/`item.sample_model_sword` 两条既有模板的预算利用率（75%）、
  武器秒伤偏离（0%）、需求等级反推（1）三处既有判断记录（见 `core/carriers/item/README.md` 判断
  记录 18/20/21）与 `core/gameplay/tests/EndToEndTests.cs` 的 `strength +15`/需求等级断言均未受
  影响。新插入的四个断点均落在原有折线（`item.budget.default`/`item.armor.default` 恰为
  `20×item_level`/`5×item_level` 的线性曲线；`item.weapon_dps.default`/`item.req_level.default`
  在 `x≥10` 段分别为 `2×item_level`/`1×item_level`）上，保证新老断点共同满足
  `curve_monotonic_finite` 单调有限要求。`item.budget_curve.exponent` 保持 `1.5` 不变（任务书
  硬性要求）。
- **`item.template`**：新增 4 条，与既有 `item.sample_blade`/`item.sample_model_sword`
  （均 `item_level=1`、`common`、主手）合计 6 条，覆盖多等级（1/10/20/30/60）×多品质（common/
  rare/epic）×多槽位（主手/头/胸/脚/戒指）——`item.sample_helm`（头，rare，等级 10）、
  `item.sample_chestplate`（胸，epic，等级 30）、`item.sample_boots`（脚，epic，等级 60）、
  `item.sample_ring`（戒指，rare，等级 20）。四条均只填单一 `stats[]` 属性（`stat.weight` 权重
  均为 1.0，消耗公式 `(Σ(值×权重)^k)^(1/k)` 单项时与 `k` 取值无关退化为该项本身，手算即可复现
  `IBudgetSolver` 反解结果），数值与推导过程记在各自 `budget_note`（"样例，非框架默认值"）：利用率
  统一取 75%（`≥70%` 阈值之上留出安全边际，不精确贴线），`affixes` 白名单只挑预算余量能覆盖的词缀
  （见下一条），且都补了 `display_ref`/两语言 `name_key`。`value_override` 沿用既有 `item.sample_
  blade` 那一条（=25）即满足"至少一条填 value_override"，四条新模板未重复填写。
- **"模板加词缀最大份额超预算"人工核算**（该阻断校验本身尚未实现，见下方判断记录，样例仍按其
  语义人工满足）：

  | 模板 | 等级/品质/槽位 | 预算上限 B | stats 消耗 | 利用率 | 词缀白名单（词缀池 `budget_share`） | 消耗+词缀最大份额 |
  |---|---|---|---|---|---|---|
  | `item.sample_blade`（既有） | 1/common/主手 | 20×1.0×1.0=20 | 15 | 75% | common 池 `affix_count=0`，不掷词缀骰 | 75%（无词缀） |
  | `item.sample_model_sword`（既有） | 1/common/主手 | 20×1.0×1.0=20 | 15 | 75% | 同上 | 75%（无词缀） |
  | `item.sample_helm` | 10/rare/头 | 200×1.5×0.8=240 | 180 | 75% | `sample_of_frost`（0.2） | 75%+20%=95% |
  | `item.sample_ring` | 20/rare/戒指 | 400×1.5×0.5=300 | 225 | 75% | `sample_of_frost`（0.2） | 75%+20%=95% |
  | `item.sample_chestplate` | 30/epic/胸 | 600×2.0×1.25=1500 | 1125 | 75% | `sample_of_the_titan`（0.15） | 75%+15%=90% |
  | `item.sample_boots` | 60/epic/脚 | 1200×2.0×0.6=1440 | 1080 | 75% | `sample_of_the_titan`（0.15） | 75%+15%=90% |

  全部 6 条 ≤ 100%，且"预算利用率过低"（`item_budget_utilization_low`）与"预算超标"
  （`item_budget_exceeded`）两条已实现的规则在 `python toolchain/validate_data.py --strict` 下
  对本数据集均 0 命中（连同 `ItemQualityMultiplierOrderRule`/`ItemAffixStatMixRatioSumRule`/
  `ItemWeaponDamageDeviatesDpsCurveRule`/`CurveMonotonicFiniteRule` 一并核实，见该命令实测输出）。
- **`loot.table`**：`loot.sample_beast` 追加第二条配置了 `quality_weights` 的条目（引用
  `item.sample_helm`，`{common:2, rare:2, epic:1}`），追加在既有三条条目之后（不改变既有条目在
  `chance_each` 掷骰顺序中的位置/随机数消耗次序，`core/gameplay/tests/EndToEndTests.cs` 里"保底掉
  1 个 `item.sample_token`"与"固定种子两次独立运行掉落逐项相等"两条断言均只依赖相对确定性/包含
  关系，不依赖具体随机数序列，已核对不受影响）。
- **`diff.tier`**：`diff.sample_story` 补一行显式 `item_level_offset: 0`（与缺省值相同，仅作为
  T-N2-8 新字段的样例展示，同 T-N2-3 给 `item.budget_curve` 补 `exponent` 样例的处理口径）；
  `diff.sample_veteran` 既有 `item_level_offset: 5` 未改动。
- **`l10n.text`/`display.map`**：新增槽位/品质/模板/词缀共 14 个 `name_key`，`zh_cn`/`en_us` 两语言
  各补一行；4 条新模板的 `display.map` 行复用既有 `sprite.item.sample_blade` 精灵集（不新增素材，
  同既有 `item.sample_token`/`item.sample_tonic` 两行的既定做法，`category: item`、无
  `weapon_style_ref`——四条新模板均非武器），按既有 id 字母序插入。

**判断记录（"模板加词缀最大份额超预算"阻断校验，T-N2-10 时尚未实现——已由 T-N2-11 补齐）**：分阶段
落地计划第 8 节阶段 N2 验收标准 5 要求"模板加词缀最大份额超预算……各报 Error"，T-N2-10 核对时
`core/carriers/item/schema/README.md`"校验规则清单"与 `core/carriers/item/core/
ItemValidationRules.cs`（当时八个 `IValidationRule` 实现类）确认：现有八条规则里只有
`item_budget_exceeded`（模板自身 `stats` 超预算）与 `item_budget_utilization_low`（利用率过低警告）
两条与预算相关，均不核算词缀；`core/carriers/item/README.md` 判断记录 17 末段"勘误"明确写着"模板加
词缀最大份额超预算……仍需预算反解……确实要等 T-N2-4"，而 T-N2-4（判断记录 19）落地的是
`IBudgetSolver.Solve`/`EquipmentScoreAnalyzer` 两个可调用契约面，未新增任何 `IValidationRule`；
T-N2-8（判断记录 15）落地的是掉落三次掷骰的运行时消费，同样未新增这条阻断规则。T-N2-10 当时只补
数据与文档，不实现校验规则（任务书"禁止事项"明确要求"本任务不实现校验规则，但样例要满足"）——本节
的人工核算表按该阻断校验的既定语义（预算反解出的词缀份额 + 模板自身消耗占比 ≤ 100%）手工保证全部
样例合规。**T-N2-11 已按同一语义落地 `ItemTemplateAffixShareExceedsBudgetRule`（检查名
`item_template_affix_share_exceeds_budget`），`data/_sample`/`games/_template` 两个数据根
`--strict` 校验 0 命中，与本节人工核算表结论一致，零改样例**，见 `core/carriers/item/README.md`
判断记录 25、`core/carriers/item/schema/README.md`"校验规则清单"新增一行。

**判断记录（`games/_template/data/game/item/**`/`diff/diff.tier.json` 保持不动，读 `games/_template/
data/README.md` 既定约定后的决定）**：该文件明确记录 `item.slot_definition`/`item.quality_
definition`/`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve` 五张表当前状态是
"游戏必填（若已有 `item.template` 内容）/空壳（尚无物品内容时）"，`item.budget_curve` 保留一条
"留作示例"的行；模板本身**没有**任何 `item.template` 数据（"本模板暂无 `item.template` 行"，同一
README 原文），这是该模板"最小可玩闭环"既定范围的既有设计（不含物品系统，见该文件"判断记录：为
什么没有 `creature.template`/`display.map`"一节的同类口径）。T-N2-10 的"填形"选项因此按既定约定
选择**保持空壳**：五张表继续保留空 `rows: []`（`item.budget_curve` 保留原有一条示例不动），不额外
造出一份脱离任何 `item.template` 的孤立槽位/品质/曲线数据——造了也不会被任何模板引用，无法体现
"最小合法示例"的实际意义，反而会让后来者误以为模板已经支持物品内容。`diff/diff.tier.json` 同理不
改：该表已有 1 条 `diff.template_normal`（`item_level_offset` 缺省 0，语义等价于显式填 0），且该
README 未把 T-N2-8 的 `item_level_offset` 列入"游戏必填"清单，本任务不越权新增字段样例。

## skill N3 示例数据（use_condition/budget_note/scaling/base_curve、overflow_policy 三态、积累型资源、health 脱战回满）

T-N3-11（分阶段落地计划第 14 节 N3 任务表第十一行；ADR-0031 决策 3/11）：在 T-N3-1～T-N3-10 已落地
的运行时改动基础上，把 `data/_sample/skill/**`、`data/_sample/target/target.chain_def.json`、
`data/_sample/arch/arch.power_type.json`、`data/_framework/arch/arch.power_type.json` 充实为覆盖
N3 新字段的样例数据集；`games/_template` 按既定约定补空壳。

- **`skill.def`**：新增 4 条技能（既有 `sample_strike`/`sample_burn`/`sample_bolt`/`sample_passive`
  不动，见下方"cast_time: 0 样例"判断记录）：
  - `skill.sample_rest`（脱战限定）：`use_condition: "not combat.in_combat"`（04 第 6.2 节
    `combat` 分组，`RulesExprSchema.Base` 已登记 `combat.in_combat: Bool`；**契约疑点/勘误**：任务书
    原文给出的示例写法是 `!combat.in_combat`，但 `core/foundation/expr/core/ExprLexer.cs:134-137`
    对单独出现的 `!` 字符直接抛 `ExprParseException`——"只支持 `!=` 运算符"，取反必须用关键字
    `not`（`ExprLexer.cs:253`/`ExprParser.ParseUnary`），本任务据实际词法/语法实现改用
    `not combat.in_combat`，若按任务书原文字面抄录会在加载期 `expr_parsable` 校验直接报错，已用
    `validate_data.py --strict` 实测确认改后写法 0 错误）；`cast_time: 3.0`（有动作时长的"休整"
    技能，不使用 `respects_gcd:false` 规避警告）、`heal` 效果 `scaling` 两条
    （`stat.stamina`×2.0、`stat.intellect`×1.0）+ `base_curve_ref:
    skill.base_curve.sample_level_curve`。
  - `skill.sample_lockpick`（目标类型限定）：`use_condition:
    "target.has_tag(skill.tag.sample_object)"`（`target` 分组 `has_tag(Id): Bool` 已登记，绑定
    的是调用方显式 `targets` 参数的第一个元素，见 `core/rules/skill/README.md` 判断记录 51"1.5 步
    位置与目标绑定"）；`cast_time: 2`，效果 `open_lock`（无 `params`，效果原语参数表允许省略）。
  - `skill.sample_burst`（`budget_note` 示例）：`budget_note` 字段非空，`school_damage` 效果
    `scaling` 两条（`stat.spell_power`×3.0、`stat.intellect`×1.0）+ 同一条 `base_curve_ref`，
    `cooldown_duration: 30`、`cost` 60 点法力——刻意设计为"大招"形态，`budget_note` 原文说明这是
    展示 `SkillBudgetAnalyzer` `ConfirmedDeviation` 分支的示例；**当前 `RulesSchemaCatalog.
    RegisterL2Schemas` 注册 `SkillBudgetValidationRule` 时 `anchorProvider` 为 `null`（T-N3-9 既有
    判断记录"`sim.anchor` 归阶段 N6，本阶段尚不存在"），该规则整体不产生任何问题，因此本字段目前
    只是数据样例，不会被实际算出比值并核对"确实超带宽"——留给 N6 接入真锚点后回归验证。
  - `skill.sample_parry`（反应类，`cast_time: 0`/`respects_gcd: false`）：`cooldown_duration: 8`+
    `cost` 5 点法力，`use_condition: "combat.in_combat"`（与 `sample_rest` 相反的战斗内限定，一并
    验证 `combat.in_combat` 正/负两种用法），效果 `interrupt`——与 `sample_strike`/`sample_bolt`
    一起构成"反应类必须有冷却/消耗"这条硬性规则（禁止事项"不靠 `respects_gcd:false` 一刀切规避"）
    的三个正例：`sample_strike`/`sample_bolt` 已有 `cost`（法力 10），`sample_parry` 额外有真实
    `cooldown_duration`，均不会触发 `SkillNoTimeCostWarningRule`（该规则判定条件是
    `cast_time==0 && respects_gcd==true`，三者均 `respects_gcd:false`，天然不落入判定范围，不是
    "靠这个字段值规避"，而是它们本身就是设计上的反应类技能）。
- **`skill.base_curve`（新表，T-N3-2 登记 schema 时尚为空壳，本任务补首条真实曲线）**：
  `skill.base_curve.sample_level_curve`，5 个断点（等级 1/15/30/45/60 → 10/40/90/150/220），单调
  递增、满足 `curve_monotonic_finite`；供上方 `sample_rest`/`sample_burst` 的 `base_curve_ref`
  引用。
- **`skill.budget_rule`**：T-N3-3/T-N3-9 已把 `skill.budget_rule.default` 的完整字段集补齐为样例
  （`beat_seconds`/三条溢价折价曲线/带宽/硬上限/控制类别权重），本任务未改动——已满足验收标准
  "`skill.budget_rule` 有行"，`SkillOptions.BudgetRuleId` 缺省值恰好指向这条记录。
- **`target.chain_def`**：新增 3 条，`overflow_policy` 三态各一条真实链（既有
  `sample_group_enemies` 的 `split` 不动）：`sample_cone_enemies`（扇形，`max_targets:2`，
  `truncate`）、`sample_line_enemies`（直线，`max_targets:2`，`cap`）、`sample_nearest_object`
  （圆形，`filters: ["tag:skill.tag.sample_object"]`，供 `sample_lockpick` 的
  `target_shape_ref` 使用；不声明 `overflow_policy`，`max_targets:1` 恒不触发溢出，验收标准只要求
  三态各 ≥1 条，均已满足，不强求每条新链都声明该字段）。三条新链目前均未被任何技能
  `target_shape_ref` 直接引用（`sample_nearest_object` 除外），与既有 `sample_group_enemies` 同一
  惯例（该链此前也未被引用）——纯 schema 覆盖样例，见 `core/rules/targeting/schema/README.md`
  `overflow_policy` 字段判断记录。
- **`arch.power_type`（积累型资源 + health 脱战回满）**：
  - `data/_framework/arch/arch.power_type.json` 的 `arch.power.health` 新增
    `refill_on_leave_combat: true`（06 第 4.5 节 2026-09-14 修订段"脱战自动回满同样适用于
    生命……是否启用与回满速率为策略配置项，默认启用"，ADR-0031）。**契约核对结论（本任务未新增
    任何字段/运行时代码）**：`refill_on_leave_combat` 字段（`PowerSchemas.PowerType`）与运行时
    消费（`PowerHost.SetInCombat` 从 `true` 切到 `false` 时对该字段为真的资源类型立即回满）在
    本任务开始前就已存在——`data/_sample/arch/arch.power_type.json` 的
    `arch.power.sample_vigor` 早已使用同一字段演示"回复型"资源（见该文件既有行）；任务书要求"先
    核对 schema 是否已有该字段，没有则上报"，核对结论是**已有**（字段与运行时消费均非本阶段
    新增），因此本任务只补 `arch.power.health` 这一条框架默认数据，不涉及任何 `ArchSchemas`/
    `CombatHost`/`PowerHost` 代码改动，不需要上报待安排的运行时任务。
  - `data/_sample/arch/arch.power_type.json` 的 `override:true` 覆盖行（原样替换
    `arch.power.health`）同步补 `refill_on_leave_combat: true`——**判断记录**：`override`
    是整行替换语义（非逐字段合并，见该文件既有 `description`），若不在覆盖行里重复声明，合并后
    的数据集会丢失框架默认的脱战回满策略，与 06 第 4.5 节"默认启用"这一策略产生分歧；本次一并
    补一句 `description` 说明这一"整行替换需要重复声明"的易漏点。
  - 新增 `arch.power.sample_fury`（积累型资源示例，06 第 2.2 节 2026-09-14 修订段"填充规则两种，
    职业选一种：回复型（起始满、匀速回复）、积累型（起始空、随战斗行为产出、脱战衰减清空）——
    两者都是既有字段的取值组合"）：`start_full:false`（起始空）、`regen_in_combat:10`（战斗中
    产出，真实游戏通常改由 `energize` 技能效果驱动，样例用被动 `regen_in_combat` 代表同一约定
    组合）、`decay_out_of_combat:50`（脱战快速衰减清零）、不设 `refill_on_leave_combat`（与
    回复型语义相反，不应同时声明）。`start_full`/`regen_in_combat`/`decay_out_of_combat` 均是
    `PowerSchemas.PowerType` 既有字段（早于本阶段登记），本任务未新增字段，只补组合样例；
    `sample_vigor` 行同步补 `description` 标注其"回复型"身份，与 `sample_fury` 对照。
- **`l10n.text`**：`l10n.power.sample_fury.name` 两语言各补一行（`arch.power_type.name_key` 有真实
  消费方，`L10nHost` 加载期即校验存在性）。**判断记录（未给新增的 4 条技能补 `l10n.*` 文本）**：
  `SkillSchemas.Def`/`AuraDef` 均未登记 `name_key`（或任何指向 `l10n.text` 的字段）——现有 4 张
  `skill.*` 表本身没有任何字段会被解析成"指向某个文本键"，`skill.aura_def.sample_burn` 等既有样例
  同样没有配文本（仅 `display.map` 有视觉覆盖行，无文本）；任务书"新技能文本两语言"字面要求本任务
  已重新核对契约面后判定为不适用（没有字段可以承载这份文本，写了也是孤儿数据，且会被
  `sample_table_empty`同类"孤儿"检查思路质疑），改为给确有 `name_key` 消费的
  `arch.power.sample_fury` 补文本，更贴合"新内容需要配套文本"这一意图的实际契约面。
- **`display.map`**：新增 4 条（`sample_rest`/`sample_lockpick`/`sample_burst`/`sample_parry`），
  复用既有 `sprite.item.sample_blade` 精灵集（不新增素材，同 T-N2-10 判断记录同一做法），
  `category: skill`，按既有 4 条技能显示行的字段形状（`kind: sprite`、`direction_count: 4`、
  单条 `dir.side_l`→`dir.side_r` 镜像）逐一对齐。
- **`games/_template/data/game/skill/skill.base_curve.json`**：新增空壳（`rows: []`）+
  `.meta`（新 guid，未与仓库内任何既有 guid 冲突，已 `grep` 核实）。**判断记录（为什么补这一张
  而不是维持"该目录完全不出现 `skill.base_curve`"）**：`games/_template/data/game/skill/` 此前
  只有 `skill.budget_rule.json` 一张空壳（T-N3-3 起），模板本身**没有**任何 `skill.def` 数据（同
  `item` 域"模板暂无 `item.template` 行"既定设计，见上文"判断记录：`games/_template/data/game/
  item/**`"一节同一口径）——`skill.base_curve` 与 `skill.budget_rule` 同属"支持性/曲线类"表
  （不是 `skill.def` 这一主表本身），既定约定是"主表缺失时支持性表仍给空壳"（`item.armor_curve`
  等四张表在 `item.template` 缺失时同样给空壳，见上文判断记录），本任务据此补齐这张此前遗漏的
  支持性表空壳，不额外造出脱离任何 `skill.def` 的孤立曲线数据。

**cast_time: 0 样例判断记录（T-N3-5 遗留、本任务按 06 第 3.1 节复核，未改动既有两条）**：
T-N3-5 已把 `skill.sample_strike`/`skill.sample_bolt` 改为 `respects_gcd:false`（反应类）以消除
"无时间成本"警告，当时判断记录明确写"样例的进一步系统性调整留给 T-N3-11"。本任务复核后维持
不变——禁止事项"不靠 `respects_gcd:false` 一刀切规避"的真实含义是"不能只改这一个字段就交差、
不管技能是否真的该是反应类"，而这两条样例改动时**已经**满足"反应类且有冷却/消耗"（均带
`cost: [{power_type: arch.power.mana, amount: 10}]`）这一收紧后的判断标准，只是当时没有
`cooldown_duration` 这一项（消耗本身已足以防止无限连打，06/ADR-0031 并未要求反应类技能必须
同时具备冷却*和*消耗，"有冷却/消耗"是或不是且）；重新设计成"有动作时长的普通攻击"反而要牵动
`CastPipeline` 节拍锁分支识别的"反应类插入"资格（T-N3-4 判断记录 51 明确指出改 `respects_gcd`
会改变该技能能否在他技能动作中插入这一可观测行为），进而波及 6 个既有测试文件、12+ 个用例（详见
`core/rules/skill/README.md` 判断记录 51"队列 + 反应类回归风险"），与本任务"只补样例数据、不
新增效果原语"的最小改动范围不符。本任务改为新增 `sample_parry` 作为"反应类 + 真实冷却"的补充
示例（见上文），额外坐实这一判断标准，不动既有两条。已跑全量 `Tests.Rules`（751 例）/`Tests.
Gameplay`（725 例）/`Replay`（12+2+10 例）确认零回归。

**既有测试断言核对（结论：无需改动）**：已检索 `core/gameplay/tests/EndToEndTests.cs`、
`core/numbers/tests/L1SampleDataTests.cs`、`core/gameplay/tests/Perf/PerfBaselineTests.cs`、
`core/rules/tests/TwoUnitsFightTests.cs` 等读取 `data/_sample`/`data/_framework` 真实文件的测试——
均未对 `skill.def`/`target.chain_def`/`arch.power_type` 三张表的记录数或具体字段值做硬编码断言
（`GameWorldFixture`/`L1SampleDataTests.BuildWorld` 只按 id 精确查找自己需要的既有记录，不枚举
表内全部记录数），本任务新增的行只是"表里多了几条没人特意去数的记录"。已跑不带 filter 的全量六
程序集（217+1115+751+587+634+725 例）与 `Replay`（12+2+10 例）确认零回归（详见提交前 `dotnet test`
输出）。Unity PlayMode 侧因当前环境不含 Unity Editor 无法本机验证，留待阶段 N3 收尾（T-N3-12）
随全量回归一并核对。

## 编码与格式

- UTF-8，无 BOM。
- 缩进 2 空格。
- 行尾 LF（不用 CRLF）。

## 与校验器的关系

`toolchain/validate_data.py` 读取本目录下的表做两道校验：第一道是本文件自己实现的骨架级通用检查（信封三键是否存在、`table` 是否等于文件名、`id`/`key` 格式是否合法，对每个数据根独立跑，不做跨根合并判定）；第二道调用 `toolchain/validator`（复用 `DataRegistry` + 全部模块已注册的 `IValidationRule`）做字段级/引用完整性/表达式/模块专属校验与跨根合并（04 第 5 节检查项清单是校验器的设计目标，实际校验范围以已注册的具体规则实现为准——例如技能/光环等嵌套结构内部的精确字段/类型/参数校验目前还有已知缺口，见 `architecture/落地计划/audit-5e779c6-20260907/foundation-rules.md` FR-05，不是每张表的每个嵌套字段都已有对应规则），详见 `toolchain/validate_data.py` 文件头注释。

判断记录：第二道校验一次性注册全部 L0～L5 模块的 `TableSchema`/`IValidationRule`（见 `toolchain/validator/Program.cs`）。ADR-0018 决策 3（校验装配入口）落地后，这段"汇总注册目录 + 可选规则接线参数 + 装配选项"逻辑已从 `toolchain/validator/Program.cs` 抽为核心库内的单一公开入口 `Presentation.Assembly.ContentValidationAssembly.Run`/`CreateRegistry`（`presentation/assembly/ContentValidationAssembly.cs`）：`toolchain/validator` 现在只做命令行参数解析 + 调用该入口 + 打印，编辑器基础套件（ADR-0018 决策 1/2 定义的独立消费方项目，随游戏走，不在本仓库）按同一入口装配"内容校验设置"面板，保证"编辑器里看到的红线 = 门禁会报的错"——两个消费方不允许各自维护第二份注册顺序。其中 `core/carriers/item` 的预算超标校验（`ItemBudgetValidationRule`）此前不区分数据根是否包含 item 域，只要注册了就会无条件要求全局默认预算曲线（`item.budget.default`）存在——单独校验 `data/_framework`（只有 `found.event_catalog`/`found.input_action` 两张纯登记表，不含任何 `item.*` 表）时会因此误报。加固J3 已改为：数据集里 `item.template` 一行都没有时该规则直接跳过（没有物品就没有预算可超标），只要数据集确实登记了 `item.template`，`item.budget.default` 缺失仍然照常报错。修复后 `data/_framework` 单独校验两道均可正常跑，不再需要 `--skip-dotnet` 规避（`python toolchain/validate_data.py --data-root data/_framework` → 0 错误）。默认调用（`_framework` + `_sample` 合并，或后续"`_framework` + 具体游戏数据"合并）同样两道校验都应 0 错误。

上面两道校验的是**数据行内容**（本目录下实际的 `.json` 表文件）；`toolchain/validator --schema-audit`（F3 新增，见 `toolchain/README.md`"元数据门禁"一节）校验的是**表结构声明本身**（`TableSchema`/`FieldSchema` 代码，不读取本目录任何数据文件），两者互不替代——字段描述缺失、复合字段未登记子结构等问题即使数据行本身完全合法也会被元数据门禁拦下。
