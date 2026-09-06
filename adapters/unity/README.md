# adapters/unity 工作台工程

本目录是 Unity 6000.3.23f1（URP 17.0.3 2D Renderer，Input System 1.20.0）工作台工程，用来
本地跑测试/灰盒验证框架的 Unity 引擎适配层实现；真正的框架产出是嵌入式包
`Packages/com.gamefoundation.adapter.unity/`（各游戏工程以 `file:` 方式引用它，不引用本工作台
工程本身）。

## 目录结构

```
adapters/unity/
  Assets/
    Editor/                      仅编辑器工具（见下 GreyBoxTools），随包不分发
      GreyBoxSceneBuilder.cs       程序化生成/重建 Assets/Framework/Scenes/GreyBox.unity
      Il2CppPlayerBuilder.cs       IL2CPP 脚本后端独立版构建入口（工程收尾 K 新增，见本文档
                                   "IL2CPP 发布路径验证"一节与 check.ps1 -Il2cpp）
    Framework/
      Resources/Fonts/            按 "font.<name>" id 预导入的字体资产（当前 noto_sans_cjk_sc.otf），
                                   UnityResourceLoader/UnityUISurface 依赖，见包 README"资源 id → 路径规则"
      Scenes/Shell.unity           Shell 场景（U3），已加入 Build Settings 第 0 位（启动场景）
      Scenes/GreyBox.unity         灰盒测试场景（U2-4），已加入 Build Settings 第 1 位
    StreamingAssets/GameFoundation/  build.ps1 -SyncContent 生成物，不提交（见 .gitignore）
    TextMesh Pro/                 TMP 官方 Essential Resources（U1 已提交）
  Packages/
    com.gamefoundation.adapter.unity/   框架产出的嵌入式包，见该目录 README.md
    manifest.json
  ProjectSettings/                URP 2D Renderer、Input System、TMP 等工程配置
```

## build.ps1 各开关（在仓库根目录跑）

| 命令 | 效果 |
|---|---|
| `powershell -File build.ps1` | 完整流程：`dotnet build/test` → 同步六个核心 DLL 到 `Runtime/Plugins/Core/` → 同步内容数据集到 `Assets/StreamingAssets/GameFoundation/`（见下） |
| `powershell -File build.ps1 -SkipTests` | 同上，跳过 `dotnet test` |
| `powershell -File build.ps1 -SyncOnly` | 跳过 `dotnet build/test`，DLL 同步 + 内容同步都执行（要求此前至少完整 build 过一次） |
| `powershell -File build.ps1 -SyncContent` | 只做内容同步（跳过 `dotnet build/test` 与 DLL 同步）；只改了 `data/_sample`/`assets/_placeholder`、没改任何 C# 代码时的快速路径 |
| `powershell -File build.ps1 -Dist 0.0.1` | 额外打一份分发包到 `dist/0.0.1/` |

内容同步（U2-1 新增，一律哈希比较、只拷变化文件、镜像删除源目录已不存在的文件）：

| 源 | 目标 | 用途 |
|---|---|---|
| `data/_sample/` | `Assets/StreamingAssets/GameFoundation/data/_sample/` | `GameFoundationBootstrap` 用只读 `StreamingAssetsFileSystem` + `FileSystemDataSource` 加载 |
| `assets/_placeholder/` | `Assets/StreamingAssets/GameFoundation/assets/_placeholder/`（整体镜像） | 保留原始目录结构，供直接按路径访问（如灰盒地面纹理） |
| `assets/_placeholder/sprites/` | `Assets/StreamingAssets/GameFoundation/sprites/` | `UnityResourceLoader` 的 `ResourceKind.Image` 路径规则 |
| `assets/_placeholder/sfx/` | `Assets/StreamingAssets/GameFoundation/audio/` | `UnityResourceLoader` 的 `ResourceKind.Audio` 路径规则（源目录名 `sfx`，目标固定叫 `audio`） |
| `assets/_placeholder/fonts/*.otf\|*.ttf` | `Assets/Framework/Resources/Fonts/`（注意不是 StreamingAssets） | `ResourceKind.Font` 走 `Resources.Load<Font>`，字体资产必须先被 Unity 资产管线导入，见包 README"资源 id → 路径规则"（缺口 1） |

## 命令行跑测试 / 编译检查 / 构建（见包 README 同一节，此处只给最终命令）

```
# 编译检查（-quit，仅此一处需要 -quit；与 -runTests 同传会导致测试提前退出，见包 README 判断记录）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -logFile <out>\compile.log

# EditMode / PlayMode（不要加 -quit）
Unity.exe -batchmode -nographics -projectPath adapters\unity -runTests -testPlatform EditMode -testResults <out>\editmode.xml -logFile <out>\editmode.log
Unity.exe -batchmode                -projectPath adapters\unity -runTests -testPlatform PlayMode -testResults <out>\playmode.xml -logFile <out>\playmode.log

# 重新生成灰盒场景（场景文件损坏/需要调整时）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -executeMethod Adapter.Unity.EditorTools.GreyBoxSceneBuilder.Build -logFile <out>\scene.log

# 重新生成 Shell 场景（U3 新增，同上）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -executeMethod Adapter.Unity.EditorTools.ShellSceneBuilder.Build -logFile <out>\scene.log

# Windows 独立版构建（启动场景 = Build Settings 第 0 位 = Shell.unity；默认脚本后端，见
# ProjectSettings 当前保存的值，历史上一直是 Mono）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -buildWindows64Player <out>\Shell.exe -logFile <out>\build.log

# Windows 独立版构建，IL2CPP 脚本后端（工程收尾 K 新增，见下"IL2CPP 发布路径验证"一节；
# 内置的 -buildWindows64Player 开关不提供临时切脚本后端的能力，必须走自定义 -executeMethod；
# 构建产物输出路径经 -gfOutputPath 命令行参数或 GF_IL2CPP_OUTPUT_PATH 环境变量二选一传入）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -executeMethod Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp -gfOutputPath <out>\Shell_il2cpp.exe -logFile <out>\build_il2cpp.log
```

跑测试/构建前需要先跑过一次 `build.ps1`（至少 `-SyncContent`），否则灰盒场景加载数据集/占位资源会
因为 `Assets/StreamingAssets/GameFoundation/` 不存在而失败；U3 新增的 `data/sample_field.json`
占位场景资源同样由该步骤生成，Shell 场景的"新游戏/读档"依赖它（见包 README U3 一节判断记录）。

## U3：UI 套件、Shell 流程、灰盒竖切测试

见包 `README.md`"U3：UI 套件默认皮肤、Shell 流程、灰盒竖切测试与独立版冒烟"一节（十个界面单元
清单、Shell 状态流程图、示例 NewGameStarter、契约缺口发现、人工验收清单、命令行跑法）。

## IL2CPP 发布路径验证（工程收尾 K 新增）

判断记录（为什么要单独验证）：`core/` 的核心逻辑类库（尤其 `core/foundation/common/json` 下
自写的零依赖 JSON 读写器）当初就是照着"AOT 编译、无反射兜底"这条约束设计的（见
`architecture/选型/01_引擎与语言选型评估.md`"发布形态验证"一节判断记录），但在本次之前从未在
真正的 IL2CPP 脚本后端下实测过——独立版默认走的一直是 Mono 后端（Windows Standalone 平台
ProjectSettings 里保存的默认值），Mono 后端本身带 JIT，即使代码里不小心留有反射依赖也不会在
Mono 下暴露问题，只有 IL2CPP（AOT，运行期不能再生成新的机器码，反射能力受限、需要显式的
AOT 泛型实例化提示）才能验证到这一条约束是否真正被遵守。

构建入口：`Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp`（源码见
`Assets/Editor/Il2CppPlayerBuilder.cs`），命令行写法见上一节。构建前用
`PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP)`
临时把脚本后端切到 IL2CPP，`BuildPipeline.BuildPlayer` 结束后在 `finally` 里还原为构建前读到的
原始值。判断记录（2026-09-06 实跑修正）：最初以为"不调用 `AssetDatabase.SaveAssets()` 就不会
落盘"，实测证伪——Unity 只要在本次会话跑过至少一次 `BuildPipeline.BuildPlayer`，`-quit` 退出
编辑器域时就会把 `PlayerSettings` 落盘一次，与是否显式 `SaveAssets()` 无关；因此 `finally` 里的
显式还原是必需步骤（保证磁盘上最终值与构建前一致），不是防御性写法，详见该脚本头注释与
`architecture/选型/01_引擎与语言选型评估.md`"发布形态验证"一节完整判断记录。

`check.ps1 -Il2cpp`（默认不跑，见该开关说明）会额外构建一份 IL2CPP 独立版产物（与默认 Mono
产物分开落地，互不覆盖），再跑一遍 `-gf-smoke`/`-gf-smoke-discrete` 两种无人值守冒烟。

### 实测数据（2026-09-06，本机 Unity 6000.3.23f1 + 已安装的 IL2CPP 模块）

| 项目 | Mono（默认独立版构建步骤） | IL2CPP（`-Il2cpp`） |
|---|---|---|
| 构建 + `-gf-smoke` 冒烟总耗时 | 14.8s | 构建失败，614s 后以清晰诊断退出（绝大部分时间是 Unity 针对该脚本后端的一次性导入/脚本编译/着色器编译，真正的构建失败判定只发生在最后约 14s，见下） |
| `-gf-smoke-discrete` 冒烟耗时（复用同一份产物） | 3.3s | 未生成产物，未跑 |
| 构建结果 | 成功 | **失败**：本机未安装 IL2CPP 编译 C++ 产物所需的原生工具链（Visual Studio 2019/2022 C++ 组件 + Windows 10 SDK ≥ 10.0.19041.0）——`Unity.IL2CPP.Bee.BuildLogic.ToolchainNotFoundException`，不属于 Unity 模块范畴，按任务约束本次不代为安装 |

结论与完整判断记录（含失败原文、耗时构成分析、`ProjectSettings.asset` 还原语义核对）见
`architecture/选型/01_引擎与语言选型评估.md`"发布形态验证"一节——该文档允许出现具体技术名，
实测数据落在那里，本文档只记录判断记录与命令行写法。IL2CPP 独立版构建/脚本后端切换/失败诊断
这条机制链路本身已验证正确（构建正确触发、失败诊断准确、`ProjectSettings.asset` 未被永久
改到 IL2CPP）；"核心类库在真正 AOT 运行时下可运行"这一结论仍需一台具备完整 IL2CPP 工具链的
机器补跑验证。
