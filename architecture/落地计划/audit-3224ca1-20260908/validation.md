# Validation Evidence

本文件汇总本轮有界验证证据，不给出最终审核结论。冻结源码与执行快照均以本轮固定的 `3224ca1247b119ef656bf928a024a1e0a7701fcc` 为准；探针只修改 `git_snapshot` 测试文件。

## 固定输入与状态

| 项目 | 证据 |
|---|---|
| 原仓库起始状态 | `D:\workespace\ws-game` 起始 HEAD=`3224ca1247b119ef656bf928a024a1e0a7701fcc`，工作树 clean；起始状态由主审首次 Git 检查确认；中途包盘点（此时已有外部修改）见 [live_package_inventory_authoritative.log](live_package_inventory_authoritative.log)。 |
| 源码冻结 | [source_snapshot.zip](source_snapshot.zip)（原件未归档：约 33MB 全量源码快照，体积超出文档归档合理范围，未随本目录一起提交；原件仍在生成该报告的 codex 产出目录内，结论以本表 SHA256 为准），SHA256=`22a8dfaa24a51056241ab0a1b0648f04762a3a9f8e614a55f286c59b0b1c3250`；展开目录为 `source_snapshot`。 |
| 执行快照 | `git_snapshot` 为 `git clone --no-hardlinks --no-checkout` 后 detach 到固定 HEAD；最终仅有两份测试探针修改。 |
| live 最终状态 | [final_live_and_snapshot_state.log](final_live_and_snapshot_state.log)：live 已到 `8927398714fb516121641f8f9a1f6ce0838a0db8`（相对冻结 `3224ca...` ahead 1），工作树 clean；冻结 snapshot 仍为 `3224ca...`。本轮未追随该提交重跑全量门禁。 |
| Unity | 本文件只汇总核心/交付验证；本轮另有真实 Unity 定向测试，见 [unity-validation.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/unity-validation.md)，不计入下表。 |

## 基线门禁

命令为 `check.ps1 -SkipUnity`，完整 stdout/退出码见 [check.process.log](check.process.log)，门禁 transcript 见 [check.transcript.log](check.transcript.log)。

| 结果 | 计数 |
|---|---:|
| PASS 步骤 | 14 |
| SKIP 步骤 | 6（Unity 相关） |
| .NET 测试 | 2413 passed，0 failed |
| Python pytest | 48 passed |
| 合并数据校验 | 60 tables，285 records，0 errors，0 warnings，1 override |
| framework-root 数据校验 | 5 tables，124 records，0 errors，1 warning，0 overrides |
| 进程退出码 | 0 |

## 新增探针结果

| 探针 | 退出码 | 实际结果 | 该 PASS/FAIL 证明的边界 |
|---|---:|---|---|
| 共享 aura 跨图重放 | 1 | 真实 GameplayAssembly 测试在第一件装备卸下后得到 `actual=False`，期望第二件仍持有时为 `True`。 | FAIL 证明共享永久 aura 的重放/句柄记账在该具体装备顺序下不满足“最后一件卸下才消失”；测试未到最后一件断言。见 [probe_a_shared_aura.log](probe_a_shared_aura.log)。 |
| SaveSystem 缺失 pending 段 | 1 | 真实 `SaveSystem.Load` 读取移除 `world.gobj_pending_loot` 段的旧存档：`beforeLoad=1`，实际残留=`1`，期望=`0`。 | FAIL 证明当前真实 Load 路径未对文档缺失段调用 `Load(JsonNull)`；直接调用 persistable 的 JsonNull 测试不能替代此证据。见 [probe_b_old_save_missing_pending.log](probe_b_old_save_missing_pending.log)。 |
| Partial 同图离开再回图重建 | 0 | `oldId=gobj.inst_1`、`newId=gobj.inst_2`；`beforeCount=1`、`afterCount=1`；旧 entry=`True`、新 entry=`False`。 | PASS 证明在 `WorldSim.ClearAll` 后用 `GameObjectFactory` 于原地图同刷新点重建实体时，pending 仍按旧 instance ID 保存且未自动重绑定。该探针未直接驱动 `SpawnHost`/`SceneRouter` 完整流程，也不把合法 `EnterMap` 新实体生成解释为重复奖励。见 [probe_c_partial_cross_map.log](probe_c_partial_cross_map.log)。 |
| Gather 满包冷却 | 0 | 显式 `SimTime=100`、`respawn_after_use=60`；满包首次交互后 `used_at=100`、掉落数=`0`；腾位后 `SimTime=101` 再交互仍 `used_at=100`、掉落数=`0`、loot rolls=`1`。 | PASS 表示复现当前冷却事务缺陷：未交付掉落仍提交冷却，腾位后不能补领；不表示期望的产品语义。见 [probe_gather_full_cooldown.log](probe_gather_full_cooldown.log)。 |

探针测试源修改位于：

- `git_snapshot/core/gameplay/assembly/tests/CR140_02_EquipmentAuraMapClearTests.cs`
- `git_snapshot/core/carriers/gobj/tests/GobjPendingLootPersistenceTests.cs`

## P1 路径探针

使用审计目录内复制的有效 1.5.0 ZIP/lock，仅修改 fixture lock 的 `version` 为 `x/../../outside_sentinel`，调用 `-AllowVersionMismatch`。完整 stdout 和退出码见 [security_path_probe.log](security_path_probe.log)，解析路径与哨兵前后状态见 [path_probe/path_probe_paths.txt](path_probe/path_probe_paths.txt)。

| 项目 | 实际值 |
|---|---|
| 退出码 | 0 |
| 六 DLL hash | 全部通过 |
| `effective_full` | `A/path_probe/outside_sentinel` |
| `target_full` | `A/path_probe/packages` |
| `effective_within_target` | `False` |
| `sentinel_before` → `sentinel_after` | `True` → `False` |
| 落地行为 | 删除哨兵目录并把包内容落到 Target 外的解析目录 |

## 1.5.0 包与兼容性

准确包证据见 [live_package_inventory_authoritative.log](live_package_inventory_authoritative.log)，离线安装完整 stdout 见 [get_framework.stdout.log](get_framework.stdout.log)。

| 项目 | 证据 |
|---|---|
| manifest | version=`1.5.0`，git_commit=`3224ca1` |
| ZIP | `ws-game-1.5.0.zip`，size=`52436782`，SHA256=`4c596d4a93c802c2fd065eb3adc0f4abc113b3b2687cc4f901ffa5b3c7e8c76b`，988 entries |
| lock | SHA256=`84cee708bf99253c452666aacc17fbf8cc7e77c4fd471d30475da6dc022bc1b0`，version=`1.5.0`，git_commit=`3224ca1` |
| 六 DLL | 实际 hash 与 lock 六项全部 match=True |
| 三个 tgz | adapter SHA256=`30f42862fb48e0eecfc92cc590e3c841d6ddaf75da0df10edb4270cddbf6535c`；framework-data=`6a8d1a80ad525393c21aa874f08a3dbf6bf9a6bf0143a9990eaa9c18bfb1801a`；toolchain=`b226bdce60705c5f94625ee5522103659b73231ef5a993a08a26c3c2c3f5dc3c` |
| 包版本 | adapter、framework-data、toolchain、game-template 均为 `1.5.0` |
| 占位资源 | `placeholder_biped.prefab`、`.controller` 及 attack/cast/hit/idle/test_autoexit 动画均存在 |
| 离线 get_framework | exit=`0`，六 hash 通过，961 files landed 到唯一审计目标 |
| 旧消费者编译 | 1.3 源码中的 `ViewKind.GameObject` 和 `ICharacterRig.HitFrameReached` 对实际 1.5 DLL 编译 exit=`0`；2 个 CS0618 预期警告、0 errors。此项为源码兼容，不包含旧二进制或 Unity 消费者 E2E。 |

其它原始结果索引见 [raw_validation_index.txt](raw_validation_index.txt)。
