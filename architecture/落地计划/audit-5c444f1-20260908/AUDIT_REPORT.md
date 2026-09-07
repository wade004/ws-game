# 文档—代码深度审计报告（v1.3.0 基线）

基线为 `D:/workespace/ws-game` 的 HEAD `5c444f1dc2f3729c8a03a68f78cacc28d24c2a2f`，根 `VERSION` 为 `1.3.0`，审计日期 2026-09-08。源仓库按约定只读；本报告与矩阵写入本目录。当前文件清单由 `git ls-files` 重新统计：C# 1091、Markdown 182、HTML 15。数量只用于本轮范围说明，不沿用旧基线盘点。

审计以 `C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract` 的锁定归档快照为代码证据；live `D:/workespace/ws-game` 在审计期间出现并行未提交修改（来源未归因），不纳入完整验收，也未由本审计编辑。源码链接统一指向归档快照，避免把并行工作树状态误当作 1.3.0 基线。

`source_zip_extract` 是固定 HEAD 生产源码的隔离验证展开目录，同时包含本审计追加的 probe、测试工程引用和验证辅助文件；生产源码证据只取固定 HEAD 文件，不能把追加 probe 视为生产变更。详细逐章映射见 [doc-code-matrix.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/doc-code-matrix.md)，运行验证见 [validation.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/validation.md)，工作树快照见 [working-tree-drift.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/working-tree-drift.md)。

本轮目标是核对 architecture/00–14、README、能力边界索引、ADR-0017 与实际代码/默认装配的差异，并对旧 1.1.0 的 13 项逐项复核。报告中的“已实现机制”“默认接线”“真实运行证据”严格分开；文档写有契约、单元测试存在或静态编译通过，都不能单独推出 Unity Runtime 或独立消费方纵切通过。

## 证据等级与审查边界

- **E1 机制证据**：从锁定 HEAD 编译并运行的独立 console/Stub/机制探针，观察到触发与终态；退出码只说明探针完成，不等于功能正确。
- **E2 静态证据**：源码调用链、默认构造参数、资源/打包脚本、契约签名和数据路径能够直接推出的风险；未由运行探针确认。
- **E3 真实 Unity/消费方证据**：Unity EditMode/PlayMode、独立版冒烟或只以发行快照为输入的 consumer smoke 实际运行日志。
- **E4 文档/能力边界证据**：正文、README、能力索引、CHANGELOG、计划或链接与当前实现不一致，或明确把能力列为预留/未默认接入；E4 不自动计为运行时缺陷。

本报告把主审已确认的源码链按各项证据表记录；已有 E1 机制和离线包校验材料，本轮没有 E3 Unity/consumer smoke，因此不把静态链表述为真实 Unity 复现。

## 当前基线判断

架构正文已形成 00–14、17 篇 ADR、模块 README 和 Unity/工具链实现的完整目录；W6/ADR-0017 已把 `model` 型表现、武器动画、命中帧同步从“契约存在”推进到代码实现与若干装配入口。但当前源码仍存在表现坐标/动画完成时序/默认资源登记/反馈批次、模型资源加载、生产装配参数、换装清理、发行快照与脚本安全等需要逐条验证的差异。已实现机制、是否默认传入 provider、是否具备真实 Unity 证据必须按下表和矩阵分别验收。

门禁和机制证据：隔离归档执行 `check.ps1 -SkipUnity` 为 20 步、14 PASS、6 SKIP；2369 个 .NET 测试与 47 个 pytest 通过，实际 1.3.0 ZIP 离线 `get_framework` 六个 DLL hash 通过；同一 HEAD 隔离构建 lock 配既有 ZIP 时六个 hash 均不匹配，入口拒绝且不落地。presentation 机制探针在真实 `EventBus`、`AnimStateMachine`、`AnimClipResolver`、`FeedbackBinder`、`FrameAnimPlayer` 上 3/3 PASS，覆盖 PR130-02～04 的局部 E1；这些不是 Unity 渲染、资源导入或消费方纵切证据。验证记录见 [validation.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/validation.md) 与 [stdout.log](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/probes-presentation/stdout.log)；Unity 本轮未启动。

| 条目 | 证据等级 | 已观察结果与限制 |
|---|---|---|
| PR130-02 | E1（机制）+ E2 | 真实状态机/resolver 探针 `anim-state-and-resolver` PASS，状态观察序列为 `Attack,Attack,Attack,Attack`，重播次数增量为 0；不证明 Unity Animator 完成回调。 |
| PR130-03 | E1（机制）+ E2 | 真实 resolver 选出 `anim.sample_sword_swing`，播放器报告 `ArgumentException`；不证明 Unity 资源导入。 |
| PR130-04 | E1（机制）+ E2 | 两条同命中规则在一次 rig emission 前后与 0.3 秒超时产生 `0,1,2` SFX 观察序列；不证明 Unity 动画事件回调。 |
| CR130-01 | E1（机制）+ E2 | 核心/玩法机制探针 1/1 PASS，复现失败购买后的库存/任务状态副作用；未作磁盘存档结论。 |
| CR130-02 | E1（机制）+ E2 | 核心/玩法机制探针 1/1 PASS，覆盖新宿主与同宿主 Load(empty) 差异；不把装备临时来源归入应持久化奖励。 |
| CR130-03 | E1（充能）+ E2（Proc ICD/school lock） | 规则探针 3/3 PASS；充能观察到 next=`10`、期望折算值=`2`，Proc ICD 与 school lock 仍由源码静态链判定；不代表 Unity 场景通过。 |
| CR130-04 | E1（机制）+ E2 | channel `channel_time=0.5`、`tick_interval=1`、`Update(1)` 实际 `1`、期望 `0`；修复应累计 `Min(dt,Remaining)`。 |
| PR130-01、PR130-05～08、PJ130-03～04 | E2 | 当前为源码/契约/打包静态链；本轮没有执行对应 Unity 或 registry 危险进程场景。 |
| PJ130-01 | E1（离线机制）+ E2 | 同一 HEAD 隔离构建 lock 配既有 ZIP 六 hash 均不匹配，`get_framework` exit 1 且未落地；远端 workflow 未执行。 |
| PJ130-02 | E1（发行快照）+ E2 | 既有 1.3.0 ZIP、adapter tgz、framework-data tgz 对 model 资源与生成器精确匹配均为 0；源码快照含对应资源。 |

## 当前发现索引（17 项：5 P1、12 P2）

下列条目来自已核对的源码定位；证据等级按上方逐项证据表记录，真实 Unity/消费方行为仍须按各条验收场景执行。

### 表现与 Unity 路线

#### PR130-01 — P1：model 放置与相机投影不共用同一 2.5D 平面

触发：创建 model View 后进行平面坐标/高度放置、相机跟随和世界—屏幕投影，比较 sprite 与 model 的共同逻辑位置。

影响：`UnityRenderer3D.SetPlacement` 把逻辑 `(X,Y,height)` 写成 Unity `(X,height,Y)`；`UnityCamera` 仍以固定 XY 朝向、`WorldToScreen` 的 `(X,Y+height,0)` 和 XY 跟随/投影处理。两套路径不在同一平面，model 与 sprite 的屏幕位置、地图点击反投影可能不对齐。

源码证据：[UnityRenderer3D.cs:163](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:163)、[UnityCamera.cs:67](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityCamera.cs:67)、[UnityCamera.cs:96](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityCamera.cs:96)、[UnityCamera.cs:144](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityCamera.cs:144)。

建议验收：同一 `Vec2`、height=0/非零、不同 facing 与 zoom 下，sprite/model 的 `WorldToScreen` 像素误差在约定容差内；仅在 height=0 时验证 `ScreenToWorld(WorldToScreen(p,0))` 回到同一逻辑平面；非零 height 比较 model、sprite 与 UI 的共同显示基准；地图点击命中同一实体。修复前同步更新 02、09、14 的坐标约定。

#### PR130-02 — P1：model Attack 没有完成回调，状态机无法可靠回落与重播

触发：model 单位第一次进入 Attack，动画播放结束后再次移动/攻击，并在 Hit 状态期间尝试进入 Attack。

影响：[UnityViewFactory.cs:697](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:697)–711 只登记 model 动画和 resolver，没有完成回调；[AnimStateMachine.cs:237](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/render/core/AnimStateMachine.cs:237)–242 仅由 Cast 逻辑收尾回落，`:265`–268 同态切换直接抑制。第一次 Attack 后不回移动/再次 Attack 不重播，Hit 优先级期间还可能阻断后续动作。

建议验收：model 的非循环 Attack 结束必回 locomotion；Attack→Attack 重新触发时可重播；Hit→Attack、Attack→Hit 的优先级和回落有明确终态；动画被打断、资源缺失与无 Animator 时都有可观察诊断和可恢复状态。同步修订 09 第 4.2 节与 ADR-0017 对完成时序的描述。

#### PR130-03 — P1：默认动画登记键与武器资源 id 不一致，sample swing 无法播放

数据触发条件限定为默认 sprite 装配下实际装备带 `weapon_style_ref` 的 `item.sample_blade`（`item.slot.sample_main_hand`），不是所有模板启动都会自动装备该物品。

触发：默认 sprite 装配实际装备带 `weapon_style_ref` 的 `item.sample_blade`（`item.slot.sample_main_hand` 可用），其表现档案为 `anim.sample_sword_swing`，进入 Attack。

影响：[UnityViewFactory.cs:414](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:414) 只登记 `anim.default.<displayId>.<state>`；[AnimClipResolver.cs:98](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/AnimClipResolver.cs:98) 直接返回武器 `AutoAttack` 资源 id；默认 `player.Play`（factory 约 580）到 [FrameAnimPlayer.cs:57](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/render/core/FrameAnimPlayer.cs:57) 的登记表中未登记该资源剪辑 ID；`player.Play` 时抛 `ArgumentException`，资源解析链断开。

当前静态链结论：该资源剪辑 ID 未登记到播放器表，`player.Play` 时抛 `ArgumentException`，而非模板启动必然抛错。

建议验收：sample 装备进入 Attack 时实际播放 `anim.sample_sword_swing`；未声明武器动画时按文档约定退回默认状态剪辑并记一次诊断；sprite/model 两路线均验证 `auto_attack_anim` 与按技能 `cast_anim_override`。

#### PR130-04 — P2：同一命中批次的多个反馈规则不能原子释放

触发：一个 `combat.damage_dealt` 事件命中多个 `sync=hit_frame` 规则，或范围攻击为多个目标产生多个等待项，再触发一次攻击方命中帧。

影响：[FeedbackBinder.cs:220](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/feedback_binder/core/FeedbackBinder.cs:220)–242 每条规则单独调用 `WaitForHitFrame`；[HitFrameSyncPolicy.cs:128](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/feedback_binder/core/HitFrameSyncPolicy.cs:128)–139 每次命中帧只 release 首个匹配项，剩余动作可能超时或串到下一次攻击。

建议验收：同一事件的全部动作作为一个批次在一次命中帧同时释放；多目标/双规则各自保序且不串批次；无命中帧按 0.5 秒兜底并逐批记诊断；逻辑伤害只结算一次。

#### PR130-05 — P2：model 缺资源路径违反 ADR-0017 的加载与降级契约

触发：`IRenderer3D.CreateModelInstance` 在 `IResourceLoader` 缓存未命中或资源不存在时创建 model View。

影响：[UnityRenderer3D.cs:113](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:113)–123 直接 `Resources.Load`，失败抛 `InvalidOperationException`；文件顶部 6–20 已明确这是当前选择，[UnityRenderer3DTests.cs:42](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UnityRenderer3DTests.cs:42)–47 也用 `Assert.Throws` 锁定该行为。实现和测试与 ADR-0017 决策 1（首次引用者 `loadAsync`，renderer 只消费已加载资源，未加载使用占位并记诊断、不隐式加载、不抛异常）冲突；modelView 构造遇到缺失资源时会中断 View 创建，不能夸大为整个进程必然崩溃。

建议验收：冷启动由 View/播放器发起 `loadAsync(ResourceKind.Model)`；加载中/失败可创建占位 model 并记一次诊断；renderer 不同步加载、不抛出调用方异常；加载完成后按同一句柄替换或重新绑定。当前 `UnityRenderer3DTests` 的 `Assert.Throws` 需随 ADR-0017 失败策略一并改写。

#### PR130-06 — P2：生产装配把 renderer3D 传给 ViewFactory，却未传给 PresentationAssembly/VfxPlayer

触发：走 `GameBootstrap`/`GameFoundationBootstrap` 的生产默认构造，播放 model socket 特效或反馈。

影响：[GameBootstrap.cs:294](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/games/_template/Runtime/GameBootstrap.cs:294)–297、[GameFoundationBootstrap.cs:447](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:447)–450 为 ViewFactory 提供 renderer3D，但 PresentationAssembly/VfxPlayer 未获得同一 provider；其 VfxPlayer 得到 null，socket 特效固定走 world 坐标降级。`hitFrameSource` 已传入，不属于本条缺口。

建议验收：三处生产装配根共享同一 renderer/model-handle/weapon-style/hit-frame provider；model `attach_mode=socket` 实际挂到移动挂点并跟随；无 provider 时明确记录降级原因。同步 02、09、14 与 ADR-0017 的“默认接线”清单。

#### PR130-07 — P2：model 换装卸载按逻辑 slot 清 socket，可能残留挂件

触发：装备时按 `EquipVisualDef.SocketId` 挂接，卸装事件同时携带 `ItemInstanceId` 与逻辑 `item.slot`；当前卸装路径未利用实例 id 反查外观定义，注入外观映射后两者 id 不同。

影响：[UnityModelView.cs:126](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityModelView.cs:126)–136 在卸装路径同时 `ClearSlot(unequipped.Slot)` 与 `ClearSocket(unequipped.Slot)`，而装备使用 def 的 SocketId。不同 id 时 socket 子模型残留。

建议验收：slot_mesh/socket_attach 两种模式，装备/替换/卸装及不同 SocketId 均清理旧句柄；通过 ItemInstanceId 反查 EquipVisualDef，重复卸装幂等；默认 factory 未注入装备外观表的边界要单独写入能力矩阵，不能把“model 装备已显示”作为默认证据。

#### PR130-08 — P2：model 影子挂在 root 下，height 会把影子一起抬离地面

触发：model 设置非零 height 并启用 Blob shadow。

影响：[UnityRenderer3D.cs:387](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:387)–388 把 blob 设为 modelRoot 子物体；`:163` 将 height 写入 root 的 Y，影子因此随角色升高，违反 09 第 3.4 节（约 140–141）“影子贴逻辑平面、不随高度位移”。

建议验收：height=0 与 height>0 时影子世界平面位置保持不变，角色本体按 height 偏移；Projected/None 模式不生成错误 blob；sprite/model 影子语义一致。

### 交付与工具链

#### PJ130-01 — P2：Release fallback 可能混用旧 zip 与新 runner 构建的 lock/DLL

触发：`.github/workflows/release.yml` 检查到附件缺失后，`:209`–224 只列缺失文件，`:243`–260 重新 build 并上传缺失附件；旧 zip 已存在但 lock 缺失或其 DLL 来自不同构建批次。

影响：同一 HEAD 的隔离构建与既有 ZIP/lock 实测六个 DLL hash 不同；以新构建 lock 配旧 ZIP 时，[get_framework.ps1:341](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/toolchain/get_framework.ps1:341)–362 拒绝组合且不落地。远端 workflow 未执行、未查询 GitHub；当前结论是缺附件 fallback 的同 HEAD 混批风险。

建议验收：缺附件时从同一已验证 zip 提取 DLL 生成 lock，或从同一构建目录补齐 zip/tgz；发现已有附件来自不同提交/哈希时直接阻断，不混构建批次。发布后以同一 commit、整包 hash、lock、全部附件清单做离线验证。

#### PJ130-02 — P2：Dist 打包遗漏 model/anim 资源与占位模型生成器

触发：执行 `build.ps1 -Dist`，只检查快照中的适配层包、模板、toolchain、placeholder assets、framework data 与 TMP essentials。

影响：[build.ps1:875](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/build.ps1:875)–887 的 Copy-DistDir 未包含 `adapters/unity/Assets/Resources/GameFoundation/models`、`anim_clips`；也未打包 `adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs`。CHANGELOG 1.3.0 的占位资产交付描述未落到独立 ZIP/UPM；独立消费者只有发行包时无法按文档使用这套框架占位 model/动画。

建议验收：解压 dist/zip/UPM 后，按文档从零消费可取得模型 prefab、动画 clip/controller 与生成器；manifest/lock 覆盖这些交付物或明确排除理由；缺资源时消费方仍按 ADR-0017 走异步占位降级。

#### PJ130-03 — P2：registry stop 可能误杀 PID 复用或无关监听进程

触发：PID 文件过期/复用，或目标端口由非 Verdaccio 进程监听，执行 `start_registry.ps1 -Stop`。

影响：[start_registry.ps1:143](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/toolchain/registry/start_registry.ps1:143)–146 直接按 PID 强杀；`:87`–90 仅按端口查监听者；`:161`–166 对全部监听 PID 强杀，未核对 exe、命令行、配置路径或启动时间。

建议验收：PID/端口命中后先核验进程身份与本脚本生成的配置、启动时间；身份不符则拒绝停止并给出诊断；端口上的无关进程绝不受影响；目标服务停止后再验证端口释放。

#### PJ130-04 — P2：1.3.0 发布号承载了不兼容 ICharacterRig 契约变更

触发：外部实现继续实现旧版 `ICharacterRig`，升级到 VERSION 1.3.0；CHANGELOG:49–54 明确 `HitFrameReached` 为强制新增事件成员。

影响：这是编译级破坏性变更，而 `architecture/11_工程规范与测试.md:156` 将契约签名变化归入 MAJOR；以 1.3.0 发布会使版本语义与迁移规则不一致，已发布包也不得同号覆盖。

建议验收：选择升 major 并在新版本提供迁移说明，或把命中帧能力拆为可选接口且保持 ICharacterRig ABI；检查 package.json、CHANGELOG、dist/lock、文档版本一致；任何已发布 1.3.0 快照保持不可变。

### 核心规则与玩法

#### CR130-01 — P1：Economy 购买失败仍会发布 pending item.added，旧任务物品被消费

触发：先以 `FullPolicy.Partial`、容量为 1 的库存放入 A5 stack=10 并派发其事件，再接受 `consumeOnProgress=true`、目标 A1 的任务；随后购买 A6 失败。`EconomyHost` 在 [EconomyHost.cs:239](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/economy/core/EconomyHost.cs:239)–250 通过 `AddItem` 与数量补偿路径未 `BeginBatch`，批次部分成功后 `Dispatch`；随后任务事件处理走 [QuestHost.cs:751](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/quest/core/QuestHost.cs:751)–776。

影响：A6 失败回滚后仍有排队的 `item.added`，QuestHost 按事件从旧库存扣 A，A5 变 A4；失败交易产生状态副作用，当前没有磁盘存档证据。该项与旧 GP26-01 的奖励路径不同，需按当前 Economy→Quest 链路验收。

建议验收：失败交易完成 `Dispatch` 后库存、任务进度和事件队列均回到调用前；成功批次只发布真实新增量；覆盖 Economy、Reward、Loot、Quest 交付和补偿路径。

#### CR130-02 — P1：一次性奖励技能的来源未持久化，读档结果依宿主生命周期分叉

触发：一次性任务/成就/遭遇奖励经 [RewardDispatcher.cs:238](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/common/core/RewardDispatcher.cs:238) 传 `sourceId`，由 [GameplayAssembly.cs:485](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:485)–489 调用三参 `Learn`；读档时 `KnownSkillsPersistable` 仅持久化永久来源（[KnownSkillsPersistable.cs:75](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/KnownSkillsPersistable.cs:75)），`SkillHost` 在 [SkillHost.cs:283](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/SkillHost.cs:283) 过滤 `PermanentGrantSource`。

影响：本应长期保存的一次性奖励被来源分类当作临时 grant，新宿主恢复存档后丢失；同一宿主读旧档时内存中的该技能未清理又继续存在，形成恢复结果依赖宿主生命周期的分叉。装备临时来源仍应保持非永久。

建议验收：一次性奖励与装备临时来源的持久化语义分别明确；新宿主和同宿主读取相同快照得到一致已知技能集合；来源被撤销或重复学习时幂等；存档迁移有版本测试。

#### CR130-03 — P2：混合时间模式只折算冷却首颗、未折算 Proc ICD 与施法 school lock

触发：连续模式切离散模式，时间因子 `0.2`；[CooldownTracker.cs:188](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/CooldownTracker.cs:188) 仅首颗 cooldown 乘 factor，`:311` 后续推进不乘；[SkillHost.cs:141](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/SkillHost.cs:141)–145 的 rescale 未覆盖 `ProcHost`（ICD 在约 132 写入、约 97 递减）；[CastPipeline.cs:719](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/CastPipeline.cs:719) 只折算 casting，未折算 school lock（约 534 写入、约 700 递减）。

影响：模式切换后同一技能家族的冷却、内部冷却和 school lock 使用不同时间单位，技能可用性与规则文档的统一时间语义不一致。

建议验收：切换前后所有倒计时（技能/category/GCD/charges、Proc ICD、school lock）使用同一因子；连续↔离散往返不漂移；在任意剩余时间、零值和切换边界验证。

#### CR130-04 — P2：引导结束后的时间余量仍结算周期效果

触发：`channel_time=0.5`、`tick_interval=1`，单次 `Update(1)`；[CastPipeline.cs:411](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/CastPipeline.cs:411) 以完整引导 dt 进入周期处理，直到 [CastPipeline.cs:430](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/CastPipeline.cs:430) 才扣 `Remaining`。

影响：该次更新在引导已经结束后仍把剩余时间用于周期结算，expected=0、actual=1；Aura 相同边界已修复，引导路径仍不一致。

建议验收：周期累计使用 `Min(dt, Remaining)`，只在引导有效时间内按 interval 产生 tick；覆盖 `channel_time=0.5`、`tick_interval=1`、`Update(1)` 的 expected=0，以及跨多个 interval、被打断和模式切换均不多结算、不漏结算。

#### CR130-05 — P2：customTeleportResolver 与内置传送双重消费，同图自定义结果可能被覆盖

触发：`GameplayAssembly` 保留 `customTeleportResolver`（约 [GameplayAssembly.cs:752](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:752)），并在 [GameplayAssembly.cs:1267](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:1267)–1295 新增 gobj 监听；[GameObjectHost.cs:351](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/carriers/gobj/core/GameObjectHost.cs:351)–360 的自定义同图传送先执行，监听链仍可能按内置 `TeleportUnit`（[GameplayAssembly.cs:1215](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:1215)–1217）二次解引用并覆盖结果。

影响：自定义 resolver 返回 null 或已成功处理后，新增 listener 仍可能二次执行，把自定义同图目标 `(99,88)` 覆盖为内置 `(1,2)`；跨图/同图行为取决于监听顺序，调用方无法依赖单一传送结果。

建议验收：每次 interact 只有一个权威传送消费方；custom resolver 成功时内置路径不再执行，返回 null 时也不执行内置传送；同图/跨图、失败、重复事件和无 resolver 均验证最终地图/坐标。

## 旧 1.1.0 十三项复核表（不预判结论）

| 旧编号 | 1.3.0 复核状态 | 当前证据/结论 |
|---|---|---|
| GP26-01 | 当前本体/Quest 事务边界已源码闭合；Economy 新链路见 CR130-01 | 不把旧奖励路径结论复制到当前；由 CR130-01 单独覆盖 Economy→Quest，使用对应机制与消费方场景验收。 |
| GP26-02 | 已闭合 | 套装 Aura 来源计数修复成立，不重报。 |
| GP26-03 | 默认路径已修；custom resolver 回归见 CR130-05 | 只对新双消费链路计当前发现。 |
| FR-01 | 部分修复；混合家族遗漏见 CR130-03 | 旧“只换算通用计时器”不原样重报。 |
| FR-02 | 已闭合 | 0 充能恢复已修，不重报。 |
| FR-03 | 已闭合 | Aura 周期尾跳已修；引导周期仍见 CR130-04。 |
| FR-04 | 已闭合 | 读档 rating cache 问题已修；技能来源恢复是 CR130-02 的新边界。 |
| FR-05 | 已闭合 | 技能嵌套坏字段校验已修，不重报。 |
| U01 | 静态修复成立；本轮未执行对应 Unity 场景 | 音乐选源已改正，不重报当前缺陷。 |
| U02 | 静态修复成立；本轮未执行对应 Unity 场景 | SFX 自然结束回收已改正，不重报当前缺陷。 |
| U03 | 静态修复成立；本轮机制探针已执行并 PASS，Unity 场景未执行 | FrameAnim 跨帧事件已改正，不重报当前缺陷。 |
| U04 | 静态修复成立；本轮未执行对应 Unity 场景 | AnimRoot、renderer 高度/颜色链已接回；model 影子高度另见 PR130-08。 |
| U05 | 旧缺附件检测已改为全 5 项；新混批 hash 风险见 PJ130-01 | 不重报旧“漏独立 tgz 完成判定”。 |

## 文档漂移与需更新项（与运行时缺陷分开）

1. 根 README:9 仍写“19 项未默认接入”且包含已由 ADR-0017 补齐的 3D/装备动画/关键帧；README:16/228 仍写 ADR 16 条，应同步为当前 17 条，并把“框架实现、默认接线、真实 Unity 证据”拆开。
2. 落地计划中旧的 `IRenderer3D`“声明降级/未实现”描述（如 :82、:469、:478、:833、:959）应标成历史记录或改为当前 W6-B 现状；:1152 的 FindUnits 不能写成“仅未注入为空”，当前 `SkillHost` 无条件返回空的边界要准确表达。
3. architecture/01 正文窄命令已修，但图解 01:201/208 与图解 03:113/155 仍把表现侧绝对表述为只读或把 PlaybackFinished 说成唯一反向通路；应同步“只读查询 + 窄命令例外”和事件/意图路径。
4. 02/09/14、ADR-0017 与 Unity README 对模型资源异步加载、坐标平面、动画完成回落、反馈批次、影子贴地、默认装配参数的描述需与当前源码及验收结果同步；已有测试不等价于完整路线通过。
5. adapter README:309/322 的旧降级/退订缺口描述需按当前实现标成历史或移入变更记录。
6. 相对链接扫描发现的 13 个失效引用需修正，例如落地计划中的 `adr/0017` 应按文件所在层级使用 `../adr/0017...`；若只验证文件存在，不把锚点有效性误写成已验证。
7. 模块 schema README 的 `../../../architecture` 路径少一层：`core/foundation/display_info/schema/README.md:3-4`、`save_system/schema/save_slot_meta.md:3-5/29`、`scene_router/schema/README.md:3-4`、`sim_loop/schema/README.md:4` 等应按仓库相对路径修复。

## 修复依赖顺序与验收边界

建议依赖顺序：先修购买事务与一次性奖励技能存档，避免状态损失；3D 平面/模型资源加载可并行；随后修时间家族、引导周期、传送唯一消费，再修 model 动画完成、默认剪辑登记、命中帧批次、生产装配 provider、换装清理与影子；交付链修复同批次构建/完整附件、registry 身份核验和版本契约；最后更新 00–14、README、ADR/CHANGELOG、能力索引与失效链接。

静态/单元/Stub 只能证明接口、调用链或局部机制；已有基础门禁 14 PASS/6 SKIP（2369 .NET、47 pytest），实际 ZIP 离线 get_framework 六 hash 通过，混搭新 lock/旧 ZIP 六 hash 拒绝且不落地；这些结果不能替代 Unity。真实 Unity 至少需要 model 与 sprite 共平面投影、模型冷启动/资源缺失、Attack→locomotion→重播、双规则/多目标命中帧、socket 换装卸载、height 影子、默认三处装配；发行侧还需完整占位 model 资源和 registry 无关进程保护。本轮未执行对应 Unity 场景，已有测试文件不等于已执行，单个测试也不覆盖整条路线。


