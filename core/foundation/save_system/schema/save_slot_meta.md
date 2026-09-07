# save_slot_meta —— 存档文档 `sections.meta` 段

> 不是 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 总索引里登记的内容表，
> 而是存档文档（见 [10_存档与持久化.md](../../../../architecture/10_存档与持久化.md) 第 2.1 节）里
> `sections.meta` 这一段的字段说明（对应 [01_分层与依赖.md](../../../../architecture/01_分层与依赖.md)
> L0 模块表 `save_system` 行"内容表：`save_slot_meta`（见 10，不属于 04 总索引）"）。
> 供存档槽 UI 展示摘要，不参与模拟；由 `SaveSystem`（`core/SaveSystem.cs`）自身读写，
> 不经 `IPersistable`（见 `contracts/SaveSections.cs` 中 `Meta` 常量注释）。

## 字段表

| 字段（JSON key） | 类型 | 必填 | 对应 C# 类型 | 说明 |
|---|---|---|---|---|
| `save_version` | Int | 是 | `SaveMeta.SaveVersion` | 存档 schema 版本号；写入时等于运行时 `ISaveSystem.CurrentSaveVersion`，与文档顶层 `save_version` 字段保持一致 |
| `slot_id` | String（Id 文本） | 是 | `SaveMeta.SlotId` | 存档槽标识 |
| `created_at` | String | 是 | `SaveMeta.CreatedAt` | 创建时间文本，格式由调用方约定；覆盖已存在槽时保留原值 |
| `updated_at` | String | 是 | `SaveMeta.UpdatedAt` | 最近一次写入时间文本 |
| `play_time_seconds` | Int | 否 | `SaveMeta.PlayTimeSeconds` | 累计游玩时长；缺省时字段整体不出现 |
| `display_summary` | Object（String → String） | 否 | `SaveMeta.DisplaySummary` | 游戏层自定义摘要字段，键由游戏层约定；缺省或空时字段整体不出现 |
| `game_id` | String（Id 文本） | 是 | `SaveMeta.GameId` | 本存档所属的具体游戏 |
| `difficulty_id` | String（Id 文本） | 否 | `SaveMeta.DifficultyId` | 难度定义引用；缺省时字段整体不出现 |

## 往返约定

- 读取（`SaveSystem` 内部 `ParseMeta`）时，缺失任一必填字段或字段类型不符，判定整份存档
  `Corrupted`（见 10 第 5 节损坏存档处理）。
- `display_summary` 的值只接受 JSON 字符串；出现非字符串值也判定为格式非法。
- 本段不受版本迁移链约束以外的额外处理——它和其余段一样，若某次 schema 变更需要调整
  本段字段，走 [12_扩展与变更流程.md](../../../../architecture/12_扩展与变更流程.md) 登记一个
  `ISaveMigration`。
