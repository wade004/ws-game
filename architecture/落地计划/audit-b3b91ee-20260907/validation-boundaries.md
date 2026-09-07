# b3b91ee 有界运行时复现

基线：`git rev-parse HEAD` = `b3b91ee624332c56637240fd1bc7941c6b2ebbc5`。

复现入口：[ReproBoundaries.cs](repro-boundaries/Program.cs)；项目文件：[ReproBoundaries.csproj](repro-boundaries/ReproBoundaries.csproj)。代码引用真实 `Core.Foundation`、`Adapters.Stub`、`Presentation.Common`，并链接现有 [SceneRouterTestSupport.cs](../../../core/foundation/scene_router/tests/SceneRouterTestSupport.cs) 与生产 [AnimClipResolver.cs](../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/AnimClipResolver.cs)。本次没有修改生产代码或现有测试。

## 结果

| 项目 | 预期 | 实际 | 判定边界 |
|---|---|---|---|
| FND02 SceneRouter 旧回调污染 | Town 失败后开始 Forest；旧 `nav.town_square` 回调应被忽略，Router 应保持 `Loading/Loading`，直到 Forest 两资源完成 | Town scene 先失败、Town nav 保持 pending 后 `Idle/MainMenu`；开始 Forest 后让旧 nav 回调唯一一次迟到，`beforeStaleUpdate=Loading/Loading`，`afterStaleUpdate=Idle/MainMenu`；无旧回调对照为 `Idle/InWorld/world.forest_path` | `REPRODUCED`。直接使用现有 `SceneRouterTestSupport.Harness + StubResourceLoader`，没有重复消费回调。关联 [SceneRouter.cs:197](../../../core/foundation/scene_router/core/SceneRouter.cs:197) 将任意回调按 id 写入当前 `_pendingResources`。 |
| FND03 DataRegistry 坏 JSON 阻断 | 坏 JSON 首次加载后应保持阻断，除非成功加载/重载修复数据 | 初始 `IsBlocking=True, errors=1`，读取被阻断；随后直接 `Validate()` 得 `IsBlocking=False, errors=0`，读取不再阻断 | `REPRODUCED`。复现使用真实 `DataRegistry`、`InMemoryDataSource`、`WorldMapSchema`；关联 [DataRegistry.cs:239](../../../core/foundation/data_registry/core/DataRegistry.cs:239) 与 [DataRegistry.cs:314](../../../core/foundation/data_registry/core/DataRegistry.cs:314)。 |
| FND08 InputMapHost 同批边沿丢动作 | 同一 `PollEvents` 批次的 Q down/up 仍应产生一次按钮动作 | 同批结果 `active=False, actionCount=0`；分两次 Update 对照为 down `active=True, actionCount=1`、up `active=False, actionCount=1` | `REPRODUCED`。使用真实 `InputMapHost` 与 `Adapters.Stub.StubInput`；关联 [InputMapHost.cs:189](../../../core/foundation/input_map/core/InputMapHost.cs:189)。 |
| GP02 动画完成/复活未接线 | Resolver 播放 Hit 后，播放器完成回调应调用 `NotifyTransientStateFinished`；死亡/复活/视图 teardown 应重置或调用 `Forget` | 真实 `AnimStateMachine + FrameAnimPlayer + AnimClipResolver` 组件链中，`clipAtHit=anim.hit`，Hit 播完后 `playerAfterHit=<none>, stateAfterHitComplete=Hit`；移动/Attack 仍为 `Hit`；死亡后发送真实 `UnitRespawnedEvent`、移动与 Attack 请求，仍为 `stateAfterRespawnAndAttack=Death`；手工 `Forget` 对照才回到 `Idle` | `REPRODUCED`（组件链复现）。没有运行 `UnityViewFactory`、Unity Editor 或 Play Mode，因此不宣称 Unity Runtime 证明。关联 [AnimStateMachine.cs:141](../../../presentation/render/core/AnimStateMachine.cs:141) 与 [AnimStateMachine.cs:156](../../../presentation/render/core/AnimStateMachine.cs:156)。 |

`REPRODUCED` 仅表示给定序列在当前 commit 的真实实现上观察到了目标边界行为，不表示框架通过或修复完成。

## 命令日志

完整原始输出保存在 [tool-boundaries.log](repro-boundaries/tool-boundaries.log)。

在仓库根目录 `D:\workespace\ws-game-review-b3b91ee` 执行：

```text
dotnet build architecture\落地计划\audit-b3b91ee-20260907\repro-boundaries\ReproBoundaries.csproj --configuration Release
生成成功：0 个警告，0 个错误

dotnet run --project architecture\落地计划\audit-b3b91ee-20260907\repro-boundaries\ReproBoundaries.csproj --configuration Release --no-build
FND02 ... afterStaleUpdate=Idle/MainMenu ... REPRODUCED
FND03 ... initial(IsBlocking=True, errors=1) ... Validate(IsBlocking=False, errors=0) ... REPRODUCED
FND08 ... sameBatch(active=False, actionCount=0) ... splitDown(active=True, actionCount=1) ... REPRODUCED
GP02 ... playerAfterHit=<none> ... stateAfterHitComplete=Hit ... stateAfterMoveAndAttack=Hit ... stateAfterRespawnAndAttack=Death ... stateAfterManualForget=Idle ... component-chain-only ... REPRODUCED
```

未执行全套测试、Unity 测试或生产改动后的回归；本文件只记录上述隔离 console 的静态引用与一次运行结果。
