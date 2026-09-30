# 排查复盘：PlayMode 全量门禁 PRES180 稳定失败（2026-09-15）

> 本文档允许出现具体技术名（引擎/语言/框架/工具名），不受 `architecture/00～14` 与
> `architecture/adr/` 正文的禁用词约束（见仓库根 `CLAUDE.md`"硬性规则"）。代码围栏内不放相对
> 链接。

**结论先行**：一次 Unity PlayMode 全量门禁稳定失败（`VerticalSliceTests.PRES180_...`，
283/284）的排查耗时约 3.5 小时、约 156 万 token，跨 4 轮 agent、跑了十几次全量 PlayMode（每次
约 100 秒）。真因很小——"新游戏"入口不回满玩家资源池——但表面症状是一句无关的字体警告，
排查者被表面消息带偏了很长时间。本次复盘定位到 5 个具体的耗时环节，逐条给出改进措施并已落地
两件工具：`toolchain/unity_test_triage.py`（测试结果分诊脚本）与 Unity 测试程序集内的
`[TestFirstChance]` 首个异常记录回调，二者配套使用，直接堵上本次排查里"没有工具、只能整段读日志"
与"日志里没有第一现场"两个最大的漏洞。

---

## 问题

Unity PlayMode 用例 `VerticalSliceTests.PRES180_SameMapLoad_DroppedLootView_
ReconciledAfterSuppressedEntityCreated` 在全量门禁稳定失败（283/284），NUnit 结果 XML 里的
失败 message 是：

```
Expected log did not appear: [Warning] Regex: was not found in the [LiberationSans SDF] font asset
```

字面意思是"预期出现的一条字体缺字警告没有出现"，看起来与 TextMeshPro 字体资产缓存/预热时机
有关。

## 表面症状与真实根因

**表面症状**：`LogAssert.Expect` 落空——用例设置了"预期会看到某条 `[Warning]` 日志"，实际没
看到，NUnit 判定失败，报错文本指向字体资产。

**真实根因**（最终由第三轮 agent 查明，提交 `6de3970`，已合入 main）：

- `FrameworkResidentHost` 的玩家对象跨整个 `-runTests` 进程复用（同一批 PlayMode 用例共享同一个
  玩家实例），"新游戏"入口不会把资源池（法力等）回满。
- 前序用例把法力打到接近 0；`PRES180` 用例的攻击循环在 400 次预算内打不死示例生物，
  `Assert.IsTrue(died)` **真的失败**（被测行为本身没有发生）。
- Unity Test Framework 的 `UnityLogCheckDelegatingCommand` 在断言异常抛出之后**仍然继续执行**
  `CheckLogs → EvaluateLogScope`：两条 `LogAssert.Expect` 因为用例提前因断言失败而没走到预期
  日志出现的代码路径，也跟着落空，再抛一次 `UnexpectedLogMessageException`。
- NUnit `TestResult` 只记录**最后一次**抛出的异常——`died` 断言失败被覆盖，表面症状变成一句
  跟字体资产有关、实际上毫不相干的警告消息。

一句话：**"死亡没发生"这个真正的第一现场，被 Unity Test Framework 自己的日志检查机制吞掉了**，
NUnit 结果 XML 里留下的只是一个二次效应。

## 时间线与成本表

| 轮次 | 角色 | 结果 | 耗时 | token | 工具调用 |
|---|---|---|---|---|---|
| 1 | 阶段复核 agent（顺带定性） | 判定为"既有时序竞态，与本次改动无关"（在 1.30.0 基线复现），未深究 | 约 28 分钟归属本问题 | 约 25 万 | — |
| 2 | 专项排查 agent A | 锚定表面消息，深挖 TextMeshPro 字体资产缓存机制（方向正确但只是表象），尝试 9 版"按实际次数登记 Expect"的修改，每版都引出"攻击循环 0 命中"新症状，未能解释，放弃 | 104 分钟 | 62.7 万 | 284 |
| 3 | 发布 agent | 在主树重跑全量门禁再次撞上，停下 | 14 分钟 | 13.8 万 | 44 |
| 4 | 专项排查 agent B | 带着轮次 2 的线索，先加诊断标记跑死亡循环，发现 `died=False`，追到资源池未回满与 NUnit 只记录最后异常 | 67 分钟 | 54.5 万 | 258 |
| **合计** | | | **约 3.5 小时** | **约 156 万** | |

每跑一次全量 PlayMode 约 100 秒，轮次 2 跑了十几次。主会话（设计层）的协调开销未计入上表。

## 耗时都花在哪个环节

### ① 锚定表面错误消息，没有先核对"被测行为本身是否发生"

轮次 2 把全部精力投在"字体警告为什么没出现"这个问题本身——查 TextMeshPro 字体资产的加载/
缓存/预热时机，反复调整 `LogAssert.Expect` 的注册次数与时机。这个方向不能说错（表面消息确实
是字体警告），但**从未停下来问一句"PRES180 这个用例真正要验证的行为（生物死亡→掉落物→
suppressed entity 协调）到底有没有发生"**。轮次 4 一开始就先加了一个诊断标记直接打印
`died` 的值，8 分钟内就拿到了 `died=False` 这个决定性证据，直接把排查方向从"字体资产"切换到
"死亡为什么没发生"。

**改进措施**：本次已落地的两个工具都服务于这一条——`toolchain/unity_test_triage.py` 的窗口内
异常扫描把断言失败行（`Assert.IsTrue`/`AssertionException` 等）与 NUnit 记录的表面 message
并列展示，报告里固定打印提醒"更早出现的一条才是第一现场"；标准流程清单（见下）把"核对被测
行为是否发生"列为进入具体代码排查之前的必经步骤，不能跳过直接扎进表面消息指向的子系统。

### ② Unity Test Framework 的"最后异常覆盖"机制无人知道，日志里没有第一现场

`UnityLogCheckDelegatingCommand` 在断言异常之后仍执行 `CheckLogs`、NUnit `TestResult` 只保留
最后一次异常——这是 Unity Test Framework 的既有行为，不是本仓库代码的 bug，但排查过程中没有
人事先知道这条机制，只能在轮次 4 里现场摸索出来。即便知道这条机制，Unity 批处理日志本身也不会
打印"用例 X 在时刻 T 抛出了异常 Y"这类信息（详见下方 unity_test_triage.py 判断记录：日志里连
用例名都不会出现），排查者没有任何办法从日志倒推出"死亡断言先失败、字体警告异常后覆盖"这条
时序。

**改进措施**：在 Unity 测试程序集内新增 `[TestFirstChance]` 回调（本次已落地，见下方"工具化
成果"），订阅 `AppDomain.CurrentDomain.FirstChanceException`，把每一次 `AssertionException`/
`UnexpectedLogMessageException` 类异常连同触发它的用例全名、发生顺序，实时打进 Unity 日志——
这样"最后异常覆盖"机制本身即使还在起作用，日志里也留下了完整的第一现场记录，不需要再靠人工
经验去猜。

### ③ 没有工具把失败用例对应的日志片段与首个异常抽出来，只能整段读 playmode.log

本次复盘核对了引发本问题的那次门禁产出的真实 `playmode.log`（约 1.7 万行）：里面**没有任何
逐用例起止标记**，也不会打印用例名/类名（`grep -c VerticalSliceTests playmode.log` 为 0）。
轮次 2、4 的 agent 只能整段翻这份日志、或者凭经验猜测大致的行号区间，效率很低，也容易读串到
别的用例的输出。

**改进措施**：本次落地的 `toolchain/unity_test_triage.py` 直接从 NUnit 结果 XML 拿到失败用例
清单与 message/stack-trace，尝试按 `[TestFirstChance]` 标记（最精确）→ 用例名首次出现位置
（近似）→ message 原文匹配（不保证归属）→ 整份日志全局摘要（兜底）四级回退切出相关片段，
一条命令拿到"这个用例大概发生了什么"，不用再整段通读。

### ④ 机制未查清时反复试改，每版都要跑一次约 100 秒的全量 PlayMode

轮次 2 在没有查清真因（"死亡没发生"）的前提下，尝试了 9 版"按实际次数登记 `LogAssert.Expect`"
的修改，每一版都要重新跑一次全量 PlayMode 验证效果（约 100 秒/次），累计跑了十几次，但因为
方向本身就没有切中根因，每一版都只是引出新的表面症状（"攻击循环 0 命中"），没有推进排查。

**改进措施**：标准流程清单（见下）把"只在机制查清后改代码"列为硬性顺序——机制不明时，宁可
多花时间加诊断标记单独定位（轮次 4 证明这条路径只需要 8 分钟），也不要在没有因果链条的情况下
批量试错性地改代码再跑全量验证；每次改动前后必须用同一条命令对照，避免"改了但看不出差异"或
"引入了新变量却当成同一次实验"。

### ⑤ 跨用例共享状态（玩家对象进程级复用）没有隔离基线可比对

真因是"新游戏"入口不回满资源池——这是一种跨用例共享状态导致的顺序依赖问题，本身就难排查：
单独跑 `PRES180` 一个用例永远不会复现（法力池是满的），只有在它前面跑过消耗法力的用例、且
是同一个 `-runTests` 进程时才会复现。轮次 1 的阶段复核 agent 甚至因为"在 1.30.0 基线上也能
复现"就判定为"既有时序竞态，与本次改动无关"，实质上是把"顺序依赖问题在旧版本同样存在"误读成
"这不是需要深究的问题"，反而拖慢了后续轮次定位到真因的进度（虽然定性本身没有直接反驳，但没有
提示"单独跑该用例 vs. 全量跑"这一关键差异，是本可以更早排除掉"表面消息本身有问题"这个假设的
机会）。

**改进措施**：标准流程清单把"单独跑 vs. 全量跑对照"列为核对被测行为是否发生之后的下一步——
一旦"单独跑通过、全量跑失败"，就应该直接把排查方向锁定在"跨用例共享状态"这一类问题上，不必
再深挖失败用例本身的业务逻辑（本次的字体资产/TextMeshPro 方向正是在这类问题上空耗了 104
分钟）。

## 工具化成果

### `toolchain/unity_test_triage.py`

Unity 测试结果分诊脚本，输入 NUnit3 结果 XML 与对应 Unity 日志，输出失败用例的窗口片段、窗口内
首个异常/断言（并提醒 NUnit message 只是最后一次异常）、Warning/Error 摘要；已接入
`check.ps1`——EditMode/PlayMode 步骤判定失败时自动调用，报告打进门禁日志，成功分支不调用。
详细用法、四级窗口定位回退逻辑与已知限制见 `toolchain/README.md`"Unity 测试结果分诊
（unity_test_triage.py）"一节；回归测试 `toolchain/tests/test_unity_test_triage.py` 用内嵌
最小 XML+log 夹具还原"最后异常覆盖首个断言"场景。

用真实历史门禁产物（本次问题的 `playmode.xml`/`playmode.log`）验证：工具能正确识别出
`PRES180` 失败用例、还原 NUnit message，但因为当时测试程序集还没有接入 `[TestFirstChance]`
回调、日志里也没有任何用例边界标记，窗口定位一路退化到 `not_found`，只能给出整份日志范围的
Warning/Error 摘要——这个结果本身就是"②③"两条耗时环节的直接证据，也是下面这件工具必须存在
的理由。

### `[TestFirstChance]` 首个异常记录回调

Unity 测试程序集内新增的测试运行期回调，`RunStarted` 时订阅
`AppDomain.CurrentDomain.FirstChanceException`，遇到 `AssertionException`/
`UnexpectedLogMessageException` 类异常立即打印
`[TestFirstChance] <当前用例全名>: <异常类型>: <消息首行>`，`RunFinished` 时退订。与
`unity_test_triage.py` 的窗口定位第一级配套：只要今后的 PlayMode/EditMode 全量门禁跑过一次
（哪怕最终仍然失败），日志里就会留下精确到行的第一现场，`unity_test_triage.py` 直接命中
`test_first_chance` 这一级，不再需要靠人工经验或者反复试跑去猜。

## 结论与后续规则：排查 Unity 测试失败的标准流程

1. **先跑分诊工具看首个异常与用例日志片段**——`python toolchain/unity_test_triage.py --xml
   <path>.xml --log <path>.log`，看窗口定位方式（`test_first_chance` 最可靠）、窗口内异常
   列表（第一条才是第一现场，NUnit message 可能只是最后一次覆盖的表象）、Warning/Error 摘要。
2. **核对被测行为是否发生**——不要一上来就顺着表面错误消息去查相关子系统；先确认用例真正要
   验证的行为（本例是"生物是否死亡"）本身有没有发生，能加诊断标记就直接加，这一步往往比深挖
   表面消息快得多（本次轮次 4 只用 8 分钟）。
3. **单独跑 vs. 全量跑对照**——一旦发现"单独跑通过、全量跑失败"，直接把排查方向锁定为跨用例
   共享状态/顺序依赖类问题，不要继续深挖失败用例自身的业务逻辑。
4. **只在机制查清后改代码**——没有找到因果链条之前，不要批量试错性地改代码再跑全量验证；
   机制不明时优先加诊断标记单独定位。
5. **每次改动前后用同一条命令对照**——避免"改了但看不出差异"或者把引入新变量的实验误当成
   同一次实验的重复验证。

---

## 附：本次排查涉及的提交

- `6de3970`：根治 PlayMode 全量门禁 VerticalSliceTests 283/284 稳定失败（真因提交）。
- `fb284ab`：合并 `fix/pres180`，根治 PlayMode PRES180 稳定失败。
