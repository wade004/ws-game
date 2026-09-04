# 游戏层模板目录

本目录是游戏层的组装模板占位，将来用于放置"如何在具体游戏里组装框架各层"的示例（asmdef 骨架、策略配置样例、钩子注册样例等），供新游戏起步时参考。

- 真实游戏的游戏层代码与配置放各自游戏仓库，不放在本框架仓库；本框架仓库不出现任何具体游戏代号。
- 本阶段（T0-7/T0-8）只建立目录约定，具体模板内容随落地计划阶段 4（Unity 适配层 + 表现层 + UI 套件）与阶段 6（参考实现游戏竖切）逐步补充。

## 如何复制为新游戏

本目录本身是一个本地 UPM 包（`package.json` 的 `name` 为 `com.gamefoundation.game-template`），新游戏以复制该目录为起点：

1. 把整个 `games/_template/` 目录复制到新游戏工程能以相对路径引用到的位置（例如新游戏仓库内的 `packages/` 目录，或与新游戏 Unity 工程同级）。
2. 编辑复制出来的 `package.json`：把 `name` 改成新游戏专属的包名（例如 `com.<studio>.game-<name>`），按需修改 `displayName`、`description`。
3. 编辑复制出来的 `Runtime/Game.Template.asmdef`：把 `name`（及需要的话 `rootNamespace`）从 `Game.Template` 改成 `Game.<Game>`（`<Game>` 替换为新游戏的代号），文件名同步改名；`references` 中对 `Adapter.Unity` 的引用保留不变。
4. 在新游戏的 Unity 工程 `Packages/manifest.json` 中用 `file:` 相对路径引用两个包：本框架的引擎适配层包 `com.gamefoundation.adapter.unity`（`adapters/unity/Packages/com.gamefoundation.adapter.unity/`）与复制出来的游戏层包，例如：
   ```json
   {
     "dependencies": {
       "com.gamefoundation.adapter.unity": "file:../../ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity",
       "com.<studio>.game-<name>": "file:../../ws-game/games/_template"
     }
   }
   ```
   （具体相对路径层级以新游戏工程实际目录结构为准。）
5. 依赖方向单向：新游戏的程序集引用 `Adapter.Unity`，`Adapter.Unity` 不反向引用任何游戏层程序集；`Adapter.Unity` 也不出现任何具体游戏代号。
