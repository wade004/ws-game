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
