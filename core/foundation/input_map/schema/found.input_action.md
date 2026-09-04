# `found.input_action` 字段说明

对应 04_数据与内容管线.md 第 1.1 节总索引行"`found.input_action` 输入动作集定义（动作 id、
默认绑定、可重绑分组）"、第 7 节示例动作集、01_分层与依赖.md L0 模块表 `input_map` 行
"主要数据表：`found.input_action`（游戏层声明的动作集，见 04）"。

## 判断记录：登记表，主键字段名 `key`

`found.input_action` 表名 domain 前缀是 `found`，但记录本身的 id 域名是 `input`（04 第 2.2
节域名清单 `input` 行"输入动作"，示例 `input.action.primary_attack`）——与 `found.event_catalog`
同理，属于"`found` 表登记 `input` 域的记录"。若按一般内容表处理（主键字段名 `id`、domain 前缀
须等于表名首段），会与"记录 id 域名是 `input`"直接冲突。

拍板：本表按登记表处理——`TableSchema.IsRegistryTable = true`，主键字段名为 `key`（而不是
一般内容表的 `id`），不做 domain 前缀检查，与 `found.event_catalog`、`l10n.text` 同一惯例（见
`data/README.md`"记录主键"、`core/foundation/data_registry/README.md`"主键规则"）。字段里的
取值仍然是一个合法 `Id`（如 `input.action.move`），只是承载它的 JSON 字段名叫 `key`。

## 字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | Id | 是 | 动作 id，如 `input.action.move`（登记表主键字段，见上）|
| `kind` | Enum（`button`\|`axis1d`\|`axis2d`） | 是 | 动作类型：按下型 / 一维轴 / 二维轴，见 `ActionKind` |
| `default_bindings` | Array of String | 是 | 默认绑定字符串数组，语法见本模块 `README.md`"绑定字符串小语法"；数组内多条绑定之间是"或"关系 |
| `rebind_group` | String | 否 | 重绑分组，缺省视为 `"default"`；同组内的绑定互相独占（见 03 第 7 节"冲突检测"） |
| `description` | String | 否 | 说明文字 |

## 示例

见 `data/_sample/found/found.input_action.json`：03 第 7 节给出的 7 条示例动作（Move/Confirm/
Cancel/Interact/OpenMenu/Pause/CameraAdjust），`description` 字段均注明"示例动作集，不构成
任何游戏的操作定论"，与该节文字"以下动作集仅为说明数据格式的示例"呼应。
