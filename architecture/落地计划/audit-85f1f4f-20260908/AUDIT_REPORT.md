# ws-game 1.6.0 审计总入口

基线为 `85f1f4fbaff7aa3f01292f1b4b469a6a48bcc570`，版本 `1.6.0`。审计在独立分支 `codex/audit-1.6.0-85f1f4f` / 工作树 `D:\workespace\ws-game-review-85f1f4f` 完成，原仓库 `D:\workespace\ws-game` 产品代码未改动，交付时工作树干净。Luna 负责有界采证，Astra 负责独立复核；本文件只做索引、结论和验收顺序，细节保留在分报告。

## 当前结论

本轮确认 5 项 P1/P2：

| 编号 | 等级 | 当前结论 |
| --- | --- | --- |
| AUD-01 | P1 | 1.5 非空 gobj pending 以 `gobjInstanceId` 写入，1.6 读取器直接索引 `originKey`，真实 Load 返回 `PersistableThrew`；前序 inventory 段已变更为快照值，证明失败不回滚前段。 |
| AUD-02 | P2 | 缺 inventory/vendor 段时实现直接返回，状态不清空；两者属于同一缺段合同落差，可能造成跨槽旧状态残留。 |
| AUD-03 | P2 | vendor stock 仅保存 Remaining；同一 host 读档不恢复 TimerRemaining，t=9 再 Update(1) 会提前补货。 |
| AUD-04 | P2 | 1.5 到 1.6 的公开 `PendingChestLootSnapshot`/`RestorePendingChestLoot` 改名为 `PendingLootSnapshot`/`RestorePendingLoot`，没有 alias；同一旧 consumer 在 1.6 编译报 CS1061。 |
| AUD-05 | P2 | 样例 `slot_mesh` 的真实 Unity rig Apply 路径把 prefab 型 model 引用按 Mesh 读取并清空槽位网格；证据限于当前样例配置，不扩张为完整 inventory→View 结论。 |

种族光环和 `clip.events` 只作为静态待验证项，不计入上述确认数。审计结论表示当前证据边界内的问题与限制，不是“全代码无缺陷”证明；也不声称发行失败。

## 证据入口

- 核心逻辑、存档和独立 .NET probe：[core/core-findings.md](core/core-findings.md)。
- 文档与代码矩阵、项目/门禁/API/交付边界：[docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)。
- 项目问题、未实现/未接入/非目标分类：[docs-project/project-findings.md](docs-project/project-findings.md)。
- 表现与 Unity 样例 rig 证据：[presentation/presentation-findings.md](presentation/presentation-findings.md)。
- 文档项目验证入口：[docs-project/validation.md](docs-project/validation.md)。

核心 probe 最终进程退出码为 0，`EXPECTED`/`ACTUAL` 行及原始输出见 core 报告链接的 `repro/core-persistence-probe-final.log`。AUD-04 的 1.5 原始 zip、1.6 原始 DLL、反射和 consumer 编译证据在 docs-project。AUD-05 的故障现状断言使用真实 Unity `6000.3.23f1` PlayMode 探针，XML 为 `total=1 passed=1 failed=0`；测试通过代表稳定复现现状，不代表产品行为正确。

## 验证范围

- 六个 .NET 测试项目共 `2436 passed`。
- `check.ps1 -SkipUnity` 原始结果为 `13 PASS / 1 FAIL / 6 SKIP`。唯一 FAIL 是 pytest 在 Windows PowerShell 默认编码环境中的路径边界测试；UTF-8 对照暴露 Windows PowerShell 5.1 缺少 `Get-FileHash`，pwsh 7.6.5 离线路径成功。该结果是本机工具链证据，不称为 CI 失败。
- Unity 相关定向回归为 `43/43`，另有 1 个故障现状 probe；该 probe 的“通过”是 `Assert.IsNull` 现状断言，详情见表现报告。
- 原始 1.6 zip 离线 pwsh 消费成功，6 个 DLL hash 与 lock 匹配；这不等于远端 Release、registry 或游戏消费已验证。
- 未执行全量 Unity、standalone、consumer Unity、IL2CPP、性能或线上发布验证。

## 文档更新与边界清单

需要将旧 pending 字段迁移承诺改为安全策略：旧实例 id 不总能映射稳定摆放键；无法判定时应安全丢弃旧段，只有存在明确映射时才迁移。需要在 SaveSystem 文档中明确缺段默认清空，并为每个“缺段即保留”的能力显式声明例外。需要把 API 改名列为 breaking change 或提供一个 minor 周期的 Obsolete alias。需要统一 slot_mesh 的资源种类、loader 和 renderer 合同，并在文档中保留当前样例缺少完整 inventory 行的限制。

未实现、未接入和非目标项应继续分栏：owner/day/vendor 装配参数、默认 catalog 覆盖边界、SimTime 生产接线、Replay 入口、DisplayMapCoverageRule、VFX anchor、武器 style resolver 等不能合并写成同一“框架缺失”。Unity、性能、真实游戏 Runtime 和用户验收也不能由 Native 或静态检查替代。

## 建议修复顺序与验收

1. 先处理 AUD-01 至 AUD-04 的兼容与存档语义。验收旧 pending 等价形状在明确映射时安全恢复、不可映射时安全丢弃；失败段的前段提交策略有文档和测试；缺 inventory/vendor 段不残留前一槽状态；vendor timer 按快照或明确重置语义运行；旧 consumer 要么由 Obsolete alias 编译通过，要么有明确 breaking change 升级路径。
2. 再处理 AUD-05 的 mesh 资源合同。验收独立 Mesh 或可提取 prefab 槽位资产能被 loader 正确解析，缺失时保留可见占位并有诊断；增加非空正向 rig 测试，不把故障现状断言当成正确性。
3. 最后处理装配扩展与文档。验收种族光环、owner/day/vendor、clip.events 等待验证链路各有真实入口、数据和边界测试。

本轮仅审计，未实施产品修复；结论以冻结基线及所列证据为准。
