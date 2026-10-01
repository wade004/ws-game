# ui_layout_definition

见 [01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L5 模块表 `ui` 行"主要数据表：
ui_layout_definition"。内容表，主键 `id`（`UiSchemas.UiLayoutDefinition`，见
`UiLayoutSchema.cs`）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| id | Id | 是 | 面板逻辑 id |
| panel | Enum | 是 | 面板类别，见 `UiPanel`（十个值，与 `core/ViewModels` 下十个视图模型一一对应） |
| slots | Int | 否 | 动作条槽位数量，仅 `panel=action_bar` 时有意义 |
| fields | Object | 是 | 布局参数，结构留给引擎适配层/具体游戏解释，本模块不读取 |
| skin_ref | Id | 否 | 界面皮肤包引用（`skin.<名>` → 资产目录 `assets/<数据集>/ui/skin/<名>/`，不是内容表，故不登记软引用）；缺省取框架占位皮肤 `skin.default`。本模块只透传（`UiLayoutDefinition.SkinRef`），不解析皮肤包内容；皮肤包缺项回落占位并进装备完整性报告（`import_assets.py equip`，手感设计/08 第 3 节，ADR-0123）。纯新增可选字段，不升 schema 版本 |

判断记录：任务书原文"panel（enum 十个）"，09_表现层.md 第 7.1 节 UI 组成清单实际列了 11 项
（HUD、动作条、状态栏、目标框、背包与装备、任务日志、对话框、商店、菜单、设置、按键绑定面板，
另有三项仅离散模式）。为了让"面板类别"与"落地计划要求的十个视图模型"严格一一对应、不产生
孤儿，本模块把状态栏/目标框并入 Hud（`HudViewModel` 已经承载目标框数据）、商店复用
Inventory 的数据来源与 `UiIntents.Buy`/`Sell` 意图（不需要独立视图模型）、按键绑定面板并入
Settings（`SettingsViewModel` 已经承载绑定列表）。
