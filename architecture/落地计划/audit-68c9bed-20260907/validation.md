# 本轮验证记录

基线：D:/workespace/ws-game-review-68c9bed，HEAD 68c9bedaeed3485ee845f68c95136f87476caf03，执行日期 2026-09-07。主代码审计锁定该提交；随后仅额外审阅主目录前进到 0146844dc06db65585d936d4fe6788e928dd4627 的两处文档差异，未发现生产代码/测试变化。完整输出在 D:/workespace/ws-game-review-68c9bed/bin/audit-68c9bed/，可提交复查的原始文本日志在 evidence/。

## 固定验证

| 命令 | 实际结果 | 证据边界 |
|---|---|---|
| dotnet test Core.sln -c Release --artifacts-path bin/audit-68c9bed/dotnet --logger trx --results-directory bin/audit-68c9bed/results | 2,178 passed, 0 failed, 0 skipped；Numbers 104、Foundation 633、Rules 334、Carriers 280、PresentationCommon 403、Gameplay 424 | 原始 [dotnet-test.log](evidence/dotnet-test.log)，TRX 在 bin/audit-68c9bed/results/；仅证明现有 .NET 测试通过。 |
| python -m pytest toolchain/tests -q | 46 passed in 1.48s | 原始 [pytest.log](evidence/pytest.log)。 |
| python toolchain/validate_data.py | 59 tables、276 records、0 errors、0 warnings、1 override；骨架 60 files/2 roots、0 errors | 原始 [validate-data-merged.log](evidence/validate-data-merged.log)；二阶段原始 [validator-merged.log](evidence/validator-merged.log)。 |
| python toolchain/validate_data.py --data-root data/_framework | 5 tables、122 records、0 errors、1 warning；未加载 l10n.text，跳过 text key 检查 | 原始 [validate-data-framework.log](evidence/validate-data-framework.log)；二阶段原始 [validator-framework.log](evidence/validator-framework.log)。 |
| python toolchain/import_assets.py check --dataset _sample | 8 display.map、3 vfx.def、3 sfx.def、1 world.map，0 问题 | 原始 [import-assets-sample.log](evidence/import-assets-sample.log)。 |
| python toolchain/gen_event_constants.py --check | 88 个常量一致 | 原始 [eventconstants-check.log](evidence/eventconstants-check.log)。 |
| python toolchain/gen_placeholder_assets.py --check | 92/92 通过，0 失败 | 原始 [placeholder-check.log](evidence/placeholder-check.log)。 |

上述命令没有启动 Unity，没有操作真实存档，也没有修改生产文件或既有测试。dist/0.2.0 当前 manifest 为 HEAD 68c9bed、日期 2026-09-07 13:04:10；本轮未重做发布验收。

## 有界复现

项目 [AuditRepros.csproj](repro/AuditRepros.csproj) 只引用真实 Foundation、Stub adapter、Presentation.Common 和已有 FeedbackBinder test support；代码在 [Program.cs](repro/Program.cs)，运行日志在 [repro.log](repro/repro.log)。命令：

    dotnet run --project architecture\落地计划\audit-68c9bed-20260907\repro\AuditRepros.csproj -c Release

一条 A 输出覆盖两个输入分支，两个 C 输出覆盖两种队列模式，共四个场景：

1. **N15-A**：两次 Save 成功创建 bak1；正式 JSON 为 {"save_version":1,"sections":{}}，Load 实际 Corrupted，正确行为应为 LoadedFromBackup，REPRODUCED。
2. **N15-B**：只替换顶层 save_version=1.5，嵌套 sections.meta.save_version 保持 1；实际 Corrupted，正确行为应为 LoadedFromBackup，REPRODUCED。
3. **N17-Sequential**：真实 PlaySfxAction 进入 FeedbackBinder，PlaySfx 后 sink pending；实际 queue=0, sinkPending=True, gateOpen=True，门提前打开，REPRODUCED。
4. **N17-Immediate**：PlaySfx 同步执行后 BeginStep 关门；sink pending 清零后仍 gateOpen=False，无完成事件解门，REPRODUCED。Immediate 是显式 override，调用方承担节奏接管，不能扩大为默认模式卡死。

程序对意外异常输出 EXECUTION_ERROR 并返回非零；本轮无执行异常。REPRODUCED 只表示有界路径达到缺陷断言，不表示框架测试或 Unity/真实异步资源通过。

## 未执行

未执行 Unity Editor/PlayMode/IL2CPP、真实 Unity 资源异步加载、真实用户存档、发布构建、跨场景消费方演练；N01–N14、N16、N18、N19 保持静态证据。N16 没有旧布局实际复现，N18 没有运行 UnityFrameAnimPlayer，N19 没有运行完整 UnityViewFactory。
