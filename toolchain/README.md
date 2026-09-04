# toolchain

跨游戏的工具链目录：数据校验、构建辅助、资产导入等脚本，供本框架及各游戏仓库共用。

数据校验分两道（T2-12 落地）：`validate_data.py` 自己只做第一道骨架级通用检查（信封/表名/id
格式）；第二道真实校验（字段级必填/类型/枚举、引用完整性、文本键存在、Expr 可解析，以及
skill/combat/target/ai 等各模块的专属校验规则）复用 `core/foundation/data_registry.DataRegistry`
与 `core/rules/assembly.RulesSchemaCatalog`，由 `validate_data.py` 以子进程调用
`toolchain/validator`（一个 .NET 控制台工具）完成——两套判断逻辑不重复实现，`core` 内的校验
规则是唯一权威来源。

## 虚拟环境

```
python -m venv toolchain/.venv
```

`toolchain/.venv/` 已在仓库根 `.gitignore` 中忽略，不会被提交。当前阶段无第三方依赖（见 `requirements.txt`），创建虚拟环境后无需安装任何包即可运行脚本；后续如需第三方依赖，激活虚拟环境后执行：

```
pip install -r toolchain/requirements.txt
```

## 运行校验器

从仓库根目录运行（脚本内部按自身文件路径推导仓库根，从其他目录运行结果相同；第二道校验会用
`subprocess` 以仓库根为工作目录调用 `dotnet run --project toolchain/validator`，需要能在
`PATH` 里找到 `dotnet`）：

```
python toolchain/validate_data.py
```

首次运行第二道校验时，`dotnet run` 会自动编译 `toolchain/validator`（打印 MSBuild 输出），
之后的运行会复用已编译的产物，速度明显加快。

常用参数：

- `--data-root <path>`：默认 `data`（相对仓库根），指定要校验的数据根目录。
- `--dataset <name>`：只校验 `data/<name>/` 下的表，省略则校验 `--data-root` 下全部表（这个
  子目录同时作为两道校验共同的输入范围）。
- `--verbose`：输出更详细的检查过程信息（第一道骨架检查）。
- `--strict`：第二道校验里 Warning 也阻断（透传为 `toolchain/validator --strict`，等价于
  `DataRegistryStrictness.WarningsBlock`）。
- `--skip-dotnet`：只跑第一道骨架检查，跳过第二道（没有安装 .NET SDK 的环境可用）。

## 返回码约定

- `0`：两道检查全部通过，无错误。
- `1`：至少一道检查报出错误（第一道逐行打印到标准输出，汇总
  `[第一道·骨架检查] checked N files, M errors`；第二道的每条问题打印为
  `[severity] table/key/field: check: message` 格式，汇总
  `tables N, records M, errors E, warnings W`，格式定义见 `toolchain/validator/Program.cs`）。
- `2`：命令行参数错误（如指定了不存在的 `--data-root`），或找不到 `dotnet` 可执行文件且未传
  `--skip-dotnet`。

第一道脚本只做骨架级通用检查（信封三键、表名一致、id/key 格式）；04 第 5 节校验器检查项清单
中的引用完整性、枚举合法、表达式可解析、模块专属规则（效果数上限、叠加类别冲突、命中表概率
区间、抗性曲线单调性、目标链来源已注册与无环、AI 优先级唯一性等）均由第二道
`toolchain/validator` 覆盖，见该项目下方说明；两道检查项不重叠，也不重复实现同一条判断逻辑。

## `toolchain/validator`（.NET 真实校验工具）

`toolchain/validator/Validator.csproj`（`net8.0` 控制台项目，已加入 `Core.sln`）：复用
`core/foundation/data_registry.DataRegistry`（加载 + 字段级校验）与
`core/rules/assembly.RulesSchemaCatalog.RegisterAll`（一次性注册 L0～L2 全部 `TableSchema`/
`IValidationRule`/已知外键），对磁盘上的真实数据目录跑一遍完整校验。自带
`DiskFileSystem`（`toolchain/validator/DiskFileSystem.cs`）——`adapters/stub` 的
`StubFileSystem` 只有内存实现，本工具需要读真实文件，因此在工具自己的目录下补一个只读磁盘
实现（不放进 `core/`，也不修改 `adapters/stub` 任何一行）。

直接运行（不经 `validate_data.py`）：

```
dotnet run --project toolchain/validator -- --data-root <dir> [--strict] [--json] [--list-tables]
```

参数：

- `--data-root <dir>`：必填，数据根目录；相对路径按当前工作目录解析（从仓库根运行时等价于
  "相对仓库根"），绝对路径原样使用。
- `--strict`：Warning 也阻断（`DataRegistryStrictness.WarningsBlock`）。
- `--json`：输出机器可读的单行 JSON（`{tables, records, errors, warnings, blocking, issues[]}`），
  不输出人类可读文本行。
- `--list-tables`：额外列出本次加载到的全部表名与记录数（文本模式下逐行打印，JSON 模式下并入
  `tables_list` 字段）。

问题逐条打印为 `[severity] table/key/field: check: message`（`key`/`field` 缺失时对应段落省略），
`severity` 取 `error`/`warning`，`check` 是 04 第 5 节固定的检查项名或各模块 `IValidationRule`
自行命名的检查项名；末尾汇总一行 `tables N, records M, errors E, warnings W`。返回码：`0`
未阻断；`1` 阻断（存在 Error，或 `--strict` 下存在 Warning）；`2` 命令行参数错误（缺
`--data-root`、目录不存在）。

## 生成事件常量（gen_event_constants.py）

读取事件词汇登记表 `data/_sample/found/found.event_catalog.json`，为每一行生成一个
`Core.Foundation.Common.Id` 强类型常量，写入
`core/foundation/event_bus/generated/EventKeys.g.cs`（生成物，不可手改；修改登记表后
重新运行本脚本，见该文件头部注释与 `core/foundation/event_bus/README.md`）。

```
python toolchain/gen_event_constants.py
```

常用参数：

- `--catalog <path>`：事件词汇登记表 JSON 路径，相对仓库根（默认
  `data/_sample/found/found.event_catalog.json`）。
- `--output <path>`：生成文件输出路径，相对仓库根（默认
  `core/foundation/event_bus/generated/EventKeys.g.cs`）。
- `--namespace <ns>`：生成类型所在命名空间（默认 `Core.Foundation.EventBus`）。
- `--check`：不写文件，只比较生成内容与 `--output` 现有文件是否一致；不一致返回码 1。
  **提交门槛**：改过 `found.event_catalog.json` 后应先跑
  `python toolchain/gen_event_constants.py`（覆盖生成文件）再提交；CI/预提交可用
  `python toolchain/gen_event_constants.py --check` 校验生成文件与登记表是否同步，
  不同步则失败。

返回码约定：`0` 成功；`1` 数据错误（信封非法、key 格式非法、重复 key、常量名冲突）或
`--check` 模式下内容不一致；`2` 命令行参数错误（如 `--catalog` 指向不存在的文件）。
