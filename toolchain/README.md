# toolchain

跨游戏的工具链目录：数据校验、构建辅助、资产导入等脚本，供本框架及各游戏仓库共用。当前阶段（T0-7）只有数据校验器骨架。

## 虚拟环境

```
python -m venv toolchain/.venv
```

`toolchain/.venv/` 已在仓库根 `.gitignore` 中忽略，不会被提交。当前阶段无第三方依赖（见 `requirements.txt`），创建虚拟环境后无需安装任何包即可运行脚本；后续如需第三方依赖，激活虚拟环境后执行：

```
pip install -r toolchain/requirements.txt
```

## 运行校验器

从仓库根目录运行（脚本内部按自身文件路径推导仓库根，从其他目录运行结果相同）：

```
python toolchain/validate_data.py
```

常用参数：

- `--data-root <path>`：默认 `data`（相对仓库根），指定要校验的数据根目录。
- `--dataset <name>`：只校验 `data/<name>/` 下的表，省略则校验 `--data-root` 下全部表。
- `--verbose`：输出更详细的检查过程信息。

## 返回码约定

- `0`：全部文件通过检查，无错误。
- `1`：至少一项检查失败（错误逐行打印到标准输出，末尾汇总 `checked N files, M errors`）。
- `2`：命令行参数错误（如指定了不存在的 `--data-root`）。

当前脚本只做骨架级通用检查，04 第 5 节校验器检查项清单中的引用完整性、枚举合法、表达式可解析等领域规则在后续阶段逐项加入，详见 `validate_data.py` 文件头注释。

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
