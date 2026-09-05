# 回放回归测试

见 `ReplayBaselineTests.cs`（11_工程规范与测试.md 第 6 节"回放回归：固定录像 + 固定种子重放，
比对事件流与既往基线"）。两个固定场景：连续一场（`continuous_fight.replay.json`）、离散一场
（`discrete_fight.replay.json`），均由 `ReplayWorldBuilder.cs` 装配（自带一份最小自洽的嵌入式
数据，不依赖 `data/_sample`/`data/_framework`，命中表全部分支禁用，伤害结算不含随机波动）。

## 文件清单

| 文件 | 内容 | 是否提交到仓库 |
|---|---|---|
| `ReplayWorldBuilder.cs` | 世界组装 + 固定脚本（玩家/NPC 逐 tick 互相释放技能） | 是（代码） |
| `continuous_fight.replay.json` | 连续场景的固定"录像"（`ReplayData` 序列化） | 是（固定产物） |
| `discrete_fight.replay.json` | 离散场景的固定"录像" | 是（固定产物） |
| `replay_baseline.json` | 两个场景重放到底后的 `(final_tick, digest, event_log)` | 是（既往基线） |
| `ReplayBaselineTests.cs` | 只读三份固定产物、重放、比对，不在测试期间生成/覆写它们 | 是（代码） |

## 比对机制

`replay_baseline.json` 记录每个场景重放到底（`ReplayWorldBuilder.ContinuousFixedTicks`/
`DiscreteFixedSteps`，均为 20）后的 `WorldSnapshot`：`final_tick`、`digest`（事件流 + 存活实体
状态的 FNV-1a 64 位确定性摘要，见 `Core.Foundation.SaveSystem.WorldSnapshot.Capture`）、
`event_log`（完整事件 key 序列，逐项比对，不只比对摘要——摘要不一致时 `event_log` 的逐项断言
能直接定位是哪一步开始出现分歧）。

`ReplayBaselineTests` 每个场景各有两组用例：
1. `ReplayRegression_*_MatchesBaseline`：加载固定录像 → 经 `ReplayPlayer` 重放到底 →
   与 `replay_baseline.json` 比对（回归防护本体）。
2. `ReplayRegression_*_ReplayMatchesDirectRun`：同一份固定脚本重新"直跑"一遍，断言其
   `WorldSnapshot` 与"经录像重放"得到的结果逐项相等（验证录像/播放器机制本身没有失真，与 1
   相互独立，见该测试判断记录）。

## 如何更新基线

**前提：先确认这是一次有意的战斗结算行为变化**（而不是意外回归）。11 第 6 节"回放回归"存在的
意义就是在这两者之间设一道人工确认关卡——本文件的比对失败本身不说明对错，只说明"结果变了"。

1. 定位改动是否确实是"有意改变战斗结算"（如调整了效果原语的结算顺序、命中表逻辑、免疫/吸收
   规则等，导致 `combat.damage_dealt`/`unit.died`/`skill.cast_*` 等事件的序列或时机变化）。若是
   意外回归，应该去修那个改动，而不是更新基线掩盖它。
2. 确认后，临时在 `ReplayBaselineTests.cs` 里加回一个"生成器"方法（见 git 历史里本文件初次提交
   时的 `ZZZ_TEMP_GenerateTapesAndBaseline`，或直接照抄下面的写法），跑一遍生成新的
   `replay_baseline.json`（录像文件 `continuous_fight.replay.json`/`discrete_fight.replay.json`
   本身不需要重新生成——固定脚本没变，录像内容不会变，只有重放出来的结果会变）：
   ```csharp
   [Fact]
   public void ZZZ_TEMP_RegenerateBaseline()
   {
       var dir = FindDirectory();
       // ……与本 README 版本历史中的生成逻辑相同：分别 RunContinuousFixedScript/
       // RunDiscreteFixedScript、WorldSnapshot.Capture、写入 replay_baseline.json。
   }
   ```
3. 跑 `dotnet test core/gameplay/tests/Tests.Gameplay.csproj -c Release --filter "FullyQualifiedName~ZZZ_TEMP"`
   生成新文件，人工审阅 `replay_baseline.json` 的 diff（`event_log` 的变化应该能对应上你在第 1 步
   确认的具体改动，不应该有"看不懂为什么变"的行）。
4. 删除临时生成器方法，跑一遍完整的 `ReplayBaselineTests` 确认全绿。
5. **提交信息里必须注明本次更新了回放基线以及原因**（例如"更新 replay_baseline.json：
   school_damage 效果结算顺序调整为先算暴击再算减免"），不要把基线更新悄悄混进一次无关改动的
   提交里——这是本文件"有意改变战斗结算时如何更新基线"的硬性要求。

## 判断记录

见 `ReplayWorldBuilder.cs`/`ReplayBaselineTests.cs` 顶部注释：不依赖 `data/_sample`/
`data/_framework`（不受其它任务改动示例数据的影响）；离散场景不装配 `TurnScheduler`/
`TimeModelSwitch`（`sim.turn_started`/`turn_ended`/`round_ended` 由 `TurnScheduler` 自己
`PublishImmediate`，不经过 `IWorldSim.Tick`，天然不在"回放 = 重放 SimStep 序列给 world.Tick"
这一机制的覆盖范围内，见 `core/foundation/save_system/tests/DiscreteReplayTests.cs` 同款判断
记录），改由固定脚本手工交替产生 `SimStep.Discrete`。
