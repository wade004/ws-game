# AGENTS.md（面向执行 agent 的派单规则单一来源）

本文件是派发给执行 agent 的重复硬性规则的单一来源。今后派单提示词只写"先读取并遵守 AGENTS.md"，
不再逐条复述本文件已有的规则；派单提示词只写该任务特有的目标、范围与验收标准。

## 1. 工作树与 git

- 每个任务开独立工作树：`git -C D:\workespace\ws-game worktree add "<scratchpad>\<name>" -b <branch> main`。
- 所有 git 命令用 `git -C "<工作树绝对路径>"`；其它命令须在同一个工具调用内 `Push-Location … Pop-Location`（PowerShell/Bash 工具调用之间，当前目录会重置回主树）。
- 提交前先打印 `git -C "<工作树>" rev-parse --show-toplevel` 核对确实在工作树内。
- 只 `git add` 明确列出的路径；绝不用 `-A`/`.`/`--force`/`reset --hard`/`stash`。
- 不 merge、不 push（这两步由主会话执行）。
- 不改 `VERSION`/`package.json`/`packages-lock.json`/`games/_template/package.json`（这些文件由 `build.ps1 -Release` 统一写回）。
- 提交署名尾行固定 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`，不按执行 agent 自己的模型改名。
- 同一工作树同一时间只允许一个 agent 提交，避免并发写冲突。

## 2. 仓库硬性规则

- `architecture/00～14`、`architecture/adr/`、`数值设计/` 正文不出现任何引擎/语言/框架/工具名（`immunity` 例外）；具体技术名只允许写在 `architecture/选型/`、`architecture/落地计划/`、`toolchain/`、`editor/docs/`、`adapters/`、`docs/`。
- 全仓库任何文件不出现具体游戏代号，用 `<game>` 或 `sample_*` 占位。
- 改动架构结论要先出 ADR；只是细节勘误按 `architecture/12_扩展与变更流程.md` §5 处理——版本号不变、变更记录写"勘误："、关联 ADR 列 "—" 或对应 ADR 编号。
- 每个模块 README 的"判断记录"要记录关键取舍及理由，不能只改代码不留痕。
- 新增含非 ASCII 字符的 `.ps1`/`.psm1` 脚本必须带 UTF-8 BOM（无 BOM 在目标机 ANSI 代码页下会被误读，已有专门门禁测试）。
- 新文件统一 LF 换行。

## 3. 代码规则

- ABI 只新增：新增重载、新增类型、默认接口成员、可选属性；不删改既有公开签名、不给既有构造/方法加参数、不删已废弃字段（要退役用 `[Obsolete]` 之类的元数据标注，不物理删除）。
- 不删测试、不放宽既有断言、不加重试掩盖问题——先复现缺陷再根治，不能只是让测试变绿。
- 保持确定性：不依赖字典枚举顺序、`GetHashCode`、系统时间、当前文化；浮点格式化固定用不变文化（`InvariantCulture`）。
- 运行时路径不静默降级；只读分析类入口在遇到阻断态时降级要显式标记（不能悄悄吞掉问题当作正常返回）。

## 4. 构建与门禁

- `dotnet` 命令一律带 `--artifacts-path "<scratchpad>\build_<task>"`，不用仓库根默认路径，避免污染主树/工作树。
- Unity 只允许通过 `build.ps1`/`check.ps1` 以 `-batchmode` 方式调用，不直接手工开 Unity 编辑器操作工作树。
- **G1**：`dotnet build` 0 警告；`dotnet test Core.sln` 全过；`python -m pytest toolchain/tests -q` 全过；三套数据根跑 `validate_data.py --strict`——默认数据根与 `games/_template/data/game` 必须 warnings 为 0，`core/sim/tests/data` 只允许已确认的探针项警告。
- **G2**：`check.ps1 -Quick` 全 PASS。
- **G3**：`dotnet build -c Release --artifacts-path X` 之后跑 `toolchain\abi_probe.ps1 -BaselineZip "dist\ws-game-<上一版>.zip" -ArtifactsPath X -OutDir <scratchpad>\abi_<task>`，要求 breaks=0。
- **G4**：模块 README 的"判断记录" + `CHANGELOG.md` `[Unreleased]` 段新增条目——除非派单说明本次改动由后续整合单统一写变更记录。
- 涉及仿真相关改动，三份基线要零差异（`simrunner run --scenario all …`）。

## 5. 发布单专用

- 长时间命令前台执行，只管道 stdout（如 `| Tee-Object -FilePath <scratchpad>\release_X.log`）；绝不 `2>&1`、绝不用 `Start-Process`/隐藏窗口/后台作业驱动发布脚本（会让子进程在无控制台环境下静默中断）。
- 子 agent 启动的 `build.ps1 -Release` 会随该 agent 回合结束被连带终止（1.39.0 首跑在 `Compress-Archive` 处留下 0 字节 zip 与未打标签的发布提交）；因此**发布脚本只由主会话前台执行**，子 agent 不再直接启动 `-Release`，只做发布前收口与发布后核对。
- `build.ps1 -Release X -PublishRegistry` 之前，必须先：提交 CHANGELOG `## [X]` 条目、确认 `git status` 干净、确认私服状态（`start_registry.ps1 -Status`/需要时 `-Detach`）。
- 成功的唯一标记：日志末尾出现提示行 `git push origin main refs/tags/vX`。
- 失败标记：出现"门禁失败"/"发布流程终止"/"自检失败"字样。
- 一旦被拦截或失败，立刻停下汇报，不自行回退、不自行重试、不自行改动版本文件。
- 半途恢复流程（`git restore --staged --worktree` 四个版本文件 + 删 `dist/X`、`dist/release-notes-X.txt`）只能由主会话决定是否执行，执行 agent 不擅自做。
- 半途状态若"发布提交已产生但无标签"，恢复用 `git reset --soft <发布前提交>` 再按路径 `git restore --staged --worktree` 四个版本文件并删 `dist/X` 产物，不是只还原文件。
- Unity PlayMode 测试失败先用 `python toolchain/unity_test_triage.py` 分诊，不要直接改测试或改断言；
  分诊后核对断言是否在给"已知即将修复的旧错误行为"拍照（修复生效后断言过期是测试侧问题，不是
  回归），排除测试侧问题后才怀疑产品代码，详见
  `architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md`"标准流程清单"。
- 不要靠"用例之间清空全局资源缓存"解决 PlayMode 测试串味：`DontDestroyOnLoad` 单例的缓存字段
  可能有其它模块按同一份数据维护的去重表/索引等派生状态，清缓存不清派生状态会让资源永不重新
  加载，制造更隐蔽的新故障；应给失败用例配专属输入（专属资源引用字面量/数据行/占位资产），让
  断言不依赖执行顺序，详见
  `architecture/落地计划/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`。
- Unity 许可排障（Hub 界面显示 Personal 许可已激活，但批处理仍报错误码 198"无有效许可"）：Hub
  界面显示的是账号层面的授权状态，Unity 批处理真正读取的是本机磁盘上的许可文件（由 Unity 许可
  客户端写出、定期续签），两者会脱节。处理顺序：① 先在 Hub 里点"刷新"（通常会触发重新拉取并
  写盘，多数情况下这一步就能解决）；② 无效时退出账号重新登录；③ 仍无效则以管理员身份运行 Hub
  后重新激活；④ 移除许可后重新添加。已核实的相关事实：`check.ps1` 的 Unity 步骤只会因显式传
  `-SkipUnity`/`-Quick` 而 SKIP，许可失败会表现为 FAIL 而不是静默跳过；`check.ps1` 开跑前会
  检测同工程残留的 `Unity.exe` 进程并直接中止，因此跑门禁前不要在 Hub 里开着同一工程。
- PowerShell 工具前台跑 `-Release` 超过 600s 会被"转入后台继续运行"而非杀死，退出通知后再核对结果即可，不必视为失败。
- 后台任务的 output 文件含 stderr 全量（如 npm notice 上千行），不要 `Read` 整个文件，交子 agent `Grep`；管道过滤用 `Select-String '门禁通过|门禁失败| FAIL '`。
- 同一 minor 出补丁版时 `release/X.Y.x` 已存在，用非强制快进：先本地 `git push . <hash>:refs/heads/release/X.Y.x` 再 `git push origin <hash>:refs/heads/release/X.Y.x`，不用 `-f`。

## 6. 汇报格式

- 中文，先说结论，再给证据。
- 汇报要点：提交哈希、改动文件列表、每项修法各一句话说明、新增测试名、测试总数（含新增几例）、门禁结果、CHANGELOG 条目文本、待设计层确认的事项。
- 大段输出（完整日志、长表格）落盘到 scratchpad，汇报里只给路径和关键数字，不整段贴出来。
- 不擅自拍板设计冲突，遇到设计取舍未定的地方，明确标注"待设计层确认"。

## 7. 复审/验收单专用

- 只读：不改任何文件、不跑任何 git 写命令。
- 需要实证某处缺陷时，可以建一个临时工作树写一次性验证测试，验证结束后删除该工作树，不留痕在主树/正式分支。
- 报告落盘到 `<scratchpad>\review_<x>.md`，不直接改动被复审的文档/代码。
