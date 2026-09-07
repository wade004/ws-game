# com.gamefoundation.toolchain

跨游戏的数据校验/资产导入 Python 工具链，随版本号发布，内容与框架仓库根 `toolchain/` 一致
（不含 `toolchain/registry/` 自身——私服运行时不需要随游戏侧分发；也不含 `__pycache__/`、
`.venv/`、`toolchain/validator/bin|obj`——分别是编译缓存、虚拟环境、.NET 构建产物，见 `build.ps1`
打包这三个包时的排除规则）。

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
  requirements.txt / requirements-optional.txt
  install_hooks.ps1
  _console.py
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

## 依赖安装

`Tools~/requirements.txt`（必需）、`Tools~/requirements-optional.txt`（占位资产生成器等可选功能）；
`Tools~/validator` 首次使用需要 `dotnet build Tools~/validator/Validator.csproj -c Release`
一次（不随包分发预编译产物，见上方排除规则），后续 `validate_data.py` 会以子进程方式调用编译好的
`Validator.dll`。

```powershell
python -m venv <你的虚拟环境目录>
<你的虚拟环境目录>\Scripts\pip install -r <resolved>\requirements.txt
dotnet build <resolved>\validator\Validator.csproj -c Release
```

其余用法（控制台编码约定、`validate_data.py` 两道校验的分工等）与框架仓库根 `toolchain/README.md`
完全一致，本包只是同一份内容的按版本号发布形态。
