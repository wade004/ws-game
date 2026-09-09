# 1.11.0 审计证据归档

本目录对应冻结工作树 `D:\workespace\ws-game-review-6739f50` 的 HEAD
`6739f50e44ba39a023c6209af2673aaf6a1c1fdc`，版本为 `1.11.0`。原仓
`D:\workespace\ws-game` 仅用于只读对照；本审计未向原仓写入文件。

将本目录连同其父级路径放回冻结仓的
`architecture/落地计划/audit-6739f50-20260909/` 后，报告中的相对链接可按
项目根解析到 `core/`、`presentation/`、`adapters/` 等源码和证据。归档只包含
本审计目录内的报告、源码、日志、XML、文本及项目文件；`bin*`、`obj*`、`out*`
目录和 DLL、PDB、可执行文件及 .NET runtime 生成物由打包脚本排除。

独立 Unity 副本的位置、复制来源、Unity 版本和运行命令以
`presentation/presentation-findings.md` 为准。探针进程 `exit 0` 只表示运行器
正常结束，正确性须以对应 XML 的断言计数和日志判断。

稍后在本目录执行 `pack-evidence.ps1`，脚本会在本目录生成
`evidence-manifest.txt`，并在其同级生成
`audit-6739f50-20260909-evidence.zip`。ZIP 内保留完整的
`audit-6739f50-20260909/<子目录>/<文件>` 路径；manifest 不计算自身 SHA-256，
但会随证据一起归档。
