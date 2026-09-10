using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.Common;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;

namespace Presentation.ViewBinding
{
    /// <summary>
    /// <see cref="IViewBinder"/> 的默认实现（见 01 L5 模块表 <c>view_binding</c> 行、03 第 5、9 节、
    /// 09 第 2 节）：订阅 <c>entity.created</c>/<c>entity.destroyed</c> 创建/销毁 View、维护
    /// "实体 id → View" 绑定表、在 <c>sim.tick_finished</c> 捕获位置快照供插值、把选定事件转发给
    /// 相关 View 的 <see cref="IView.OnEvent"/>。
    /// <para>
    /// PRES-180 根治（architecture/落地计划/audit-e070e3f-20260908/presentation/
    /// presentation-findings.md"存档抑制与掉落物 View 候选"）：<c>SaveSystem.Load</c> 把"逐段 Load +
    /// 失败回滚"整段包在 <c>IEventBus.SuppressDispatch</c> 作用域内（该方法判断记录"读档不是业务
    /// 事件"），作用域内经 <c>Enqueue</c>/<c>PublishImmediate</c> 提交的事件被直接丢弃，不会补发——
    /// 若某个 <c>IPersistable.Load</c> 在此期间调用 <c>IWorldSim.AddEntity</c>（如地面掉落物同图读档
    /// 恢复，见 <c>DroppedLootPersistable.Load</c>/<c>LootHost.RestoreDropped</c>）或立即从
    /// <c>IWorldSim</c> 移除实体，本类原本"只在构造期订阅 <c>entity.created</c>/<c>entity.destroyed</c>"
    /// 的做法就会漏掉这批变化：逻辑实体已经在 <c>WorldSim</c> 里创建/移除，但 View 绑定表没有同步
    /// （"有逻辑实体、无绑定 View"或反过来"View 残留、逻辑实体已不存在"）。<c>SaveSystem.Load</c> 在
    /// 该抑制作用域<b>外</b>正常派发 <c>SaveLoadedEvent</c>（<c>save.loaded</c>，"本次读档完成了"不是
    /// 重放，理应正常送达），本类型额外订阅它并调用 <see cref="OnSaveLoaded"/> 做一次以
    /// <see cref="ISimSnapshot"/>（<see cref="ISimSnapshot.GetAllEntityIds"/>/
    /// <see cref="ISimSnapshot.Exists"/>）当前状态为准的全量对账：缺 View 的按与
    /// <see cref="OnEntityCreated"/> 完全一致的规则补建（复用同一方法，跳过 AreaTrigger、记录未映射
    /// 分类、幂等去重同一套逻辑），已绑定但对应实体已不存在的按 <see cref="OnEntityDestroyed"/> 补销毁
    /// ——两个方向合起来同时覆盖"entity.created 被丢弃"与"entity.destroyed 被丢弃"两类残留，不需要
    /// 区分具体是哪个持久化段触发的。对账在收到 <c>save.loaded</c> 时同步执行一次即完成，不依赖后续
    /// 任何一次 <c>sim.tick_finished</c> 补发；两个循环内部都先查 <c>_views</c> 再决定是否创建/销毁，
    /// 重复收到 <c>save.loaded</c>（如迁移紧接读档两次派发相关事件）不会重复建 View。
    /// </para>
    /// <para>
    /// 插值：<see cref="SyncAll"/>/<see cref="GetInterpolatedPosition"/> 用
    /// <c>pos = prev + (curr - prev) × alpha</c>（见 03 第 3.1 节）；<c>prev</c>/<c>curr</c> 两份
    /// 快照只在每次 <c>sim.tick_finished</c> 时更新一次（<see cref="OnTickFinished"/>），
    /// <see cref="ISimSnapshot"/> 之间的其它任意时刻读到的都是同一个 <c>curr</c>（WorldSim 只在
    /// <c>Tick</c> 内部修改实体状态，两次 tick 之间状态稳定），因此不需要在每次 <see cref="SyncAll"/>
    /// 调用时重新查询 <see cref="ISimSnapshot"/> 来"追新"。
    /// </para>
    /// <para>
    /// 转发规则：<see cref="ViewBinderOptions.ForwardedEventKeys"/> 里的每个事件 key，收到后按
    /// <see cref="IExprReadableEvent.TryGetField"/> 依次尝试 <c>unitId</c>/<c>targetId</c>/
    /// <c>sourceId</c>/<c>entityId</c>/<c>casterId</c>/<c>gobjInstanceId</c> 六个候选字段名：
    /// 命中且该 id 对应一个已绑定 View 时，把原始事件转发给该 View 的 <see cref="IView.OnEvent"/>；
    /// 一个事件可以同时携带多个候选字段（如 <c>combat.damage_dealt</c> 同时有 <c>sourceId</c> 与
    /// <c>targetId</c>），此时对应的每个已绑定 View 都会各收到一次（例如攻击者与目标各自触发不同的
    /// 反馈表现），同一 View 不会因为多个字段解析出同一个 id 而重复收到两次（用 <see cref="HashSet{T}"/>
    /// 去重）。
    /// </para>
    /// <para>
    /// 判断记录：<see cref="SyncAll"/>/<see cref="Count"/>/<see cref="TryGetView"/> 三个成员未出现在
    /// 03 第 9 节给出的 <c>ViewBinder</c> 接口签名（只有 <c>onEntityCreated</c>/<c>onEntityDestroyed</c>/
    /// <c>getInterpolatedPosition</c> 三个方法）——本类型把接口 <see cref="IViewBinder"/> 严格对齐
    /// 文档给出的最小契约，三个补充成员只作为具体类 <see cref="ViewBinder"/> 的公开成员（供主循环
    /// 组装代码持有具体类型调用），不污染文档定义的最小接口面。
    /// </para>
    /// </summary>
    public sealed class ViewBinder : IViewBinder, IDisposable, IAnchorQuery
    {
        private static readonly string[] CandidateEntityFields =
        {
            "unitId", "targetId", "sourceId", "entityId", "casterId", "gobjInstanceId"
        };

        private readonly IViewFactory _factory;
        private readonly ISimSnapshot _snapshot;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly ViewBinderOptions _options;

        /// <summary>缺口 6（<see cref="IAnchorQuery"/>）：镜像判定复用 <see cref="RenderConventionHost"/>
        /// 同一套逻辑，不重复实现（见 <see cref="IAnchorQuery"/> 类型注释判断记录）。</summary>
        private readonly IRenderConventionHost _renderConvention;
        private readonly IPresentationDiagnostics _diagnostics;

        /// <summary>P2-08 根治新增：可选的装备外观来源，供 <see cref="OnSaveLoaded"/> 对"同图内继续
        /// 存活的既有 View"做装备外观对账（见该方法判断记录）。默认 null——未装配时
        /// <see cref="OnSaveLoaded"/> 的对账行为与改动前完全一致，只做 View 创建/销毁两件事，不触碰
        /// 任何装备外观。</summary>
        private readonly EquipmentVisualSource? _equipmentVisualSource;

        private readonly Dictionary<Id, IView> _views = new Dictionary<Id, IView>();
        private readonly Dictionary<Id, Id> _displayIds = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, Vec2> _prevPositions = new Dictionary<Id, Vec2>();
        private readonly Dictionary<Id, Vec2> _currPositions = new Dictionary<Id, Vec2>();

        /// <summary>缺口 5（退订）：构造期建立的全部事件订阅句柄，供 <see cref="Dispose"/> 统一退订
        /// （惯例同 <c>Presentation.FeedbackBinder.Core.FeedbackBinder</c>）。</summary>
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private bool _disposed;

        /// <summary>创建但映射不出已知 <see cref="ViewKind"/> 的实体 <c>Kind</c> 字符串（诊断用，
        /// 见 <see cref="Common.EntityKindMapping"/> 契约缺口说明），供测试/日志查看，不参与任何
        /// 业务判断。</summary>
        public IReadOnlyList<string> UnmappedEntityKinds => _unmappedEntityKinds;
        private readonly List<string> _unmappedEntityKinds = new List<string>();

        public ViewBinder(
            IEventBus bus,
            IViewFactory factory,
            ISimSnapshot snapshot,
            IDisplayInfoRegistry displayInfo,
            ViewBinderOptions? options = null,
            IRenderConventionHost? renderConvention = null,
            IPresentationDiagnostics? diagnostics = null,
            EquipmentVisualSource? equipmentVisualSource = null)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _options = options ?? new ViewBinderOptions();
            _renderConvention = renderConvention ?? new RenderConventionHost();
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _equipmentVisualSource = equipmentVisualSource;

            _subscriptions.Add(bus.Subscribe<EntityCreatedEvent>(SimEventKeys.EntityCreated, e => OnEntityCreated(e.EntityId, e.Kind, e.DisplayId)));
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, e => OnEntityDestroyed(e.EntityId)));
            _subscriptions.Add(bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => OnTickFinished()));
            _subscriptions.Add(bus.Subscribe(SaveEventKeys.SaveLoaded, _ => OnSaveLoaded()));

            for (var i = 0; i < _options.ForwardedEventKeys.Count; i++)
            {
                _subscriptions.Add(bus.Subscribe(_options.ForwardedEventKeys[i], OnForwardableEvent));
            }
        }

        /// <summary>
        /// ABI/API 兼容 façade（外部审计 audit-76d16a5-20260910 PJ114-01/A2 附加发现——通用表面差异
        /// 工具 toolchain/abi_surface 对 1.12.0/1.13.0 基线跑当前工作树时另外找到的一处同类破坏，
        /// 不在原 PJ114-01 报告文字范围内，但同一根因、同一套修复手法）：1.12.0/1.13.0 的唯一构造
        /// 函数物理 IL 签名是七个参数，没有这里最后新增的 <see cref="EquipmentVisualSource"/> 参数。
        /// C# 的可选参数是编译期特性——上面主构造函数虽然 <c>equipmentVisualSource</c> 带默认值
        /// <c>null</c>，但物理 IL 签名仍是完整的八个参数；1.12.0/1.13.0 编译产物里对七参数构造函数
        /// 的调用（省略这个新增可选实参时，编译器把当时能看到的默认值固化进调用点 IL），在只替换
        /// 正式 DLL、不重新编译的情况下，找不到匹配的物理方法而 <c>MissingMethodException</c>——与
        /// <c>core/rules/skill/core/SkillHost.cs</c> 的十七/十八参数构造同一根因，见该处判断记录里
        /// 引用的复现命令与结论，这里不重复贴一遍。本重载补回物理七参数签名、转发到主构造函数，
        /// <c>equipmentVisualSource</c> 固定传 <c>null</c>——旧调用方不会得到装备外观联动，其余行为
        /// 与 1.12.0/1.13.0 完全一致。
        /// <para>
        /// 判断记录（不是可选参数，理由与 SkillHost 同一重载决议原理）：本重载的七个参数全部不带
        /// 默认值——若也写成可选参数会与主构造函数在"只传 4～7 个参数"的调用点产生重载二义性。全部
        /// 七个参数都必填后，C# 重载决议规则保证恰好传 7 个参数时精确匹配本重载，不需要为
        /// <c>equipmentVisualSource</c> 代入默认值；传更少参数的调用只能匹配主构造函数。
        /// </para>
        /// </summary>
        [Obsolete("1.12.0/1.13.0 的七参数构造签名，仅为源码/二进制兼容保留；新代码请使用带 equipmentVisualSource 的八参数构造函数。")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public ViewBinder(
            IEventBus bus,
            IViewFactory factory,
            ISimSnapshot snapshot,
            IDisplayInfoRegistry displayInfo,
            ViewBinderOptions? options,
            IRenderConventionHost? renderConvention,
            IPresentationDiagnostics? diagnostics)
            : this(bus, factory, snapshot, displayInfo, options, renderConvention, diagnostics, equipmentVisualSource: null)
        {
        }

        /// <summary>退订构造期建立的全部事件订阅（缺口 5）。退订后不再创建/销毁 View、不再更新
        /// <see cref="OnTickFinished"/> 快照、不再转发事件；幂等，多次调用只生效一次。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }

        /// <summary>当前绑定的 View 数量。</summary>
        public int Count => _views.Count;

        /// <summary>按实体 id 查找已绑定的 View；未绑定返回 false。</summary>
        public bool TryGetView(Id entityId, out IView? view) => _views.TryGetValue(entityId, out view);

        public void OnEntityCreated(Id entityId, string kind, Id displayId)
        {
            if (_views.ContainsKey(entityId))
            {
                return;
            }

            // 判断记录（区域触发实体显式跳过，不查外形映射）：加固任务把 AreaTrigger 从"纯数据记录"
            // 改为真正的 Entity 子类（见 core/gameplay/area_trigger/contracts/AreaTriggerEntity.cs）
            // 后，它同样会经 WorldSim.AddEntity 触发 entity.created；但区域触发体在设计上永远没有
            // 可见外观（05_对象模型与世界.md 第 1.5/7 节：只负责"检测进入/离开+发事件"，不是一个会被
            // 渲染的对象），任何 CreateView 调用对它来说必然查不到 display.map 行，只会让
            // UnityViewFactory 徒增一次性"缺失 DisplayInfo"警告（噪音，不是需要修的内容缺口）。这里
            // 在 EntityKindMapping.TryMap 之前显式跳过：不创建 View、不记入 _unmappedEntityKinds
            // （那份列表是给"确有渲染需求但内容漏配"的场景用的诊断，本情形不属于该类），不抛异常。
            if (string.Equals(kind, EntityKinds.AreaTrigger, StringComparison.Ordinal))
            {
                return;
            }

            if (!EntityKindMapping.TryMap(kind, out var viewKind))
            {
                _unmappedEntityKinds.Add(kind);
                return;
            }

            var view = _factory.CreateView(viewKind, displayId, entityId);
            view.Bind(entityId);

            _views[entityId] = view;
            _displayIds[entityId] = displayId;

            // 初始化 prev = curr = 当前位置，避免第一帧从 (0,0) 插值出跳变（见类型注释）。
            if (_snapshot.Exists(entityId))
            {
                var pos = _snapshot.GetPosition(entityId);
                _prevPositions[entityId] = pos;
                _currPositions[entityId] = pos;
            }
        }

        public void OnEntityDestroyed(Id entityId)
        {
            if (_views.TryGetValue(entityId, out var view))
            {
                view.Destroy();
                _views.Remove(entityId);
            }

            _displayIds.Remove(entityId);
            _prevPositions.Remove(entityId);
            _currPositions.Remove(entityId);
        }

        /// <summary>PRES-180 根治：<c>save.loaded</c> 触发的全量对账（见类型注释）。分两个独立方向，
        /// 顺序无关紧要（互不依赖对方结果）：先销毁绑定表里指向"已不在 <see cref="ISimSnapshot"/> 里"
        /// 的陈旧 View（覆盖 <c>entity.destroyed</c> 被丢弃的情形），再为"<see cref="ISimSnapshot"/> 里
        /// 存在、但绑定表里还没有"的实体补建 View（覆盖 <c>entity.created</c> 被丢弃的情形）。
        /// <para>
        /// 安全退化判断记录（PRES-180 版本判据根治，2026-09-09）：<see cref="ISimSnapshot.GetAllEntityIds"/>/
        /// <see cref="ISimSnapshot.GetRawKind"/> 改为默认接口方法后（默认分别返回空集合/<c>null</c>，
        /// 见该接口类型注释），只实现了 <see cref="ISimSnapshot"/> 旧成员集合（<c>WorldSimSnapshot</c>
        /// 之外的第三方实现，未覆盖这两个新成员）的自定义快照走到本方法时选择的退化方式是——<b>只做
        /// "销毁已不存在实体的 View"这一半，跳过"按存活实体补建 View"这一半</b>：下面第一个循环
        /// （销毁）只依赖 <see cref="ISimSnapshot.Exists"/>，这是本接口自始至终的强制成员，不受本次
        /// 改动影响，继续正确销毁陈旧 View；第二个循环（补建）遍历 <c>GetAllEntityIds()</c> 的返回值，
        /// 默认实现返回空集合时该循环天然零次迭代，不需要额外的"是否为默认实现"判断分支，也不会因为
        /// <c>null</c> 元素或占位值引入误判。未选择"整体跳过并记一次诊断"是因为：本方法无法区分
        /// "空集合是默认实现的兜底值"还是"世界读档后确实没有存活实体"（两者返回值完全相同），若每次
        /// 空集合都记诊断，合法的空世界读档也会被误记为异常情形，噪音大于信号；而"只做销毁"这一半
        /// 不依赖该区分——无论空集合的成因是什么，销毁分支都应该照常执行，补建分支都没有信息可用，
        /// 二者组合起来是当前信息下能做到的最安全默认行为。回归见
        /// <c>presentation/view_binding/tests/ViewBinderTests.cs</c>
        /// <c>OnSaveLoaded_LegacySimSnapshotOnlyImplementsOldMembers_DegradesSafely</c> 用例（自定义
        /// <see cref="ISimSnapshot"/> 只实现旧接口成员，验证仍可编译、<c>OnSaveLoaded</c> 不抛异常、
        /// 销毁分支正常生效、补建分支保持空操作）。
        /// </para>
        /// <para>
        /// P2-08 根治（第十四轮审核"同图已有 View 读档外观"）：前两个循环只处理"View 有没有"（销毁/
        /// 补建），完全不覆盖"同一个实体读档前后 View 身份不变、但装备内容变了"这第三种情形——例如
        /// 同图从有装备存档 A 读到空装备存档 B，实体仍在、View 仍是同一个对象，第一个循环
        /// （<see cref="ISimSnapshot.Exists"/> 为真）不会销毁它，第二个循环
        /// （<c>_views.ContainsKey</c> 为真）直接 <c>continue</c> 跳过——旧外观因此原地残留，见
        /// <see cref="IEquipmentVisualResettable"/> 类型注释。第三段循环补上这个缺口：<c>existingIds</c>
        /// 在最前面（两个循环开始之前）捕获"本次 <c>save.loaded</c> 触发之前已绑定的实体 id 集合"，
        /// 处理完前两个循环后，仍留在 <c>existingIds</c>∩<c>_views</c> 里的就是"身份未变、被完整
        /// 保留"的那部分 View（被第一个循环销毁的已经从 <c>_views</c> 里移除，天然被排除；第二个循环
        /// 新建的从未进入过 <c>existingIds</c>，同样天然被排除——它们已经在
        /// <c>IViewFactory.CreateView</c>/<c>UnityViewFactory.ReplayEquippedVisuals</c> 里按当前装备
        /// 重放过一次，不需要在这里重复处理）。<see cref="_equipmentVisualSource"/> 未装配（默认
        /// null）时整段跳过，行为与改动前完全一致；已装配时对每个保留下来的 View 做类型测试
        /// （<c>is IEquipmentVisualResettable</c>），未实现该接口的 View（无装备外观概念，如
        /// gobj/掉落物）静默跳过。<c>EquipmentVisualSource.ReplayEquippedForUnit</c>
        /// 既刷新该类型内部"实例 id -&gt; 外观定义"表，又直接返回本次真实装备快照，二者用同一次调用
        /// 拿到，保证 <see cref="IEquipmentVisualResettable.ResetEquipmentVisuals"/> 查表时表内容已是
        /// 最新——全过程只调用 View 自身方法，不经过 <see cref="IEventBus"/>，不合成/补发任何
        /// <c>item.equipped</c>/<c>item.unequipped</c> 全局业务事件，重复收到 <c>save.loaded</c>
        /// （同类型注释"幂等去重"）用同一份快照重复对账，结果不变。
        /// </para>
        /// </summary>
        private void OnSaveLoaded()
        {
            var existingIds = _equipmentVisualSource != null ? new List<Id>(_views.Keys) : null;

            foreach (var entityId in new List<Id>(_views.Keys))
            {
                if (!_snapshot.Exists(entityId))
                {
                    OnEntityDestroyed(entityId);
                }
            }

            var liveIds = _snapshot.GetAllEntityIds();
            for (var i = 0; i < liveIds.Count; i++)
            {
                var entityId = liveIds[i];
                if (_views.ContainsKey(entityId))
                {
                    continue;
                }

                // 见 OnEntityCreated 判断记录：kind/displayId 复用同一份原始字符串/Id，与
                // entity.created 正常路径完全一致的映射、AreaTrigger 跳过、未映射诊断规则。
                var rawKind = _snapshot.GetRawKind(entityId);
                var displayId = _snapshot.GetDisplayId(entityId);
                if (rawKind == null || displayId == null)
                {
                    // 防御性分支：GetAllEntityIds 与随后两次按 id 查询之间实体被移除（本类型当前
                    // 没有任何路径会在同一次 OnSaveLoaded 内部触发这种移除，纯属未来演进的安全网，
                    // 不代表已知可复现场景）。
                    continue;
                }

                OnEntityCreated(entityId, rawKind, displayId.Value);
            }

            if (existingIds == null)
            {
                return;
            }

            for (var i = 0; i < existingIds.Count; i++)
            {
                var entityId = existingIds[i];
                if (!_views.TryGetValue(entityId, out var view) || !(view is IEquipmentVisualResettable resettable))
                {
                    continue;
                }

                var equipped = _equipmentVisualSource!.ReplayEquippedForUnit(entityId);
                resettable.ResetEquipmentVisuals(equipped);
            }
        }

        public Vec2 GetInterpolatedPosition(Id entityId, double alpha)
        {
            if (!_currPositions.TryGetValue(entityId, out var curr))
            {
                throw new InvalidOperationException($"实体 \"{entityId}\" 未绑定 View，无法插值位置");
            }

            var prev = _prevPositions.TryGetValue(entityId, out var p) ? p : curr;
            return prev + (curr - prev) * alpha;
        }

        /// <summary>驱动全部已绑定 View 的 <see cref="IView.SyncPose"/>（见 09 第 2 节
        /// "syncPose 由主循环在插值阶段调用"）。<paramref name="alpha"/> 的来源：09 全文未定义
        /// "表现时钟"这一具体契约名，只泛泛提到"插值仍用于固定步长模拟与渲染帧率解耦，见 03"；
        /// <c>Core.Foundation.SimLoop.ISimClockHost.Advance</c> 才是真正产出 alpha 的地方（返回值即
        /// 插值系数，见其接口注释），但 <c>Core.Gameplay.Assembly.GameplayAssembly.Advance</c> 当前
        /// 丢弃了这个返回值、也不对外暴露 <c>ISimClockHost</c> 实例——本模块因此不新造一个"包一层
        /// alpha"的悬空契约（09 勘误：此前的 <c>Presentation.Common.IPresentationClock</c> 恰是这样
        /// 一个零实现、零调用点的契约，已删除），<paramref name="alpha"/> 改由调用方（引擎适配层/
        /// W3b）自行从 <c>IClock.RequestFixedStep</c> 固定步驱动或改造后的
        /// <c>GameplayAssembly.Advance</c> 换算得到并直接传入，真正把"alpha 从哪来"接通属于 W2/W3b
        /// 的后续工作，见 presentation/common/README.md"契约缺口"。</summary>
        public void SyncAll(double alpha)
        {
            foreach (var pair in _views)
            {
                var entityId = pair.Key;
                var view = pair.Value;

                var pos = GetInterpolatedPosition(entityId, alpha);
                var facing = _snapshot.Exists(entityId) ? _snapshot.GetFacing(entityId) : 0.0;
                var height = _snapshot.Exists(entityId) ? _snapshot.GetHeight(entityId) : 0.0;
                var direction = ResolveDirection(facing, entityId);

                view.SyncPose(pos, direction, height);
            }
        }

        /// <summary>方向经 <see cref="Direction.FromQuantized"/>（sprite 型，档位来自该 View 的
        /// <c>DisplayInfo.Sprite.DirectionCount</c>）或 <see cref="Direction.Continuous"/>（model 型，
        /// 见 09 第 3.2 节）；查不到 <c>DisplayInfo</c>（未登记/尚未 Reload）时按
        /// <see cref="ViewBinderOptions.DefaultDirectionCount"/> 兜底量化，不让整条 SyncAll 因为
        /// 单个缺失的 DisplayInfo 行而抛异常。</summary>
        private Direction ResolveDirection(double facingRadians, Id entityId)
        {
            if (_displayIds.TryGetValue(entityId, out var displayId))
            {
                var info = _displayInfo.Lookup(displayId);
                if (info != null)
                {
                    if (info.Kind == DisplayKind.Model)
                    {
                        return Direction.Continuous(facingRadians);
                    }

                    if (info.Kind == DisplayKind.Sprite && info.Sprite != null)
                    {
                        return Direction.FromQuantized(facingRadians, info.Sprite.DirectionCount);
                    }
                }
            }

            return Direction.FromQuantized(facingRadians, _options.DefaultDirectionCount);
        }

        /// <summary>缺口 6：<see cref="IAnchorQuery"/> 实现，见接口注释判断记录。</summary>
        public Vec2? GetAnchorWorldPosition(Id entityId, Id anchorId)
        {
            if (!_views.ContainsKey(entityId))
            {
                _diagnostics.Warn($"锚点查询：实体 \"{entityId}\" 未绑定 View");
                return null;
            }

            if (!_displayIds.TryGetValue(entityId, out var displayId))
            {
                _diagnostics.Warn($"锚点查询：实体 \"{entityId}\" 没有关联的 DisplayId");
                return null;
            }

            var info = _displayInfo.Lookup(displayId);
            if (info == null)
            {
                _diagnostics.Warn($"锚点查询：DisplayId \"{displayId}\" 查不到 DisplayInfo（未登记/尚未 Reload）");
                return null;
            }

            if (info.Kind != DisplayKind.Sprite || info.Sprite == null)
            {
                _diagnostics.Warn($"锚点查询：实体 \"{entityId}\" 是 model 型外形，锚点查询只服务 sprite 型（见 09 第 3.3.2 节挂点查询）");
                return null;
            }

            var spriteInfo = info.Sprite;
            if (!spriteInfo.AnchorPoints.TryGetValue(BareAnchorName(anchorId), out var anchorDef))
            {
                _diagnostics.Warn($"锚点查询：\"{displayId}\" 未登记锚点 \"{anchorId}\"");
                return null;
            }

            if (!_snapshot.Exists(entityId))
            {
                _diagnostics.Warn($"锚点查询：实体 \"{entityId}\" 在 ISimSnapshot 中不存在");
                return null;
            }

            var facing = _snapshot.GetFacing(entityId);
            var direction = Direction.FromQuantized(facing, spriteInfo.DirectionCount);
            var (slotId, flipX) = _renderConvention.ResolveDirectionSlot(direction, spriteInfo);

            // GP-PRES-07 收口：按当前方向槽位取 offset_by_direction 覆盖值，缺省回退 Offset（见
            // AnchorDef.ResolveOffset 判断记录）；镜像（flipX）仍然是独立于方向覆盖的第二步—— 09
            // 第 3.2 节镜像规则表本身就是"用已有方向的美术资源经水平翻转顶替另一方向"，覆盖值/默认
            // 值一旦选定，镜像处理方式不变。
            var baseOffset = anchorDef.ResolveOffset(slotId);
            var offset = flipX ? new Vec2(-baseOffset.X, baseOffset.Y) : baseOffset;
            var entityPos = _snapshot.GetPosition(entityId);
            return entityPos + offset;
        }

        /// <summary>把调用方传入的锚点 <see cref="Id"/>（如 <c>"anchor.hand_main"</c>，见
        /// <c>VfxAttach.Anchor</c> 调用惯例——<see cref="Id"/> 要求"至少一个点分段"，
        /// <c>display.map.anchor_points</c> 的裸键名（如 <c>"hand_main"</c>）本身不满足该格式）还原成
        /// <c>anchor_points</c> 字典使用的裸键名：取最后一个点分段，同
        /// <see cref="Presentation.Common.DirectionSlots.StripPrefix"/>/
        /// <c>SpriteViewBase.LayerNameFromSlotId</c> 同一惯例。</summary>
        private static string BareAnchorName(Id anchorId)
        {
            var value = anchorId.Value;
            var dotIndex = value.LastIndexOf('.');
            return dotIndex < 0 ? value : value.Substring(dotIndex + 1);
        }

        private void OnTickFinished()
        {
            foreach (var entityId in new List<Id>(_views.Keys))
            {
                if (!_snapshot.Exists(entityId))
                {
                    continue;
                }

                var curr = _snapshot.GetPosition(entityId);
                var previousCurr = _currPositions.TryGetValue(entityId, out var storedCurr) ? storedCurr : curr;

                _prevPositions[entityId] = previousCurr;
                _currPositions[entityId] = curr;
            }
        }

        private void OnForwardableEvent(IEvent evt)
        {
            if (!(evt is IExprReadableEvent readable))
            {
                return;
            }

            HashSet<Id>? matchedEntityIds = null;

            for (var i = 0; i < CandidateEntityFields.Length; i++)
            {
                if (!readable.TryGetField(CandidateEntityFields[i], out var value) || value.Kind != ExprValueKind.Id)
                {
                    continue;
                }

                var id = value.AsId;
                if (!_views.ContainsKey(id))
                {
                    continue;
                }

                (matchedEntityIds ??= new HashSet<Id>()).Add(id);
            }

            if (matchedEntityIds == null)
            {
                return;
            }

            foreach (var id in matchedEntityIds)
            {
                _views[id].OnEvent(evt);
            }
        }
    }
}
