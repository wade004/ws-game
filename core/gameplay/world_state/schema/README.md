# `world.flag_schema` 数据表字段

对应 [`WorldStateSchemas.cs`](WorldStateSchemas.cs) 的 `TableSchema` 声明；字段语义详见
[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单、
[05_玩法层_角色单位技能系统.md](../../../../architecture/05_玩法层_角色单位技能系统.md) 第 8 节。
本表**不参与运行期加载**（本模块不实现任何 `IValidationRule`，见 `../README.md` 判断记录"该表只
文档化"），只声明结构供内容作者按命名空间登记标志含义。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `world.<命名空间...>`：标志的命名空间前缀或具体标志键 |
| `kind` | Enum(`bool`\|`int`\|`number`\|`string`\|`id`) | 是 | 该前缀/标志键下取值的 Expr 标量类型，对应 `IWorldState.Set` 的 `ExprValueKind` |
| `description` | String | 是 | 该命名空间/标志的含义说明（纯供内容作者与人工评审参考） |
| `allowed_values` | Array | 否 | 该标志允许的取值枚举；缺省表示不限制，本模块不对其做任何运行期强制 |

## 子结构登记表（ADR-0019 / F1b）

`allowed_values` **未登记 `Item` 子结构**，保持登记前行为不变（只检查"存在且是数组"）。判断记录：

1. **元素类型随同记录 `kind` 取值动态变化，契约表达不了这种"判别字段在父级同层"的依赖**：
   `allowed_values` 的元素理论上应与同记录 `kind` 字段取值同型（`bool`/`int`/`number`/`string`/
   `id` 之一的 JSON 标量），但判别字段 `kind` 是 `allowed_values` 的**父级同层字段**，不在数组
   元素内部——`FieldSchema.Item` 只能声明单一 `FieldKind`（选不出"跟随 kind 变化"的类型）；
   `FieldSchema.Variants` 的判别字段必须与其所属 `Object` 同层（见 `DataRegistry.ValidateVariantObject`
   实现：判别字段从 `Variants` 所在的同一个 JSON 对象里取），`allowed_values` 是 Array 不是
   Object，且判别字段根本不在数组元素内部，两种机制都不适用。这不是 ADR-0019 首批范围内"Map 型
   对象"（任务书任务书第 5 条：键为 id/任意字符串、值同构）的情形——本字段是"元素类型依赖同记录
   另一个字段取值"的动态定型，技术原因不同，但同属"本轮契约表达力覆盖不到、需要后续单独扩展"的
   情形，一并在此记录、供上游汇总。
2. **无运行时解析代码可作登记依据**：`core/gameplay/world_state.WorldState.ValidateAgainstSchema`
   只读取 `kind` 做类型匹配，从未读取 `allowed_values`；本表本身也不参与运行期加载（见类型注释
   "仅作文档化 schema"）。ADR-0019 / 04 第 3.2 节"以运行时解析代码为唯一依据登记子结构"——本字段
   没有任何运行时读取代码，因此本轮不登记，不属于"遗漏"。
3. **后续扩展方向（仅记录，不在本轮实现）**：若后续确有需要机器校验 `allowed_values` 内容与
   `kind` 一致，可行路径包括——(a) 扩展 `FieldSchema`/`VariantSchema` 支持"数组元素类型引用父级
   同层字段"这类跨字段判别（比 `Variants` 更通用的机制）；(b) 由 `world.flag_schema` 专属的
   `IValidationRule`（若未来该表参与运行期加载、需要真正校验）直接读取同记录 `kind` 字段做业务
   判断，不依赖 `FieldSchema` 层的静态登记。两条路径均超出 ADR-0019 首批范围。

见 `tests/WorldStateSchemaCoverageTests.cs`（锁定"数组元素任意形状混杂不报错，字段本身非数组仍报
`field_type`"这一当前行为）。
