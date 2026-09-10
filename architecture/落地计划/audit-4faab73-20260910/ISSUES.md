# ws-game 1.16.2 问题清单

与 [AUDIT_REPORT.md](AUDIT_REPORT.md) 同一冻结对象和证据边界。排序为 4 条 P2 + 5 条独立 P3；ABI 覆盖声明的文档更新并入 ABI-1162-01，不另计数。

## P2

| ID | 触发 | 预期 | 实际 | 源码证据 | 责任 | 修复验收 |
|---|---|---|---|---|---|---|
| V-01 | `schema_version=4294967297` | 大于 Int32.MaxValue 阻断 | 回绕为 1，`errors=0 blocking=False` | [DataRegistry.cs:737](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/data_registry/core/DataRegistry.cs:737) | DataRegistry 框架 | 转换前检查上界；大值报 `schema_version` Error，读屏障阻断。 |
| V-02 | `SetBalance=9007199254740993`，Save/Load | long 精确往返 | 写出/读回 `9007199254740992` | [CurrencyPersistable.cs:44](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/gameplay/economy/core/CurrencyPersistable.cs:44) | Economy 持久化格式 | 保留现有 Int64 合同，使用精确整数表示并完成 Save/Load 往返；safe-double 收窄不作为本轮等价修复，破坏性迁移需另行决策。 |
| V-03 | `new JsonNumber(9223372036854775808d)` 迁移到 Int | `TryGetInt64=false`、`field_type` 阻断 | `True/-9223372036854775808`，迁移后不阻断 | [JsonValue.cs:108](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/common/json/JsonValue.cs:108); [DataRegistry.cs:1004](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/data_registry/core/DataRegistry.cs:1004) | JsonValue/DataRegistry | 无原文 double 仅接受 `[-2^63,2^63)`；迁移路径出现 `field_type` Error。 |
| ABI-1162-01 | public indexer `int` 参数改 `string` | surface 报 break，旧 consumer 失败可见 | 旧 consumer `MissingMethodException`，surface `breaks=0` | [SurfaceDumper.cs:126](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/toolchain/abi_surface/SurfaceDumper.cs:126); raw [run5-console.log](delivery/run5-console.log):30-39 | ABI 工具链 | dump 编码 `GetIndexParameters()`；compare 报 break，保留旧 consumer smoke。 |

## P3 文档

| ID | 文档证据 | 事实 | 修复 |
|---|---|---|---|
| DOC-162-01 | DataRegistry README `:46-54`、XML `DataRegistry.cs:183-186` | 默认重复阻断之外，`AllowOverride + override:true` 可整行覆盖，`final:true` 阻断；`data/README.md:96-113` 已准确。 | 更新 README/XML 条件语义。 |
| DOC-162-02 | 落地计划 `:260` | 写三个 `.tgz`，当前正式交付为四个包。 | 改为四个并同步附件清单。 |
| DOC-162-03 | 落地计划 `:1368` | `WorldMapSchema.cs:60-79,92-99` 已登记 teleport nested item；引用目标完整性仍不由 schema 保证。 | 区分结构登记与引用/业务约束。 |
| DOC-162-04 | 落地计划 `:1363` | ATB 是合法预留值但 ADR-0013 本版延期，不是明确非目标。 | 改为延期/预留。 |
| DOC-162-05 | 04 `:362-363` | Number 的非有限值当前报 `field_finite`；Int 失败路径报 `field_type`；V-03 独立处理 Int 上界。 | 收窄 `field_finite` 文案范围。 |

## 不计入问题的边界

| 分类 | 项目 | 结论 |
|---|---|---|
| 游戏责任 | talent 完整 allocator | 无既定通用契约，游戏实现。 |
| 条件支持/上层消费 | TargetPoint | cast 意图可携带落点，SkillHost 不消费；与 teleport_points 不同。 |
| 条件支持/上层消费 | teleport_points | 元素结构已登记，命名点由 TeleportTargetResolver 解析，引用目标完整性不由 schema 保证。 |
| 条件支持 | 离散召唤/loot | SummonTickHandler 在 Discrete 跳过、duration 不推进；LootExpiryTickHandler 跳过清理但绝对时钟推进；扩大行为需 ADR。 |
| 未默认接线 | weaponVfx | Resolver 已构造暴露，无默认生产调用，调用方接线。 |
| 条件支持/调用方责任 | gatherClock | GobjOptions.SimTime 默认恒 0，调用方注入模拟时钟；刷新机制存在。 |
| 未默认接线 | Replay、Quest.Update、owner/day/vendor | API 存在，生产根默认不驱动或传 null。 |
| 延期 | ATB | 合法枚举/预留位存在，TurnScheduler 明确不支持。 |
| 明确非目标 | day_cycle | 计划与规则宿主明确限定；不构成框架待办。 |
| 条件支持/实现方边界 | 导航跨帧预算、空间完整索引化 | 02 将性能要求收窄为实现方边界；具体适配器实现不代表 Core 契约承诺。 |
| 建议项 | 孤儿记录检测 | 04 标为建议，不是本版门禁承诺。 |
| 未提供 | 跨 aura 定义共享槽位 | ADR-0023 已明确静态分组与按定义分槽。 |

旧 DOC-116-01～04、TOOL-116-01，以及 F-01/F-02/F-03 已有对应修订或成功复测，见 [AUDIT_REPORT.md](AUDIT_REPORT.md) 的关闭项。
