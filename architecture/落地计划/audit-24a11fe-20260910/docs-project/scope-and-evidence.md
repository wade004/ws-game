# 审计范围、责任与证据边界

## 输入与只读边界

- frozen source：`D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD `24a11fe28f9647cd532c41f56f7ab18c00fb8516`。
- source repo `D:\workespace\ws-game` 仅读；正式包来自其 `dist`，不重建、不解压覆盖正式包。
- audit output：`D:\workespace\ws-game-artifacts\audit-24a11fe-20260910`。
- 唯一一次 check 命令及原始 transcript：`logs/check-skipunity-transcript.txt`。未启动、停止或改写真实用户 registry；没有占用真实 4873 的测试。
- check 复用 DLL：`check/artifacts/bin/<Project>/release/<Assembly>.dll`。ABI 与文档 probe 的 intermediate files 均在 audit output。

## 证据层级

1. 合同/ADR/架构正文：定义框架责任和明确非目标。
2. 当前代码/默认装配：证明实现和 wiring；不能替代 Unity 或真实消费方运行。
3. `check.ps1 -SkipUnity`：24 steps 中 17 PASS、7 SKIP；ABI、Unity、standalone smoke、consumer 均为 SKIP。ABI 因默认 baseline 缺失为 SKIP。
4. 独立严格 ABI：显式旧正式包 1.12.0，旧 consumer 不重编译换当前六 DLL，运行成功；结果只适用于这组基线和调用路径。
5. schema/Unity 代理：由 schema agent 提供，主报告引用其路径，不在此重跑。

## 分类词典

- **需更新文档**：代码/合同已确定，文档陈述落后或把两条命令混为一谈。
- **已对齐**：当前代码、默认装配和可取得的静态门禁证据一致。
- **游戏责任**：框架提供接口/机制，游戏或宿主决定起始状态、内容策略、路由与玩法。
- **明确边界/预留延期**：ADR/架构已决定本版本不执行，例如 ATB；导航跨帧预算、完整共享空间索引化仍按既有性能边界解释。
- **未提供证据**：本轮没有该运行层证据；不自动等于未实现。

## 总报告整合接口

总报告应链接本目录四份文档、`check-skipunity-transcript.txt`、`formal-release-stream-check.txt`、`abi-strict-1.12/summary.txt`、`abi-strict-1.12/formal-zip-abi-summary.txt` 与 `abi-gate-negative-oracle/summary.txt`。数字必须取这些当前文件，不复用旧 audit 报告数字。
