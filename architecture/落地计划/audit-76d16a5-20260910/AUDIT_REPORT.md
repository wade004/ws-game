# ws-game 1.14.0 整体代码深度 review：文档—代码对照与交付门禁审计

## 结论

冻结的 1.14.0 源码与正式发行包版本、lock、七个 DLL entry hash 和四个 package manifest 均一致；脱离 Unity 的本轮门禁完成了构建、六工程测试、数据/schema、事件常量、资产工具和包清单检查。当前交付有六项已复现的 P2 问题，另有一项仅静态审计的工具风险：

1. 1.13.0 正式 consumer 只替换五个正式 Core DLL 到 1.14.0 后，因 `SkillHost` 17 参数构造签名消失而抛 `MissingMethodException`。这与 CHANGELOG 对 MINOR/旧 binary 兼容的文字冲突。
2. `check.ps1 -SkipUnity` 中 ABI 步骤在缺失基线时显示 PASS，但脚本实际以 `SkipIfBaselineMissing=$true` 跳过，因此该行不是 ABI 证明。
3. QuestHost 目标数组 reload 越界；EconomyHost none→timer 未建立计时器；StatHost 派生缓存与 reload 定义分叉；ExprValueJson 非法 Id 使正式 Quest 校验冒泡 `ArgumentException`。四项均有 core 独立 probe。

另有一个未做破坏性复现的工具安全问题：ABI 脚本对显式已有 `OutDir` 直接递归删除，缺少路径边界检查。它与上述运行时兼容性结果分开统计。

## 基线与范围

- 冻结 detached worktree：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`
- HEAD：`76d16a54e54f0f11204d97d7460563c8a0dc8cd8`
- VERSION：`1.14.0`
- 审计输出根：`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910`
- 原仓 `D:\workespace\ws-game` 只作为只读输入。审计期间主仓出现的未提交 `toolchain/registry/start_registry.ps1` 变动，以及未跟踪 `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py` 不属于冻结基线，未被修改或清理。
- 审计覆盖 architecture 00–14、ADR、能力索引、模板/module README、当前源码、正式包、工具链和独立 ABI consumer。旧 c9ff301 归档及 followup 仅用于演进背景，不重用旧测试数字。

责任分类、证据等级和每章对照见 [文档—代码矩阵](docs-project/doc-code-matrix.md) 与 [责任与证据边界](docs-project/scope-and-evidence.md)。

## 严重度与修复验收

### PJ114-01 — P2：SkillHost 旧二进制 ABI 断裂

契约 owner 是框架公开 ABI/发布维护者，条件是旧 consumer 只替换正式 DLL。实际证据是 1.13 formal consumer old run exit 0、new formal replacement exit 11，`MissingMethodException` 指向旧 17 参数构造器；脚本只替换五个依赖 DLL，未重建 consumer；保留的 consumer 程序集 SHA256 为 `13500df60474618a7960649f8ff1241a2cad460cfe576e30bbd033fd5457e00a`。

修复有两条可审计路径：A 保留 MINOR 兼容，恢复精确 17 参数转发入口，并要求旧 binary 替换运行 exit 0、旧源码重编译通过；B 认可破坏性变更，按工程规范升级版本/迁移契约，验证迁移后 consumer，通过后仍保留旧 binary 失败作为已知差异。只改 CHANGELOG 不能修复 ABI。

证据和命令见 [项目发现](docs-project/project-findings.md)、[验证记录](docs-project/validation.md) 与 `docs-project/logs/api-compat/`。

### PJ114-02 — P2：ABI gate 缺基线时的假 PASS

`check.ps1` 记录 22 步中的 ABI 行为 PASS；冻结树没有默认的 1.12.0 基线 ZIP，`abi_probe.ps1` 因默认 `SkipIfBaselineMissing=$true` 直接 exit 0，且 check 丢弃子进程 stdout。预期是可见 SKIP/无资格，或兼容性发布模式阻断，不能把该行算 ABI 通过。

最小验收是无基线时汇总不写 PASS；有基线时保存 old/new exit、consumer hash、DLL hash，并覆盖本次新增/变化的公开签名。

### CORE114-01 / CORE114-02 / CORE114-03 / CORE114-04 — P2：可选模块 reload/validation 运行时缺口

四项均是框架模块启用后的通用行为问题，具体游戏内容不是触发前提：

- QuestHost 在活跃任务目标从一条变为两条后继续更新时，`ObjectiveCounts` 旧长度导致 `IndexOutOfRangeException`；应迁移/调整进度或原子拒绝不兼容 reload。
- EconomyHost 既有 none 状态库存耗尽后 reload 为 timer，timer 仍为 null，Update 后库存保持 0；fresh host 可补回 2；应在定义转换时初始化/重采样 timer。
- StatHost 两个相同 unit 在 default_base reload 后出现先查者 0、后查者 77，而两者 base 都 77；应失效派生缓存或明确定义显式 base 与派生值。
- ExprValueJson 对 `{"$id":"BAD"}` 冒泡 `ArgumentException`；正式校验应输出 blocking report，合法联合值仍通过。

源码、输入条件、实际输出和最小验收见 [项目发现](docs-project/project-findings.md) 与 [core findings](core/core-findings.md)；current-v5/current-v3 日志由 [证据索引](evidence-index.md) 定位。四项是 A 级独立 probe 结果，不能用单元测试全绿或 fresh host 对照抹平。
### PJ114-03 — P2（静态工具风险）：ABI 输出目录无边界清理

`toolchain/abi_probe.ps1:116-117` 对显式已有 `$OutDir` 递归删除。本轮未用已有资料目录进行破坏性运行，因此这是源码审计，不与实际运行缺陷混计。修复后应默认生成唯一目录，显式已有目录直接拒绝或经过专用临时根和唯一前缀验证，并用 sentinel 证明边界。

## 交付门禁实际结果

一次命令 `check.ps1 -SkipUnity` exit 0，22 steps 为 16 PASS、6 SKIP、0 FAIL；六个 Unity 编译/测试/独立版/consumer 步骤全部 SKIP。脱离 Unity 的 .NET 六工程为 2,840 passed/0 skipped/0 failed，pytest 为 100 passed/2 skipped；合并数据 0 errors/3 warnings/1 override，framework 根 0 errors/1 warning，schema audit 63 tables/783 fields/0 errors/0 warnings，placeholder 92/92，sample import 0 issues。原始日志和 exit 文件见 [validation](docs-project/validation.md)。

Unity 代理独立保存的当前 1.14 验证为 EditMode 70/70、PlayMode 272/272，筛选 Deleted/表现探针按其独立 XML 记录；本 docs 子任务未重复运行，具体 oracle 与证据边界见 [presentation findings](presentation/presentation-findings.md) 与 [presentation scope](presentation/scope-review.md)。完整发布认证仍需不跳过 Unity 的 consumer smoke，以及适用时的发布形态/IL2CPP 证据。

## 正式包核验

只读正式包 `D:\workespace\ws-game\dist\ws-game-1.14.0.zip` SHA256 为 `68e4cdec66333931d679ea2ebd1c97d77e8bcca9675061c74b8d5531fd841561`；lock version 为 1.14.0、git commit 为 76d16a5。正式 ZIP 中六个 Unity Core DLL、headless `Adapters.Stub.dll` 的精确 entry 流 hash 全部与 lock 相等，四个 package manifest 精确 version 全为 1.14.0。这个结果只证明正式 ZIP 字节核验，不把 check 生成 DLL 当正式包运行证明。

## 解耦后的能力结论

Talent 完整点数生命周期没有既定 allocator 契约，需另行决定 owner/状态/迁移；召唤连续跟随已有，Discrete 是当前时间模型边界；Quest/escort 的窄 API 与宿主驱动责任分开；模板新局 starter 是可替换最小示例；WorldMap 点的 required 性取决于首个出生点/命名引用用途；导航预算、轨迹碰撞、day_cycle、ATB 和 editor 按本版契约边界处理。已实现但未默认接线、游戏内容责任、未提供能力、明确非目标和暂不落地不列为框架缺陷。

## 建议顺序

先确定 ABI 的兼容或破坏迁移路线，同时使缺基线门禁可判定；再收紧 ABI 工具输出目录清理；最后按可选模块契约补齐需要的 Runtime/consumer 证据。所有后续修复都应保留独立 oracle、输入版本、输出 hash 和适用条件。

相关文件：

- [文档—代码矩阵](docs-project/doc-code-matrix.md)
- [项目发现](docs-project/project-findings.md)
- [验证记录](docs-project/validation.md)
- [责任与证据边界](docs-project/scope-and-evidence.md)
- [core findings](core/core-findings.md)
- [presentation findings](presentation/presentation-findings.md)
- [presentation scope](presentation/scope-review.md)
- [证据索引](evidence-index.md)





## P3 与未确认候选

仅有一项已确认的 P3 文档漂移：`RewardSchemaFields.cs:9-13` 仍有“不能表达嵌套”的过时注释。ADR-0019 runtime-set coverage 载体说明、能力索引当前六类阅读指引属于已对齐/可选编辑性澄清，不计入 P3。Periodic `scaling_stat`、模板热重载、WorldMap 本版文档经当前源码复核已准确。Equipment set cache 仍只有静态候选，没有独立负例。逐项位置见 [文档—代码矩阵](docs-project/doc-code-matrix.md) 的更新清单。



