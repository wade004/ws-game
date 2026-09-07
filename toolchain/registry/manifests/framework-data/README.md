# com.gamefoundation.framework-data

框架级数据表（`data/_framework/`）+ 占位资产包（`assets/_placeholder/`）+ TextMeshPro 运行期资源
（`assets/textmesh_pro_essentials/`），随版本号发布，与 `com.gamefoundation.adapter.unity`（引擎
适配层 + 六个核心 DLL）、`com.gamefoundation.toolchain`（Python 工具链）并列为私服发布的三个包
之一（见根 `toolchain/registry/README.md`）。

## 包内布局

```
Data~/
  data/_framework/            框架级数据表（found.event_catalog、found.input_action 等）
  assets/_placeholder/        通用占位资产包（精灵、特效、音效、音乐、地图分层图、字体等源素材）
  assets/textmesh_pro_essentials/   TextMesh Pro 运行期必需资源（TMP_Settings.asset、SDF 着色器等）
package.json
README.md（本文件）
```

`Data~` 是 Unity 的保留命名约定：以 `~` 结尾的目录不会被 Unity 资产数据库扫描/导入，但目录本身
和其中的文件在磁盘上真实存在（UPM 把包解析到 `Library/PackageCache/com.gamefoundation.
framework-data@<version 或 hash>/` 后，`Data~/` 就在这个解析出的路径下），可以被普通文件 I/O
按路径读到——这正是"不当 Unity 资产导入、但游戏侧构建脚本能读到字节"这一需求的标准做法。

## 判断记录：为什么内容不能直接被引擎运行时读到，需要一步"同步到 StreamingAssets"

框架引擎适配层的 `UnityFileSystem.GetContentRootDir()`（见 `com.gamefoundation.adapter.unity`
包内 `Runtime/EngineAdapter/UnityFileSystem.cs`）返回的内容根固定是 `Application.
streamingAssetsPath`（即游戏工程自己的 `Assets/StreamingAssets/`，在独立版构建里对应平台相关的
只读内容目录）——`DataRegistry`/`IResourceLoader` 全部从这个根目录按相对路径读数据表和占位资产的
字节，且这个根目录只可能是"当前游戏工程自己的 StreamingAssets"，不可能是"某个 UPM 包在
`Library/PackageCache/` 下解析出的路径"（`Application.streamingAssetsPath` 是 Unity 固定语义，
不支持重定向到包目录，独立版构建也只会把游戏工程自己 `Assets/StreamingAssets/` 下的内容打进最终
包）。

因此，无论框架的数据/资产以哪条通道（zip 快照的 `file:` 引用，还是本私服的按版本号依赖）分发到
游戏侧，游戏侧都需要一步"从获得的内容位置，把 `data/_framework`/`assets/_placeholder`/
`assets/textmesh_pro_essentials` 拷贝进自己工程的 `Assets/StreamingAssets/GameFoundation/`（前两者）
和 `Assets/TextMesh Pro/`（后者，见下"为什么这一份不进 StreamingAssets"）"——zip 通道下这一步由
本框架仓库自己的 `build.ps1`（同步进**框架自己**的工作台工程）或消费方按
`games/_template/README.md` 手工拷贝/`toolchain/consumer_smoke.ps1` 自动镜像拷贝完成；私服通道下
新增 `toolchain/sync_package_content.ps1`，按 UPM 解析出的包路径做同一件事：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File toolchain\sync_package_content.ps1 `
  -UnityProjectPath <你的 Unity 工程根目录>
```

默认按包名 `com.gamefoundation.framework-data` 在 `<工程根>\Library\PackageCache\` 下查找
`com.gamefoundation.framework-data@*` 目录（UPM 对 `file:`/仓库源解析出的包固定这个命名规则，
版本号或内容哈希拼在 `@` 后面，不假设具体后缀），取其 `Data~/` 内容，按哈希比较增量同步到：

- `Data~/data/_framework` → `<工程根>/Assets/StreamingAssets/GameFoundation/data/_framework`
- `Data~/assets/_placeholder` → `<工程根>/Assets/StreamingAssets/GameFoundation/assets/_placeholder`
  （整体镜像，保留原始子目录名）
- `Data~/assets/_placeholder/{sprites,sfx,vfx}` → `<工程根>/Assets/StreamingAssets/
  GameFoundation/{sprites,audio,vfx}`（P07 根治新增，2026-09-07：额外按 `UnityResourceLoader`
  实际查找的目标子目录名再同步一份——加载器不读上一条镜像出来的 `assets/_placeholder/<原始子目录
  名>/...`，只认 `GameFoundation/sprites|audio|vfx/...`，两棵目录树是不同的路径；映射表见
  `toolchain/resource_layout_map.json`，与框架仓库 `build.ps1` 同步进工作台工程用的是同一份）
- `Data~/assets/textmesh_pro_essentials` → `<工程根>/Assets/TextMesh Pro`
  （不进 `StreamingAssets`——TMP 运行期资源必须是被 Unity 资产管线正式导入过的 `.asset`/
  `Shader` 等对象，`TMP_Settings` 是靠 Unity 在场景/首次绘制时按固定资源路径
  `Resources.Load<TMP_Settings>` 找到的单例，不是靠 `IResourceLoader` 读字节，因此必须落在
  普通 `Assets/` 目录下让 Unity 正常导入，而不是 `StreamingAssets`，与 `com.gamefoundation.
  adapter.unity` 包 `Runtime/EngineAdapter/UnityUISurface.cs` 顶部"判断记录（TMP 运行期依赖）"
  同一结论、`games/_template/README.md`"复制为新游戏：改哪几处"第 8 步手工拷贝的对象一致）。

这一步需要在游戏工程首次解析完包（`Library/PackageCache/` 下已经有对应目录）之后运行一次，此后
每次升级版本号、重新解析包，都应该重跑一遍以获取新版本的内容（脚本本身按哈希比较，只拷贝变化的
文件，可以反复安全调用）。

`toolchain/sync_package_content.ps1` 除了服务本包，也同样支持 `-PackageName com.gamefoundation.
toolchain` 把工具链包的 `Tools~/` 同步到游戏工程本地某个目录（较少见，多数场景直接在包解析路径下
用 `python <Library/PackageCache 下的路径>/Tools~/validate_data.py` 调用即可，不必拷出来，见
`com.gamefoundation.toolchain` 包 README）。
