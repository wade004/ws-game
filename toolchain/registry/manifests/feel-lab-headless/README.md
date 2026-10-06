# com.gamefoundation.feel-lab.headless

手感实验室的无头命令行与自包含实验室根。它是**可选的开发期设施**（[ADR-0160](../../../../architecture/adr/0160-框架三层拆分运行时包手感实验室可选包与样板仓库.md)）：
框架的四个运行时包（`com.gamefoundation.adapter.unity`、`framework-data`、`toolchain`、`adapter.headless`）不含它，
游戏的独立版构建也永远不含它。游戏想在自己的数据上校准手感、回归标准脚本时才装这个包。

与 Unity 侧的 `com.gamefoundation.feel-lab.unity`（引擎实验室宿主与面板，仅编辑器编译）是一对：本包跑无头 suite/invariants，
那个包在引擎里看同一份数据的真实画面。两个包各自独立，互不依赖。

## 判断记录：为什么是独立的包，不放进 `com.gamefoundation.toolchain`

此前实验室命令行与实验室数据随 `toolchain` 包分发，结果是每个游戏的工具链安装里都带着一份只在校准手感时才用的预编译程序集与数据集
（约占 `toolchain` 包的一半体积），且让"运行时包 / 开发期设施"的边界模糊。拆出来之后：`toolchain` 只含数据校验、仿真等游戏每天都用的工具；
实验室作为开发期设施按需安装。门禁断言（`toolchain/_gate_package_boundary.ps1`）保证运行时包里不出现任何实验室文件。

## 包内布局

```
Tools~/feellab/
  bin/                       预编译命令行（FeelLab.dll、Lab.Kernel.dll 及其依赖）
  lib/                       FeelLab.csproj 在"无源码树"时引用的 9 个预编译 DLL
  Directory.Build.props      隔离用的空文件（避免被消费方仓库的 MSBuild 配置波及）
  labroot/                   自包含实验室根（工作目录约定见下）
    data/_framework  data/_feel  data/_feel_templates     框架数据（与 framework-data 包同源同版本）
    data/_lab  data/_lab_action  data/_equip              实验室数据集与占位装备集
    lab/fixtures/scripts  lab/fixtures/baselines          标准脚本与基线夹具
package.json
README.md（本文件）
```

`Tools~` 同 `Data~`/`Lib~`，是 Unity 保留命名约定：以 `~` 结尾的目录不会被 Unity 资产数据库扫描/导入。

## 使用

```
cd Tools~/feellab/labroot
dotnet ../bin/FeelLab.dll suite         # 全部标准脚本 × 六个格子对基线
dotnet ../bin/FeelLab.dll invariants    # 手感不变量
```

游戏自己的数据根通过脚本里的 `extraDataRoots` 追加；命令清单与参数见框架仓库根 `lab/README.md`（本包只随附编译产物与数据）。

## 版本

与框架同版本号发布，发布到同一个私服（`toolchain/registry/`）。`toolchain/get_framework.ps1 -FromRegistry` 不把本包写进
`Packages/manifest.json`，只在 `ws-game.lock` 的 `source.optional_packages` 里登记为可选包。
