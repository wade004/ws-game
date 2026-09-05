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
    Framework/
      Resources/Fonts/            TMP 占位字体（NotoSansCJKsc-Regular），UnityUISurface 依赖
      Scenes/GreyBox.unity         灰盒测试场景（U2-4），已加入 Build Settings 第 0 位
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

## 命令行跑测试 / 编译检查 / 构建（见包 README 同一节，此处只给最终命令）

```
# 编译检查（-quit，仅此一处需要 -quit；与 -runTests 同传会导致测试提前退出，见包 README 判断记录）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -logFile <out>\compile.log

# EditMode / PlayMode（不要加 -quit）
Unity.exe -batchmode -nographics -projectPath adapters\unity -runTests -testPlatform EditMode -testResults <out>\editmode.xml -logFile <out>\editmode.log
Unity.exe -batchmode                -projectPath adapters\unity -runTests -testPlatform PlayMode -testResults <out>\playmode.xml -logFile <out>\playmode.log

# 重新生成灰盒场景（场景文件损坏/需要调整时）
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -executeMethod Adapter.Unity.EditorTools.GreyBoxSceneBuilder.Build -logFile <out>\scene.log

# Windows 独立版构建
Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -buildWindows64Player <out>\GreyBox.exe -logFile <out>\build.log
```

跑测试/构建前需要先跑过一次 `build.ps1`（至少 `-SyncContent`），否则灰盒场景加载数据集/占位资源会
因为 `Assets/StreamingAssets/GameFoundation/` 不存在而失败。
