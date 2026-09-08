# Unity presentation validation — 1.5.0 / 3224ca1

验证对象是 `D:\workespace\ws-game` 的 HEAD `3224ca1247b119ef656bf928a024a1e0a7701fcc`、版本 `1.5.0`。Unity 只在外部隔离副本运行：
`C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-3224ca1-20260908\unity_probe\adapters\unity`。
副本的 Unity 源文件来自 `source_snapshot`；六个核心 DLL 来自已核验的
`get_framework_target\ws-game-1.5.0\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core`，其 `MANIFEST.txt` 记录 `git_commit: 3224ca1`。本轮没有写入 live 源树、`source_snapshot` 或 `git_snapshot`。

Unity 版本为 `6000.3.23f1`。首次导入命令因新增探针漏写 `using UnityEngine.TestTools` 退出 1；只在隔离探针代码中补齐引用后，重新导入退出 0。最终 Unity 进程已退出。导入日志还记录了本机 URP 资源类型错误和 Licensing access-token warning；它们没有阻止目标 PlayMode 测试完成，具体原文保留在原始日志。

## 实际执行

| 命令 | 退出码 | 耗时 | 结果 |
|---|---:|---:|---|
| `Unity.exe -batchmode -nographics -quit -projectPath ...\unity_probe\adapters\unity -logFile editor-import-3.log` | 1 | — | 隔离探针编译错误：缺 `UnityEngine.TestTools` 引用 |
| 同上，`editor-import-4.log` | 0 | — | 隔离工程导入成功 |
| `... -runTests ... -testFilter PlayAutoExitClip_AnimatorTransitionsBeforeDetectionFrame_StillRaisesFinishedExactlyOnce` | 0 | 8741 ms | 1/1 passed |
| `... -runTests ... -testFilter PresentationAudit150Tests` | 0 | 9555 ms | 3/3 passed |
| `... -runTests ... -testFilter PresentationAudit150Tests.AnimatorAutoExit_BeforeFirstRendererTick_RecordsFinishedAfterIdle` | 0 | 约 9 s（Unity 日志 06:27:36–06:27:45Z；测试 XML 0.0615643 s） | 1/1 passed |

首批新增三项探针的 XML 为 `total=3 passed=3 failed=0 skipped=0`，运行时长 `0.3738025` 秒。另运行一个既有自动退出回归和一个首次 Tick 边界探针，各 1/1 PASS；合计 5 个不同定向用例。三个缺陷现状断言的 PASS 表示复现成功，另外两个为正常自动退出回归。

## 观察结果

### Sprite CreateView 装备重放

`PresentationAudit150Tests.SpriteCreateView_ReplayBeforeBind_DropsEquippedEventAndDoesNotRequestHat` 使用真实 `UnityViewFactory`、`EquipmentVisualSource`、`UnitySpriteView`、`UnityResourceLoader`、`data/_sample` 和真实 sample sprite 数据。`CreateView` 触发 snapshot replay，随后调用 `Bind` 与 `SyncPose`：

```text
PRESENTATION150 sprite_replay expected_after_create_bind_sync>0 actual=0 entity_id=unit.audit150_sprite_replay mapped=True
PRESENTATION150 sprite_replay_control expected_after_bound_event>0 actual=0.5
```

结果确认：snapshot 已进入真实装备映射，但 replay 发生在 `Bind` 前，`SpriteViewBase.EntityId` 尚未设置，装备事件被过滤，帽子覆盖资源没有请求加载；同一事件在 Bind 后作为对照会请求加载。该测试的 `PASS` 表示缺陷复现断言成立。

### Animator 自动退出

先运行当前源码中真实的 `ModelViewTests.PlayAutoExitClip_AnimatorTransitionsBeforeDetectionFrame_StillRaisesFinishedExactlyOnce`，结果 1/1 passed。随后隔离探针使用真实 `UnityRenderer3D`、`Animator`、`anim.test_autoexit` 资源与真实 renderer tick 记录实际状态：

```text
PRESENTATION150 animator_autoexit expected_finished=1 actual_finished=1 ever_entered_target=True current_idle=True
```

结果确认：Animator 曾进入 `test_autoexit`，自动返回 `idle` 后仍收到一次 `anim_event.finished`。这是当前修复行为的 Runtime 支持证据。

新增同步边界探针没有让宿主自动 Tick：`CreateModelInstance` 后调用真实 `renderer.PlayAnim(..., blendSeconds: 0)`，从真实视觉根取得 `Animator`，依次执行 `Animator.Update(0)`、`Update(0.25)`、`Update(0.10)`，确认状态路径为 `idle -> test_autoexit -> idle -> idle`，然后才调用 `renderer.Tick()` 两次。实际日志为：

```text
PRESENTATION150 animator_before_first_tick expected_idle=true actual_states=idle->test_autoexit->idle->idle received_before_tick=0 received_after_first_tick=0 received_after_second_tick=0
```

结果确认：在首次 renderer Tick 前，真实 Animator 已进入并自动退出目标状态；由于 `Tick` 期间从未观察到目标状态，当前机制未发出 `anim_event.finished`。该探针 1/1 `PASS` 表示缺陷复现断言成立，测试 XML 为 `total=1 passed=1 failed=0 skipped=0`；它不否定上面的正常自动退出回归，而是覆盖其未覆盖的首次观察边界。

### 条件 socket 路径

`PresentationAudit150Tests.MissingPlaceholderSocket_CustomSocketAddedByReplacement_IsNotReattached` 使用真实 `UnityRenderer3D.CreateModelInstance`、`AttachToSocket` 与内部测试钩子 `CompleteAsyncModelSwapForTest`。占位模型没有 `socket.custom`，首次 `AttachToSocket` 提前返回；替换视觉实例再加入同名 `socket.custom`，但此前没有登记子实例：

```text
PRESENTATION150 conditional_socket expected_registered=false expected_reattached=false actual_parent_before=GameFoundation.EngineHost actual_parent_after=GameFoundation.EngineHost replacement_socket_children=0
```

结果确认：替换后新挂点子节点数仍为 0，子模型保持在原引擎根节点。该测试覆盖的是明确标注的 test-hook 条件路径，绕过了真实 `Resources.Load` 的时序，因此不能单独宣称完整异步资源链已证明；`PASS` 表示条件机制复现断言成立。

## 代码与原始证据

复现代码：

- `unity_probe\adapters\unity\Packages\com.gamefoundation.adapter.unity\Tests\Runtime\PresentationAudit150Tests.cs`
- 当前源码已有对照：`source_snapshot\adapters\unity\Packages\com.gamefoundation.adapter.unity\Tests\Runtime\ModelViewTests.cs`

原始日志与结果：

- `unity-validation-raw.log`：本轮命令、退出码、DLL 来源/hash、最终 HEAD/status/process 检查、关键 marker 与结果 XML。
- `unity_probe\adapters\unity\presentation150-custom-2.log`
- `unity_probe\adapters\unity\presentation150-custom-results-2.xml`
- `unity_probe\adapters\unity\presentation150-animator-before-first-tick.log`
- `unity_probe\adapters\unity\presentation150-animator-before-first-tick-results.xml`
- `unity_probe\adapters\unity\modelview-autoexit.log`
- `unity_probe\adapters\unity\modelview-autoexit-results.xml`
- `unity_probe\adapters\unity\editor-import-3.log`、`editor-import-4.log`

隔离副本建立时 live 基线为 `3224ca1247b119ef656bf928a024a1e0a7701fcc`；Unity 副本只取该基线的 `source_snapshot` 文件和同一提交的已核验 DLL，不读取后来 live checkout 的生产改动。期间共享 live checkout 曾出现其他代理的 7 条未提交修改；在本轮最后一次核对时，其他代理已将共享 checkout 推进为提交 `8927398714fb516121641f8f9a1f6ce0838a0db8`，当前无未提交状态。该 checkout 漂移不改变已运行副本的输入；本轮没有清理、覆盖或写入 live。各时点的 `git status --porcelain=v1` 与最终 HEAD 原文保存在 `unity-validation-raw.log`。针对本审计副本的 Unity 进程数为 0（其他项目进程不计入）。
