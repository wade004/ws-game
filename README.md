# ws-game

本仓库是「游戏技术基础架构」框架仓库：技术无关、游戏无关，面向后续所有游戏。

`architecture/` 是已定稿的架构文档集，是本仓库唯一的规范来源；入口见 `architecture/README.md`。

## 顶层目录规划

```
architecture/          架构文档集（已定稿），本仓库唯一的规范来源
core/                  L0~L4 纯逻辑类库，零引擎依赖
  foundation/ numbers/ rules/ carriers/ gameplay/
presentation/          L5 表现层的引擎无关部分
adapters/
  stub/                桩适配层，专供测试
  <engine_name>/       每种引擎一套适配层实现
games/_template/       游戏层模板（真实游戏放各自仓库）
data/_sample/          示例数据表（真实游戏数据放各自仓库）
assets/_placeholder/   通用占位资产包
toolchain/             校验、构建、资产导入等跨游戏工具
build/                 构建产物，不入库
```

任何具体游戏的代号、数据与游戏层代码都不放在本仓库。
