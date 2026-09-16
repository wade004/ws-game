# com.gamefoundation.toolchain

跨游戏的数据校验/资产导入 Python 工具链 + 数值仿真命令行入口（simrunner，T-N6-7），随版本号
发布，内容与框架仓库根 `toolchain/` 一致（不含 `toolchain/registry/` 自身——私服运行时不需要
随游戏侧分发；也不含 `__pycache__/`、`.venv/`、`toolchain/validator/bin|obj`、
`toolchain/simrunner/bin|obj`——分别是编译缓存、虚拟环境、.NET 构建产物，见 `build.ps1` 打包
这三个包时的排除规则；`validator/bin/`、`simrunner/bin/` 两处预编译产物由 `build.ps1` 打包时
单独补齐，见下方包内布局一节，不是通配符排除规则的例外，只是"先整体排除、再针对这两个精确
路径单独放回"）。

## 包内布局

```
Tools~/
  validate_data.py            数据校验入口（骨架级 + 调用 validator 做真实校验）
  gen_event_constants.py      事件常量生成器
  gen_placeholder_assets.py   占位资产生成器
  import_assets.py            资产导入 CLI
  import_sample_assets.py     样例资产导入驱动
  asset_import/                资产导入子模块
  validator/                   .NET 校验器源码（DiskFileSystem.cs、Program.cs、Validator.csproj，
                                不含 bin/obj 构建产物——首次使用需要 `dotnet build` 一次）
    bin/                        预编译产物（Validator.dll + 全部依赖 DLL，见下方"依赖安装"一节
                                判断记录）与空 Directory.Build.props（挡住消费方仓库根同名文件被
                                隐式继承），供直接 `dotnet Tools~/validator/bin/Validator.dll`
                                执行，不需要先 `dotnet build`
    lib/                        八个 DLL（六个核心 DLL：Core.Foundation/Core.Numbers/Core.Rules/
                                Core.Carriers/Core.Gameplay/Presentation.Common；+ Core.Sim/
                                Adapters.Stub，T-N6-7 起随本包一起打包，见下方"依赖安装"一节
                                判断记录），Validator.csproj 用 <Reference HintPath> 直接引用——
                                本包不随附 presentation/、core/ 源码，不能靠 ProjectReference
                                现场编译
  simrunner/                   数值仿真命令行入口源码（Program.cs、SimRunner.csproj，T-N6-6/
                                T-N6-7；ADR-0035 决策 5"报告为结构化产物、基线对比工具输出改动
                                前后统计量差异"）
    bin/                        预编译产物（SimRunner.dll + 全部依赖 DLL）与空
                                Directory.Build.props，同上惯例，供直接
                                `dotnet Tools~/simrunner/bin/SimRunner.dll run ...` 执行
    lib/                        七个 DLL（五个核心 DLL：Core.Foundation/Core.Numbers/Core.Rules/
                                Core.Carriers/Core.Gameplay——不需要 Presentation.Common；+
                                Core.Sim/Adapters.Stub），SimRunner.csproj 同款 <Reference
                                HintPath> 回退分支
  requirements.txt / requirements-optional.txt
  install_hooks.ps1
  _console.py
  sim_baseline.ps1             数值仿真基线更新流程的薄封装（T-N6-6/T-N6-7），对
                                `simrunner/` 的一层参数简化；项目路径按本脚本自身目录解析
                                （`Join-Path $PSScriptRoot "simrunner"`，两种布局下都与本脚本
                                同级，见该脚本判断记录），`dotnet run --project` 会自动走
                                `SimRunner.csproj` 的 `lib/` 回退分支现场编译（本包不含
                                `core/sim` 源码树）；需要跳过现场编译、直接执行上面已经预编译
                                好的 `simrunner/bin/SimRunner.dll` 时，改按上方"命令行入口"
                                （核心仓库 `core/sim/README.md`）一节的 `dotnet <dll路径> run
                                ...` 用法直接调用，不经过本脚本
  sync_package_content.ps1     私服交付通道配套工具：把 com.gamefoundation.framework-data/
                                com.gamefoundation.toolchain 两个包内容同步/定位到消费方 Unity
                                工程可用的位置（见该脚本头注释、com.gamefoundation.framework-data
                                包 README.md）
  resource_layout_map.json     sprites/audio/vfx 目标子目录名映射表（P07 根治，2026-09-07新增）：
                                sync_package_content.ps1 与框架仓库 build.ps1 共用同一份，避免
                                两处各自维护导致漂移，见该文件内 "_comment" 判断记录
package.json
README.md（本文件）
```

`Tools~` 同 `Data~`，是 Unity 保留命名约定：以 `~` 结尾的目录不会被 Unity 资产数据库扫描/导入，
只是随包一起落在磁盘上（UPM 解析到 `Library/PackageCache/com.gamefoundation.toolchain@<version
或 hash>/`），供游戏侧按路径直接用 `python` 调用，不经过 Unity 的资产导入流程（这些脚本本来就不是
Unity 资产，是命令行工具）。

## 调用方式

```powershell
# <resolved> = <你的 Unity 工程根目录>\Library\PackageCache\com.gamefoundation.toolchain@<版本或哈希>\Tools~
python <resolved>\validate_data.py --framework-root <framework-data 包解析路径>\Data~\data\_framework --data-root <你的游戏>\data
```

`<resolved>` 路径可以用仓库根 `toolchain\sync_package_content.ps1 -PackageName com.gamefoundation.
toolchain -ResolveOnly` 打印出来（`-ResolveOnly` 只解析路径、不做任何拷贝，见该脚本头注释），
避免每次手动去 `Library/PackageCache/` 底下找带哈希后缀的目录名。

数值仿真基线比对（T-N6-7）：

```powershell
dotnet <resolved>\simrunner\bin\SimRunner.dll run --scenario all `
  --framework-root <framework-data 包解析路径>\Data~\data\_framework --data-root <你的游戏>\data `
  --out <out-dir> --baseline-dir <你的游戏基线目录>
```

完整参数/退出码说明见框架仓库 `core/sim/README.md`"命令行入口"一节；`Tools~/sim_baseline.ps1`
是同一入口的薄封装（默认现场编译，见该脚本判断记录）。

## 依赖安装

`Tools~/requirements.txt`（必需）、`Tools~/requirements-optional.txt`（占位资产生成器等可选功能）。

T-N6-7 起 `Tools~/validator/bin/Validator.dll`、`Tools~/simrunner/bin/SimRunner.dll` 均已随包
预编译（见上方包内布局一节），`validate_data.py`/`sim_baseline.ps1` 优先以子进程方式直接执行这些
已编译好的产物，不需要先手动 `dotnet build`；两者各自的 `bin/` 下也附一份空
`Directory.Build.props`，挡住消费方仓库根同名文件被隐式继承。仍需要现场编译时（例如精简掉了
`bin/` 目录，或改动了本包源码），两个项目各自的 `lib/` 都已自带独立发行包所需的全部 DLL（见上方
包内布局一节；`simrunner` 不需要 `Presentation.Common.dll`），`dotnet build`/`dotnet run` 直接
引用这些 DLL 完成编译，不需要另外拿到本框架仓库的 `presentation/`、`core/` 源码——这是独立包，
只需要按上面"依赖安装"这一节操作即可自包含运行。

```powershell
python -m venv <你的虚拟环境目录>
<你的虚拟环境目录>\Scripts\pip install -r <resolved>\requirements.txt
# 仅在需要现场重新编译时才需要下面两行（正常情况下直接执行 bin/ 下的预编译产物即可）：
dotnet build <resolved>\validator\Validator.csproj -c Release
dotnet build <resolved>\simrunner\SimRunner.csproj -c Release
```

判断记录（P02 根治，2026-09-07，审计 `architecture/落地计划/audit-7e63d66-20260907/
project-review.md` P02）：此前 `Validator.csproj` 无条件用 `ProjectReference` 指向
`../../presentation/Presentation.Common.csproj`（经其传递引用 `core/` 下四个程序集）与
`../../adapters/stub/Adapters.Stub.csproj`，两者在本包内都不存在（本包不含 `presentation/`/
`core/`/`adapters/` 任一层目录），导致 `dotnet build Tools~/validator/Validator.csproj` 直接
报引用路径不存在；`validate_data.py` 的 `find_repo_root()` 拼出的 `<repo_root>/toolchain/
validator` 在本包内同样不存在（本包内该文件在 `Tools~/validate_data.py`，`repo_root` 会被
解析成包根而不是 `toolchain/` 的上一级）。两处均已改为不依赖"随包附带完整仓库布局"这一假设：
`Validator.csproj` 改引用本目录下 `lib/` 子目录里编译好的 DLL；`validate_data.py` 改为相对
自身文件所在目录解析 `validator/` 子目录（两种布局下 `validator/` 都与 `validate_data.py`
直接同级）。详见框架仓库根 `toolchain/README.md`"`toolchain/validator`（.NET 真实校验工具）"
一节同一判断记录。

其余用法（控制台编码约定、`validate_data.py` 两道校验的分工等）与框架仓库根 `toolchain/README.md`
完全一致，本包只是同一份内容的按版本号发布形态。
