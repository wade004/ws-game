# 回归记录

只记录**全量回归**（见 `AGENTS.md` §4"回归分级"：里程碑收口 / 升级框架或依赖版本之后 / 改了
生产装配入口或多模块共享数据之后，三种时刻之一）的结果；日常切片的定向重跑（自身运行时冒烟 +
被直接波及模块的定向重跑）不记录在此，落在各自的提交信息与模块 README"判断记录"里。

每轮只追加一行，不改历史行。字段：`run_id | 通过/失败 | 对应提交 sha | 日期`。
`run_id` 格式 `full-YYYYMMDD-NN`（当天第 N 次全量回归，从 01 起）。

| run_id | 通过/失败 | 对应提交 sha | 日期 |
| --- | --- | --- | --- |
| full-20260920-01 | 通过（31/31） | f7a76cc6 | 2026-09-20 |
| full-20260920-02 | 通过（31/31） | 7f4e15c4 | 2026-09-20 |
| full-20260921-01 | 通过（31/31，695s；首跑因消费方项目残留 Unity 进程占用实例失败，进程自行退出后重跑通过） | d8a15139 | 2026-09-21 |
| full-20260921-02 | 通过（31/31，488s） | 98f877c3 | 2026-09-21 |
| full-20260921-03 | 通过（31/31，448s） | 35d79c8f | 2026-09-21 |
| full-20260921-04 | 通过（31/31，450s） | afcc93bc | 2026-09-21 |
| full-20260921-05 | 通过（31/31，477s；首跑 Unity 编译检查因包管理器 IPC 断连失败，同轮其余 Unity 步骤均通过，重跑通过） | c013260d | 2026-09-21 |
| full-20260921-06 | 通过（31/31，561s；首跑消费方演练被其它工程并行 Unity 进程阻塞，修等待范围后重跑；第二跑打包自检因工作树未跟踪文件记 -dirty，订正提交 f31bbaab 后第三跑通过） | 1526e3c5 | 2026-09-21 |
| full-20260922-01 | 通过（31/31，552s） | 6228455f | 2026-09-22 |
| full-20260922-02 | 通过（31/31，486s；首跑 pytest 失败于 ADR-0065 链接折行截断，修复提交 7735d13d 后重跑通过） | 4753b500 | 2026-09-22 |
| full-20260922-03 | 通过（31/31，门禁墙钟 368s，步骤求和 592s；首次两线并行） | 9231f639 | 2026-09-22 |
| full-20260922-04 | 通过（31/31，门禁脚本总墙钟 338.8s） | ab91cebc | 2026-09-22 |
| full-20260922-05 | 通过（31/31，门禁脚本总墙钟 369.9s） | ff535852 | 2026-09-22 |
| full-20260922-06 | 通过（31/31，门禁脚本总墙钟 353.1s） | 0ff6ee62 | 2026-09-22 |
| full-20260922-07 | 通过（31/31，门禁脚本总墙钟 306.1s；前两跑 pytest 私服用例在并行门禁下被中断失败，同一提交第三跑通过，根因另行排查） | 7063c9cd | 2026-09-22 |
| full-20260923-01 | 通过（32/32，门禁脚本总墙钟 315.7s；前两跑失败：新增 Unity 用例缺 using 致编译失败、包清单一致性与消费方演练撞 dist 不可变守卫，分别由 4b232a13/c693beb8/80423b98 修复） | 737fed21 | 2026-09-23 |
| full-20260923-02 | 通过（32/32，门禁脚本总墙钟 336.5s；首跑失败于 SpriteEquipVisualWiringTests 真实朝向用例——否定断言读共享加载器缓存、依赖用例执行顺序，由 bcc5eef5 改为断言实际应用的层资源后通过） | 53868e50 | 2026-09-23 |
| full-20260923-03 | 通过（32/32，门禁脚本总墙钟 367.1s；首跑 2 步失败：ADR-0074 样例行与既有行共用同一条物理 resource_ref 致重导入报错，连带 test_import_sample_assets 一并失败，由 abf80d0b 修复） | abf80d0b | 2026-09-23 |
| full-20260923-04 | 通过（32/32，门禁脚本总墙钟 421.4s） | 8b6f3d2f | 2026-09-23 |
| full-20260923-05 | 通过（32/32，门禁脚本总墙钟 407.7s） | 6c5a13e6 | 2026-09-23 |
| full-20260924-01 | 通过（32/32，门禁脚本总墙钟 385.6s；1.67.0 发布门禁。前一跑失败于消费方演练连挂 3 步——残留 Unity 进程归属判定的 -projectPath 正则匹配不到逐参数加引号的命令行，退化成 Unknown 后保守等待，把另一个仓库里正常运行的 Unity 批处理等满 60s 超时，由 eefd4df9 修复） | eefd4df9 | 2026-09-24 |
| full-20260924-02 | 通过（32/32，门禁脚本总墙钟 396.1s；1.68.0 发布门禁。前一跑 Unity PlayMode 翻红一例——音效可观测性新用例的预热断言写成 IsLoaded && warmedUp，而加载器缓存是整个 PlayMode 会话共享的，全量下资源已被更早用例加载、IsLoaded 立即为真而本次 LoadAsync 回调未及执行，单跑绿全量红，由 999f568a 改为只断言 IsLoaded） | 999f568a | 2026-09-24 |
| full-20260925-01 | 通过（32/32，门禁脚本总墙钟 424.6s；1.69.0 发布门禁，一次通过） | 96536d0c | 2026-09-25 |
| full-20260925-02 | 通过（32/32，门禁脚本总墙钟 447s；1.70.0 发布门禁。前两跑：第一跑 ABI 探针拦下——执行层给 AiHost/SummonTickHandler 公开构造追加可选参数改变物理签名，由 bb3d75d2 补回旧签名转调重载；第二跑发布前置校验拦下——工作树有未跟踪答复稿） | bb3d75d2 | 2026-09-25 |
| full-20260925-03 | 通过（32/32，门禁脚本总墙钟 400.7s；1.71.0 发布门禁，一次通过） | 5e68a851 | 2026-09-25 |
| full-20260926-01 | 通过（32/32，1.72.0 发布门禁，一次通过） | 2a0ccfcb | 2026-09-26 |
| full-20260926-02 | 通过（32/32，1.73.0 发布门禁，一次通过） | b9ca114b | 2026-09-26 |
| full-20260926-03 | 通过（32/32，1.74.0 发布门禁。前一跑消费方演练第 1 步失败——Unity 包管理器 IPC 断连、包解析 1.47s 后取消、3.6s 退出码 1、error CS 0 处，同工程后续 4 步全过，判定为环境抖动，未改代码直接重跑） | a437e973 | 2026-09-26 |
| full-20260926-04 | 通过（32/32，1.75.0 发布门禁，首次在短路径工作树 D:\wt\<名> 发版。前三跑分别被新工作树环境缺口拦下：①未跑 build.ps1 -SyncContent；②PlayMode 用例依赖主检出里的过期扁平 paperdoll 产物（已修 a0880576）；③工作树缺私服 .npmrc/htpasswd。均非产品代码问题） | 8d5fa86d | 2026-09-26 |
| full-20260926-05 | 通过（32/32，1.76.0 发布门禁，一次通过） | f5e66989 | 2026-09-26 |
| full-20260926-06 | 通过（32/32，1.77.0 发布门禁，一次通过） | cdeb3722 | 2026-09-26 |
| full-20260926-07 | 通过（32/32，1.78.0 发布门禁，一次通过） | 86953277 | 2026-09-26 |
| full-20260926-08 | 通过（32/32，1.79.0 发布门禁，一次通过） | 108c59e5 | 2026-09-26 |
| full-20260926-09 | 通过（32/32，1.80.0 发布门禁，一次通过） | ed15ad62 | 2026-09-26 |
| full-20260927-01 | 通过（32/32，1.81.0 发布门禁，一次通过） | fe26e748 | 2026-09-27 |
| full-20260927-02 | 通过（32/32，1.82.0 发布门禁，一次通过） | 3b04a109 | 2026-09-27 |
| full-20260927-03 | 通过（32/32，1.83.0 发布门禁，一次通过） | 1eef7005 | 2026-09-27 |
| full-20260927-04 | 失败（PlayMode 3/342 红：ADR-0103 续推重复失败通知 ×2、ADR-0104 复现用例未做渲染隔离；修复 aca07307/9f5d5578） | 50def3b9 | 2026-09-27 |
| full-20260927-05 | 通过（32/32，1.84.0 发布门禁，PlayMode 342/342） | 9f5d5578 | 2026-09-27 |
| full-20260927-06 | 通过（32/32，1.85.0 发布门禁，PlayMode 350/350，一次通过） | 961b1701 | 2026-09-27 |
| full-20260928-01 | 通过（32/32，1.86.0 发布门禁，PlayMode 350/350，一次通过） | dd9c5d5a | 2026-09-28 |
| full-20260928-02 | 通过（32/32，1.87.0 发布门禁，PlayMode 350/350，一次通过） | b73a5e02 | 2026-09-28 |
| full-20260929-01 | 门禁通过（32/32，PlayMode 352/352）；打包阶段并行测试里计时探针误报（平均 184.59ms），未出包 | 1c156594 | 2026-09-29 |
| full-20260929-02 | 门禁通过（32/32）；打包阶段同一条计时探针再次误报（平均 175.84ms），未出包；探针改按最小耗时判定 beff0e8f | f10220c7 | 2026-09-29 |
| full-20260929-03 | 失败（PlayMode 351/352：音效自然播完计时用例在机器重负载下超时，环境性，未改代码） | beff0e8f | 2026-09-29 |
| full-20260929-04 | 通过（32/32，1.88.0 发布门禁，PlayMode 352/352） | beff0e8f | 2026-09-29 |
| full-20260929-05 | 通过（32/32，1.89.0 发布门禁，PlayMode 354/354，一次通过） | b88836cf | 2026-09-29 |
| full-20260930-01 | 通过（32/32，1.90.0 发布门禁，PlayMode 356/356，一次通过） | a06f914f | 2026-09-30 |
| full-20260930-02 | 通过（32/32，1.90.1 发布门禁，PlayMode 358/358，一次通过） | e31f852c | 2026-09-30 |
| full-20260930-03 | 通过（32/32，1.91.0 发布门禁，PlayMode 362/362，一次通过） | eb8c36a4 | 2026-09-30 |
| full-20261001-01 | 通过（32 步：29 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL；EditMode 91/91、PlayMode 362/362，ADR-0121 十条缺陷收口；首跑卡在 consumer_smoke 私服冷启动（环境性，见汇报），私服起好后重跑通过） | d856bd31 + 收口提交 | 2026-10-01 |
| full-20261001-02 | 通过（G1 全量部分，因改 Directory.Build.props 多工程共享构建配置触发：dotnet build Core.sln -c Release 0 警告 0 错误；dotnet test Core.sln -c Release --no-build 总计 5329、通过 5327、失败 0、跳过 2；check.ps1 -SkipUnity -Quick 30 步通过；未跑 Unity 步骤与 check.ps1 非 Quick 全量） | 9efb4dfa | 2026-10-01 |
| full-20261001-03 | 通过（测试覆盖第二批收口，check.ps1 全量含 Unity：33 步 = 30 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL；dotnet test 5342 通过 0 跳过、pytest 661 通过 0 跳过、EditMode 91/91、PlayMode 362/362；用例数下限首次实跑全部达标；ABI 探针 breaks=0；序号顺延为 -03，-02 已被同日手册提交占用） | 19c6f426 | 2026-10-01 |
| full-20261001-04 | 通过（测试覆盖第三批收口，check.ps1 全量含 Unity：33 步 = 30 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL；dotnet test 6439 通过 0 跳过、pytest 661 通过 0 跳过、EditMode 91/91、PlayMode 362/362（含 MovementStopAndBlockingPlayModeTests 11/11）；ABI 探针 PASS；用例数下限 dotnet_test 抬至 5790） | 889a9736 + 收口提交 | 2026-10-01 |
| full-20261001-05 | 通过（测试覆盖第四批收口，check.ps1 全量含 Unity：35 步 = 32 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL / 0 环境性 SKIP，脚本墙钟 772s；dotnet test 8034 通过 0 跳过、pytest 869 通过 0 跳过（I-5 6c PYTHONUTF8 重跑 869/869、6d PowerShell 5.1+7 各 380/380）、Unity EditMode 185/185、PlayMode 374/374；ABI 探针 RESULT=OK breaks=0；用例数下限四项抬至 7230/780/160/330） | 085a3be6 + 收口提交 ca958951 | 2026-10-01 |
| full-20261001-06 | 通过（1.92.0 发布阻塞修复后重跑，pwsh check.ps1 全量含 Unity：35 步 = 32 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL / 0 环境性 SKIP，脚本墙钟 764s；dotnet test 8034 通过 0 跳过、pytest 872 通过 0 跳过（6c 872/872、6d PowerShell 5.1+7 各 380/380）、含 Unity：EditMode 185/185、PlayMode 374/374；ABI 探针 PASS） | 52296498 | 2026-10-01 |
| full-20261001-07 | 通过（合并前，分支 feature/ai-transformation_20261001：合并 feature/targeted-gate 与 feature/branch-version-conventions_20261001 后在 D:\wt\ai-transformation 短路径工作树跑，check.ps1 全量不带 -Quick/-SkipUnity：36 步 = 33 PASS / 3 SKIP（IL2CPP 三步未开 -Il2cpp）/ 0 FAIL / 0 环境性 SKIP，脚本墙钟 814s；dotnet test 8034 通过 0 跳过、pytest 934 通过 0 跳过（6c 934/934、6d PowerShell 5.1+7 各 440/440）、Unity EditMode 185/185、PlayMode 374/374；ABI 探针 PASS；pytest 下限实测后上调至 840） | 66254da7 + 其后只改 gate_floors.json/timing/本日志的提交 | 2026-10-01 |
| full-20261001-08 | 通过（合并后，main 快进到 8261822c 后在主检出跑 check.ps1 全量含 Unity：36 步全过，脚本墙钟 793.9s；dotnet test 8034/8034、pytest 934/934（6c 934/934、6d PowerShell 5.1+7 各 440/440）、EditMode 185/185、PlayMode 374/374；ABI 探针 PASS） | 8261822c | 2026-10-01 |
| full-20261001-09 | 通过（合并前，分支 feature/branch-version-label_20261001，在 D:\wt\branch-version-label 跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 793.1s；dotnet test 8034/8034、EditMode 185/185、PlayMode 374/374） | 05c300aa | 2026-10-01 |
| full-20261001-10 | 通过（合并后，main 快进到 05c300aa 后在主检出跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 818.8s；dotnet test 8034/8034、pytest 972/972（6d PowerShell 5.1+7 各 477/477）、EditMode 185/185、PlayMode 374/374） | 05c300aa | 2026-10-01 |
| full-20261001-11 | 通过（合并前，分支 feature/gate-timing-autolog_20261001，在 D:\wt\gate-timing-autolog 跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 876.7s；dotnet test 8034/8034、pytest 997/997（6d PowerShell 5.1+7 各 501/501）、EditMode 185/185、PlayMode 374/374；逐步耗时已由门禁自动写入 timing/） | 2a255dc4 | 2026-10-01 |
| full-20261001-12 | 通过（合并后，main 快进到 ddbabd67 后在主检出跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 960.9s；dotnet test 8034/8034、pytest 997/997（6d PowerShell 5.1+7 各 501/501）、EditMode 185/185、PlayMode 374/374；逐步耗时见 timing/20261001_main.jsonl，按 AGENTS.md §1b 随本分支入库） | ddbabd67 | 2026-10-01 |
| full-20261001-13 | 失败（合并前，分支 feature/targeted-gate-coverage_20261001：36/37，仅消费方演练 registry 冒烟失败——包管理器子进程解析中途消失，`IPC stream failed to read (Not connected)`，环境性；dotnet 8034/8034、pytest 1036/1036、PlayMode 374/374 均过） | 31e0d4a1 | 2026-10-01 |
| full-20261001-14 | 失败（同上重跑：36/37，消费方演练首次编译同一签名失败，环境性；其余全过） | 31e0d4a1 | 2026-10-01 |
| full-20261001-15 | 失败（同上重跑：36/37，PlayMode 373/374，已登记计时类用例 SfxEngineReportedFinishPlayModeTests 短音效自然播完超时，环境性；消费方演练本次通过）。期间排查确认包管理器断连为环境偶发：同签名可用中途杀进程稳定复现，与本分支代码及私服同版本号包无关，该工作树单跑消费方演练两次 11/11 通过 | 31e0d4a1 | 2026-10-01 |
| full-20261001-16 | 通过（合并前第四次，同一提交未改代码：37 步全过，脚本墙钟 921.7s；dotnet test 8034/8034、pytest 1036/1036（6d PowerShell 5.1+7 各 540/540）、EditMode 185/185、PlayMode 374/374） | 31e0d4a1 | 2026-10-01 |
| full-20261001-17 | 通过（合并后，main 快进到 d3602221 后在主检出跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 911.8s；dotnet test 8034/8034、pytest 1036/1036（6d PowerShell 5.1+7 各 540/540）、EditMode 185/185、PlayMode 374/374；逐步耗时追加在 timing/20261001_main.jsonl） | d3602221 | 2026-10-01 |
| full-20261001-18 | 通过（合并前，分支 bugfix/upm-evidence-stale-pid_20261001，在 D:\wt\upm-evidence 跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 945.5s；dotnet test 8034/8034、pytest 1054/1054（6d PowerShell 5.1+7 各 558/558）、EditMode 185/185、PlayMode 374/374） | 148130a6 | 2026-10-01 |
| full-20261001-19 | 作废（合并后，main 快进到 e3146149 后在主检出跑全量：37 步中 6d 失败 1 条 test_two_lines_run_concurrently；同期测试泄漏的 GIT_* 环境变量写坏了共享 .git/config（core.bare=true 等），本次结果不可信，作废，待泄漏修复合入后重跑） | e3146149 | 2026-10-01 |
| full-20261001-20 | 失败（合并前，分支 bugfix/test-git-env-leak_20261001，在 D:\wt\test-git-env-leak 跑全量：37 步 3 步失败——pytest 1083/1084（本分支复现用例嵌套重跑真实仓库 build.ps1，被拦截的 -Dist 在拦截前写共享占位文件，与 Unity 线争用，已在 bcd7d2b2 修复）；消费方演练私服发布 E404（工作树缺 htpasswd 与私服 db 密钥，环境性）；PlayMode SfxEngineReportedFinish 计时用例（已知环境性抖动）） | f1ab93b4 | 2026-10-01 |
| full-20261001-21 | 通过（合并前第二次，同分支：37 步全过，脚本墙钟 891.3s；dotnet test 8034/8034、pytest 1085/1085（6d PowerShell 5.1+7 各 559/559）、EditMode 185/185、PlayMode 374/374；前后主检出 .git/config 哈希不变） | bcd7d2b2 | 2026-10-01 |
| full-20261001-22 | 通过（合并后，main 快进到 8a67f045 后在主检出跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 897s；dotnet test 8034/8034、pytest 1085/1085（6d PowerShell 5.1+7 各 559/559）、EditMode 185/185、PlayMode 374/374；前后 .git/config 哈希不变；逐步耗时追加在 timing/20261001_main.jsonl，由本分支带入） | 8a67f045 | 2026-10-01 |
| full-20261001-23 | 通过（合并前，分支 feature/pending-timing-records_20261001，在 D:\wt\pending-timing 跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 921.7s；dotnet test 8034/8034、pytest 1113/1113（6d PowerShell 5.1+7 各 586/586）、EditMode 185/185、PlayMode 374/374；前后主检出 .git/config 哈希不变） | e9976972 | 2026-10-02 |
| full-20261001-24 | 通过（合并后，main 快进到 f8defcda 后在主检出跑 check.ps1 全量含 Unity：37 步全过，脚本墙钟 1017.7s；dotnet test 8034/8034、pytest 1113/1113（6d PowerShell 5.1+7 各 586/586）、EditMode 185/185、PlayMode 374/374；逐步耗时由待领目录领走，入库于 timing/20261002_main.jsonl） | f8defcda | 2026-10-02 |
| full-20261001-25 | 失败（合并前，分支 feature/game-feel_20261002，在 D:\wt\game-feel 跑 check.ps1 全量含 Unity：43 步 6 步失败——Unity 编译检查 CS7036：games/_template/Runtime/GameBootstrap.cs(353,32) 改走 GameplayAssembly 最长重载时漏传 timeModelSwitchOptions/combatParticipantsResolver/deathPolicyOptions 三个无缺省值的参数（S10 引入；游戏模板不在 Core.sln，只有 Unity 编译能发现），EditMode/PlayMode/独立版构建与冒烟、消费方演练随之失败；非 Unity 步骤全过：dotnet test 8801/8801、pytest 1217/1217、feellab suite 180/180；已在 faa23985 修复，真问题非环境性） | a20d960c | 2026-10-02 |
| full-20261001-26 | 通过（合并前，同分支修复后重跑：43 步全过（IL2CPP 三步未传 -Il2cpp 跳过），脚本墙钟 1105.3s；dotnet test 8801/8801、pytest 1217/1217（6d PowerShell 5.1+7 各 606/606）、EditMode 185/185、PlayMode 375/375、feellab suite 180/180（跨格子不变量随 dotnet test 的 Tests.Lab 一并通过）；工作树 D:\wt\game-feel，已并入 main 5ed5edf1） | faa23985 | 2026-10-02 |
| full-20261001-27 | 通过（合并前，分支 feature/feel-m1-fixes_20261002，在 D:\wt\feel-m1fix 跑 check.ps1 全量含 Unity：43 步全过（IL2CPP 三步未传 -Il2cpp 跳过），脚本墙钟 1075.5s；dotnet test 8804/8804、pytest 1217/1217（6d PowerShell 5.1+7 各 606/606）、EditMode 188/188、PlayMode 377/377、feellab suite 180/180；同分支此前一次定向门禁 45 步中仅消费方演练 1 步失败（其余通过，IL2CPP 三步跳过）（新增的模板 PlayMode 手感装配用例在消费方工程里读不到 data/_feel），已在 consumer_smoke.ps1 同步 data/_feel 修复，真问题非环境性；逐步耗时入库于 timing/20261002_feel-m1-fixes_20261002.jsonl） | 69d3fe2d | 2026-10-02 |
| full-20261001-28 | 通过（合并后，main 手感落地 M1 并入后在主检出跑 check.ps1 全量含 Unity：43 步全过（IL2CPP 三步未传 -Il2cpp 跳过），脚本墙钟 1126.7s；dotnet test 8804/8804、pytest 1217/1217（6d PowerShell 5.1+7 各 606/606）、EditMode 188/188、PlayMode 377/377、feellab suite 180/180、数值仿真三份基线零差异、ABI 探针 OK、消费方演练 PASS；逐步耗时写入待领目录 timing/_pending/20261002_main_120128.jsonl，留给下一条分支领走） | 836f3685 | 2026-10-02 |
| full-20261001-29 | 失败（1.93.0 发布首跑，main 上 ad52ef29 之后的发布流程：check.ps1 全量含 Unity 门禁本身 43 步全过（IL2CPP 三步未传 -Il2cpp 跳过），脚本墙钟 1232.4s；dotnet test 8804/8804、pytest 1217/1217（6d PowerShell 5.1+7 各 606/606）、EditMode 188/188、PlayMode 377/377、feellab suite 180/180、消费方演练 PASS；失败在门禁之后的 -Release 打包阶段：docfx 手册概念页数 282≠283（docs/manual/concepts/toc.yml 引用的 lab/README.md 未被 docfx.json 概念页通配覆盖），已按 AGENTS.md §5 用 git reset --soft 回退半途发布提交 698fdc39，不产生标签） | ad52ef29 | 2026-10-02 |
| full-20261001-30 | 通过（1.93.0 发布二跑，在 c19e9d65（docfx 概念页通配补 lab/**/README.md）上跑 check.ps1 全量含 Unity：43 步全过（IL2CPP 三步未传 -Il2cpp 跳过），脚本墙钟 1244.8s；dotnet test 8804/8804、pytest 1217/1217（6d PowerShell 5.1+7 各 606/606）、EditMode 188/188、PlayMode 377/377、feellab suite 180/180、数值仿真基线比对 PASS、ABI 探针 PASS、消费方演练 PASS；其后打包通过，发布提交 b7ac4dc3，标签 v1.93.0；发布门禁带 -NoTiming，不产生耗时记录） | c19e9d65 | 2026-10-02 |
