# adapters/conformance —— L-1 引擎适配层契约一致性测试套件

对应 `architecture/11_工程规范与测试.md` 第 6 节、`architecture/02_引擎适配层.md`、ADR-0016：
`core/foundation/engine_adapter/contracts/` 下 13 个接口，每个都有至少一份契约一致性场景，
同一份场景源码可以对桩实现（`adapters/stub/`）与任意引擎实现（当前是 Unity，
`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/`）跑一遍，
验证"这份实现是否满足契约在语义/性能约定上的承诺"，而不只是"这份实现自己内部逻辑自洽"。

## 目录结构

- `IConformanceAssert.cs`：与测试框架无关的最小断言抽象（`True`/`Equal`/`Throws`/`Skip` 等）。
- `ConformanceContext.cs`：场景运行时需要的、随实现而异的协作点（推进一帧、触发窗口关闭请求、
  模拟一次写入失败等）。
- `ConformanceScenario.cs`：一个场景 = 名字 + 场景体，场景体签名
  `IEnumerator Run(TImpl impl, IConformanceAssert assert, ConformanceContext ctx)`。
- `WindowScenarios.cs` / `ClockScenarios.cs` / `Renderer2DScenarios.cs` / `AudioScenarios.cs` /
  `InputScenarios.cs` / `FileSystemScenarios.cs` / `ResourceLoaderScenarios.cs` /
  `Navigation2DScenarios.cs` / `SpatialQueryScenarios.cs` / `UISurfaceScenarios.cs` /
  `PlatformScenarios.cs` / `Renderer3DScenarios.cs` / `CameraScenarios.cs`：按接口拆分的场景列表，
  每个文件一个静态 `All` 数组。合计 46 个场景（≥ 40，见任务验收要求）。

本目录本身不引用任何测试框架（xUnit/NUnit）也不引用 `UnityEngine`——它只依赖
`core/foundation/common`（`Vec2`/`Id`/`Rect`/`SubscriptionHandle`）与
`core/foundation/engine_adapter/contracts`（13 个接口本身）。这样同一份源码才能被两侧各自的
包装层原样编译进两套完全不同的程序集（一个是 xUnit 测试工程，一个是 Unity 包）。

## 两侧包装位置

- xUnit（dotnet 侧，跑桩实现）：`core/foundation/engine_adapter/tests/ConformanceStubTests.cs`。
- NUnit/PlayMode（Unity 侧，跑 `UnityEngineHost` 各实现）：
  `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/ConformanceUnityTests.cs`。

## 判断记录

1. **场景体签名用 `IEnumerator Run(...)` 而不是任务书原文的 `void Run(...)`。**
   见 `ConformanceContext.cs` 顶部注释：桩（`StubClock.Advance`）能同步立即完成"推进一帧"，
   Unity（`UnityClock` 由 `UnityEngineHost.Update`/`FixedUpdate` 真实驱动）只能通过
   `yield return null`/`yield return new WaitForSecondsRealtime(...)` 真的等一帧——没有任何同步
   API 能在方法调用内部伪造一次引擎帧。`void Run` 无法表达这种"这一步需要真的等一帧"的协作，
   因此改为 `IEnumerator Run`：不含 `yield` 语句的场景（多数）在两侧包装层的第一次 `MoveNext()`
   内就跑完全部断言，与 `void Run` 的执行效果等价；含 `yield return ctx.AdvanceTime(seconds)` 的
   场景（`IClock`/`IResourceLoader` 的少数几个）才真正体现两侧驱动方式的差异。

2. **xUnit 包装方式：`Tests.Foundation.csproj` 用 `Compile Include` 直接编译本目录源码，
   不做成独立类库加进 `Core.sln`。** 任务书列出了两种可选方案（"或把 conformance 做成独立
   netstandard 类库 `adapters/conformance/Adapters.Conformance.csproj` 加进 `Core.sln`，并由
   `build.ps1` 一并拷贝 DLL——二选一并记录"）。选择前者的理由：
   - 本目录只是一组测试场景，不是任何游戏/框架运行时都需要依赖的产物，不需要参与
     `build.ps1` 的六 DLL 同步、不需要进 dist 分发包、不需要 `MANIFEST.txt` 记录哈希——做成
     独立类库会把这些全部牵连进来，而这些机制存在的目的是"游戏工程消费框架产物"，与
     "框架自己验证两套引擎实现是否满足同一份契约"是两件不同的事。
   - `Tests.Foundation.csproj` 已经用同一种 `Compile Include="../*/tests/**/*.cs"` glob
     把 `core/foundation/*/tests/` 下的源码直接编译进同一个 xUnit 工程，本目录用同样的手法
     （`Compile Include="../../../adapters/conformance/**/*.cs"`）是最小改动、风格一致的做法。
   - Unity 侧的编译不经过这条 dotnet glob，而是另立一个 Unity 本地包
     （见下一条），因此"是否把 conformance 做成 .csproj 类库"这个选择只影响 dotnet 侧，
     不影响 Unity 侧能否编译。

3. **Unity 侧消费方式：`adapters/conformance` 同时也是一个 Unity 本地源码包
   （`package.json` + `Runtime/Adapters.Conformance.asmdef`），由
   `adapters/unity/Packages/manifest.json` 以 `file:../../../adapters/conformance` 引用，
   仅供 `adapters/unity` 这个"引擎适配层工作台"工程消费；不进 dist、不进
   `games/_template`。** 与本目录顶层的 `*.cs` 文件共存不冲突——`package.json`/`*.asmdef` 不是
   `.cs` 文件，不会被 dotnet 侧的 `Compile Include` glob 误纳入；`Runtime/` 子目录下是本目录顶层
   同一批 `.cs` 文件的符号链接？—— **不是符号链接**，是 Unity 包要求 `Runtime/`
   子目录承载运行时源码的目录约定，本包的 `Runtime/` 目录直接就是本目录顶层这些场景源码文件本身
   （`package.json` 与 asmdef 放在包根，场景源码放在包根下的 `Runtime/` 子目录——见该子目录）。
   `adapters/unity/Packages/manifest.json` 的 `testables` 未列出这个新包，因此它只是一个普通
   运行时依赖，其中不含任何测试代码（场景本身不是测试用例，是被两侧测试各自枚举/驱动的数据 +
   委托），不会被误判为"这个包自己也要跑测试"。

4. **Unity 侧对 `IWindow`/`IFileSystem`/`INavigation2D`/`ISpatialQuery` 四个接口新构造独立
   实现实例，其余 9 个接口使用 `UnityEngineHost.Ensure()` 返回的共享单例。** 这不是单纯的
   "能不能独立 new"，而是两条各自独立的理由：
   - `IClock`/`IAudio`/`ICamera`/`IResourceLoader` 四个实现依赖 `UnityEngineHost.Update`/
     `FixedUpdate` 每帧调用其内部的 `TickFrame`/`TickFixedStep`/`Tick` 方法才会真正产生效果
     （`UnityClock.TickFrame`/`TickFixedStep`、`UnityResourceLoader.Tick` 都是
     `internal`，只由 `UnityEngineHost` 调用）——独立 `new UnityClock()` 出来的实例永远不会有
     任何引擎循环驱动它，`OnFrame`/`RequestFixedStep` 回调永远不会触发，`LoadAsync` 的
     后台线程结果永远不会被主线程消化。这几个接口的场景**必须**用共享实例才有真实语义。
   - `IRenderer2D`/`IRenderer3D`/`IInput`/`IUISurface`/`IPlatform` 五个实现的构造函数需要
     `Transform`/`Camera`/`UnityResourceLoader` 等依赖，独立构造成本高、容易与真实运行时的
     组装方式脱节，改用共享实例更贴近真实使用场景；这些场景的写法克制在"只做加法、不做全局
     清空"（新建自己的资源 id、自己的句柄，不调用会影响其它对象的清空类方法），避免污染同一次
     `-runTests` 运行里其它测试用例的前置状态。
   - `IWindow`/`IFileSystem`/`INavigation2D`/`ISpatialQuery` 四个实现不依赖任何引擎循环驱动
     （纯粹按方法调用同步生效），独立构造反而更安全：`ISpatialQuery.Clear()`
     会清空整个空间索引，若操作共享实例会波及同一次运行里其它依赖空间查询的测试；
     `IWindow.SetFullscreen`/`SetResolution` 修改的是进程级 `UnityEngine.Screen` 状态（与是否
     共享实例无关，但独立实例至少不会污染共享 `UnityEngineHost.Window` 自身的
     `Created`/`Title` 等状态字段）；`adapters/unity` 包既有的
     `Tests/Runtime/UnityWindowTests.cs`（`Create_SetsFieldsAndCreatedTrue` 等三个用例）已经是
     "每个用例 `new UnityWindow()`"这一惯例的先例，本套件延续同一惯例。

5. **`IRenderer3D` 场景组不用 `assert.Skip`，改为按 `ConformanceContext.SupportsRenderer3D`
   在"正常路径"与"应抛 NotSupportedException"两条断言路径间切换。** 任务书原文允许"场景声明
   降级并跳过"，但"Unity 侧全部方法必须抛同一种 `NotSupportedException`"本身就是
   `UnityRenderer3D.cs` 类型注释里显式声明的行为约定，具备可确定性验证的价值，直接断言比跳过
   更有把关意义（真的改坏了这条"声明降级"约定时，跳过发现不了，断言能发现）。

6. **`IFileSystem` 的"写入失败时旧内容不变"场景用 `ConformanceContext.SimulateNextWriteFailure`
   钩子，桩侧提供、Unity 侧在枚举层面直接不跑这一条（而不是运行期 `Skip`）。**
   `StubFileSystem.FailNextWrite()` 是一个确定性的测试专用开关；Unity 的真实文件系统没有同等
   确定性的触发方式（构造一个必然失败的路径依赖具体操作系统/文件系统权限模型，跨机器不可靠）。
   场景体本身仍然保留"钩子为空则 `assert.Skip(...)`"这条防御性分支（`IConformanceAssert.Skip`
   在 NUnit 侧落地为 `Assert.Ignore`，是真正的运行期动态跳过 API）——但
   `Tests/Editor/ConformanceUnityEditorTests.cs` 的 `FileSystemNames()` 直接把这一条场景名从
   Unity 侧要跑的列表里过滤掉，不让它真的以 `Ignored` 收尾。原因：NUnit 的 `Assert.Ignore` 会把
   整条测试装配（`.xml` 结果根节点）的聚合 `result` 属性从 `"Passed"` 改写成
   `"Skipped:Ignored"`，与 `check.ps1` 既有 EditMode/PlayMode 两步"`result == Passed` 才算通过"
   的判定逻辑冲突——修改这条既有判定逻辑超出本任务对 `check.ps1` 的写入范围（"仅新增步骤"，见
   任务书硬性规则 1），因此选择在枚举层面回避，而不是碰这条判定逻辑。桩侧
   （`core/foundation/engine_adapter/tests/ConformanceStubTests.cs`）没有这个顾虑，正常跑
   全部 5 条 `IFileSystem` 场景。

7. **`IResourceLoader` 场景组不覆盖"注册后加载成功"路径。** 桩实现的成功路径只需要
   `Register(id)` 一行；Unity 实现真的会去 `StreamingAssets` 目录读一份磁盘文件，要让这条路径
   在两侧都确定性成功，需要预先在两侧都放一份内容一致的资源文件，这属于内容管线职责，不属于
   "引擎适配层契约是否被满足"要验证的范围。本组场景只覆盖两侧都能不依赖真实资源文件确定性触发
   的契约条款（未注册资源的失败路径、`IsLoaded`/`GetLoadProgress` 取值范围、`Unload` 的宽松
   语义）。
