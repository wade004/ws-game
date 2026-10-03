# ws-game 框架手册

本站点由框架仓库自动生成，随版本快照一起分发（`dist/<版本>/manual/`），分三部分：

| 部分 | 内容 | 来源 |
|---|---|---|
| [API 参考](api/index.md) | 按命名空间 → 类型 → 成员逐条列出的公开接口，含源码里 `///` 注释写的用途、参数、返回值 | 各类库源码的 XML 文档注释 |
| [模块说明](../../README.md) | 每个模块目录下的 `README.md`：模块职责、用法、判断记录 | 仓库内全部已入库的 `README.md` |
| [架构与决策](../../architecture/README.md) | 架构文档集（00～14）、选型、落地计划、手感设计、数值设计、全部 ADR | `architecture/` 下的 markdown |

## 范围与边界（设计决定）

- API 参考覆盖 `Core.sln` 里的类库：`Core.Foundation`、`Core.Numbers`、`Core.Rules`、`Core.Carriers`、
  `Core.Gameplay`、`Core.Sim`、`Presentation.Common`、`Adapters.Stub`，以及诊断转发工程
  `Adapters.Unity.DiagnosticsForwarding`（它按引用编译 Unity 适配层包里的三份源文件，所以只有这三份
  的类型出现在手册里）。
- **Unity 适配层 UPM 包**（`adapters/unity/Packages/com.gamefoundation.adapter.unity`）由 Unity 编译，
  不在 `Core.sln` 里，其余类型**不纳入** API 参考；该包的概念说明见 [模块说明](../../README.md)。
  这是定案而不是待补：抽取 API 需要引擎自带的程序集作编译引用（实测直接把包源码交给 docfx，会因找不到
  `UnityEngine` 等命名空间报 20 条编译错误、一页都生成不出），而手册要在任何装了 dotnet 的机器上可复现，
  不能绑定某台机器上的引擎安装路径；适配层的对外契约本来就由核心/表现层的接口承担，该包只是这些接口的引擎实现。
- 模块 README 与架构文档保持仓库里的相对路径，彼此之间的链接可点；指向源码文件（`.cs`）、
  数据文件、脚本等非 markdown 文件的链接在站点里是断的，请回仓库查看。
- 注释里的 `cref` 引用（指向私有成员、测试类型或重载组的）在页面上显示为纯文本，不可点击。

## 怎么本地重新生成与浏览

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs\manual\build.ps1
dotnet docfx serve docs\manual\_site
```

站点使用了搜索与目录的脚本，直接用浏览器打开 `.html` 文件（`file://`）会缺功能，请用上面的
`docfx serve` 或任意静态文件服务器打开。
