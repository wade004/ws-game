# ws-game 1.18.0 深度审计归档（codex 第十八轮，基线 d6fda65）

本目录归档 codex 第十八轮外部深度审核（`D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\`）的
证据与报告原件。权威基线是主检出提交 `d6fda65cd6b00ede5c5f6f606f724d2ab0f60603`（`VERSION=1.18.0`）；
原始输出根在归档提交后仍保留于 `D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\`，本目录是其
可长期随仓库留存的子集副本。

## 原件未归档

以下路径在原始审计输出根中存在，但按任务边界（`AUDIT_REPORT.md`/`docs-project/`/`core/`/`presentation/`/
`validation/`/`delivery/` 的 md/日志/源码子集）或体积/重复内容/构建产物原则未纳入本次归档：

| 路径（相对原始输出根） | 大小 | 原因 |
|---|---|---|
| `evidence.zip` | 728KB | 任务边界明确排除的证据封存包，与已展开的原始文件内容重复 |
| `evidence.manifest.txt`、`evidence.sha256` | 13KB | 描述上述 `evidence.zip` 的清单/哈希，随其一并不纳入 |
| `docs/` | 0（空目录） | 原始输出根内此目录为空，无内容可归档 |
| `presentation/UnityCopy/` | 约 1.6GB | 完整 Unity 工程镜像副本（`adapters`/`assets`/`data`/`games` 的完整拷贝 + `Library/`/`Bee/`/`BurstCache` 等引擎生成缓存），与仓库本身内容重复，不是独立证据；权威结果已由本目录保留的 `presentation/playmode-selected.xml`（Unity 选测结果）、`presentation/presentation-review.md`、`presentation/evidence-hashes.json` 记录 |
| `validation/build/source/`、`validation/rerun-worldstate-20260911/build/source/` | 各约 9.7～9.9MB | 隔离重建用的 `core`/`presentation` 源码镜像，逐字节等同于仓库本身在该提交下的源码，不是独立证据 |
| `validation/build/pconsumer/`、`validation/rerun-worldstate-20260911/build/pconsumer/` | 各约 2MB | 六个核心 DLL + Presentation 消费程序的编译产物（`.dll`/`.pdb`/`.deps.json`/`.exe`/`.runtimeconfig.json`），按二进制产物规则排除；除 `schema_audit_allowlist.json` 已单独保留外，行为结论已由 `validation/raw/` 下的日志完整记录 |
| `validation/release-repair/ws-game-1.18.0-mutated.zip`、`pair-original/ws-game-1.18.0.zip`、`pair-repaired/ws-game-1.18.0.zip` | 各约 54MB | 篡改副本自检用的正式发布 zip 及其两次拷贝，超 2MB 体积上限；`ws-game-1.18.0-original.lock`/`ws-game-1.18.0-repaired.lock` 两份锁文件本体与 `get-framework-original.log`/`get-framework-repaired.log` 两份校验行为日志已保留，足以还原结论 |
| `validation/release-repair/pair-original/target/` | 0（空目录） | 篡改副本 + 正常 lock 场景校验阻断，未落地任何内容，原始目录本就是空的 |
| `validation/release-repair/pair-repaired/target/` | 约 75MB | 篡改副本 + 修复分支风格 lock 场景下（根治前）跳过校验后落地的完整框架目录（`assets`/`data`/`packages`/`toolchain` 等），与仓库/`dist/` 内容重复，不是独立文本证据；"跳过校验并成功落地"这一结论已由 `get-framework-repaired.log` 完整记录 |
| `validation/repro/obj/` | 278KB | `dotnet build` 中间产物，非探针源码本身 |
| `docs-project/core-independent/invalid-first-source-duplicate.log` | 8.4MB | 超 2MB 体积上限；该日志本身按 `docs-project/core-independent/run.log` 类型注释已被标注"首次工程构造误包含源码，不作正式 DLL 证据"，正式证据是同目录保留的 `run.log` |
| `docs-project/core-independent/bin/`、`obj/` | 约 2MB | 独立消费程序编译产物，按二进制产物规则排除；结论已由同目录 `run.log`、`formal-dll-verification.json` 记录 |
| `docs-project/abi-property/{consumer,new,old,surface-tool}/{bin,obj}/` | 约 680KB | ABI 属性负例探针各工程的编译产物；结论已由同目录 `result.json`、`consumer-new.log`、`surface-report.txt` 等记录 |
| `docs-project/abi-formal-112-to118/abi_surface_tool/`、`consumer/lib/`、`current-lib/` | 约 2.8MB | 严格 ABI 验证用的工具/消费方/DLL 编译产物；结论已由同目录 `summary.txt`、`surface-report.txt`、`run-against-baseline.log`、`run-against-current.log` 记录 |
| `docs-project/abi-formal-input/` | 1.3MB | 1.12.0 基线 DLL 输入副本（二进制），非文本证据 |
| `presentation/probe/lib/`、`bin/`、`obj/` | 约 3MB | Presentation 探针依赖的六个核心 DLL 拷贝与编译产物；探针源码（`Program.cs`、`ViewBindingTestSupport.cs`、`Probe.csproj`）与 `probe.log`、`assembly-hashes.json` 已保留 |
| `presentation/build/` | 8.3MB（全为 `bin`/`obj`） | 过滤后此目录不含任何非构建产物文件，整体未归档 |
| `core/probe/bin/`、`obj/` | 约 4.2MB | 任务/施法探针的编译产物；探针源码（`Program.cs`、`QuestProbe.cs`、`Probe.csproj`、`Directory.Build.props`）与 `probe.log` 已保留 |
| `delivery/output/build/check/bin/`、`obj/`（六个工程） | 约 54MB | `check.ps1` 全量步骤重新编译产生的构建中间物；门禁结果已由同目录 `abi_probe.log` 与 `delivery/output/raw/check.log` 记录 |

原始输出根本身（含上述未归档路径）不受本次归档影响，仍按其自身生命周期保留在
`D:\workespace\ws-game-artifacts\` 下。归档时用统一规则筛选：任务范围限定为 `AUDIT_REPORT.md`/
`docs-project/`（含 `abi-property`、`core-independent` 的源码与日志）/`core/`/`presentation/`
（含 `playmode-selected.xml`）/`validation/`/`delivery/` 的 md/日志/源码子集；在此范围内进一步排除
任何 `bin`/`obj` 目录、`*.dll`/`*.exe`/`*.pdb`/`*.deps.json`/`*.runtimeconfig.json` 文件、单文件
超过 2MB 的文件，以及与仓库/`dist/`/引擎工程内容逐字节重复的源码或落地目录镜像。全部文本文件确认
本就是 LF 行尾（原始日志逐一核对无 CR，未发生需要转换的情况）、UTF-8 编码（未发现 UTF-16 BOM）。
对本目录全量文本做不区分大小写的具体游戏代号禁用词扫描（与 `check.ps1` 全量步骤同一扫描口径），
命中数为 0。

## 先读

- [AUDIT_REPORT.md](AUDIT_REPORT.md)：总报告，本轮结论（7 条 P2 + `grid_snap` 通用能力决策 +
  九类文档更新）、问题汇总表、能力边界与责任、验证与发布证据、覆盖与未读范围。
- [docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)：逐章文档—代码对照矩阵、
  文档更新清单、未实现/未接线/游戏责任分类表。
- [docs-project/abi-property/](docs-project/abi-property/)：TOOL-118-ABI 反例证据
  （`result.json`、`consumer-new.log`、`surface-report.txt` 等）。
- [docs-project/abi-formal-112-to118/](docs-project/abi-formal-112-to118/)、
  [docs-project/abi-formal.log](docs-project/abi-formal.log)：用 1.12.0 正式基线对当前 1.18.0
  六个核心 DLL 重跑的严格 ABI 验证，`breaks=0, additions=275`。
- [docs-project/core-independent/](docs-project/core-independent/)：正式 Core DLL 独立消费程序复现
  （`run.log`、`formal-dll-verification.json`）。
- [core/core-review.md](core/core-review.md)：核心侧模块覆盖、CORE-118-QUEST/CORE-118-CAST 两条缺陷、
  能力边界、补充静态检查。
- [presentation/presentation-review.md](presentation/presentation-review.md)、
  [presentation/playmode-selected.xml](presentation/playmode-selected.xml)：表现与适配审核、
  Unity 选测结果（本轮 10 项，9 通过、1 失败——PRES-118-CAMERA 新增镜头行为 oracle）。
- [validation/validation.md](validation/validation.md)：门禁、正式包身份、ABI 基线、Python 环境、
  交付实验；配 [validation/raw/](validation/raw/)（原始日志）、
  [validation/release-repair/](validation/release-repair/)（TOOL-118-LOCK 复现：篡改
  `Adapters.Stub.dll` 后正常 lock 阻断、修复分支风格 lock 根治前跳过校验的对照）、
  [validation/repro/](validation/repro/)（Presentation 消费程序探针源码）。
- [followup-2026-09-11.md](followup-2026-09-11.md)：本轮修复跟进核实表（7 条 P2 + `grid_snap` +
  九类文档；本 agent 负责 TOOL-118-ABI/TOOL-118-LOCK 与除 `grid_snap` 外的九类文档行填齐，其余由
  并行执行的核心/表现层 agent 落地，提交哈希由整合 agent 回填）。

## 目录结构

```
AUDIT_REPORT.md                 总报告
README.md                       本文件
docs-project/
  doc-code-matrix.md              文档—代码对照矩阵
  abi-formal.log                  1.12.0→1.18.0 严格 ABI 验证日志
  abi-formal.exit.txt             上述验证退出码
  final-worktree-state.json       冻结工作树状态快照
  presentation-independent.log    主审重跑的表现探针
  abi-formal-112-to118/           严格 ABI 验证的基线/当前 dump、compare 报告（已排除工具/DLL 产物）
  abi-property/                   TOOL-118-ABI 反例：result.json、consumer-old/new.log、
                                   surface-report.txt、compare.log 等（已排除各子工程 bin/obj）
  core-independent/                正式 Core DLL 独立消费复现：run.log、formal-dll-verification.json
core/
  core-review.md                   核心侧审核报告
  source-and-evidence.sha256       源码与证据哈希清单
  probe/                           任务/施法探针源码（Program.cs、QuestProbe.cs、Probe.csproj、
                                   Directory.Build.props）+ probe.log（已排除 bin/obj）
presentation/
  presentation-review.md           表现与适配审核报告
  playmode-selected.xml/.log       Unity 选测结果与日志
  evidence-hashes.json             证据哈希清单
  run-selected-unity.ps1           选测触发脚本
  unity-process-id.txt             Unity 进程快照
  probe/                           表现探针源码 + probe.log、assembly-hashes.json（已排除 lib/bin/obj）
validation/
  validation.md                    执行验证总记录
  generate_evidence.py、package_evidence.py   证据生成/打包脚本
  identity.json、markdown-links.md、tracked-modules.md
  raw/                             check.ps1 -SkipUnity、pytest 原始日志等
  release-repair/                  TOOL-118-LOCK 复现：release-repair.ps1、release-yaml-excerpt.txt、
                                   两份 lock 本体、get-framework-{original,repaired}.log
                                   （已排除篡改用大 zip 与落地目录镜像）
  repro/                           Presentation 消费程序探针源码（已排除 obj）
  rerun-worldstate-20260911/       另一次独立重跑的原始日志（已排除源码/编译产物镜像）
delivery/
  output/
    sentinel.txt                    哨兵文件
    raw/check.log                   check.ps1 全量步骤日志
    build/check/abi_probe.log       ABI 探针日志（已排除六工程 bin/obj）
```

## 重现原则（与原始输出根一致，转录自
[../audit-4faab73-20260910/README.md](../audit-4faab73-20260910/README.md) 同类归档惯例）

原始日志/XML 和证据目录保持不可变，本归档同样不回头改写已复制的原始日志/XML 内容（除本 README 与
新增的 followup 文档外）。任何重跑都应在新的临时目录进行，不直接在本目录或原始输出根执行会覆盖日志
的 runner；具体重现命令见 [AUDIT_REPORT.md](AUDIT_REPORT.md) 与 `validation/validation.md`、
`validation/release-repair/` 下的脚本。

## 证据边界

本轮是深度质量审核，不是发布认证，也不是任何具体游戏的验收。未做完整 Unity 全套、Standalone 全套、
IL2CPP、完整独立 Unity 消费方演练或在线私服验证；不使用旧版本的成功日志代替。TOOL-118-ABI 是"工具
盲点"finding——反例证明的是"public 实例属性改成同名同类型 static 属性时 ABI 工具漏检"，不代表 1.18.0
当前正式公开属性已发生该变化；本轮用修复后的新版 `toolchain/abi_surface`（属性/索引器/事件访问器补齐
static/virtual/abstract/final 编码）对 1.12.0（完整探针）、1.13.0（dump/compare）两份历史基线重跑
当前工作树，结果均为 `breaks=0`（additions 分别为 275、186），未发现历史真实破坏（见根目录
`toolchain/README.md`"`abi_surface` dump/compare 覆盖范围补齐（TOOL-118-ABI 根治）"一节）。
TOOL-118-LOCK 的复现同样是"修复分支重建的 lock 缺附加字段"这一具体场景的反例，不代表任何已发布正式
lock 文件本身缺失字段——本机 `dist/ws-game-1.18.0.lock` 用修复后 `toolchain/_lock_writeback.ps1` 的
`New-WsGameLockObjectFromZip` 重新生成与仓库已发布的同名文件逐字段一致（见
`toolchain/tests/test_lock_writeback_repair_parity.py`）。

旧 1.11～1.17.0 审计 ZIP/candidate 只作历史输入；旧 raw 与结论不覆盖当前 1.18.0。
