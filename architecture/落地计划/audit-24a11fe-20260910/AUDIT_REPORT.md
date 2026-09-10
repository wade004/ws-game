# ws-game 1.16.1 文档与代码/项目深度审计

审计基线：冻结副本 `D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD `24a11fe28f9647cd532c41f56f7ab18c00fb8516`；输出目录 `D:\workespace\ws-game-artifacts\audit-24a11fe-20260910`。主仓 tracked 源码保持未改；审计构建曾产生 ignored 中间物，正式 ZIP 始终只读。

## 当前结论

文档与工具链审计已形成可复核证据。唯一一次 `check.ps1 -SkipUnity` 共 24 steps：17 PASS、7 SKIP；机器计数见 `docs-project/check-summary-count.txt`。默认 ABI 因缺少 baseline 明确为 SKIP，不能写成全量验证 PASS。显式以正式 1.12.0 包为 baseline 的旧 consumer→当前六 DLL ABI probe 通过；同一旧 consumer 换入正式 `ws-game-1.16.1.zip` 流式读取的六 DLL 后也通过。正式 1.12/1.14/1.15/1.16/1.16.1 ZIP 的 lock、同名 DLL 副本 hash 和 manifest 均已流式核对通过。

非 Unity 的六个 .NET 工程共 3001/3001（127+813+449+398+564+650），toolchain pytest 为 161 passed、4 skipped；这些数字来自当前 transcript，不与 bounded runner 的重复子集相加。

当前需更新的文档集中在 ADR 索引计数、Skill `FindUnits` 旧描述、DataRegistry reference_integrity/field_range 检查项和 ADR22 wrapper JSON 命令边界。另确认 ABI surface 对 public→protected 的 visibility 变化漏报：自包含最小旧 consumer 实际抛 `MethodAccessException`，而 surface compare 仍 `breaks=0`；这是门禁覆盖 finding，不表示 1.16.1 当前 DLL 已发生该破坏。schema agent 另外确认 F-01 非有限 Number 放行、F-03 Expr 整数溢出外抛为 P2，F-02 非有限 Range 边界为 P3；证据来自 .NET 8 内容工具/框架加载路径。

## 问题汇总

| 类别 | ID | 实际结果 | 预期/处置建议 |
|---|---|---|---|
| 已确认 P2：框架数据合同 | F-01 | 真实 `skill.aura_def` 的合法 JSON `1e309` 读成 Infinity，`errors=0 blocking=False` 且记录可读 | Number 加载/校验入口至少一处拒绝非有限值，保留正常 exponent |
| 已确认 P2：框架表达式合同 | F-03 | 真实 `skill.proc_def.condition` 的 `9223372036854775808` 从 `LoadAll` 外抛 `OverflowException`，之后 `GetAll` 仍可读 count=1 | 归一为带位置的 `ExprParseException`，并保证 LoadAll 阻断/状态语义 |
| 已确认 P2：ABI 工具门禁 | ABI-116-01 | public→protected 的最小库替换使旧 consumer 抛 `MethodAccessException`，surface compare 仍 `breaks=0` | surface dump/compare 记录 method/ctor/field/event visibility，并加入 public→protected 负例；旧 consumer 作为辅助证据 |
| 已确认 P2：文档问题 | DOC-116-02 | `FindUnits` README 仍声称恒空/未接线 | 按默认 `Spatial` 注入和 null-only fallback 更新 README |
| P3：文档/工程说明 | DOC-116-01/03/04、TOOL-116-01 | ADR 计数、DataRegistry `IdList`/`field_range` 检查项、ADR22 wrapper/direct validator 命令边界、`-SkipUnity` ignored dist 副作用说明漂移 | 更新索引与命令/副作用说明；不改产品能力 |
| P3：相关 API 观察 | F-02 | `FieldRange` 接受 NaN/Infinity 边界，`Contains(0)` 可反常为 true | 与 F-01 统一 finite policy，单列观察，不升级为独立高优先级能力缺失 |

CORE114-01 QuestHost reload 目标迁移、CORE114-02 Economy 策略转换、CORE114-03 StatHost derived cache、CORE114-04 ExprValueJson 非法 Id 正式校验均已由 core bounded runner 复测通过，当前未复现历史 P2；证据见 `core/core-findings.md` 及其 `logs/runner-current.log`。Equipment set threshold 仅保留“reload 后需下一次装备变化触发清理”的已知时机边界，不计本轮 P2。

Unity 有界复核在独立临时副本完成：full EditMode `70/70`、full PlayMode `272/272`，各自 exit 0；定向 XML 也已保留。Unity 插件中的六个 Core/Presentation DLL 与本轮 `check/artifacts/bin/<assembly>/release` 来源逐项 hash 一致。该运行使用复制到 Unity 临时副本的 check DLL，不等同于正式 ZIP 已运行 Unity；正式 ZIP 仅有独立流式 lock/hash 与旧 consumer ABI 证据。详见 `presentation/presentation-findings.md` 及 `presentation/hashes/`。

## 责任边界

ATB 是预留延期项，本版不执行；`NewGameStarter` 未注入后的抛错属于游戏/宿主起始状态责任；`NotSupportedException` 占位/降级扫描不构成缺口；默认不登记 `gobj`，自定义共用空间索引由宿主负责过滤。talent allocator、escort、轨迹碰撞及具体内容按游戏责任处理；Quest 状态机和框架机制仍属于框架审计对象。

## 证据入口

详见 [docs-project/README.md](docs-project/README.md)、[docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)、[docs-project/project-findings.md](docs-project/project-findings.md) 和 [docs-project/validation.md](docs-project/validation.md)。check 原始日志在 `logs/check-skipunity-transcript.txt`；schema 与 Unity 最终证据分别见 `schema/`、`presentation/`，均已纳入本报告。

## 当前状态

`AUDIT_COMPLETE`: schema、core、Unity 证据已归并；完整性已由安全归档脚本复核。最终 ZIP 仅纳入报告、复现源码、raw logs、hash manifests，并排除 build/bin/obj/Library、DLL/EXE/ZIP。Standalone、IL2CPP 与 consumer rehearsal 未在本轮 `-SkipUnity` 检查中执行，分别保留为 SKIP 边界。
