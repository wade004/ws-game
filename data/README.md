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
  游戏内容，具体游戏不应依赖其中任何具体 id。
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

## 记录主键

- 一般表：主键字段是 `id`，取值必须符合 04 第 2.1 节 id 格式 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`，且 domain 前缀必须等于表名的第一段（例如 `stat.definition` 表里的记录 id 必须以 `stat.` 开头）。
- `l10n.text` 表例外：没有 `id` 字段，主键是 `key`（格式 `l10n.<来源域>.<来源记录 name>.<字段名>`，见 04 第 7.2 节）与 `locale` 的复合键。
- 主键规则：内容表用 `id`（domain 前缀 = 表名第一段）；跨 domain 的登记表（`found.event_catalog`、`found.input_action`、`l10n.text`）用 `key`，不做 domain 前缀检查（见 `core/foundation/data_registry/README.md`"主键规则"一节、`event_bus/schema/found.event_catalog.md`"与通用 id 规则的偏差"、`core/foundation/input_map/schema/found.input_action.md`"判断记录：登记表，主键字段名 key"）。`found.input_action` 记录的 id 域名是 `input`（不是表名首段 `found`），与 `found.event_catalog` 同理。

## 编码与格式

- UTF-8，无 BOM。
- 缩进 2 空格。
- 行尾 LF（不用 CRLF）。

## 与校验器的关系

`toolchain/validate_data.py` 读取本目录下的表做两道校验：第一道是本文件自己实现的骨架级通用检查（信封三键是否存在、`table` 是否等于文件名、`id`/`key` 格式是否合法，对每个数据根独立跑，不做跨根合并判定）；第二道调用 `toolchain/validator`（复用 `DataRegistry` + 全部模块已注册的 `IValidationRule`）做字段级/引用完整性/表达式/模块专属校验与跨根合并（04 第 5 节检查项清单是校验器的设计目标，实际校验范围以已注册的具体规则实现为准——例如技能/光环等嵌套结构内部的精确字段/类型/参数校验目前还有已知缺口，见 `architecture/落地计划/audit-5e779c6-20260907/foundation-rules.md` FR-05，不是每张表的每个嵌套字段都已有对应规则），详见 `toolchain/validate_data.py` 文件头注释。

判断记录：第二道校验一次性注册全部 L0～L5 模块的 `TableSchema`/`IValidationRule`（见 `toolchain/validator/Program.cs`）。ADR-0018 决策 3（校验装配入口）落地后，这段"汇总注册目录 + 可选规则接线参数 + 装配选项"逻辑已从 `toolchain/validator/Program.cs` 抽为核心库内的单一公开入口 `Presentation.Assembly.ContentValidationAssembly.Run`/`CreateRegistry`（`presentation/assembly/ContentValidationAssembly.cs`）：`toolchain/validator` 现在只做命令行参数解析 + 调用该入口 + 打印，编辑器基础套件（ADR-0018 决策 1/2 定义的独立消费方项目，随游戏走，不在本仓库）按同一入口装配"内容校验设置"面板，保证"编辑器里看到的红线 = 门禁会报的错"——两个消费方不允许各自维护第二份注册顺序。其中 `core/carriers/item` 的预算超标校验（`ItemBudgetValidationRule`）此前不区分数据根是否包含 item 域，只要注册了就会无条件要求全局默认预算曲线（`item.budget.default`）存在——单独校验 `data/_framework`（只有 `found.event_catalog`/`found.input_action` 两张纯登记表，不含任何 `item.*` 表）时会因此误报。加固J3 已改为：数据集里 `item.template` 一行都没有时该规则直接跳过（没有物品就没有预算可超标），只要数据集确实登记了 `item.template`，`item.budget.default` 缺失仍然照常报错。修复后 `data/_framework` 单独校验两道均可正常跑，不再需要 `--skip-dotnet` 规避（`python toolchain/validate_data.py --data-root data/_framework` → 0 错误）。默认调用（`_framework` + `_sample` 合并，或后续"`_framework` + 具体游戏数据"合并）同样两道校验都应 0 错误。

上面两道校验的是**数据行内容**（本目录下实际的 `.json` 表文件）；`toolchain/validator --schema-audit`（F3 新增，见 `toolchain/README.md`"元数据门禁"一节）校验的是**表结构声明本身**（`TableSchema`/`FieldSchema` 代码，不读取本目录任何数据文件），两者互不替代——字段描述缺失、复合字段未登记子结构等问题即使数据行本身完全合法也会被元数据门禁拦下。
