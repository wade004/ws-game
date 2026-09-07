# 7e63d66 审计交付

基线是 7e63d6695644644e20f30e006b9efa7e531afa34，工作树为 D:/workespace/ws-game-review-7e63d66。本轮只写本审计目录，不改生产代码、不改现有测试、不启动 Unity、不发布、不联网下载，也未触碰 D:/workespace/ws-game 的用户存档。

结论是：框架有可复用基础，但本轮不能批准生产就绪。本轮共 19 项：10 项 P1、9 项 P2。C01/C05 与 P01/P02/P03 已由隔离 console、发行包或 Unity fixture 实际标记 REPRODUCED；其余 10 项代码问题及 P04–P07 是静态证据并明确标记 NOT_EXECUTED。测试通过不等于真实 Unity、独立包消费、私服权限或完整游戏纵切已验收。

## 交付物

- [code-review.md](code-review.md)：C01–C12 独立审查段；C01/C05 为真实 Core console 复现，其余为静态。
- [project-review.md](project-review.md)：P01–P07 项目、发行和消费者边界。
- [doc-code-matrix.md](doc-code-matrix.md)：architecture 00–14、README 漂移和未默认接入能力索引。
- [previous-findings.md](previous-findings.md)：68c9bed 的 N01–N19 逐条去重，避免把已修案例重报。
- [validation.md](validation.md)：非 Unity 门禁、复现命令、发行材料和历史 Unity 记录边界。
- [repro/](repro/)：保留 CORE-A/CORE-B 的真实类 console 复现工程。
- [evidence/](evidence/)：复制后的原始命令日志、只读发行记录、GitHub JSON 输出和历史 Unity XML。

## 验证基线

全量非 Unity 门禁为 14 PASS、6 Unity SKIP；六个 .NET 测试程序集合计 2,214 passed、0 failed、0 skipped，门禁内 pytest 为 46 passed。PJT-A 的 Quick 只是外部 artifacts 构建检查，不是全量门禁；其 SyncOnly 在干净 checkout 缺默认 Release DLL 时失败。PJT-C 的 ZIP/UPM validator 消费均失败，PJT-B 的同步会删除消费者 TMP sentinel。

发行材料只读核验：主目录 dist/1.0.0 的 MANIFEST、lock、ZIP 和三个 TGZ 都是 1.0.0，发布身份为 git commit ab52097；ZIP SHA256 为 e5921c429f9a70503a85f9f1f525fe876fd5544535f1921362ef3c0e56454d33，六个 DLL 与 lock 匹配。审计源码基线仍是 7e63d66，这是发行快照的时间边界，不是材料漂移。GitHub CI 和 Release workflow 均 success，但最新 Release 的打包与上传步骤 skipped，因为附件已存在；不能把绿灯写成无附件 fallback 已验收。

历史 editmode 54/54 与 playmode 170/170 XML 是主目录已有的 2026-09-07 记录，本轮只复制读取，未重跑，也未绑定最终 ZIP hash。当前报告不以它们证明最终包的 Unity 运行。

## 后续顺序

先修 C/P1 的存档、奖励/任务、效果生命周期和完整包消费，再保护消费者资源；随后以最终包 hash 绑定真实游戏纵切（新局、移动攻击、装备/任务、死亡、存读档、退出重入），最后更新章节矩阵与升级文档。当前文档和测试结果支持继续工程化，不支持宣称任意新游戏或完整生产流程已经就绪。
