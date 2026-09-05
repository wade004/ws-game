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
    id、`AppStateMachineConfig.Default()` 等价于该表默认数据行），但截至本次改动两张表尚未有
    实际 JSON 数据文件落地（对应模块目前只提供内存态默认配置，未接入数据注册表读取，见
    `core/foundation/app_lifecycle/schema/found.game_state.md`"本模块不做什么"一节）——一旦所属
    模块把它们接成真正从数据表加载，新增的数据文件应直接放 `data/_framework/found/`，不要放
    `data/_sample/`。
  - 判断记录（`arch.power_type`）：`core/rules/common/contracts/WellKnownPowers.cs` 硬编码
    `arch.power.health` 为固定常量（非可配置默认值，与 `stat.definition` 等表被
    `CombatOptions`/`MovementOptions` 之类"可配置默认值"引用的情况不同——后者游戏层可以整体
    改配置指向别的 id，不构成"框架代码只认这一个固定值"），按判定规则本应算框架级；但该表
    当前与 `data/_sample` 下的 `stat.definition`/`prog.*` 等示例数据深度耦合（
    `core/numbers/tests/L1SampleDataTests.cs` 端到端断言 `arch.power.health` 通过
    `max_source: {kind: stat, stat: stat.stamina}` 引用示例属性表算出的具体数值），拆分会破坏
    该测试且该测试不在本次改动的写入范围内，故本次**保留 `arch.power_type` 整表在
    `data/_sample`**，作为已知的判断记录/遗留项如实记录，不强行拆分。
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

- 不同根贡献的记录按主键并集合并；同一主键在两个根间重复，判定为阻断错误（消息中点出两个根各自
  的位置）。
- 同一张表在不同根的信封 `schema_version` 不一致，判定为阻断错误（消息中点出两个根各自的位置），
  不再合并该表。
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

`toolchain/validate_data.py` 读取本目录下的表做两道校验：第一道是本文件自己实现的骨架级通用检查（信封三键是否存在、`table` 是否等于文件名、`id`/`key` 格式是否合法，对每个数据根独立跑，不做跨根合并判定）；第二道调用 `toolchain/validator`（复用 `DataRegistry` + 全部模块 `IValidationRule`）做完整的字段级/引用完整性/表达式/模块专属校验与跨根合并（04 第 5 节检查项清单），详见 `toolchain/validate_data.py` 文件头注释。

判断记录：第二道校验一次性注册全部 L0～L5 模块的 `TableSchema`/`IValidationRule`（见 `toolchain/validator/Program.cs`），其中部分规则（如 `core/carriers/item` 的预算超标校验）不区分数据根是否完整，只要注册了就会无条件要求某些全局默认表存在——这类规则设计上假定"跑校验的数据集是一份完整的游戏/示例数据"，不是本次多根改动引入的行为。因此单独校验 `data/_framework`（只有 `found.event_catalog`/`found.input_action` 两张纯登记表，天然不构成"完整数据集"）时，第二道校验会因为这类全局规则报错，属预期现象；要验证"框架级数据表自身信封/主键/id 格式自洽"，用 `--skip-dotnet` 只跑第一道骨架检查即可（`python toolchain/validate_data.py --data-root data/_framework --skip-dotnet` → 0 错误）。默认调用（`_framework` + `_sample` 合并，或后续"`_framework` + 具体游戏数据"合并）不受此限制，两道校验都应 0 错误。
