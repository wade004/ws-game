# main 验证摘要（2026-09-07）

基线：`8d7057c049aa623ffd916d61540608550790b801`，目录 `D:/workespace/ws-game-main-audit`。本摘要是 tracked 的结果索引，原始输出在被忽略的 `bin/audit-20260907/`。

| 命令 | 退出码/计数 | 证据边界 |
|---|---:|---|
| `check.ps1 -SkipUnity -LogFile bin/audit-20260907/check.log` | `0`；17 步，11 PASS + 6 SKIP | 脚本结果受 stdout/退出码数组误判（F1）约束；Unity、发布形态和消费方未跑 |
| `check.ps1 -SkipUnity -Quick -LogFile bin/audit-20260907/quick.log` | `0`；17 行，8 PASS + 9 SKIP | 同上；Quick 不是全 PASS |
| `dotnet test Core.sln -c Release --no-build` | `0`；6 程序集 1846，跳过 0 | 直接命令；不包含 Unity |
| `python -m pytest toolchain/tests -q` | `0`；19 passed | 直接命令；仅 toolchain tests |
| `dotnet run --project toolchain/validator -- --data-root data/_framework --json` | `0`；5 tables/122 records/0 errors/1 warning/blocking=false | 直接命令；framework 数据集 |
| `python toolchain/import_assets.py check --dataset _sample` | `1`；16 个问题 | 直接交叉检查；暴露参考样例契约断裂，不等同运行时资产丢失 |
| `powershell -NoProfile -ExecutionPolicy Bypass -File architecture/落地计划/audit-20260907/repro-check-native-exit.ps1` | `0`；实际 check 函数 AST 探针 `ACTUAL_CHECKSTEP_RESULT=PASS` | 证明门禁失败假阳性；不修改生产代码 |
| `dotnet run --project architecture/落地计划/audit-20260907/repro-rng/RngRestoreRepro.csproj` | 复现 `equal=False`、`B_different_after_restore=True`、`short_state_accepted=True` | 仅证明 RNG 恢复边界；附件输出见 foundation 分报告，不等同完整 Save/Replay E2E |
| `dotnet run --project architecture/落地计划/audit-20260907/evidence/GP-PRES-01-world-clear-repro.csproj --configuration Release` | `restored_before_clear=true; loot_after_scene_clear=False` | 仅证明 WorldSim `ClearAll` 机制片段；不等同完整 ShellHost/SceneRouter/Unity E2E |

本次未执行 Unity 全量、独立版、消费方演练或 IL2CPP；这些状态保持未验证。
