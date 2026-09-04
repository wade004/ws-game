# 数据目录约定

本目录存放全部内容数据表（"数据即内容"，见 [`../architecture/04_数据与内容管线.md`](../architecture/04_数据与内容管线.md)）。字段规范、id 规范、schema 版本与迁移、校验器检查项均以该文档为唯一权威来源；本文件只约定文件级组织方式，不重复定义字段。

## 目录结构

```
data/<game_or_sample>/<domain>/<table>.json
```

- `<game_or_sample>`：具体游戏的数据放各自游戏仓库自己的 `data/<game>/` 下（游戏代号由游戏仓库自己决定），本框架仓库只保留 `data/_sample/`，仅供 `DataRegistry` 冒烟测试与校验器自测使用，不代表任何真实游戏内容。
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

## 编码与格式

- UTF-8，无 BOM。
- 缩进 2 空格。
- 行尾 LF（不用 CRLF）。

## 与校验器的关系

`toolchain/validate_data.py` 读取本目录下的表做校验；当前阶段（T0-7/T0-8）只做骨架级通用检查（信封三键是否存在、`table` 是否等于文件名、`id`/`key` 格式是否合法），04 第 5 节校验器检查项清单中的引用完整性、枚举合法、表达式可解析、外形映射存在等领域规则在后续阶段逐项加入，详见 `toolchain/validate_data.py` 文件头注释。
