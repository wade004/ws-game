# 1.13.0 表现责任与契约边界复核

本文件是对本轮 Unity 证据的责任 review，基线为 `c9ff30107413083188c597c0b65cf1691c9dfe9b` / `VERSION=1.13.0`。本次只做静态契约、owner、启用条件与证据边界复核；不重跑 Unity、fixture 或清理外部副本。已归档的 70 Edit / 270 Play XML 是此前执行得到的结果，本文件不把它们表述为本轮新运行。

## 结论表

| 项目 | 契约与 owner | 启用条件 | 证据与实际 oracle | 责任结论 |
|---|---|---|---|---|
| 同图已有 View 的 SaveLoaded 外观 | `ViewBinder` 和 `EquipmentVisualSource` 属表现框架；模板 `GameBootstrap` 负责把 `EquipmentHost` 快照、装备目录和 factory 接起来 | `UnityViewFactory` 的可选 `equipmentVisualSource`、model/sprite View 与装备目录均需装配；模板默认接入，消费方可显式不用 | [ExistingViewEquipmentSaveLoadAuditTests.cs](ExistingViewEquipmentSaveLoadAuditTests.cs)、[view-equipment-filtered-final.xml](view-equipment-filtered-final.xml)、[view-equipment-filtered-final.log](view-equipment-filtered-final.log)：Load B 后 equipment=0、同一 View/socket identity，等待后 socket child=1；正确 oracle 为 0 | 确认表现框架已有 View 的刷新缺口（P2）。新 View replay 通过不能覆盖同图 SaveLoaded 失败；自定义消费方未装配 source 时属于其接入选择 |
| DataHotReload Changed/Created/Renamed | 标准实现 owner 是 `games/_template/Runtime/DataHotReload.cs`，模板 `GameBootstrap` 负责生产装配 | `GameOptions.EnableDataHotReload=true`（默认 true）；有效实现只在 `UNITY_EDITOR || DEVELOPMENT_BUILD`，发布构建为空壳 | 生产 changed-file probe 已包含在归档 Play 证据；[data-hotreload-play-evidence.log](data-hotreload-play-evidence.log) 有真实 `ReloadTable` 成功。正确 oracle 为既有合法表值 5→7，并在 finally 回到 5 | Changed 成功路径已证明；这是模板开发工具路径，不是框架核心对所有游戏运行形态的保证 |
| DataHotReload Deleted overlay | 同上，删除语义仍由模板 watcher 与 `DataRegistry.Reload` 共同负责 | 仅 Editor/Development 有 watcher；fixture 用临时 framework/game 根和独立 `DataRegistry`，不改 `data/game` | [DataHotReloadDeletedOverlayAuditTests.cs](DataHotReloadDeletedOverlayAuditTests.cs)、[data-hotreload-edit-evidence.log](data-hotreload-edit-evidence.log)：`before=2; after_delete=2; manual_reload_fallback=1`。正确 oracle 是删除 override 后自动回到 1 | 已确认模板工具 P2：代码 110–112 行只订阅 Changed/Created/Renamed，未订阅 Deleted；诊断 PASS 表示捕获缺陷，不是删除功能通过 |

## View SaveLoaded 的责任划分

架构与变更记录承诺的是实体 View 的 SaveLoaded 对账，以及跨图/存档恢复创建的新 View 能重放当前装备。当前 [ViewBinder.cs](../../../../presentation/view_binding/core/ViewBinder.cs) 125 行订阅 `SaveLoaded`，234 行进入 `OnSaveLoaded`，但 248–250 行对已存在的 `_views` 直接 `continue`。这足以解释实体仍在场时不重建 View 的行为，但它本身不负责装备 socket 清理。

装备外观的通用 owner 是 [EquipmentVisualSource.cs](../../../../presentation/render/core/EquipmentVisualSource.cs)：68–70 行只订阅 `ItemAdded`、`ItemEquipped`、`ItemUnequipped`；91 行的 `ReplayEquippedForUnit` 是给新 View 构造期使用的重放入口。`UnityViewFactory` 423–441 行只有在新 View 创建时调用该重放。模板 [GameBootstrap.cs](../../../../games/_template/Runtime/GameBootstrap.cs) 287–315 行默认构造装备目录、`EquipmentSnapshotResolver` 和 `EquipmentVisualSource`，因此本模板路径属于框架表现生命周期缺口；没有选择装配可选 source 的其他游戏不能据此升级为框架缺陷。

真实 probe 的独立 fixture 只在外部副本 `_sample/item/item.template.json` 临时追加一个合法 `item.sample_model_sword` 行，未向模板 `data/game` 注入 sample 依赖；[fixture-restore.log](fixture-restore.log) 证明源文件与 StreamingAssets 恢复为同一原始 SHA。保存顺序是空装备 B →真实 Add/Equip A→保存 A→真实 Load B，随后重新查询 View、model root、socket，并等待最多 8 秒。故观察到的 `equipment_after_load=0; view_same=True; socket_same=True; socket_childCount_after=1` 能区分“装备状态已恢复为空”和“外观仍残留”。正确性断言失败保留在 XML 中，不能改写为诊断 PASS。

## DataHotReload 的承诺与失败边界

[games/_template/README.md](../../../../games/_template/README.md) 159–176 行把 `EnableDataHotReload` 默认开启、Editor/Development 生效、300ms 去抖、成功补发 `data.load_completed`、失败补发 `data.validation_failed` 写成模板承诺。实现 [DataHotReload.cs](../../../../games/_template/Runtime/DataHotReload.cs) 79 行开始受编译条件保护；[GameBootstrap.cs](../../../../games/_template/Runtime/GameBootstrap.cs) 212–221 行按开关挂载并监视 framework/game 两个根。

删除 override 不是“旧值应继续保留”的合法功能 oracle。多根 `DataRegistry.Reload` 的契约在 [DataRegistry.cs](../../../../core/foundation/data_registry/core/DataRegistry.cs) 261–328 行：重新从全部根定位并按合并规则重建目标表；手动 Reload 因而回到 framework 值 1。watcher 未收到 Deleted，实测自动路径继续读到旧 override 2，之后手动 Reload 才到 1。该行为归责模板 watcher 的事件覆盖，不推给游戏内容数据。

失败数据语义需要单独表述。`DataHotReload.ReloadTable` 200–219 行捕获异常时忽略本次变更；收到 blocking report 时发布 `DataValidationFailedEvent` 并返回。它没有在本层承诺“保留可读的旧表快照”；README 明确写的是全局只读查询阻断，直到下一次成功重载/加载。`DataRegistryTests.Validate_AfterBadTableLoad_StaysBlocked_EvenWithoutTouchingBadTable`（802–840 行）是 .NET 契约证据：坏表错误持续阻断，无关表重载不能解除，修复并重载坏表后才解除。本轮 Unity 只验证成功 Changed 入口，没有把失败文件场景升级为新的 Unity Runtime 结论。

## NAV / SPATIAL 只按适配器契约复核

NAV 的证据只覆盖 `INavigation2D.FindPath` / Raycast 的窄通道与端点精确性：`UnityNavigation2D.FindPath` 127 行、直线兜底 149–166 行、`SegmentHasClearContact` 758 行；实际回归 oracle 是端点可行走且直线无阻挡时返回精确首尾，归档 Edit 通过。最新 [02_引擎适配层.md](../../../../architecture/02_引擎适配层.md) 168 行已明确跨帧分摊由实现自行决定、不是接口默认保证，因此不把导航跨帧预算当本轮适配器行为违约。

SPATIAL 的证据只覆盖 `ISpatialQuery.QueryRadius` / `QueryCone` 及当前 bucket 实现：`QueryRadius` 85 行、半径扩张 100–101 行、`QueryCone` 109–115 行、`CandidatesNear` 208 行、`MaxRadiusHint` 227 行。独立 oracle 分开看：实体跨 bucket 移动后仍命中；注销后不再命中；二者归档回归通过。最新 [02_引擎适配层.md](../../../../architecture/02_引擎适配层.md) 190 行已将完整索引化收窄为实现方自行决定的性能边界，因此不能从未全索引化推导本轮功能失败。

## 通用机制与游戏策略的归责

- VFX 锚点持续跟随是表现通用契约的静态差距：[09_表现层.md](../../../../architecture/09_表现层.md) 280 行明确 `attach_mode: anchor` 表示挂接到 sprite 锚点跟随，`socket` 表示挂接到 model 挂点跟随；当前 `VfxPlayer.Spawn` 113–145 行只在生成时解析位置，`Update` 315–354 行只推进对象池与 pending 超时，没有持续刷新 anchor/socket 位置。本轮没有新增 VFX 跟随 Unity Runtime 复现；这项差距归表现通用机制 owner，不能借某个游戏未选该策略来推翻契约，也不能把它误报为本轮游戏层缺陷。
- `TargetPoint` 是技能请求可携带的可空字段；地面点选到具体目标的解析与消费由 AI/玩家辅助施法的游戏层负责。模板未提供该策略不等于框架契约缺失。
- Swing/Impact 的 `WeaponStyleResolver` 与 `PresentationAssembly.WeaponStyle` 是通用解析机制；没有默认生产调用方时，调用接线属于表现/游戏消费方选择。不能把“未默认接线”改写为解析器不存在或整个武器系统失败。
- gather 时钟由 `GobjOptions.SimTime` 注入；默认恒定值是组合根接入边界。需要刷新 respawn 的游戏应提供统一模拟时钟，不能把未选择 provider 的模板直接归责为框架逻辑错误。
- 新局完整 reset 的游戏专属初始状态由 `SampleNewGameStarter.Start` 8–22 行说明，实际最小实现 34–44 行只设置起始位置、地图和模板；`Presentation.Shell.NewGameStarter` 契约见 [ShellHostTypes.cs](../../../../presentation/shell/contracts/ShellHostTypes.cs) 25–38 行，明确由游戏层创建初始玩家/各持久化段状态并返回地图，`ShellHost` 负责后续场景编排。框架持久化段清空契约与游戏开局策略应分开核查。VFX、TargetPoint、Swing/Impact、gather 时钟和新局 reset 均不改变本轮两个已确认 P2 的归属。

## 证据计数口径

[editmode-full-final.xml](editmode-full-final.xml) 的 70/70 与 `playmode-full-final.xml`（原件未随本次归档提交到本仓库，见 `toolchain/tests/.linkcheck-ignore` 说明）的 270/270 是归档的最终基线证据，本次责任 review 未重新运行。Deleted overlay 用例的 1/1 是缺陷签名断言通过；View Save/Load 用例按正确 oracle 失败。fixture 只为通用机制测试提供合法 `_sample` 输入，不引入 `ws-game-wow` 依赖；原始 XML/log/probe/hash 文件本轮未改。两类结果、生产 Changed 成功、NAV/SPATIAL 回归分别记录，不能合并成“全链路表现已通过”。
