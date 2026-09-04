# faction 数据表字段说明

对应 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单：
`fac.faction`"阵营定义：id、默认敌友矩阵行"、`fac.reaction_matrix`"阵营对阵营的默认反应
（敌对/中立/友好）"。**判断记录：04 未给出这两张表的字段表**，以下字段为实现期按任务书 T2-3
给出的最小字段集补录，待 04 正式登记时以 04 为准同步本文件。

## `fac.faction`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `fac.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `default_reaction` | Enum（`hostile`\|`neutral`\|`friendly`） | 是 | 与未在 `fac.reaction_matrix` 中显式登记的阵营之间的默认关系 |

## `fac.reaction_matrix`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `fac.reaction.<name>` |
| `from` | Reference→`fac.faction` | 是 | 关系的发起方 |
| `to` | Reference→`fac.faction` | 是 | 关系的接收方 |
| `reaction` | Enum（`hostile`\|`neutral`\|`friendly`） | 是 | `from` 对 `to` 的显式反应 |

矩阵不要求对称：同时需要 `(A,B)`、`(B,A)` 两个方向的显式关系时要登记两行；只登记一行时，
未登记方向按其 `from` 端的 `default_reaction` 回退（见 `IFactionMatrix.GetReaction` 判断记录）。

## 判断记录

- **枚举取值固定为三档**：`hostile`/`neutral`/`friendly`，对应 00 第 3 节"缩到小矩阵（敌对/
  中立/友好等有限枚举）"；未预留第四档，若未来某款游戏确有需要（如"崇拜"），应作为该游戏层的
  独立扩展而非改动本模块的固定枚举（呼应"框架不为具体游戏定制"的原则）。
- **矩阵条目主键 `id` 而不是复合键 `(from,to)`**：与 04 记录主键约定一致（内容表主键固定为
  `id`），`(from,to)` 唯一性不做数据校验层面的强制去重——若同一 `(from,to)` 出现两条不同
  `reaction` 的登记行，`FactionMatrix` 构造期按 `GetAll` 返回顺序后者覆盖前者（`Dictionary`
  索引器赋值语义），不视为错误；后续若需要严格去重，应作为独立的 `IValidationRule` 补充。
