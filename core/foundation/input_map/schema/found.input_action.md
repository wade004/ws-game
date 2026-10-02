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
| `class` | Enum（`move`\|`attack`\|`skill`\|`dodge`\|`interact`\|`item`\|`menu`） | 否 | 动作类别（手感设计/01 第 2.1 节）。**缺省 = 不进输入缓冲**（既有动作行为完全不变）；仅 `kind=button` 且声明了类别（非 `move`）的动作入缓冲。同类别的缓冲记录互相覆盖 |
| `buffer_ms` | Number（0..1000，毫秒） | 否 | 该动作自己的缓冲窗口；缺省取行动者手感档案输入组的 `buffer_ms`。0 = 只在按下当 tick 有效。毫秒经 `FeelCalibration` 按模拟步长换算为 tick |
| `priority` | Int | 否 | 缓冲替换/取用优先级（大者优先）；缺省取类别缺省：dodge 40 > attack/skill 30 > item 20 > interact 10 > 其余 0 |
| `hold_threshold_ms` | Number（>0，毫秒） | 否 | 按住阈值；声明后按下先成为"按住待定"（不可消费），抬起时按持续时长判点按/按住。缺省：attack/skill 类取档案的 `hold_threshold_ms`（若有），其余类别不区分按住 |
| `repeat_policy` | Enum（`refresh`\|`ignore`） | 否 | 同一动作在槽内未过期时再次按下：`refresh`（缺省）刷新过期时刻与方向快照；`ignore` 保持原记录 |
| `face_on_accept` | Bool | 否 | 取用成功时是否转向按下瞬间的方向快照；缺省取类别缺省（attack/skill/dodge 为真） |
| `grace_conditions` | Array of Id | 否 | 该动作的宽限条件 id 列表（引用 `found.grace_condition`）；缺省空 = 无附加宽限条件。机制见本模块 README"宽限窗口" |

| `skill_slot` | String | 否 | 缓冲接受后要施放的技能所在的技能绑定槽位名（与 `SkillBindingHost` 的 slot 同名，如 `slot_0`）。缺省 = 本动作不映射技能，生产装配的缓冲出口不对它发 `cast` 意图（手感落地 S10 新增） |
| `control_space` | Enum（`world`\|`camera_relative`） | 否 | 轴动作的控制空间（仅 `axis2d`）：`world`（缺省）轴值原样当世界方向；`camera_relative` 轴值按当前相机偏航换算成世界方向（摇杆上 = 相机前方在地面上的投影，旋转不改模长）。要求宿主给输入映射配相机朝向查询（`ICameraOrientation`），没配则在声明动作集时报错，不静默当成偏航 0 |

新增的 7 个字段（手感落地 S1）加 `skill_slot`（S10）均为可选加法字段：旧数据行不写它们时，`ActionDefinition` 的行为与此前逐项相同（`Class` 为空 ⇒ 不进缓冲）。

## 示例

见 `data/_sample/found/found.input_action.json`：03 第 7 节给出的 7 条示例动作（Move/Confirm/
Cancel/Interact/OpenMenu/Pause/CameraAdjust），`description` 字段均注明"示例动作集，不构成
任何游戏的操作定论"，与该节文字"以下动作集仅为说明数据格式的示例"呼应。
