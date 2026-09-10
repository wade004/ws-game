# Presentation Unity 有界复核

本轮基线为冻结仓 `D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD `24a11fe28f9647cd532c41f56f7ab18c00fb8516`，VERSION `1.16.1`。Unity 使用 `6000.3.23f1`，工程位于独立临时副本 `D:\workespace\ws-game-unity-audit-24a11fe\copyRoot\adapters\unity`。冻结仓、产品仓和旧审计证据未被修改。

复制准备沿用真实工程布局，同步 `adapters/unity`、`adapters/conformance`、`games/_template`、`data`、`assets`。除当前六个 DLL 外，1,081 个源/资源文件与冻结源逐项 SHA-256 一致；StreamingAssets 实际内容 320 个非 `.meta` 文件逐项一致，包含 `data`、`scene`、`nav_mesh`、`audio`、`sprites`、`vfx` 和资源字体。Unity 导入后生成的 `.meta` 文件属于临时副本导入产物。清单见 [tracked-copy-summary.txt](hashes/tracked-copy-summary.txt) 和 [streaming-assets-copy.txt](hashes/streaming-assets-copy.txt)。

六个 Core/Presentation DLL 来自本轮 `check/artifacts/bin/<assembly>/release`，复制到 Unity 包的 `Runtime/Plugins/Core`；来源与目标哈希全部一致，见 [six-dll-copy.txt](hashes/six-dll-copy.txt)。这些 DLL 有意替换冻结包内同名插件，以确保本轮 Runtime 消费当前检查产物。

针对性 raw 先行保留：EditMode `DataHotReloadDeletedOverlayTests` 为 1/1，PlayMode `EquipmentVisualSaveLoadResetTests|VfxAnchorFollowTests` 为 3/3。随后用相同临时副本执行完整常规套件：EditMode 70/70，PlayMode 272/272；两次 Unity 进程 exit 0。完整 XML、定向 XML 和日志均为不可覆盖 raw：

- [full-edit.xml](full-edit.xml)；[full-edit.log](logs/full-edit.log)；[full-edit.exit.txt](full-edit.exit.txt)
- [full-play.xml](full-play.xml)；[full-play.log](logs/full-play.log)；[full-play.exit.txt](full-play.exit.txt)
- [editmode-current.xml](editmode-current.xml)；[editmode-current.log](logs/editmode-current.log)；[editmode-current.exit.txt](editmode-current.exit.txt)
- [playmode-current.xml](playmode-current.xml)；[playmode-current.log](logs/playmode-current.log)；[playmode-current.exit.txt](playmode-current.exit.txt)

可复用副本准备脚本为 [prepare-unity-copy.ps1](repro/prepare-unity-copy.ps1)，它从冻结树重建一个全新 `CopyRoot`，再注入本轮六个 DLL；已有副本即拒绝覆盖，不递归删除。准备后由 [run-unity-current.ps1](repro/run-unity-current.ps1) 只调用传入的 `ProjectRoot`，默认先跑定向过滤；传入 `-Mode full` 使用独立文件名 `full-edit`/`full-play`，遇到已有 XML、日志或 exit 文件即拒绝覆盖。归档解压后可用新临时路径复现：

```powershell
# 先从冻结 24a11fe 构建本轮 DLL；ArtifactsPath 的 bin 作为 DllRoot
New-Item -ItemType Directory -Force -Path 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910\check-artifacts' | Out-Null
& 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen\check.ps1' `
  -SkipUnity `
  -ArtifactsPath 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910\check-artifacts'
& .\presentation\repro\prepare-unity-copy.ps1 `
  -FrozenRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen' `
  -DllRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910\check-artifacts\bin' `
  -CopyRoot 'D:\workespace\ws-game-unity-audit-24a11fe-rebuild\copyRoot'
New-Item -ItemType Directory -Force -Path 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910\presentation' | Out-Null
& .\presentation\repro\run-unity-current.ps1 `
  -Mode full `
  -ProjectRoot 'D:\workespace\ws-game-unity-audit-24a11fe-rebuild\copyRoot\adapters\unity' `
  -EvidenceRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-rebuild-20260910\presentation'
```

这里的 `check.ps1 -SkipUnity` 只负责从冻结源码生成可注入的 Core/Presentation DLL；它不是 Unity 运行证明。`prepare-unity-copy.ps1` 和 runner 都要求新路径，保留原审计 raw 不变。

当前运行命令为：

```powershell
& .\presentation\repro\run-unity-current.ps1 `
  -Mode full `
  -ProjectRoot 'D:\workespace\ws-game-unity-audit-24a11fe\copyRoot\adapters\unity' `
  -EvidenceRoot 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\presentation'
```

本轮只覆盖 Unity EditMode/PlayMode 常规测试；未运行 standalone、IL2CPP、registry、consumer smoke 或新的 Unity 构建发布链。full 结果是当前运行证据，不借用旧版 70/272 计数。
