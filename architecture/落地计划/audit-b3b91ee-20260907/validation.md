# 本轮验证与局限

## 固定验证结果

以下命令由主审在固定基线执行，结果作为本轮验证记录；本文件不把历史日志当成本轮证据。

| 命令 | 结果 | 证据边界 |
|---|---|---|
| `dotnet test Core.sln -c Release --artifacts-path bin/deep-review-b3b91ee/dotnet --logger trx --results-directory bin/deep-review-b3b91ee/results` | 全 2090 通过：Foundation 621、Numbers 102、Rules 306、Carriers 266、Presentation 393、Gameplay 402 | .NET 单元/集成测试通过；不能推出 Unity、跨场景 Runtime 或发布形态通过。 |
| `python -m pytest toolchain/tests -q` | 43 通过 | toolchain 测试通过；不能覆盖所有真实文件名碰撞和缺失原生程序路径。 |
| `python toolchain/validate_data.py` | 59 表、276 记录、0 错误、0 警告、1 override；骨架 60 files/2 roots，0 错误 | 合并根数据校验通过；不代表 Unity/消费方通过。 |
| `python toolchain/validate_data.py --data-root data/_framework` | 5 表、122 记录、0 错误、1 warning；骨架 5 files/1 root，0 错误 | warning 为 `l10n.text` 未加载而跳过 `text_key` 检查；不能泛称全部校验 0 warning。 |
| `python toolchain/import_assets.py check --dataset _sample` | 8 display、3 vfx、3 sfx、1 world，0 问题 | 仅 `_sample` 资产契约检查；不证明真实游戏资源、Unity 导入或地图/导航可玩。 |
| `python toolchain/gen_event_constants.py --check` | 88 常量一致 | 生成常量静态一致。 |
| `python toolchain/gen_placeholder_assets.py --check` | 92/92 | placeholder 生成结果静态一致。 |

版本核对：`VERSION`、adapter package、template package 及模板依赖均为 `0.2.0`。原工作树已有 `dist/0.2.0`，其 MANIFEST 记录 commit `b3b91ee`（2026-09-07 05:19:09）；本审计树因 ignored 规则没有 `dist`，不能据此声称 dist 陈旧或完成发布验证。

## 工具复现

有界复现已落盘于 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt)，复现器说明见 [repro/validation-repros.md](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/validation-repros.md)。

- **TOOL-01**：[tool-01-repro.ps1](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-01-repro.ps1) 从真实 `check.ps1` AST 提取 `Test-NativeExitCode`，以 PowerShell 5.1 执行 `cmd.exe /c exit 0` 后调用不存在的 `__ws_game_audit_missing_executable__`；[tool-01-repro.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-01-repro.txt) 记录实际 True，证明缺失程序沿用 `LASTEXITCODE=0` 的假阳性路径。
- **TOOL-02**：[tool-02-repro.py](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-02-repro.py) 隔离临时目录真实调用 `sfx_cmd.run`，导入不同内容的 `sfx.fire.hit` 和 `sfx.fire_hit`；[tool-02-repro.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-02-repro.txt) 记录一文件、同 resource_ref、第二源覆盖。VFX 同类归一化由静态源码核对；不能把相同内容别名算作冲突。

两项工具复现只证明各自触发路径，不替代完整工具集/CI 验收。TOOL-02 的修复验收还需确认两个 ID 可独立加载，重复同 ID 的覆盖策略有明确文档。

除工具外，`.NET` 有界复现器 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) 的 R1–R6 触发了 GP-01、FND-10、FND-01、FND-04、FND-05、RC-04，R7 在真实 Combat/Carriers 组件上触发 RC-02。边界复现器 [validation-boundaries.md](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-boundaries.md) 触发 FND-02、FND-03、FND-08 和 GP-02；GP-02 是 `AnimStateMachine + FrameAnimPlayer + AnimClipResolver` 组件链，仍需直接 UnityViewFactory/Unity 验收。合计 13 个问题、14 个用例有运行复现，其余 20 个问题保持静态等级。

## 未执行与证据边界

本轮未执行 Unity 编译、EditMode、PlayMode、IL2CPP、独立发布构建、消费方演练或真实异步资源/跨场景 Runtime。已在有界 .NET/组件复现中观察到的行为仍不等同 Unity/完整 Runtime 通过。尤其 GP-02/03/06/09/10 需要 Unity/资源加载器实测，FND-06/07/09、GP-04/05/07/08、RC-01/03/05/06/07/08/09/10/11 需要最小 Runtime/集成夹具复验。

本轮也未重新运行历史 audit 目录中的脚本或日志；历史 `audit-20260907` 文件只用于识别旧结论，不能作为当前执行证据。主审应在所有修复后重新运行上述固定命令，并补跨槽/切图/死亡销毁/离散节奏/异步资源/模板动画和消费方场景验收。
