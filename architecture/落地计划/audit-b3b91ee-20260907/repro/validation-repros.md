# b3b91ee 有界运行期复现

基线：`b3b91ee`（仓库 `D:/workespace/ws-game-review-b3b91ee`）。

复现器：[`AuditRepros.csproj`](AuditRepros.csproj)、[`Program.cs`](Program.cs)。工程引用真实 `Core.Foundation`、`Core.Numbers`、`Core.Rules`、`Core.Carriers`、`Core.Gameplay` 与 `Adapters.Stub` 项目；仅链接已有测试 support，不复制生产实现。R7 使用真实 `CarriersAssembly`、`CreatureUnit`、`CreatureFactory.Despawn`、`CombatHost` 和 `WorldSim`，空间/导航使用仓库现成 stub 适配器。输出日志为审计目录根下的 [`validation-repros.txt`](../validation-repros.txt)。

命令：

```powershell
dotnet run --project architecture\落地计划\audit-b3b91ee-20260907\repro\AuditRepros.csproj --configuration Release
```

没有运行全套测试或 Unity。每条用例的 `PASS_FOR_REPRO` 表示按预期触发候选缺陷，不表示框架测试通过；`NOT_REPRODUCED` 只表示候选序列实际执行但未触发缺陷；若构造或执行未发生，使用 `NOT_EXECUTED`。

| 用例 | Expected | Actual | 结果 |
|---|---|---|---|
| R1 QuestPersistable：空快照 → Accept → Load 旧快照 | 空对象读档清除新接取任务 | `Object{}` 后 Accept 成功，Load 后仍为 `Active` | `PASS_FOR_REPRO` |
| R2 EquipmentPersistable：空对象 Load | 空对象读档卸下当前装备 | 已装备物品仍在 `main_hand` | `PASS_FOR_REPRO` |
| R2b EquipmentPersistable：旧同槽装备 Load | 旧 inventory/equipment 快照只恢复 A，旧档外的 live B 不得出现 | 先 `InventoryPersistable.Load(oldBag)` 再 `EquipmentPersistable.Load(oldEq)` 后，B 在背包，装备为 A | `PASS_FOR_REPRO` |
| R3 SaveSystem：合法 `slot.a.bak1` 与 `slot.a` 备份冲突 | 两个槽独立，`slot.a.bak1` 可列出 | `slot.a.bak1` 先保存 X；保存 `slot.a` A 后备份仍 X；保存 B 后备份 payload 变 A，`ListSlots=[slot.a]` | `PASS_FOR_REPRO` |
| R4 WorldSim：旧 TimerHandle → ClearAll → 新同 ID | 旧句柄不可影响新计时器 | old/new 都为 `TimerHandle(1)`，old.Cancel 后新句柄不再存活 | `PASS_FOR_REPRO` |
| R5 TurnScheduler：fixed_order，AP3，A 后加入 B | A/B 均有 AP3 移动账本 | A 消费成功剩 2；B 消费失败剩 0 | `PASS_FOR_REPRO` |
| R6 CastPipeline：离散 action cost，目标超距 | OutOfRange 失败不扣 AP | `OutOfRange` 且 `consumeCalls=1` | `PASS_FOR_REPRO` |
| R7 CarriersAssembly：真实生物进战 → Despawn → World.Tick → Combat.Update | Despawn 后脱战清理不应访问已注销资源 | 真实组装中生物进战后 `CreatureFactory.Despawn`、`World.Tick` 移除实体，再 `Combat.Update(10)` 抛 `InvalidOperationException: 单位 "creature.audit_live" 未注册` | `PASS_FOR_REPRO` |

本次构建无 unexpected exception；详细原始输出见审计目录根下的 [`validation-repros.txt`](../validation-repros.txt)。
