using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 资源种类：图片、音频、字体、数据表、场景、导航网格、特效、模型（见 ADR-0016 决策 5、
    /// ADR-0017 决策 a）。
    /// Scene 对应 world.map 的 scene_ref 字段（05_对象模型与世界.md 第 4.1 节），加载后交给
    /// 场景路由使用；NavMesh 对应 world.map 的 nav_ref 字段（同节），加载后交给 INavigation2D
    /// 使用；Effect 对应 vfx.def 的 resource_ref 字段（09_表现层.md 第 5.1 节），加载后交给
    /// IRenderer2D/IRenderer3D 使用，使 emitParticle 的 effectId 能解析到具体特效资产；
    /// Model 对应 display.map（kind: model）的 model_ref 字段、display.equip_visual 的
    /// model_ref 字段，以及（12 §5 勘误，见 02 第 1.7/1.12 节与 ADR-0017 修订记录）
    /// display.equip_visual 的 mesh_ref 字段（04_数据与内容管线.md 第 7.1、7.1.2 节），加载后
    /// 交给 IRenderer3D 使用，使 createModelInstance/attachToSocket/setSlotMesh 的资源引用能
    /// 解析到具体模型、骨骼动画资产或可提取的槽位网格（见 ADR-0017 决策 a：加载责任仍沿用
    /// ADR-0016 决策 6——谁首次引用该资源 id 谁负责调用 loadAsync，IRenderer3D 只消费已加载
    /// 完成的资源；mesh_ref 的具体资产形态与提取规则见 02 第 1.7 节"mesh_ref 资源合同"）。
    /// AnimationClip（12 §5 勘误新增，见 ADR-0017 修订记录）对应 display.anim_set.clips[*].
    /// resource_ref（04 第 7.1.1 节）：model 型剪辑资产本身，加载后交给 IRenderer3D 播放，
    /// 或交给消费方读取/合并剪辑内建的关键帧事件（见 09 表现层 model 动画关键帧登记条款）——
    /// 与 Model 种类同一处境（运行期没有公开 API 能把裸字节反序列化成可用的动画资产，只能消费
    /// 已被引擎资产管线预先导入好的资源），具体解析路径由引擎适配层实现决定。
    /// </summary>
    public enum ResourceKind
    {
        Image,
        Audio,
        Font,
        DataTable,
        Scene,
        NavMesh,
        Effect,
        Model,
        AnimationClip
    }

    /// <summary>加载完成或失败时触发一次，success 为 false 表示加载失败。</summary>
    public delegate void LoadCallback(Id resourceId, bool success);

    /// <summary>
    /// 图片、音频、字体、数据表、场景、导航网格、特效、异步加载（见 02_引擎适配层.md 第 1.7
    /// 节）。必需接口。加载过程不得阻塞主线程；数据表资源经此加载后交给 L0 的 DataRegistry
    /// 解析，图片/音频/字体资源加载后交给 IRenderer2D/IAudio/IUISurface 使用，scene/nav_mesh
    /// 资源加载后分别交给场景路由/INavigation2D 使用，effect 资源加载后交给
    /// IRenderer2D/IRenderer3D 使用（见 ADR-0016 决策 5）。
    /// 线程约定：回调可能在非主线程触发，调用方须自行处理回到主线程的切换，或由适配层实现
    /// 保证回调总在主线程的下一次 IClock.onFrame 之前排队执行（见 02 第 2 节第 2 条）。
    /// 资源首次加载的责任归属（见 ADR-0016 决策 6，写入 02 第 1.7 节）：谁首次引用一个资源 id
    /// （某个 View、某个播放器、某个界面面板），谁负责调用 LoadAsync；IRenderer2D、IAudio、
    /// IUISurface 的实现只消费已加载完成的资源，遇到未加载的资源 id 时使用实现内建的
    /// 可见/可听占位（例如洋红色方块、静音）并记录一条诊断信息，不得隐式触发该资源的加载，
    /// 也不得抛出异常或使调用方的调用崩溃。
    /// </summary>
    public interface IResourceLoader
    {
        void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback);

        bool IsLoaded(Id resourceId);

        /// <summary>用于加载画面展示百分比，允许粗粒度估算，取值范围 [0, 1]。</summary>
        double GetLoadProgress(Id resourceId);

        void Unload(Id resourceId);
    }
}
