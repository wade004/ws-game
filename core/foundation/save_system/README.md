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
- 不组装具体世界：确定性回放的契约、数据类型与默认实现（`IReplayRecorder`/`IReplayPlayer`/
  `ReplayRecorder`/`ReplayPlayer`/`ReplayData`/`ReplayInputRecord`/`WorldSnapshot`，见
  `contracts/Replay.cs`、`core/ReplayRecorder.cs`、`core/ReplayPlayer.cs`，T1-9）均已落地，但
  世界如何装配（阶段处理器、意图词汇表等具体游戏内容）由调用方经 `WorldFactory` 委托提供。
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
- 备份路径：独立子目录 `<SavesDir>/backups/<slotId.Value>.bakN.json`，`N` 从 1 到
  `SaveSystemOptions.BackupCount`（默认 1）。备份用"读旧内容、原子写到备份路径"完成，不使用
  重命名（`IFileSystem` 未提供该能力，也不应绕过 `writeTextAtomic` 语义多步写入——落地方案与
  分阶段计划.md T1-6 禁止事项）。**FND-01 收口（外部审核 `code-review.md`）：备份此前落在与
  正式槽文件相同的目录、相同 `.json` 后缀**——`<slot>.bakN.json` 与"槽 id 恰好长得像
  `<slot>.bakN`"的合法槽正式文件路径可能逐字节相同（例如槽 `slot.a.bak1` 的正式文件与槽
  `slot.a` 的第 1 份备份撞名），保存/删除其中一个会覆盖或删除另一个；`ListSlots`/内部槽计数
  还需要一个按文件名猜测"是不是备份"的启发式过滤，这个过滤本身又会把长得像备份的合法槽误判
  成备份而从列表隐藏。现改为独立子目录，备份路径与任何合法槽路径不可能重合，`ListSlots`/
  存档槽计数不再需要、也已移除那条按文件名猜测的过滤——`IFileSystem.ListFiles` 按约定递归
  列举、相对路径含 `/` 分隔层级（见 `engine_adapter/README.md`"IFileSystem"一节），两处已有的
  "跳过含 `/` 的相对路径"逻辑天然正确排除 `backups/` 目录下的全部文件，不需要额外改动。
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
`world.current_position` → `world.dropped_loot` → `world.vendor_stock` → `world.difficulty` →
`spawn_state`（以上四段为 10 第 3 节步骤 7a"世界附属段"）→ `sim.turn_state`（步骤 7b，
ADR-0013 离散时间模型，只在装配了离散模式时有内容）→ `rng.stream_states`）。写入与读取都按
该顺序处理已注册/已存在的段；**未列入 `KnownOrder` 的自定义段在这些段之后，按其 key 的序数
（ordinal, `StringComparer.Ordinal`）排序处理**——测试
`Load_CallsPersistablesInSaveSectionsOrder_CustomSectionsLast` 验证。（W2 收边补齐：上述 7a/7b
五个段此前未登记进 `KnownOrder`，落入自定义段分支按 key 序数排序，导致 `sim.turn_state` 实际
排在全部 7a 段之前，与 10 文档"7a 后 7b"的文字顺序不完全一致，见 A4 审计 F2；现已登记，顺序
与文档一致。）

`meta` 段永远由 `SaveSystem` 自己读写，不经 `IPersistable`：`RegisterPersistable` 对
`SectionKey == "meta"` 直接抛 `ArgumentException`。

写入时只序列化"已注册"的段；读取顺序按"全部已注册的 `IPersistable`"计算（与写入顺序同一套
`KnownOrder` + 自定义段序数排序规则），不再依赖当次读取的这份文档里实际有哪些 key。文档存在
但未注册对应 `IPersistable` 的段记一条警告并跳过（不视为错误，允许"新版本运行时读一份携带
旧版本已废弃段"的存档）；**已注册但文档缺失该段**（例如从更早版本的存档迁移而来，或该段本
就是本轮才新引入、旧存档产生于引入之前）**默认仍然会调用一次 `Load`，参数为 `JsonNull`**——
CR150-03 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：此前的实现按"文档里实际
存在哪些段"计算读取顺序，已注册但文档缺失的段完全不出现在这个顺序里，因此永远不会被调用
`Load`，对应模块当前已经积累的运行期状态（例如先交互过若干次、再读一份还没有这个段的旧档）
原样保留、不会被清空，污染读档后的世界与紧接着的下一次存档（外部审计真实 `SaveSystem.Save`
生成合法档案后只移除新段、`Load` 读取：`LoadStatus.Loaded`，但该模块的 pending 数量原样保留
而不是归零）。现在 `IPersistable` 新增默认接口方法 `KeepStateWhenSectionMissing`（默认
`false`）：默认值下，已注册但文档缺失的段视为收到一次 `Load(null)`，要求实现方清空/归零到
"从未发生过"的默认态；极少数段需要相反语义（文档缺该段时保留当前状态、完全不调用 `Load`）
可显式覆盖为 `true`（判断记录见下，取代原判断记录 3）。

## Save / Load 失败语义

- `Save`：任一已注册段 `Save()` 抛异常 → 整体中止，不写入任何文件，返回
  `PersistableThrew`；目标槽不存在且已存在槽数已达 `MaxSlots`（`MaxSlots > 0` 时）→
  `SlotLimitReached`（覆盖已存在的槽不受此限制）；`WriteTextAtomic` 返回 false → `WriteFailed`。
- `Load`：目标槽正式文件与全部备份均不存在 → `NotFound`；均无法解析为合法信封（缺
  `save_version`/`sections` 字段、字段类型不对，或 JSON 语法错误）→ `Corrupted`，原始文件不被
  覆盖或删除；文档版本高于当前运行时版本 → `MigrationFailed`（"不承诺向前兼容"，10 第 5 节）；
  文档版本低于当前版本但迁移链缺少衔接版本、某个迁移函数抛异常、或迁移链某一步/终点会越过
  当前运行时版本（FND-09 收口，见下）→ `MigrationFailed`；某个已注册段 `Load()` 抛异常 →
  `PersistableThrew`，此前已成功 `Load` 的段**会被按正向顺序（与正常读档同一顺序，CORE-180-02
  根治，见下）回滚到读档前的状态**（AUD-01 根治，见下，取代本段此前"不回滚，由调用方自行处理
  部分加载状态"的旧表述）。
- **FND-07 收口（外部审核 `code-review.md`）：正式文件与全部备份统一作为候选，按优先级
  （正式文件 → bak1 → bak2 → … → bak`<BackupCount>`）依次做完整信封校验，取第一个通过的。**
  此前只要正式文件不存在就立即返回 `NotFound`，从不尝试任何备份——"正式文件缺失但备份完好"
  这种本该可以恢复的场景被直接判定为槽不存在；同时信封校验此前只用 `ContainsKey` 判断
  `save_version`/`sections` 两个 key 是否存在，不检查值的类型，`{"save_version":1,
  "sections":null}` 这类"字段名齐全、内容是垃圾"的文档会被当成合法候选放行，若它恰好是正式
  文件，会在这里"成功"一次、从此不再尝试任何备份，直到 `Load` 更深处的 `sections` 类型检查才
  失败——但那时已经错过了本该被尝试的有效备份。现在两处一起收口：`TryParseEnvelope` 额外要求
  `save_version` 是数字、`sections` 是对象；一个候选"通过信封校验"即代表它是可以被继续处理的
  合法文档。`NotFound` 现在只在正式文件与全部备份**都不存在**（磁盘上一个候选都没有）时返回；
  存在至少一个候选但没有一个通过校验才是 `Corrupted`。`LoadResult.Status` 沿用既有的
  `LoadedFromBackup`（不新增枚举值）：正式文件缺失、或正式文件存在但未通过信封校验，只要有
  某个备份通过校验，都归为 `LoadedFromBackup`。
- **N15 收边补齐（外部审计 68c9bed）：`TryParseEnvelope` 的类型检查进一步加深，直接复用
  `Load` 后续实际会用到的两个校验方法本身（`TryGetInt`/`TryGetSectionsMeta`），不再手写一份
  平行的、可能再次悄悄变浅的判断。** FND-07 把校验加深到"`save_version` 是数字、`sections`
  是对象"后，仍然留了两个具体缺口：(1) `save_version` 只要求"是数字"，`1.5` 这类非整数版本号
  能通过，却会在 `Load` 的 `TryGetInt` 整数校验处判 `Corrupted`；(2) `sections` 只要求"是对象"，
  `{"save_version":1,"sections":{}}` 这类"sections 本身合法但缺失必填 `meta` 子段"的文档能
  通过，却会在 `Load` 的 `TryGetSectionsMeta` 校验处判 `Corrupted`。两种情形若恰好是正式文件，
  仍然会在 `ReadValidEnvelope` 这里"成功"一次、从此不再尝试任何备份，直到 `Load` 更深处才失败——
  与 FND-07 要解决的问题同一形状，只是校验深度不够。现在信封校验与 `Load` 后续两处早期校验
  使用同一套方法，不再依赖两处代码手工保持同步。本方法仍不校验 `meta` 内部字段（`game_id` 等，
  见 `ParseMeta`）——那一层可能因迁移链而在不同版本间有不同必填字段形状，留给 `Load` 迁移完成
  后再校验。
- **N16 收边补齐（外部审计 68c9bed）：`ReadValidEnvelope` 在正式文件与全部新布局备份都
  不可用后，额外按精确路径只读尝试旧顶层布局遗留的备份（`<SavesDir>/<slotId>.bakN.json`，
  FND-01 之前的布局，与正式槽同目录）。** 只探测这几个确定的路径，不做任何目录扫描，找到且能
  通过信封校验时视为 `LoadedFromBackup`，并顺手把内容复制一份到新布局路径（仅当新路径尚无同
  编号备份时才写，不覆盖）；旧顶层文件本身不删除、不移动。**判断记录：为什么不做"扫描顶层目录、
  按文件名批量迁移/删除"**——那种做法与 FND-01 已经明确建立、并被回归测试
  （`SlotIdLooksLikeBackupFileName_IsIndependentFromRealBackup_ListedLoadedAndDeletedCorrectly`）
  锁定的硬约束冲突："顶层目录里任何 `<x>.json` 都可能是一个货真价实、与任何备份无关的正式槽"——
  按文件名模式猜测哪些文件是"旧备份"并据此移动/删除，无法与"这就是一个真实正式槽"的情形区分，
  会造成真实的数据损坏（本任务前一版实现确实在该回归测试上复现了这个问题）。因此本次修复只做
  只读、按需、精确路径的兜底恢复，不做批量迁移；`ListSlots`/内部槽计数/`DeleteSlot` 的行为完全
  不受影响，旧布局备份也**不会**因此被排除在配额计数之外——这一点在当前"槽 id 允许长得像备份
  文件名"的硬约束下无法安全达成，是本次收边的已知局限，不是遗漏。

- **C01 收口（外部审计 7e63d66 第四轮，P1，成立）：`TryReadLegacyBackup` 的精确路径
  `<SavesDir>/<slotId>.bakN.json` 核对信封内 `meta.slot_id` 与请求槽名是否一致，不匹配即不算
  候选。** N16 收边补齐时只做"这个精确路径下有没有能通过信封校验的内容"这一层判断，没有考虑到
  这个精确路径本身也可能是另一个货真价实、id 长得像 `<slotId>.bakN` 的正式槽（FND-01 判断记录
  早已明确这种命名碰撞是允许的合法槽名，例如槽 `slot.a.bak1` 自己的正式文件路径恰好等于槽
  `slot.a` 的这个旧顶层备份精确路径）——此前只要该路径下内容能通过信封校验就无条件当作
  `slotId` 的备份返回，会把另一个独立正式槽的存档内容跨槽"借"给请求的 `slotId`（真实 Runtime
  console 复现：`Load("slot.a")` 在 `slot.a` 从未存过、也没有新布局备份的前提下，被
  `slot.a.bak1.json`——即槽 `slot.a.bak1` 自己的正式文件——命中并返回 `LoadedFromBackup`）。
  现在核对信封内 `sections.meta.slot_id`：显式声明且与请求槽名不同即判定"属于另一个槽"，跳过
  （既不当备份用，也不计入候选存在信号，避免把纯属路径命名巧合的文件转化成 `Corrupted` 判定
  信号）；`slot_id` 缺失（早于该字段引入的真正旧存档）视为无法反证身份，按原语义放行——见
  `SaveSystem.LegacyCandidateMatchesSlot`。验收：`SaveSystemTests.
  Load_RequestedSlotHasNoOwnBackup_ButPathCollidesWithAnotherRealSlotsFormalFile_ReturnsNotFound_NoCrossSlotRead`
  （`Load("slot.a")` 正确返回 `NotFound`，`slot.a.bak1` 仍可独立 `Load`/`ListSlots`/`DeleteSlot`）、
  `Load_LegacyBackupWithoutSlotIdField_StillRecoversForRequestedSlot`（缺 `slot_id` 字段的真正
  旧存档不受影响）；既有 FND-01（`SlotIdLooksLikeBackupFileName_...`）与 N16 全部三条回归测试
  保持通过。
- **C10 收口（外部审计 7e63d66 第四轮，P2，成立）：`ReadValidEnvelope`/`TryReadLegacyBackup`
  选中候选前额外核对 `ParseMeta` 是否真的能成功（`IsCandidateMetaUsable`），不再只看
  `TryParseEnvelope` 的顶层形状。** FND-07/N15 已经把信封校验加深到"顶层字段类型正确"，但仍然
  只是"能通过 `Load` 后续两处早期校验"这一层，没有覆盖更深处 `ParseMeta` 本身要求的 meta 语义
  必填字段（`created_at`/`updated_at`/`game_id` 等）——`{"save_version":1,"sections":{"meta":{}}}`
  这类顶层形状合法、meta 却是空对象的文档能通过 `TryParseEnvelope`，若它恰好是正式文件，
  `ReadValidEnvelope` 仍会在这里"成功"一次并停止尝试任何备份，直到 `Load` 更深处的 `ParseMeta`
  才失败判 `Corrupted`——即便存在完好可用的备份也不会被尝试，与 FND-07/N15 要解决的问题同一
  形状，只是校验深度又往下差了一层。`IsCandidateMetaUsable` 只在候选顶层 `save_version` 已经
  等于当前运行时版本（无需迁移）时才提前做这层语义校验——需要迁移的候选，其 meta 在迁移完成前
  的形状允许与当前版本要求不同，这正是迁移链存在的意义，维持原有行为交给 `Load` 迁移完成后
  再校验。验收：`SaveSystemTests.Load_FormalPassesShapeCheckButMetaMissingRequiredFields_
  FallsBackToHealthyBackup`（回退到健康备份）、`Load_FormalMetaSemanticGap_
  NoHealthyCandidateAvailable_ReturnsCorrupted`（没有健康候选时仍正确判 `Corrupted`，不误判为
  `NotFound`）。
- **AUD-01 根治（外部审核第九轮，P1，architecture/落地计划/audit-85f1f4f-20260908）：`Load` 中途某段
  `Load()` 抛异常时，此前已成功 `Load` 过的段按正向顺序回滚到读档前状态（CORE-180-02 根治，见下，
  取代最初实现"按逆序"的做法），取代此前"不回滚"的合同。**
  真实探针复现：`world.gobj_pending_loot` 段读到 1.5 旧格式非空数据时抛异常（见下方
  `GobjPendingLootPersistable` 一节），此时排在它之前的 `player.inventory` 段已经按新档内容覆盖，
  最终 `LoadStatus=PersistableThrew` 但 inventory 停留在本次失败读档写入的中间值，不是读档前的旧
  内容——调用方拿到的是一份"部分是新档、部分是旧档、且整体标记失败"的不一致状态，比"完全不加载"
  更难处理。现在的实现：`Load` 在开始逐段调用 `Load()` 之前，对全部已注册段各调用一次 `Save()`
  取一份"读档前状态"快照（快照本身允许失败，失败的段只记诊断，不中止整个读档流程）；某段 `Load()`
  抛异常时，对此前按顺序已经成功 `Load()` 过的段按正向顺序（与它们最初被加载的顺序相同，
  CORE-180-02 根治，见下）重新调用一次 `Load(快照)`，尽力恢复现场
  （单个段的回滚调用本身再次抛异常也只记诊断、继续尝试其它段的回滚，不让一个段的回滚失败连锁
  阻断其它段）。回滚是"尽力恢复"，不是"把失败伪装成功"——最终 `LoadResult.Status` 仍然是
  `PersistableThrew`，调用方仍然能且应该按失败处理这次读档（例如提示用户、不切换场景）；回滚只是
  保证"失败时看到的状态尽量接近读档前"，减少半新半旧状态带来的排障成本。验收：
  `SaveSystemTests.Load_LaterSectionThrows_RollsBackEarlierSuccessfullyLoadedSection_ToPreLoadState`、
  `Load_LaterSectionThrows_RollbackDoesNotMaskFailureStatus`。
- **CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）：AUD-01 的
  "尽力回滚"只覆盖此前已成功 `Load()` 过的段，抛异常的那一段自身从不在回滚列表里——如果它的
  `Load()` 实现在校验数据形状之前就已经改动了 live 状态（真实探针复现：
  `Core.Carriers.Item.EquipmentPersistable.Load` 先无条件清空当前装备、再校验 JSON 形状，坏 shape
  抛异常后装备已经丢失且已经发出 `StatChanged`/`ItemUnequipped`），SaveSystem 这一层完全没有尝试
  恢复它。同一类"先改状态、后校验形状"的模式在
  `Core.Gameplay.Achievement.AchievementHost.Load`（无条件清空该玩家全部成就进度）、
  `Core.Gameplay.Spawn.SpawnHost.Load`（无条件清空全部刷新点记录）、
  `Core.Carriers.Unit.SkillBindingPersistable.Load`（无条件解绑全部技能绑定）里各自独立复现过；
  `Core.Gameplay.WorldState.WorldState.Load`、`Core.Gameplay.Difficulty.DifficultyHost.Load` 也在
  校验形状之前无条件重置了字段。另有一类不同成因但同一后果的缺陷——边解析边直接调用 live host
  写方法（不是先清空，而是逐条目边校验边提交），排在后面的条目格式非法时前面已经真正提交，见
  `Core.Gameplay.Economy.CurrencyPersistable.Load`/`VendorStockPersistable.Load`/
  `Core.Foundation.SaveSystem.RngStreamsPersistable.Load`。全部按同一原则改造：每个 `Load()` 先
  完整解析校验成临时恢复计划（不触碰任何字段），只有整份数据校验通过才一次性提交。`SaveSystem`
  自身也加了一层兜底：`Load` 中途某段抛异常时，回滚列表现在把抛异常的这一段自身也纳入（用它自己
  的读档前快照重新调用一次 `Load()`），不再只回滚"此前成功的其它段"——对已经遵循"先校验后提交"
  的段这是安全的幂等 no-op，对任何未来仍然踩了这个坑的段是额外防线。
- **CORE-170-03 补充（同上）：`SaveSystem.Load` 回滚会重新调用某些段真正的运行时逻辑
  （如 `EquipmentPersistable.Load` 为复用真实装备联动会调用 `EquipmentHost.Equip`/`Unequip`），这
  会正常派发真实领域事件（`ItemEquipped`/`ItemUnequipped`/`StatChanged`）；`AchievementHost` 一类
  按 `custom_event` 观察条件计数的消费者会把"读档/回滚期间的重放"误当成一次真实玩家操作再计一次
  数（真实探针复现：成就进度从 1 被回滚重放的事件错误推高到 2 并触发解锁）。现在 `SaveSystem.Load`
  把整段"逐段 `Load` + 失败回滚"逻辑包在 `IEventBus.SuppressDispatch()` 抑制作用域内——见
  `core/foundation/event_bus/README.md`"SuppressDispatch"一节——作用域内 `Enqueue`/
  `PublishImmediate` 提交的事件被直接丢弃，不进队列、不派发给任何订阅者；`SaveMigratedEvent`/
  `SaveLoadedEvent` 仍在作用域外正常派发（"本次读档完成了"这个通知不是重放）。验收：
  `Tests.Gameplay.Assembly.CORE_170_03_SaveRollbackEventSuppressionTests`、
  `Tests.Carriers.Item.CORE_170_03_EquipmentPersistableLoadFailureTests`，以及各段自己模块下的
  `CORE_170_03_*`/`Load_BadShape_*` 定向用例。

- **CORE-180-01 根治（第十一轮外部审计，P1，architecture/落地计划/audit-e070e3f-20260908）：成功
  读档时，`IEventBus.SuppressDispatch` 抑制作用域会连带丢弃规则层依赖的"内部同步事件"
  （`stat.changed`/`progression.state_restored` 等），导致等级恢复但评级换算属性缓存不恢复、
  装备恢复但资源池上限/当前值不恢复。** 真实探针复现：进度恢复到等级 10 后，`RulesAssembly`
  订阅的 `progression.state_restored`→`StatHost.RecomputeRatingStats`/`stat.changed`→
  `PowerHost.RecomputeMax` 两条内部重算路径在抑制作用域内被同 CORE-170-03 一起丢弃，`DispatchPending`
  之后 Rating 缓存仍停留在读档前的值、装备驱动的资源池上限仍停留在读档前的值，紧随其后按上限
  clamp 的 `player.vitals`（见 `PlayerVitalsPersistable`）因此把生命值错误 clamp 到读档前的旧
  上限。**判断记录（为什么不是"给内部同步事件开白名单，抑制作用域内照常派发"）**：`stat.changed`
  这个事件 key 同时被 `RulesAssembly` 自己的内部重算订阅、也被外部业务/测试订阅者共享，
  `EventBus.DispatchOne` 按 key 无差别派发给该 key 下的全部订阅者，无法只放行内部订阅者、外部
  订阅者继续抑制——`CORE_170_03_SaveRollbackEventSuppressionTests.
  Load_BadShapeEquipmentSection_RestoresEquipment_AndDoesNotLeakEventsToObservers` 已经显式断言
  排空事件队列后 `StatChanged` 一个都不应该出现在外部订阅者手里，给该事件类型开白名单会直接
  违反这条既有验收。改为新增 `IDerivedStateRebuilder` 接口（`contracts/IDerivedStateRebuilder.cs`）：
  `SaveSystem`（本模块，L0）持有一个可选引用（`SetDerivedStateRebuilder` 注入，默认 null），
  `Load` 在真正开始逐段读档前调用一次 `BeforeLoad()`，每个已注册段成功 `Load()` 之后立即调用一次
  `OnSectionLoaded(sectionKey)`——两个回调都完全绕开事件总线，直接由装配根（
  `Core.Gameplay.Assembly.GameplayAssembly`）的实现直接调用 `StatHost`/`PowerHost` 等目标模块的
  方法，因此不受 `SuppressDispatch` 影响，也不会被任何事件订阅者观察到（不产生任何新的可观察
  事件，只是把读档前已经在做的内部重算显式地再做一遍）。回调本身抛异常只记诊断、不影响"这一段
  `Load` 成功了"这一事实，不会把钩子失败误判成存档段失败。装配细节（触发时机、`ReloadArchetypeAndRace`）
  见 `core/gameplay/assembly/README.md`"CORE-180-01/03 根治"一节。验收：真实
  `GameplayAssembly`+`SaveSystem.Load`（含不经过 `GameplayAssembly.RestoreFromSlot`、直接调用
  `SaveSystem.Load` 的路径）返回 `Loaded` 后等级/评级/资源池上限/当前值与快照一致，
  `DispatchPending` 不改变结果；`CORE_170_03_*` 系列既有测试（含上面这条显式断言 `StatChanged`
  零泄漏的用例）继续全绿。测试：
  `Tests.Foundation.SaveSystem.CORE_180_01_DerivedStateRebuilderTests`（若干真实
  `RulesAssembly`+`SaveSystem` 组合定向用例）、`Tests.Gameplay.Assembly.
  CORE_180_01_SuccessfulLoadDerivedStateTests`。

- **CORE-180-02 根治（同上，P2）：失败读档的回滚顺序此前是"逆序"（后加载的段先回滚），违反了
  段与段之间真实存在的依赖关系。** `SaveSections.KnownOrder` 把 `player.progression` 排在
  `player.equipment` 之前，是因为装备重新装备（`EquipmentPersistable.Load` 复用真实
  `EquipmentHost.Equip`）需要读到已经恢复到位的等级去做需求校验；逆序回滚会先用读档前快照恢复
  Equipment（此时等级字段仍是本次失败读档写入的低等级值），装备因等级需求不满足而重新装备失败、
  物品被迫留在背包，紧接着才轮到 Progression 恢复等级——为时已晚，没有人再重试装备。真实探针
  `ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION` 复现：等级正确回滚到 2，但装备没有跟着回来（`equipped_after=False`）。
  **根治：`SaveSystem.RollbackLoadedSections` 改为按正向顺序（与 `readOrder`/正常读档同一顺序）
  重放各段的读档前快照**——回滚在本质上变成"再做一次读档，只是把文档换成读档前的快照"，先恢复
  Progression（等级），再恢复 Equipment（此时能读到正确等级，重新装备按预期成功），与正常读档
  路径共享同一套已经验证过的依赖顺序，不需要为回滚单独维护一份"应该谁先谁后"的规则。对彼此没有
  依赖的段（多数自定义段），正向/逆序不影响"最终恢复到读档前状态"这一结果本身，AUD-01/CORE-170-03
  既有回归测试不依赖具体回滚顺序，只依赖"最终恢复到位"，改动后继续通过。验收：注入后段异常后
  progression/inventory/equipment/vitals 全部回到 live 状态，高等级装备重新装备且库存无重复。
  测试：`Tests.Foundation.SaveSystem.SaveSystemTests.
  CORE_180_02_Rollback_RestoresEquipmentAfterProgression_UsingForwardOrder`（新增，真实
  `GameplayAssembly` 组合）。

- **CORE-180-03 根治（同上，P2，已确认）：同图 `GameplayAssembly.RestoreFromSlot`（目标地图与当前
  地图相同、不触发 `EnterMap`）只把 `player.race_id`/`player.archetype` 两个字段本身写回
  `PlayerUnit`，从未重放种族/职业的属性修正与被动光环——旧种族的属性加成/被动光环残留，新种族
  的完全没有生效。** 详见 `core/gameplay/assembly/README.md`"CORE-180-01/03 根治"一节
  （`RulesAssembly.ReloadArchetypeAndRace` 的实现与判断记录）；本模块这一侧只负责在
  `IDerivedStateRebuilder.OnSectionLoaded(SaveSections.PlayerRaceId)` 触发时机上提供保证——
  `player.race_id` 段无论文档是否携带该字段都会被处理一次（`RaceIdPersistable` 未声明
  `KeepStateWhenSectionMissing` 例外，缺段时仍会以 `JsonNull` 调用一次 `Load` 清空为 `null`，
  `OnSectionLoaded` 因此总会触发，覆盖跨图/同图两条路径，也覆盖"文档没有种族段"的情形），
  且晚于 `player.archetype`（`SaveSections.KnownOrder` 固定顺序），触发时两个字段都已经是本次
  读档的最终值。

- **CORE-110-01 根治（第十二轮外部审核，P2，已确认，architecture/落地计划/audit-ac3b622-20260909）：
  失败读档触发的回滚（`RollbackLoadedSections`）此前只重放各段自己的字段（`IPersistable.Load(快照)`），
  从不调用 `IDerivedStateRebuilder.OnSectionLoaded`——依据是旧判断记录"回滚路径的正确性由
  CORE-180-02（回滚改判为正向顺序）本身保证，不依赖本钩子"。真实探针证明这条判断只对字段类状态
  成立，对派生类状态（评级换算属性、种族/职业被动光环、资源池上限/当前值——都不是任何一个段自己
  的字段，是若干字段的函数）不成立：字段已经回滚到 A，但函数从未重新算过，结果仍停留在读档失败
  前短暂生效过的 B。** 两个真实子场景：(a) 种族段回滚后，字段回 A，但评级/光环仍是 B；(b) 装备段
  成功回滚（`equipped_after=True`），但 `player.known_skills` 段失败前那次 `OnSectionLoaded
  (PlayerEquipment)`（见上方 CORE-180-01 一条）已经把 Power 上限下调 clamp 到 B 的值，回滚只恢复了
  装备字段，上限/当前值没有跟着重算，停留在 100（应为 200）。**根治：`RollbackLoadedSections` 对
  每个成功回滚（`Load(快照)` 未抛异常）的段，紧接着按与正常读档主循环完全相同的方式回调一次
  `IDerivedStateRebuilder.OnSectionLoaded(sectionKey)`**（仍在 `SuppressDispatch` 作用域内，抛异常
  同样只记诊断）——回滚循环天然按 CORE-180-02 已经改判的正向顺序处理，因此这一步等价于"用读档前
  快照重新走一遍正常读档的派生重建依赖顺序"，不需要为回滚单独定义一套规则。装配根侧
  （`GameplayAssembly.DerivedStateRebuilder`）配合改为"滚动更新"`_previousArchetypeId`/
  `_previousRaceId`（每次 `OnSectionLoaded(PlayerRaceId)` 调用后更新为刚生效的新值，而不是只在
  `BeforeLoad` 快照一次固定不变）——同一个 `OnSectionLoaded` 实现因此对正向阶段（旧=A、新=B）与
  回滚阶段（旧=B、新=A）都能推导出正确的"旧/新"参数传给 `RulesAssembly.ReloadArchetypeAndRace`，
  不需要为回滚单独写一份"反向"逻辑，详见 `core/gameplay/assembly/README.md`"CORE-110-01/02 根治"
  一节。子场景 (b) 的资源池当前值另有一条独立收口：`player.vitals` 是资源池当前值唯一的权威段
  （见 `PlayerVitalsPersistable` 类型判断记录），但它可能根本没有机会在这次失败读档里被触碰到
  （失败发生在它之前）——`Load` 的失败分支现在总是尝试把 `player.vitals`（若已注册且有读档前快照）
  一并纳入回滚集合（即便它本不在这次失败读档实际触碰过的段列表里），确保它总能在
  `player.equipment`/`player.race_id` 的派生重算之后拿到"最后一句话"的机会，用真实的读档前快照
  值把可能被派生重算副作用（`PowerHost.RecomputeMax` 的下调 clamp）改动过的当前值纠正回来。
  接口判断记录同步修订，见 `contracts/IDerivedStateRebuilder.cs`。验收：真实 A/B 存档 fixture 下，
  失败回滚后字段、评级/光环、资源池上限/当前值全部回到 A，`SuppressDispatch` 作用域内不向外部
  订阅者泄漏任何业务事件（同 CORE-170-03 既有惯例）。测试：`Tests.Gameplay.Assembly.
  CORE_110_01_RaceSubCase_RollbackRestoresFieldAndDerivedStatAndAura`、`Tests.Gameplay.Assembly.
  CORE_110_01_EquipmentSubCase_RollbackRestoresPowerMaxAndHealth`。

## `LoadResult.CurrentMapId` / `CurrentPosition`（缺口 11）

- 10_存档与持久化.md 只定义了存档文档 `world.current_map_id`/`world.current_position` 两个段
  的内容（第 2.3 节），没有定义 `ISaveSystem.Load` 这个 C# 方法本身的返回结构应该携带什么——
  这两个新增字段属于本模块 API 的实现细节补齐，不是对 10 号文档的勘误。
- `SaveSystem.Load` 在 `Loaded`/`LoadedFromBackup`/`PersistableThrew` 三种状态下都会尝试直接从
  已解析出的 `sections` 原始 JSON 解析这两个字段（`TryGetSectionId`/`TryGetSectionVec2`），**不
  依赖调用方是否已经为 `world.current_map_id`/`world.current_position` 注册对应的
  `IPersistable`**（见 `Core.Carriers.Unit.UnitPersistable.CurrentMapId`/`CurrentPosition`）——
  这是为了让"读档前需要先知道要加载哪张地图"的调用方（例如场景路由）在任何
  `PlayerUnit` 实体存在之前就能拿到目标地图，不必等所有段都 `Load()` 完成。
- 段缺失（旧存档没有这两个字段）或格式不符时两者均为 `null`，不记诊断、不阻断读档——10
  第 2.3 节把它们标为"必填"，但本模块对"必填字段实际缺失"统一采取宽松兜底（呼应
  `UnitPersistable.Load` 对 `JsonNull` 的处理），不因为这两个辅助字段解析失败就拒绝整次读档。
- `NotFound`/`Corrupted`/`MigrationFailed` 三种失败状态下二者恒为 `null`（未走到 `sections`
  解析步骤）。

## RngStreamsPersistable

`core/RngStreamsPersistable.cs` 是 `rng.stream_states` 段的 `IPersistable` 实现：`Save()`
遍历 `IRngHost.Streams`（已按 `Id` 序数排列）写出每条流的 `RngStreamState.ToString()` 文本；
`Load()` 逐条 `Id.TryParse` + `RngStreamState.TryParse` 后调用 `IRngHost.SetStreamState`——
目标流按 `IRngHost` 的懒创建语义按需创建，不要求流在读档前已存在。

**AUD-02 收边（外部审核第九轮，P2）：本段显式声明 `KeepStateWhenSectionMissing=true`，是"缺段清空"
默认合同的显式例外。** 理由：RNG 分流没有合法的"空"默认状态——每条流懒创建时都必须有一个主种子
可派生，本类型自己不掌握"该用哪个种子重置"这一决定权（那是调用方构造 `IRngHost` 时给的，与"存档
缺这一段"无关），把它清空成某个任意种子并不比保留调用前 `IRngHost` 已经在跑的状态更"正确"，只是
换一种同样武断的随机序列。这与本类型既有的字段级兼容策略（"旧存档没有 `master_seed` 字段时退化为
不 `Reset`、只逐条恢复流状态"）是同一种"缺失时保留优于武断清空"的选择，只是把同一判断显式提升到
整段缺失这一层。

## 自动存档触发点

`ISaveSystem.ShouldAutoSave(AutoSaveTrigger)` 只做 `SaveSystemOptions.AutoSave` 策略判断
（10 第 6 节默认值：`SavePoint` 开、`MapSwitch` 关、`QuestComplete` 开），不订阅任何事件、
不产生副作用。把某个触发点接到具体游戏时机（场景路由完成地图切换、任务状态机进入完成
节点……）并在返回 true 时实际调用 `Save`，是上层（`scene_router`、玩法层任务系统等）的
职责——机制到此为止，接线是游戏层的事。

判断记录（加固任务）：`core/gameplay/assembly/GameplayAssembly.cs` 的 `RequestAutosave`
本地函数此前无条件调用 `Save`，未接本方法的 `SavePoint` 判断——存档点物件交互
（`GobjOptions.SaveRequester`）与对话中的存档动作（`DialogHost.saveRequested`）两个接线点
共用这一份逻辑，均已属于"触发点已接线"状态，现补上 `ShouldAutoSave(AutoSaveTrigger.SavePoint)`
判断，`OnSavePoint=false` 时两者均不再调用 `Save`（见该本地函数判断记录、
`core/gameplay/tests/EndToEndTests.cs` 新增用例）。

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

本模块提供 `ReplayData`/`ReplayInputRecord`/`ReplayStepRecord`/`WorldSnapshot` 数据类型与
`IReplayRecorder`/`IReplayPlayer` 契约（10 第 8 节），并在 `core/ReplayRecorder.cs`、
`core/ReplayPlayer.cs` 给出默认实现。`IReplayPlayer.StepTo` 返回的 `WorldSnapshot`
（`Tick`/`EventLog`/自写 FNV-1a 64 位 `Digest`）定义在 `contracts/Replay.cs`；世界如何组装
（阶段处理器、意图词汇表等具体游戏内容）由调用方经 `WorldFactory` 委托提供，本模块自身不
知道任何业务内容。

**离散步回放（ADR-0013 补齐任务）**：`ReplayData` 新增 `Steps`（`IReadOnlyList<ReplayStepRecord>`，
按 tick 号记录该 tick 实际使用的是 `Continuous` 还是 `Discrete` 步，后者含
`actorId`/`phase`）与 `FormatVersion`（历史上曾是 2，见下"离散模式纯录像回放"）。调用方在驱动
主循环时每 tick 调用一次 `IReplayRecorder.RecordStep(tick, step)`（与 `RecordInput` 同一 tick
编号体系），`IReplayPlayer.StepTo` 据此还原每个 tick 应该重放的 `SimStep` 种类——一份录像因此
可以正确覆盖"连续 → 离散 → 连续"的模式切换。**旧格式兼容**：本任务之前唯一存在过的格式（无
`format_version`/`steps` 字段）解析后 `FormatVersion` 为 1、`Steps` 为空，`StepTo` 对没有
对应记录的 tick 一律按 `Continuous` 处理，与该格式历来的唯一行为完全一致——旧录像文件不需要
任何迁移即可继续读取。

**离散模式纯录像回放（离散模式回放完整性任务，`FormatVersion` 由 2 升到 3）**：上一段描述的
机制把"这个 tick 是谁的回合、什么阶段"（`ReplayStepRecord.ActorId`/`Phase`）也录进了录像，
本质是把 `Core.Foundation.SimLoop.TurnScheduler` 的内部决策结果直接喂给
`IWorldSim.Tick`——这与 03 第 3.2 节步骤 6"回放 = 意图序列……存档与重放只记录意图"的拍板结论
不完全一致（意图之外还多记了一份调度器决策）。`IReplayPlayer.LoadDiscrete`（配
`DiscreteWorldFactory`，产出一个已 `BeginCombat` 的 `TurnScheduler`）是修正后的路径：重放时
真正驱动这个 `TurnScheduler`（`NextStep`/`SubmitIntent`/`Tick`/`NotifyStepConsumed`，与
`Core.Gameplay.Assembly.GameplayAssembly.Advance` 同一驱动算法，本模块是 L0 不能反向依赖
`GameplayAssembly` 所在的 L4，故原样内联该算法本身而不是调用那个类，见 `IReplayPlayer.LoadDiscrete`
判断记录），只在 `NextStep` 返回空（轮到的行动者需要外部输入）时，才从 `ReplayData.Inputs`/
新增的 `ReplayData.EndTurns`（`ReplayEndTurnRecord`，"结束回合"发生时刻，与提交意图是两种独立
记录，不共用 `IntentKind` 字符串哨兵值）里查出录制时的决策原样提交/结束回合——"轮到谁"这件事
交给重放时重新驱动的 `TurnScheduler` 自己算，不从录像里读。`IReplayRecorder.RecordStep` 在这条
新路径下降级为纯诊断信息（人工核对"录制时这一步实际是谁"用），不再被 `LoadDiscrete` 播放路径
读取。`Load`/`WorldFactory` 这条旧路径（整段录像直接按 `ReplayStepRecord` 重放给 `world.Tick`，
不涉及 `TurnScheduler`）原样保留，未使用 `LoadDiscrete` 的调用方行为不变。

## 迁移回归样例（存档版本迁移回归任务新增）

`core/foundation/save_system/tests/Migration/` 存放存档版本迁移的固定回归产物（惯例同
`core/gameplay/tests/Replay/` 对回放录像/基线文件的处理：手工构造、提交到仓库、测试只读不写）：

- `v1_sample.save.json`：一份手工构造的旧版本（`save_version: 1`）存档文档；`world.current_position`
  段故意写成 `[x, y]` 两元素数组（本任务选定的"真实且最小"迁移场景——`world.current_position` 是
  `ISaveSystem.Load` 自身直接解析的字段，要求 `{x, y}` 对象形状，见 `LoadResult.CurrentPosition`
  文档，框架自己就能定义、也真正需要一个迁移函数把数组转成对象，不需要越权替游戏层的段编造
  迁移决定）。
- `v_future_sample.save.json`：一份"运行时尚未见过"的未来版本（`save_version: 999`）样例，验证
  `LoadStatus.MigrationFailed` 拒绝路径与原始文件不被覆盖/删除。
- `SaveVersionMigrationRegressionTests.cs`：读取上述两份样例，验证——旧样例迁移成功
  （`LoadResult.MigratedFromVersion` 正确、各段可读、`world.current_position` 迁移后能被正确
  解析）→ 迁移后的世界重新存档、再读一遍无损（新落盘文件的 `world.current_position` 应已是新
  的对象形态，不是继续携带旧数组形态）；未来版本样例被拒绝且给出点名两个具体版本号的诊断消息，
  原始文件保持不变。

**判断记录（新增迁移时必须同时新增旧样例）**：`ISaveMigration` 实现的正确性只有"喂一份真实构造
的旧版本文档跑一遍迁移链"才能验证——纯内存拼字符串的单元测试（如 `SaveSystemTests.cs` 里的
`RenameSectionMigration` 用例）验证的是迁移链**机制**本身（跳数、缺环节报错、异常处理等），
不是某一次具体迁移**内容**是否正确；后者必须有一份独立于迁移函数实现本身、可被人工审阅的旧
样例文档，否则"迁移函数改错了但样例文档也跟着一起改错"这种自欺欺人的回归漏洞完全可能发生。
往后任何游戏层模块新增 `ISaveMigration` 实现（存档 schema 又发生不兼容变更）时，必须同时在自己
的测试目录下补一份对应旧版本的手工样例文档与一条"旧样例迁移成功"的回归测试，不能只靠内存拼
JSON 字符串验证——本条约定同样适用于 `ISettingsStore` 的设置文件迁移（见上"设置文件"一节）。

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
3. **已注册但文档缺失的段，是否该用 `Load(JsonNull)` 调用一次**（CR150-03 根治，
   architecture/落地计划/audit-3224ca1-20260908，P2，**取代本条此前"拍板：缺失段跳过不调用"
   的记录**）：原判断记录只考虑了"当次运行内、同一进程反复交互再存读档"这一种复现路径，未
   覆盖"某个模块的运行期状态在读档前就已经非空、读档读到的又恰好是一份还没有对应段的旧档"这
   一更常见的场景——按原策略整段跳过 `Load` 调用，等价于"读档对这个模块完全没有发生过"，该
   模块读档前任何残留状态原样保留，这与"读一份旧档"应有的"至少把这个模块归零到旧档能表达的
   状态（即从未发生过）"这一直觉相悖，也与本模块其它大多数段"缺失关键字段时退化为清空/默认
   值"的既有个例（如 `master_seed` 缺失退化为不重置，但那是"字段级"缺失，段本身仍然存在、仍
   然会被调用 `Load`）不是同一件事。现在默认改为：已注册但文档缺失的段仍然调用一次 `Load`，
   参数为 `JsonNull.Instance`——`IPersistable.Load` 的实现方因此确实需要处理"收到 JsonNull"
   的情况（多数既有实现已经如此，为了兼容"段本身存在但值恰好是 `Save()` 主动返回的 `JsonNull`"
   这一此前就已存在的合法情形）。已审计全部 16 个既有 `IPersistable` 实现，`Load(JsonNull)`
   均已正确处理为"清空到默认态"或"无副作用的 no-op（等价于原地保留当前状态）"，无一会因为
   本次改动抛异常或产生错误行为，本次改动因此不需要任何既有实现跟着改代码。极少数段确实需要
   "文档缺该段时保留当前状态、完全不调用 `Load`"这一相反语义时，由该段自身把新增的默认接口
   属性 `KeepStateWhenSectionMissing` 显式覆盖为 `true` 声明——当前没有任何既有段需要这么做。
4. **`DeleteSlot` 的返回值含义**：10 文档未定义该方法返回值语义。本实现选择"删除前该槽
   是否存在"（即本次调用是否真正删除了什么），而非"是否所有文件都成功删除"——因为
   `IFileSystem.DeleteFile` 对不存在的备份文件返回 false 是正常情况（`BackupCount` 大于
   实际已产生的备份数量时），不应据此判定整个 `DeleteSlot` 失败。
5. **`RegisterPersistable`/`RegisterMigration` 的重复登记处理**：10 文档未讨论。参照
   `hook_registry.DeclareHookPoint`、`event_bus.EventCatalog.FromDefinitions` 的既有惯例
   （重复 key 直接抛 `InvalidOperationException`），本模块对重复 `SectionKey`/
   `FromVersion` 采用同一策略，不做"后者覆盖前者"的静默处理。
6. **`ListSlots`/`CountSlots`（`MaxSlots` 判断）对嵌套路径的过滤**：`IFileSystem.
   ListFiles` 按 02 文档只约定"递归列举、路径相对 dirPath"，未约定存档目录下是否可能出现
   子目录或非存档文件。本实现保守处理：只认直接位于存档目录下（结果中不含 `/`）、以
   `.json` 结尾的文件为一个存档槽；其余一律忽略，不计入槽数、不出现在 `ListSlots` 结果里。
   **FND-01 收口后不再额外要求"文件名不匹配 `<slot>.bakN.json` 备份命名模式"**——备份已经
   搬进独立子目录 `backups/`（见上"文档格式与存储位置"一节），天然落在"结果含 `/`"的更深
   层级，被上面那条过滤规则排除，不需要再按文件名猜测；这条按文件名猜测的旧过滤本身还有
   副作用——会把长得像备份命名模式的**合法槽**（例如槽 id 恰好是 `slot.a.bak1`）连同它自己的
   正式文件一起误判成"备份"而从 `ListSlots`/槽计数里隐藏，移除后不再有这个问题。
7. **FND-09 收口（外部审核 `code-review.md`）：迁移链拒绝任何会越过当前运行时版本的单步迁移，
   循环终点必须恰好等于 `CurrentSaveVersion`。** `TryRunMigrationChain` 原循环条件只看
   `version < CurrentSaveVersion`：若登记了一条如 `1 → 3` 的迁移函数，但当前运行时版本只到
   2，跑完这一步后 `version` 变成 3（不再小于 2），循环判定"已到达终点"并返回成功——实际却
   把文档越级迁移到了当前运行时根本不认识、从未经过当前版本任何校验逻辑验证的结构，且
   `LoadResult.MigratedFromVersion`/落盘的 `save_version` 会让调用方误以为迁移正常完成到了
   "当前版本"。10 第 5 节"存档版本高于当前运行时版本 → 不承诺向前兼容"这条拒绝语义现在对
   "迁移链中途产出的越界结果"同样成立，不只检查文档最初的 `save_version`：循环体内每算出一个
   `migration.ToVersion` 就立即检查是否超过 `CurrentSaveVersion`，超过则判定 `MigrationFailed`；
   循环正常退出后再显式校验一次"退出时的版本必须恰好等于 `CurrentSaveVersion`"作为双重防御。
   当前版本恰好等于某条迁移函数的 `ToVersion`（如上例里 `CurrentSaveVersion` 改成 3）时，
   `1 → 3` 依然合法成功——这条收口只拒绝"越过"，不拒绝"恰好到达"。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 存档模型分段结构与固定汇总/加载顺序 | 是 | `display_summary` 等游戏自定义摘要字段的具体内容 |
| `Persistable` 契约与 `SaveSystem` 编排（汇总/迁移/落盘/事件） | 是 | 各游戏特有模块实现自己的 `Save`/`Load`（`world_state_flags`、`player.*`、`world.*` 等段） |
| 原子写入、备份轮转、损坏回退 | 是（经 `IFileSystem`） | 无 |
| 迁移函数链机制 | 是 | 具体每次 schema 变更对应的 `ISaveMigration` 实现 |
| 自动存档触发点判断机制 | 是 | 具体启用哪些触发点、把触发点接到游戏时机 |
| 确定性回放 | 是（契约、数据类型与默认实现：`ReplayRecorder`/`ReplayPlayer`，T1-9） | 是否把回放暴露为玩家可见功能；世界装配逻辑（经 `WorldFactory` 提供） |
| 设置文件存储与迁移 | 是 | 具体设置字段结构与含义 |
