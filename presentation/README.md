# L5 表现层 Presentation

职责：提供与具体引擎无关的表现层抽象与规则引擎，实际由以下子模块构成（各自职责与依赖详见其自身
README）：`common`（View 绑定协议与铁律，跨子模块共享契约）、`render`（混合渲染约定、纸娃娃与程序
动画）、`camera`（镜头）、`vfx_sfx`（VFX/SFX 播放）、`feedback_binder`（反馈绑定规则引擎）、`ui`
（UI 框架与数据绑定）、`shell`（游戏外壳）、`view_binding`（View 生命周期绑定）、`assembly`（表现层
组装配置）。

依赖：各子模块经 `common` 统一依赖 Core.Gameplay（L4，含向下传递引用 Core.Carriers/Core.Rules/
Core.Numbers/Core.Foundation）；presentation/ 本身零引擎依赖，只依赖引擎适配层（L-1）声明的抽象
接口（`IRenderer2D`/`IRenderer3D`/`ICamera`/`IAudio` 等），具体渲染/音频/UI 承载实现由引擎适配层
提供。

> 勘误（2026-09-06）：本文件此前标题误写为「Presentation.Common」、依赖描述沿用的是 `common` 单个
> 子模块的口径，未覆盖 presentation/ 实际已有的 9 个子模块，本次修正为描述整个 L5 层。
