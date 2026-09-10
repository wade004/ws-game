# 文档/工具链验证记录

## check

`check-skipunity-transcript.txt` 是当前冻结副本执行的唯一 `check.ps1 -SkipUnity` 原始日志。汇总为 24 steps、17 PASS、7 SKIP；ABI 默认基线缺失因此 SKIP。六个 .NET 测试工程为 3001/3001（127+813+449+398+564+650），toolchain pytest 为 161 passed、4 skipped。SKIP 的 ABI、Unity、standalone、consumer 证据不得写成 PASS。

## 严格 ABI（旧正式包）

`abi-strict-1.12/summary.txt` 记录显式 baseline `dist/ws-game-1.12.0.zip`，旧 consumer 编译一次后分别针对旧 DLL 与当前 DLL 运行；当前运行退出码为 0，surface compare 为 `breaks=0 allowed=0 additions=221 RESULT=OK`。这是独立严格 ABI PASS，不能改写默认 check 的 SKIP。

`abi-strict-1.12/formal-zip-abi-summary.txt` 进一步从正式 `dist/ws-game-1.16.1.zip` 流式读取六个 core DLL，替换同一旧 consumer 的运行目录，不重新编译 consumer；consumer hash 前后相同，运行退出码 0。正式 zip sha256 为 `5b5a842d1259dd6e4735d6796a16f0331033aeb38138a09b543b104a74b4dd7b`。

`abi-gate-negative-oracle/summary.txt` 是最小漏报 oracle：公开 `Api.Read()` 降为 protected 后，两个 surface dump 相同、compare 仍为 0；旧 consumer 实际运行抛 `MethodAccessException`。这是 ABI surface 门禁覆盖问题，独立于本框架当前六 DLL 的 strict ABI 结果。

原始运行输出保存在 `abi-gate-negative-oracle/run-current.log`：`Unhandled exception. System.MethodAccessException: Attempt by method 'Program.<Main>$(System.String[])' to access method 'ApiContract.Api.Read()' failed.` 因此结论是“门禁不能识别此类兼容破坏”，不是“1.16.1 当前 DLL 已发生此破坏”。

自包含入口已在新目录 `abi-gate-negative-oracle-rerun3` 复跑：校验冻结 HEAD，镜像最小 API 与 `abi_surface` 源码，重建 baseline/current/old consumer 后得到相同结果；过程 raw 为 `logs/abi-gate-negative-oracle-rerun3.raw.txt`。归档时 build/bin/obj 仍排除，保留入口、源码与文本结果。

## ADR-0022 wrapper probe

`validate-precompiled-probe/validator-list-tables-json.stdout.txt`：预编译 `Validator.dll --list-tables --json` 退出 0，顶层含 `tables_list`，每张表含 `field_meta`。

`validate-precompiled-probe/json.stdout.txt`：`validate_data.py --json` 退出 0，stdout 可解析 JSON，顶层为 `skeleton/validator/exit_code`，没有 `tables_list`；stderr 保留人类诊断。严格模式的同一框架数据因已知 l10n 警告退出 1，而非严格模式退出 0，说明 wrapper 的 exit semantics 被保留。

## 正式包流式核对

`formal-release-stream-check.txt` 对 1.12.0、1.14.0、1.15.0、1.16.0、1.16.1 逐包读取，不重建、不覆盖 zip：每个 lock DLL 与选定 entry hash 匹配，并额外验证 zip 中同名 DLL 的全部副本均只有一个 hash 且等于 lock；包根四个 npm manifest 与版本一致（1.12.0 没有 headless 包，记录为预期缺席）；每个版本 `result=PASS`。

`formal-release-stream-check.txt` 由 `verify-formal-release.ps1` 生成，源码保留在同一 `docs-project` 目录，供总报告复现。

## schema / Expr 代理证据

schema agent 的 .NET 8 独立 consumer 与真实框架表证据在 `schema/raw/presentation-consumer-rerun.log`、`schema/raw/schema-consumer-rerun.log`，结论和源码锚点见 `schema/schema-findings.md`。F-01 以真实 `skill.aura_def` 的 `duration` 与 periodic interval 读取 `1e309` 得到 Infinity 且 `errors=0 blocking=False`；F-03 以真实 `skill.proc_def.condition` 的超范围整数在 `LoadAll` 外抛 `OverflowException`，之后 `GetAll` 仍可读 count=1。两者均是 .NET 8 内容工具/框架加载路径证据，不能改写为 Unity 崩溃报告。F-02 是公开 `FieldRange` 非有限边界防御观察，列为 P3。

Unity XML 若进入总报告，计数应取每个 test-run 节点的 `total`/`passed`，不要使用 test-suite 的 testcase count；后者可能包含描述性 suite 数而不是实际执行数。
