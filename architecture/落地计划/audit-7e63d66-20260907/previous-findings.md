# 68c9bed 历史发现去重

旧审计基线为 68c9bed，不能替代本轮固定 HEAD 7e63d6695644644e20f30e006b9efa7e531afa34 的证据。以下 19 行只说明旧例在当前源码中的状态，以及与本轮 C/P 编号的关系；“已修”是静态核验，不等同 Runtime 或 Unity 验收。

| 旧编号 | 当前状态与边界 |
|---|---|
| N01 | 货币读档覆盖而非累加已修；本轮不重报。 |
| N02 | Quest 简单 Grant=false 与直接 Reject 原例已修；Encounter/Achievement 发奖失败见 C04，实际量回滚见 C05，任务部分扣除见 C06。 |
| N03 | 原同图存活实体映射问题已修；Shell 同图重载清除保存 timer 见 C11。 |
| N04 | Dispel 触发深度传播已修；吸收耗尽入口仍重置深度，见 C03。 |
| N05 | 来源等级缺失的 mitigation fallback 已修；真实 Despawn 后 stats/powers 来源假设仍见 C02。 |
| N06 | Unity 测试存档根已改为 persistentDataPath 下专属子目录，静态已修，本轮未运行 Unity。 |
| N07 | 临时授予来源（如装备）不再写入永久技能快照；同 host 永久技能 Load 只增不减见 C09。 |
| N08 | LevelUp 已先写新等级再发布事件；本轮不重报旧顺序问题。 |
| N09 | 独立 aura handle 计数已修；Replace 换句柄后的来源撤销边界见 C08。 |
| N10 | 显式目标的 tag/expr 过滤已修；本轮不重报旧目标例。 |
| N11 | 第二任务在第一任务耗尽库存后的简单交付例已修；部分不足和重复模板目标见 C06。 |
| N12 | 单任务跨堆叠消费例已修；多个消费者竞争导致的部分扣除边界见 C06。 |
| N13 | Level 重开会 Abort 旧 Encounter；Abort 不销毁实体是范围边界，本轮不新增问题。 |
| N14 | 正常 gossip 同图/跨图路由与落点已修；失败导航边界未验收，不计本轮新增编号。 |
| N15 | 空 sections 与非整数顶层版本候选校验已修；meta 语义缺失仍见 C10。 |
| N16 | 旧布局按请求槽读取已有；合法槽名跨槽 legacy 命中见 C01。 |
| N17 | 资源加载成功/失败回调正常路径已修；timeout 驱动完成链仍见 C07。 |
| N18 | 播放中冷 clip 按 ID 重查已修，本轮未运行 Unity。 |
| N19 | 瞬发 Attack 不再因同批 Success 立即 Idle，OnComplete 接线已修，本轮未运行 Unity。 |

历史 followup 当前副本见 [followup-2026-09-07c.md](../audit-68c9bed-20260907/followup-2026-09-07c.md)。其中全量 Unity 门禁只指向旧会话正文，本轮不将其计为重跑；旧 b3 复现日志属于历史基线。当前运行命令与证据见 [validation.md](validation.md)。
