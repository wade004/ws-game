# Unity copy and evidence layout

冻结源：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`，HEAD `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`，VERSION `1.14.0`。

独立 Unity 工程：`D:\workespace\ws-game-unity-audit-76d16a5\copyRoot\adapters\unity`，Unity `6000.3.23f1`。`copyRoot` 保留真实工程布局，并同步了 `adapters/unity`、`adapters/conformance`、`games/_template`、`data`、`assets`；`adapters/unity/Assets/StreamingAssets/GameFoundation` 使用冻结仓已生成的完整内容，包含 `data`、`scene`、`nav_mesh`、字体与其他资源，344 个文件逐项一致，清单见 [streaming-assets-copy.txt](../hashes/streaming-assets-copy.txt)。六个 Core/Presentation DLL 只取自当轮 `build/check-artifacts/bin/<assembly>/release`，逐项来源与目标哈希见 [six-dll-copy.txt](../hashes/six-dll-copy.txt)。

可复现筛选命令在 [run-1.14-presentation.ps1](run-1.14-presentation.ps1)，默认只运行 Deleted overlay EditMode 与 View/VFX PlayMode；`-Mode full` 运行完整 Edit/Play。原始 XML/log 在本目录，探针源码副本在 [repro](repro)。本轮未改冻结仓、原仓、产品源码或既有测试，未启动独立版/IL2CPP、registry 或 consumer smoke。
