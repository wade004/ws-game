# L3/L4 独立审查笔记 — HEAD 5e779c600d7dd3c9ef995d34e844c48641f20a85

2026-09-07。只读审查 D:/workespace/ws-game；审查前后 git status --short 均为空。未在源树运行 build/test，没有修改源文件。主要审查默认装配、库存装备、奖励事务、Quest、Loot、Spawn、Encounter、Achievement、Difficulty、AreaTrigger 的状态和生命周期；05/07/08/10按对应契约核对，不声称所有行均逐行审阅。已读历史 main、b3b91ee 修复跟进与 7e63d66 审查/矩阵，下面不重新报告已经修复的旧问题。

## 高可信问题

### GP26-01 — P1 — 失败发奖只回滚库存，已排队 item.added 仍消耗玩家原有任务物品

证据等级：真实 Core 源码 console 复现成功。

触发：InventoryHost(MaxSlots=1, FullPolicy=Reject)，玩家原有 A5（stack_size=10）；先派发旧入包事件，再接一个 consumeOnProgress=true、目标 A1 的任务。RewardDispatcher.Grant([A1,B1])先加入A1，再因B不能占新槽而失败。

实际：Grant返回false时库存已回滚为A5；随后的正常 EventBus.DispatchPending仍派发本次失败事务产生的ItemAdded(A1)。QuestHost按模板扣物品，扣掉玩家原有A1，最终库存A4，任务进度1且ObjectivesComplete。

精确锚点（均以 D:/workespace/ws-game 为根）：
- core/gameplay/common/core/RewardDispatcher.cs:101–113：先TryAddItem，失败仅按数量RemoveByTemplate补偿；建议评论范围110–113。
- core/carriers/item/core/InventoryHost.cs:163：加入成功立即Enqueue ItemAddedEvent，补偿不会撤销这个事件。
- core/gameplay/quest/core/QuestHost.cs:699–745：收到事件后按evt.Count决定take，通过RemoveCollectedItems扣当前库存；:745是确定的扣旧物品入口。

影响：失败事务离开调用栈后仍可产生持久副作用；不只错误通知，已经复现真实物品净损失与任务推进。C05的actualCount修复只解决回滚数量，未解决事件提交边界。Achievement collect_count等消费者也可能被虚假发奖事件推进，但本次仅运行Quest组合，不把其它消费者标为已复现。

建议：库存事务应在整批提交后发布事件，或提供能同时回滚实例delta与未提交事件的事务接口。不能只忽略Quest这一个消费者，因为item.added契约是所有订阅者的事实来源。

验收：上述Grant=false后派发任意轮事件，库存仍A5、consume任务进度0；成功事务恰好发布真实新增量；再覆盖Quest交付失败、Loot/Economy补偿路径。

### GP26-02 — P2 — 套装门槛共享Aura时，卸去高门槛装备会删除仍满足的低门槛效果

证据等级：真实 CarriersAssembly/EquipmentHost/AuraHost 源码 console 复现成功；测试数据通过CarriersSchemaCatalog校验，blocking=False（仅未装本地化表warning）。

触发：合法item.set有两件装备，1件和2件门槛都引用同一aura_def；AllowMultiSourceTiming=false默认、max_stacks=1、StackOverflowPolicy.Replace。先穿A得到h1，穿B达到2件门槛把h1替换为h2，然后卸B，A仍穿着。

实际：AuraQuery.HasAura=false，stat.power从101回到1；仍满足1件门槛却失去+100效果。

精确锚点：
- core/carriers/item/core/EquipmentHost.cs:167–192：InstanceReplaced只遍历_grantedAuras并迁移普通装备计数，没有处理_appliedSetBonuses。
- 同文件:104：套装另用_appliedSetBonuses持有句柄。
- 同文件:578–590：门槛各自ApplyAura并保存独立句柄，撤销时直接RemoveAura；建议评论范围583–590。

影响：C08修复覆盖普通grants.auras，但套装效果完全绕过该来源计数与替换迁移机制。阈值降落时删除共享光环后，低门槛仍标记applied，后续RecomputeSetBonuses也不会重建。

建议：统一普通装备与套装门槛的Aura授予来源/引用计数，Replace同步所有持有者；不能仅把旧句柄换新，因为两个门槛共享新句柄仍需要正确计数。

验收：穿A、穿B、卸B后保留1件门槛效果；卸A后清空；覆盖两种卸载顺序、两个不同套装共享Aura与默认RefreshOnly共享句柄策略。

### GP26-03 — P2 — teleporter物件跨图目标在默认interact意图链被丢弃

证据等级：当前源码完整静态调用链；未执行完整SceneRouter/Unity运行复现。

触发：通过默认interact意图交互teleporter物件，teleport_target_ref解析到另一张地图。

精确锚点：
- core/carriers/gobj/core/GameObjectHost.cs:338–364：跨图行为明确委派给上层，只return teleportTargetRef；同图才在:360 SetPosition。
- 同文件:94–100：返回值填入InteractResult.DispatchedRef，Success=true、Outcome=NoAction。
- core/carriers/gobj/core/InteractIntentTickHandler.cs:59–65：默认处理器取得result后仅检查Success，未消费DispatchedRef；建议评论范围59–65。
- core/carriers/assembly/CarriersAssembly.cs末尾：默认RegisterPhaseHandler安装该处理器。
- core/gameplay/assembly/GameplayAssembly.cs:732只注入TeleportResolver；真正TeleportUnit调用点:682和:705仅为Dialog与AreaTrigger。

复核命令：rg -n 'DispatchedRef' core presentation adapters games --glob '*.cs'。除了声明、赋值和测试，没有生产消费者。既有GameObjectHostTests.Interact_Teleporter_CrossMap_ReturnsDispatchedRefWithoutMoving只证明L3返回引用符合契约，不能证明L4完成切图；N14的GameplayAssemblyTeleportTests只有Gossip测试。

影响：目标解析成功且交互报告成功，但玩家地图/位置完全不变；该能力并未在07标为预留。区分于已修复的Dialog/AreaTrigger跨图导航问题。

建议：为默认interact处理器提供跨图转发出口，或由L4订阅明确的传送请求事件，统一调用现有导航实现。

验收：提交真实interact意图→world.Tick→router.Update后旧图实体被清理、玩家进入目标地图及正确坐标；同图传送继续只改变位置。

## 额外静态接线线索（主审决定是否收录，不计入上面三项）

- 默认采集点时钟未注入：core/carriers/gobj/contracts/GobjOptions.cs:74的SimTime默认恒0；GameObjectHost.cs:305–311用它写used_at并判断respawn_after_use。全仓对Options.SimTime赋值仅gobj测试夹具，没有CarriersAssembly/GameplayAssembly/Unity生产赋值。默认正冷却采集点首次使用后永远达不到冷却。对应07:152明确声明respawn_after_use。该线索静态确定，但本次没有运行默认GameplayAssembly采集往返复现。
- ArchetypeId恢复只改PlayerUnit字段（UnitPersistable.cs），实际职业属性/资源的重新ApplyTo没有在该段发生。是否作为可用多职业跨槽缺陷须结合游戏初始化/职业恢复责任再审核，不列高可信最终finding。
- Progression.RestoreState没有LevelUp，是否使rating缓存残留须检查装备恢复/StatChanged等完整恢复路径，未做推断升级。

## 文档—代码矩阵

| 章节/文档 | 当前结果与证据边界 |
|---|---|
| architecture/05_对象模型与世界.md | 对象、世界生命周期、空间同步与Spawn/AreaTrigger机制存在。:75明示questLog占位，真实Quest状态在QuestHost；不能把该占位当独立bug。:216明示world.map部分字段未校验，是已公开边界。当前同图读档仅回滚簿记而不重建实体的语义已在08:316明确拍板，SpawnHost.Load保留孤儿实体的行为不能再次当作C11重报。 |
| architecture/07_载体层_物品生物物件.md | Inventory/Equipment/Creature/Gobj主要机制存在；套装门槛效果存在GP26-02，跨图teleporter默认链存在GP26-03。:82–90明确附魔、宝石、耐久、绑定、随机属性为扩展预留；affix为空不等于交付缺陷。:152采集点respawn_after_use与默认恒0时钟的接线线索见上。 |
| architecture/08_玩法层_掉落任务对话关卡.md | Loot/Quest/Dialog/Encounter/Difficulty/Achievement/Economy主体存在。:81–84 auto/daily有数据与单模块实现，默认GameplayAssembly仍未调用Quest.Update、dayProvider=null、ownerResolver=null；是此前7e63d66矩阵已经登记的未默认接入边界。Escort是契约和手动UpdateProgress入口；多选一奖励/组合支付、:248难度词缀池是明确扩展边界。奖励事务存在GP26-01。 |
| architecture/10_存档与持久化.md | 已补master_seed(:79)与beginRecording(masterSeed...)(:174)，不要重报7e63d66的旧文档缺口。默认RegisterPersistables现在包含progression/archetype/known_skills/bindings/rng；旧主干遗漏已修。引用字段已存不等于职业/天赋运行状态完整恢复，也不等于完整世界快照。 |
| core/gameplay/assembly/README.md | :12构造例缺必填saveSystem；:69仍说TeleportUnit只改MapId不改位置，与当前:1208–1229及README后面的N14说明冲突；:97–98仍说Rng由调用方额外注册、本方法不持有IRngHost，与GameplayAssembly.Rng及RegisterPersistables末尾实际注册相反。:77说四个处理器但表列五个，属于次要漂移。 |
| core/gameplay/common/README.md | :73起“全部生效或全部不生效”和C05段“失败回滚到调用前、不多不少”范围过宽；GP26-01证明后续事件派发能改旧库存。不是旧actualCount bug重报。 |
| core/gameplay/quest/README.md | 第5条已明确调用方须显式Update，默认缺入口应列装配边界；第10条“任一步失败不改变任何状态”同样需要加入事件提交边界。 |
| core/gameplay/spawn/README.md | 7e63d66 C11修复及同图孤儿实体的取舍已有明确说明，未当新bug。 |
| core/carriers/item/README.md及EquipmentHost | 普通装备grant的C08引用计数/Replace修复存在，但未覆盖独立套装门槛表，见GP26-02。 |

## 历史问题复核

当前确实存在：Inventory/Equipment/Quest全量替换；Currency.SetBalance替换；known-skills持久集合替换；LeaveMap终止Encounter/Level；默认progression/archetype/RNG注册；DroppedLoot恢复后按地图Reattach；C05实际数量回滚；C06扣物品总量预检；C04 Encounter失败不提交胜利、Achievement持久化pending_reward。上述不作为本轮新问题。

## 可复现材料

目录：C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/gameplay-repros
- Repro.csproj：net8.0独立console，绝对Compile Include仓库Core源码与Stub；所有bin/obj在此目录。
- Program.cs：两个有界复现。套装复用只读原EquipmentReplaceHandleTests数据构造，再添加合法item.set，不运行原测试；Xunit shim仅让原夹具源码编译。
- successful-run.log：两例成功运行输出，dotnet进程退出0。SET_REPLACE after_unequip aura=False stat=1；REWARD_FAIL before_dispatch success=False inventory=5；REWARD_FAIL after_dispatch inventory=4 quest=ObjectivesComplete progress=1。
- 命令：dotnet run --project <上述目录>/Repro.csproj -c Release。为了避免操作源工程，刻意不ProjectReference原仓库项目，而是绝对编译源文件；因此这是相同源码算法/装配的独立console证据，不代替原方案全套构建测试、Unity或发布验证。
