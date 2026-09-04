# `l10n.locale` / `l10n.text` 字段说明

对应 04_数据与内容管线.md 第 7.2 节"本地化"、第 1.1 节总索引 `l10n.locale`/`l10n.text` 两行、
01_分层与依赖.md L0 模块表 `localization` 行"主要数据表：`l10n.text`（按语言的文本表）、
`l10n.locale`（支持语言清单）"。

## 判断记录：从 `data_registry/core/BuiltinSchemas.cs` 迁入

`BuiltinSchemas.cs` 原本临时持有这两张表的 `TableSchema`（其类型注释早已预告"届时应从本类
移除对应登记...归各模块目录（如 l10n 归 T1-7 本地化模块）"）。本任务把两张表的
`TableSchema` 原样搬到本模块（`contracts/L10nSchemas.cs` 的 `L10nSchemas.Locale`/
`L10nSchemas.Text`），字段定义、主键规则、`IsRegistryTable`/`HasLocaleCompositeKey` 取值均
未改变；`data_registry` 侧仅保留一处指向本文件的注释。

## `l10n.locale`

内容表，主键 `id`（domain 前缀须为 `l10n`，如 `l10n.locale.zh_cn`）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | 语言 id，如 `l10n.locale.zh_cn` |
| `fallback` | Optional&lt;Id&gt; | 否 | 回退语言 id，指向另一条 `l10n.locale` 记录；可为空 |
| `is_default` | Bool | 是 | 是否为默认语言；全表必须恰好一条为 true（`L10nHost` 构造期校验），回退链不得成环（含自我回退） |

## `l10n.text`

登记表，主键是 `key` + `locale` 的复合键（`TableSchema.HasLocaleCompositeKey`），不做 domain
前缀检查（`key` 的格式规范见下）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | String | 是 | 文本键，格式 `l10n.<来源域>.<来源记录 name>.<字段名>`，例如 `l10n.quest.deliver_letter.title` |
| `locale` | Reference → `l10n.locale` | 是 | 语言 id |
| `text` | String | 是 | 正文，支持 `{变量名}` 占位（见下"变量代入"） |

## 变量代入

正文中以 `{变量名}` 占位，运行时由调用方提供 `IReadOnlyDictionary<string,string>` 做替换；
不支持嵌套；变量缺失时原样保留占位符并记警告（`L10nOptions.WarnOnMissingVar`，默认开启）。

## 缺失文案回退策略

`IL10nHost.Text` 查找顺序：当前语言 → 沿 `fallback` 链逐级查找 → 默认语言（即便当前语言的
回退链未显式指向默认语言，默认语言也是最终兜底）→ 仍未找到则按
`L10nOptions.MissingKeyPolicy`（`ReturnKey`/`ReturnEmpty`/`ReturnMarker`，默认 `ReturnKey`）
返回并记警告。
