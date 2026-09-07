# 工作树漂移记录

记录时间：2026-09-08 05:28:55 +09:00（审计收尾快照）  
源目录：`D:/workespace/ws-game`  
HEAD：`5c444f1dc2f3729c8a03a68f78cacc28d24c2a2f`  
版本：`1.3.0`

审计收尾时执行 `git status --short` 原样结果如下（17 个已跟踪修改、2 个未跟踪文件）：

```text
 M adapters/conformance/Runtime/ConformanceContext.cs
 M adapters/conformance/Runtime/Renderer3DScenarios.cs
 M adapters/stub/StubRenderer3D.cs
 M adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs
 M adapters/unity/Assets/Resources/GameFoundation/models/placeholder_biped.controller
 M adapters/unity/Assets/Resources/GameFoundation/models/placeholder_biped.prefab
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityEngineHost.cs
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/AnimClipResolver.cs
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs
 M adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/ConformanceUnityTests.cs
 M core/foundation/engine_adapter/tests/ConformanceStubTests.cs
 M data/_sample/display/display.anim_set.json
 M presentation/render/core/AnimStateMachine.cs
 M presentation/render/core/ModelCharacterRig.cs
 M presentation/render/tests/AnimStateMachineTests.cs
?? adapters/unity/Assets/Resources/GameFoundation/anim_clips/hit.anim
?? adapters/unity/Assets/Resources/GameFoundation/anim_clips/hit.anim.meta
```

修改来源未归因，本审计未编辑源目录，也未将这些工作树内容纳入完整验收。

两份报告的源码证据均指向 `source_zip_extract`：该展开目录以固定 HEAD 生产源码为依据，同时含审计追加的 probe、测试工程引用和验证辅助文件。工作树漂移不改变本报告适用的启动基线；任何后续验收需先重新固定提交或单独审查这些并行修改。
