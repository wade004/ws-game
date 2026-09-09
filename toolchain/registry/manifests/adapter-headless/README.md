# com.gamefoundation.adapter.headless

引擎适配层（L-1）的桩实现（"无头适配层"），随版本号发布，与 `com.gamefoundation.adapter.unity`
（引擎适配层 + 六个核心 DLL）、`com.gamefoundation.framework-data`（框架级数据表 + 占位资产包）、
`com.gamefoundation.toolchain`（Python 工具链）并列为私服发布的四个包之一（见根
`toolchain/registry/README.md`）。[ADR-0018](../../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md)
决策第 3 条起，本程序集由"仅测试用"转正为框架正式交付物：确定性、无渲染、无输入，供不接真实
引擎的无头宿主（自动化测试、CI、内容编辑器的数值沙盘等）使用，与框架自身测试用的是同一份实现，
结果一致性由此保证。

## 判断记录：为什么本包不是 Unity 依赖，不写入 `Packages/manifest.json`

`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/`com.gamefoundation.
toolchain` 三者均服务"游戏工程需要引用什么"这一需求，`toolchain/get_framework.ps1 -FromRegistry`
因此会把它们写进游戏工程 `Packages/manifest.json` 的 `dependencies`。本包不同：它服务的是"无头
宿主"（测试/CI 进程、内容编辑器基础套件的沙盘子进程），这些宿主通常不是同一个 Unity 工程本身，
不需要经 UPM 包解析拿到本包——按普通 `npm install com.gamefoundation.adapter.headless@<version>
--registry <私服地址>` 或直接 `npm pack`/手工下载 tarball 获取即可。`get_framework.ps1
-FromRegistry` 因此只在 `ws-game.lock` 的 `source.optional_packages` 字段登记本包为"本次引用的
框架版本额外提供、按需自取"的可选包，不写入 `manifest.json`，避免 Unity 包解析器尝试解析一个它
不需要、也不应该解析的包。

## 包内布局

```
Lib~/
  Adapters.Stub.dll          桩实现程序集（只依赖 Core.Foundation.dll，见"依赖"一节）
package.json
README.md（本文件）
```

`Lib~` 同 `Data~`/`Tools~`，是 Unity 保留命名约定：以 `~` 结尾的目录不会被 Unity 资产数据库扫描/
导入，只是随包一起落在磁盘上（UPM 解析到 `Library/PackageCache/com.gamefoundation.adapter.
headless@<version 或 hash>/` 后，`Lib~/` 就在这个解析出的路径下），供宿主进程按路径引用这份
编译产物；本包不随附任何 Unity 侧脚本/预制体，`.unity`/`keywords` 等字段只是与另外三个包保持
同一份 `package.json` 结构约定，不代表本包会被 Unity 资产管线处理。

## 依赖

`Lib~/Adapters.Stub.dll` 只引用六个核心 DLL 之一 `Core.Foundation.dll`——宿主进程需要自行另外
获取 `Core.Foundation.dll`（本包不重复携带，可从同版本号的 `com.gamefoundation.adapter.unity`
包 `Runtime/Plugins/Core/` 下取得，或框架 zip 快照的同一路径）。

## 使用方式

```csharp
using Adapters.Stub;

var engine = new StubEngine(); // 一次性构造全部 13 个引擎适配层接口的桩实例
engine.Clock.Advance(1.0 / 60.0);
```

具体桩清单与各类型行为见框架仓库根 `adapters/stub/README.md`（本包只随附编译产物，不随附源码）。
