# L0 基础层 · save_system 存档系统

职责：存档槽读写、版本迁移、自动存档触发点判断、设置文件存储（`SaveSystem`、`Persistable`
契约，见 [01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `save_system` 行、
[10_存档与持久化.md](../../../architecture/10_存档与持久化.md) 全文）。拍板决策 9（见
[adr/0009-存档是唯一持久化.md](../../../architecture/adr/0009-存档是唯一持久化.md)）确立存档是
本架构唯一的持久化机制；本模块是该机制唯一的实现入口，具体读写落盘经引擎适配层的
`IFileSystem`（见 [02_引擎适配层.md](../../../architecture/02_引擎适配层.md) 第 1.6 节）完成。

依赖：`core/foundation/common`（`Id`、`Common.Json`）、`core/foundation/engine_adapter`
（`IFileSystem`）、`core/foundation/event_bus`（`IEventBus`、`IEvent`）、
`core/foundation/rng`（`IRngHost`、`RngStreamState`，供 `RngStreamsPersistable`）。不引用任何
更上层模块，不引用任何具体引擎适配层实现（测试用 `adapters/stub` 的 `StubEngine`）。

不负责什么：

- 不实现除 `rng.stream_states` 外的任何具体存档段——`world_state_flags`、
  `player.progression` 等段的 `IPersistable` 实现属于各自所在模块的职责（Progression、
  Inventory、QuestState、WorldState……），本模块只提供契约、固定顺序常量
  （`SaveSections`）与汇总/迁移/落盘的编排逻辑。
- 不解释业务字段含义：`display_summary` 的键、`Args`（回放意图参数）的结构等一律是
  游戏层/上层模块的约定，本模块只搬运 `JsonValue`。
- 不实现确定性回放：`IReplayRecorder`/`IReplayPlayer`/`ReplayData`/`ReplayInputRecord`
  只建立契约与数据类型（见 `contracts/Replay.cs`），具体实现留给 T1-9（10 第 8 节）。
- 不读取系统时间、不使用反射、不使用多线程/锁、不直接触碰文件路径以外的任何引擎/平台 API。

## 目录

```
save_system/
  README.md
  contracts/
    IPersistable.cs        SaveSections.cs         SaveMeta.cs
    SaveRequest.cs          SaveResult.cs            LoadResult.cs
    SaveSlotInfo.cs          ISaveMigration.cs        ISaveSystem.cs
    SaveSystemOptions.cs     Events.cs                ISettingsStore.cs
    SettingsStoreOptions.cs  Replay.cs                ISaveDiagnostics.cs
  core/
    SaveSystem.cs            SettingsStore.cs
    InMemorySaveDiagnostics.cs   RngStreamsPersistable.cs
  schema/
    save_slot_meta.md
  tests/
    SaveSystemTests.cs
```

## 文档格式与存储位置

存档文档信封（JSON）固定为：

```json
{
  "save_version": 3,
  "sections": {
    "meta": { "save_version": 3, "slot_id": "slot.demo", "created_at": "...", "updated_at": "...", "game_id": "game.demo" },
    "world_state_flags": { "...": "..." },
    "...": "..."
  }
}
```

- `save_version`（信封顶层）是迁移链据以判断的权威版本号；`sections.meta.save_version`
  是 10 第 2.1 节字段表要求的 meta 段字段，写入时与顶层保持一致（判断记录见下）。
- 落盘路径：`<IFileSystem.GetUserDataDir()>/<SaveSystemOptions.SavesDirName>/<slotId.Value>.json`
  （默认 `SavesDirName = "saves"`）；`slotId` 满足 `Id` 格式（`^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`），
  直接作文件名是安全的（不含路径分隔符、不含引擎/平台保留字符）。
- 备份路径：同目录下 `<slotId.Value>.bakN.json`，`N` 从 1 到 `SaveSystemOptions.BackupCount`
  （默认 1）。备份用"读旧内容、原子写到备份路径"完成，不使用重命名（`IFileSystem` 未提供
  该能力，也不应绕过 `writeTextAtomic` 语义多步写入——落地方案与分阶段计划.md T1-6 禁止事项）。
- 写入正式文件只调用一次 `WriteTextAtomic`；失败时旧正式文件内容保持不变（依赖该接口的
  原子语义），返回 `SaveFailureReason.WriteFailed`，测试 `Save_WriteFails_ReturnsWriteFailed_AndOldSaveStillLoadable`
  用 `StubFileSystem.FailNextWrite()` 验证这一点。
- 设置文件与存档槽完全独立：`<GetUserDataDir()>/<SettingsStoreOptions.FileName>`（默认
  `settings.json`），不落在 `saves/` 子目录下，删除任意存档槽不影响它。

## Persistable 汇总顺序

`SaveSections.KnownOrder` 给出 10 第 3 节"SaveSystem 汇总顺序"的固定全序（`meta` 之后依次
`world_state_flags` → `player.progression` → `player.archetype` → `player.inventory` →
`player.equipment` → `player.known_skills` → `player.skill_bindings` → `player.quest_state` →
`player.achievement_state` → `player.currencies` → `world.current_map_id` →
`world.current_position` → `rng.stream_states`）。写入与读取都按该顺序处理已注册/已存在的段；
**未列入 `KnownOrder` 的自定义段在这些段之后，按其 key 的序数（ordinal, `StringComparer.Ordinal`）
排序处理**——测试 `Load_CallsPersistablesInSaveSectionsOrder_CustomSectionsLast` 验证。

`meta` 段永远由 `SaveSystem` 自己读写，不经 `IPersistable`：`RegisterPersistable` 对
`SectionKey == "meta"` 直接抛 `ArgumentException`。

写入时只序列化"已注册"的段；读取时只对"文档中存在且已注册"的段调用 `Load`——文档存在但
未注册对应 `IPersistable` 的段记一条警告并跳过（不视为错误，允许"新版本运行时读一份携带
旧版本已废弃段"的存档）；已注册但文档缺失该段（例如从更早版本的存档迁移而来，迁移函数
选择保留旧段名不动，但当前运行时的持久化实现已重命名）同样记警告跳过，**不会**用
`JsonNull` 调用 `Load`（判断记录见下）。

## Save / Load 失败语义

- `Save`：任一已注册段 `Save()` 抛异常 → 整体中止，不写入任何文件，返回
  `PersistableThrew`；目标槽不存在且已存在槽数已达 `MaxSlots`（`MaxSlots > 0` 时）→
  `SlotLimitReached`（覆盖已存在的槽不受此限制）；`WriteTextAtomic` 返回 false → `WriteFailed`。
- `Load`：目标槽正式文件与全部备份均不存在 → `NotFound`；均无法解析为合法信封（缺
  `save_version`/`sections` 字段，或 JSON 语法错误）→ `Corrupted`，原始文件不被覆盖或删除；
  文档版本高于当前运行时版本 → `MigrationFailed`（"不承诺向前兼容"，10 第 5 节）；
  文档版本低于当前版本但迁移链缺少衔接版本、或某个迁移函数抛异常 → `MigrationFailed`；
  某个已注册段 `Load()` 抛异常 → `PersistableThrew`，此前已成功 `Load` 的段**不回滚**
  （由调用方决定如何处理这种"部分加载"状态，例如整体回到主菜单重新读档）。

## RngStreamsPersistable

`core/RngStreamsPersistable.cs` 是 `rng.stream_states` 段的 `IPersistable` 实现：`Save()`
遍历 `IRngHost.Streams`（已按 `Id` 序数排列）写出每条流的 `RngStreamState.ToString()` 文本；
`Load()` 逐条 `Id.TryParse` + `RngStreamState.TryParse` 后调用 `IRngHost.SetStreamState`——
目标流按 `IRngHost` 的懒创建语义按需创建，不要求流在读档前已存在。

## 自动存档触发点

`ISaveSystem.ShouldAutoSave(AutoSaveTrigger)` 只做 `SaveSystemOptions.AutoSave` 策略判断
（10 第 6 节默认值：`SavePoint` 开、`MapSwitch` 关、`QuestComplete` 开），不订阅任何事件、
不产生副作用。把某个触发点接到具体游戏时机（场景路由完成地图切换、任务状态机进入完成
节点……）并在返回 true 时实际调用 `Save`，是上层（`scene_router`、玩法层任务系统等）的
职责——机制到此为止，接线是游戏层的事。

## 设置文件

`SettingsStore`（`ISettingsStore`）落盘信封 `{ "settings_version": N, "settings": {...} }`；
`Load()` 对外只返回 `settings` 内容本身，`settings_version` 信封字段不暴露给调用方（调用方
只需要知道"这是当前版本的设置"，不需要关心版本号）。设置文件不存在时 `Load()` 返回空对象，
不视为错误；文件存在但版本号低于 `SettingsStoreOptions.CurrentVersion` 时按注册的迁移链
"尽力迁移"——链条不完整或某一步抛异常时，直接使用迁移到此为止的结果，**不抛异常、不阻断**
（判断记录见下）。这与 `ISaveSystem.Load` 对存档槽的"迁移失败即 `MigrationFailed`、拒绝继续"
是刻意不同的两种策略：存档槽迁移失败必须让玩家明确知道（存档损坏提示流程），设置文件
迁移失败不应该阻止游戏启动（退化为部分/默认设置远好于直接崩溃或拒绝进游戏）。

## 回放（`IReplayRecorder`/`IReplayPlayer`）

本任务只建立契约与 `ReplayData`/`ReplayInputRecord` 两个数据类型（10 第 8 节），不提供实现。
`IReplayPlayer.StepTo` 的返回类型在 10 伪代码里是 `WorldSnapshot`——该类型属于世界模型/
`sim_loop` 范畴，本模块（L0 `save_system`）不定义它，暂用 `object` 占位；T1-9 实现回放播放器
时按需调整本接口签名或在更上层重新声明。

## 诊断

`ISaveDiagnostics`/`InMemorySaveDiagnostics` 与 event_bus 的 `IEventDiagnostics`、hook_registry
的 `IHookDiagnostics` 同一惯例：`Warn(message)`/`Error(message, exception?)`，默认实现只把
消息收集到内存列表，不依赖任何引擎适配层接口。备份回退、损坏文件跳过、缺失段跳过、迁移
失败等都记一条诊断，不只是静默处理。

## 判断记录（文档歧义处）

1. **meta 段 `save_version` 与信封顶层 `save_version` 的关系**：10 第 2.1 节把 `save_version`
   列为 meta 段字段之一，但第 4、5 节讨论"存档读出 save_version"、"迁移链据此判断"时并未
   区分是文档顶层还是 meta 段内。任务书"core:"一节已拍板信封形态为
   `{ save_version, sections }`（顶层携带该字段），本实现额外让 `sections.meta.save_version`
   镜像同一个值（满足 10 第 2.1 节字段表"meta 段含 save_version 字段"的字面要求），但迁移
   链只认信封顶层字段，`ParseMeta` 读到的 `SaveMeta.SaveVersion` 只用于展示，不驱动任何逻辑。
2. **`ISaveMigration.Migrate` 的入参/返回形状**：10 第 5 节伪代码 `migrate(data: Map<String,
   Any>): Map<String, Any>` 没有说明 `data` 是整份文档还是某个段。本实现选择整份信封
   `{ save_version, sections }`，因为 10 文档的版本号语义是"存档 schema 版本"这一整体概念，
   没有按段拆分版本号；`SaveSystem` 在调用后强制把返回文档的 `save_version` 覆盖为迁移函数
   声明的 `ToVersion`，迁移函数实现只需要关心 `sections` 内容变化，不必自己正确维护该字段。
3. **已注册但文档缺失的段，是否该用 `Load(JsonNull)` 调用一次**：任务书"拍板：缺失段跳过
   不调用，并记诊断警告"——已按此实现，`IPersistable.Load` 的实现方因此永远不需要处理
   "收到 JsonNull"的情况（除非该段自己选择把 `Save()` 的结果设为 `JsonNull`，那种情况下
   文档里该段是存在的，只是值为 null，会正常触发 `Load(JsonNull.Instance)` 调用）。
4. **`DeleteSlot` 的返回值含义**：10 文档未定义该方法返回值语义。本实现选择"删除前该槽
   是否存在"（即本次调用是否真正删除了什么），而非"是否所有文件都成功删除"——因为
   `IFileSystem.DeleteFile` 对不存在的备份文件返回 false 是正常情况（`BackupCount` 大于
   实际已产生的备份数量时），不应据此判定整个 `DeleteSlot` 失败。
5. **`RegisterPersistable`/`RegisterMigration` 的重复登记处理**：10 文档未讨论。参照
   `hook_registry.DeclareHookPoint`、`event_bus.EventCatalog.FromDefinitions` 的既有惯例
   （重复 key 直接抛 `InvalidOperationException`），本模块对重复 `SectionKey`/
   `FromVersion` 采用同一策略，不做"后者覆盖前者"的静默处理。
6. **`ListSlots`/`CountSlots`（`MaxSlots` 判断）对嵌套路径与备份文件的过滤**：`IFileSystem.
   ListFiles` 按 02 文档只约定"递归列举、路径相对 dirPath"，未约定存档目录下是否可能出现
   子目录或非存档文件。本实现保守处理：只认直接位于存档目录下（结果中不含 `/`）、以
   `.json` 结尾、且文件名不匹配 `<slot>.bakN.json` 备份命名模式的文件为一个存档槽；其余一律
   忽略，不计入槽数、不出现在 `ListSlots` 结果里。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 存档模型分段结构与固定汇总/加载顺序 | 是 | `display_summary` 等游戏自定义摘要字段的具体内容 |
| `Persistable` 契约与 `SaveSystem` 编排（汇总/迁移/落盘/事件） | 是 | 各游戏特有模块实现自己的 `Save`/`Load`（`world_state_flags`、`player.*`、`world.*` 等段） |
| 原子写入、备份轮转、损坏回退 | 是（经 `IFileSystem`） | 无 |
| 迁移函数链机制 | 是 | 具体每次 schema 变更对应的 `ISaveMigration` 实现 |
| 自动存档触发点判断机制 | 是 | 具体启用哪些触发点、把触发点接到游戏时机 |
| 确定性回放契约 | 是（契约与数据类型） | 是否把回放暴露为玩家可见功能；回放实现本身（T1-9） |
| 设置文件存储与迁移 | 是 | 具体设置字段结构与含义 |
