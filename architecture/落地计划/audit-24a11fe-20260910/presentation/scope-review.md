# Presentation scope and evidence boundary

本轮仅审查冻结 `24a11fe` 的 Unity presentation adapter 与 template/conformance 运行接线。独立副本复制自冻结树，当前 Core/Presentation DLL 由本轮 check artifacts 提供；所有复制内容与来源哈希见 `hashes/`。

证据分层如下：完整 EditMode 70/70 与 PlayMode 272/272 是当前 Unity Test Framework XML 运行结果；定向 1/1 与 3/3 是同一副本的补充过滤运行。日志中的 Unity licensing/d3d12 环境记录和测试框架捕获栈不改变 XML 的 passed/failed 计数，语义结论以 XML 结果、源码和哈希联合判断。未把 exit 0 单独当成功能证明。

复核命令和副本路径记录在 `presentation-findings.md`；runner 保留 raw，重复运行需换 EvidenceRoot 或先使用新的独立归档目录。冻结仓、产品仓、旧审计目录均为只读输入。
