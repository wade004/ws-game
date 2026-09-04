# `data_registry` 的 schema 登记方式

`01_分层与依赖.md` L0 模块表 `data_registry` 行"主要数据表"一栏写的是"元表：
`schema_registry`（记录各表 schema 版本，非 04 总索引登记的内容表）"。

## 判断记录：`schema_registry` 不落地为数据文件

`schema_registry` 在本实现中**不是**一份独立的 JSON 数据文件，而是代码内的 `TableSchema`
注册表：调用方经 `IDataRegistry.RegisterSchema(TableSchema)` 逐张登记，`DataRegistry` 内部
用 `Dictionary<string, TableSchema>` 持有；`IDataRegistryView.GetSchema(table)` /
`IDataRegistryView.Tables` 提供对外的只读导出。理由：

- 04 全篇（含第 3 节"schema 版本与迁移"、第 4 节 DataRegistry 接口）从未把 `schema_registry`
  列进第 1.1 节"表清单"总索引，也没有给出它的字段表——01 模块表这一栏的措辞本身就是"元表"
  （相对内容表而言的元数据），不是要求新增一张内容表。
- `TableSchema`（字段清单、主键规则、迁移链、是否登记表）是纯结构性声明，天然属于"代码即
  文档"的强类型场景：迁移函数（`MigrateDelegate`）是可执行逻辑，无法用 JSON 表达，必须落在
  代码里；把"哪些字段""迁移链怎么串"拆成一份平行的 JSON 元数据、再与代码里的迁移函数对齐，
  只会制造两份真相源不一致的风险。
- 各模块自己的表 schema 理应随各模块实现登记在各自模块（如本目录的 `BuiltinSchemas`、
  未来 L1～L4 各模块的等价物），而不是集中提交一份跨模块的 `schema_registry.json`——这与
  04 第 8 节"新增一个内容"标准流程"改哪些表"栏目所暗示的"schema 定义随模块代码走"一致。

若后续确有需要把 schema 元数据导出为数据文件（如供工具链离线读取而不启动 `DataRegistry`），
应作为一次独立的"新增数据表"改动，走 12_扩展与变更流程.md 的审批流程，而不是本任务范围内
默认展开。

## 信封格式（见 `data/README.md`）

```json
{ "table": "<与文件名一致>", "schema_version": <正整数>, "rows": [ ... ] }
```

## 主键规则

见 `../README.md`"主键规则"一节；权威定义见 `data/README.md`"记录主键"。

## 迁移链规则（04 第 3 节）

- `TableSchema.CurrentSchemaVersion` 是代码期望的最新版本；`TableSchema.Migrations` 是一组
  `TableMigration { FromVersion, ToVersion, Migrate }` 环节。
- 加载时若文件 `schema_version` 低于 `CurrentSchemaVersion`，从该版本出发，反复寻找
  `FromVersion == 当前版本` 的环节应用、把当前版本推进到 `ToVersion`，直至等于
  `CurrentSchemaVersion`；任一步找不到衔接环节，报 `schema_version` 错误（"缺少迁移环节"）。
- 文件 `schema_version` 高于 `CurrentSchemaVersion`：报错（代码版本落后于数据，不允许静默
  忽略未知的新字段结构）。
- 迁移函数只做结构转换（见 `MigrateDelegate`/`TableMigration` 类型注释），不做业务判断。

## Vec2 的 JSON 表示（判断记录）

04 未规定 `Vec2` 字段在 JSON 数据文件里的具体形状（只在接口记法层面提到 `Vec2` 类型）。
本模块拍板：`{"x": Number, "y": Number}`（对象，两个数值字段），与 `巡逻路径`
（`ai.patrol_path`，04 第 1.1 节"有序 Vec2 列表"）等未来表对齐时可复用同一约定；若后续某个
具体模块的落地实现选择了不同表示（如 `[x, y]` 数组），应在该模块自己的 `schema/*.md` 里
明确记录偏差，不应静默假设与本约定一致。
