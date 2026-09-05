using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// 应用状态机配置：主状态允许转移集合（<c>(from, to)</c> 对）与 InWorld 子状态允许转移
    /// 集合（对应数据表 <c>found.game_state</c>，见 schema/found.game_state.md）。可由游戏层
    /// 扩展（<see cref="AllowTransition"/>、<see cref="AllowSubTransition"/>、
    /// <see cref="AddCustomSubState"/>，见 01_分层与依赖.md L0 模块表 <c>app_lifecycle</c> 行
    /// 策略配置项"各状态的允许转移表可由游戏层扩展自定义子状态"）。
    /// <para>
    /// 判断记录：任务书把 <c>AllowTransition</c>/<c>AllowSubTransition</c> 列在"配置可由
    /// 游戏层扩展"一节，语义是"扩展点"（即写操作，向配置里追加一条允许的转移），因此本类型
    /// 把它们实现为返回 <see cref="AppStateMachineConfig"/> 自身的链式修改方法；查询是否允许
    /// 某转移则用另外命名的 <see cref="IsTransitionAllowed"/>/<see cref="IsSubTransitionAllowed"/>
    /// ——若查询方法与写入方法同名同参数，<c>AppStateHost</c> 内部做合法性检查时会有把"检查"
    /// 误写成"顺手改配置"的风险，拆成两个不同名字更安全，且不改变任务书给出的公开方法名字。
    /// </para>
    /// </summary>
    public sealed class AppStateMachineConfig
    {
        private readonly HashSet<(AppState From, AppState To)> _mainTransitions = new HashSet<(AppState, AppState)>();
        private readonly HashSet<(string From, string To)> _subTransitions = new HashSet<(string, string)>();
        private readonly HashSet<string> _customSubStates = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>
        /// 03_运行时骨架.md 第 2 节状态机表逐行对应的默认配置：
        /// 主状态 Boot→MainMenu；MainMenu→Loading；Loading→InWorld；
        /// InWorld→Pause、InWorld→MainMenu、InWorld→Loading；Pause→InWorld、Pause→MainMenu
        /// （"MainMenu→退出应用"不是状态转移，见 <see cref="IAppStateHost.RequestExit"/>）。
        /// 另加 Loading→MainMenu（T1-9 收尾修正，见下方判断记录 3）。
        /// 子状态 Explore→Combat、Explore→Dialog、Explore→MenuOverlay、Explore→Cutscene；
        /// Combat→Explore、Dialog→Explore、Cutscene→Explore；另加 Combat→MenuOverlay
        /// （见下方判断记录，覆盖"MenuOverlay 叠加在 Combat 上"这一 03 原文举例场景）。
        /// <para>
        /// 判断记录 3（T1-9 收尾修正）：03 第 2 节状态机表 <c>Loading</c> 一行"允许转移到"
        /// 只写了 <c>InWorld</c>（加载完成的正常路径），未覆盖"加载失败"这一异常路径——
        /// 但 <c>Loading</c> 由 <c>SceneRouter</c> 驱动加载数据表与资源引用（见 03 第 6 节步骤 3、
        /// 04_数据与内容管线.md 校验器"运行时不做静默降级"的原则），加载失败时若无法转移到
        /// 任何状态，应用会卡在 <c>Loading</c> 且没有出口。补一条 <c>Loading→MainMenu</c>
        /// 默认转移，供加载失败时回退到主菜单（03 第 2 节表本身随本次修正同步勘误）。
        /// </para>
        /// </summary>
        public static AppStateMachineConfig Default()
        {
            var config = new AppStateMachineConfig();

            config.AllowTransition(AppState.Boot, AppState.MainMenu);
            config.AllowTransition(AppState.MainMenu, AppState.Loading);
            config.AllowTransition(AppState.Loading, AppState.InWorld);
            config.AllowTransition(AppState.Loading, AppState.MainMenu);
            config.AllowTransition(AppState.InWorld, AppState.Pause);
            config.AllowTransition(AppState.InWorld, AppState.MainMenu);
            config.AllowTransition(AppState.InWorld, AppState.Loading);
            config.AllowTransition(AppState.Pause, AppState.InWorld);
            config.AllowTransition(AppState.Pause, AppState.MainMenu);

            config.AllowSubTransition(SubStateId.Explore, SubStateId.Combat);
            config.AllowSubTransition(SubStateId.Explore, SubStateId.Dialog);
            config.AllowSubTransition(SubStateId.Explore, SubStateId.MenuOverlay);
            config.AllowSubTransition(SubStateId.Explore, SubStateId.Cutscene);
            config.AllowSubTransition(SubStateId.Combat, SubStateId.Explore);
            config.AllowSubTransition(SubStateId.Dialog, SubStateId.Explore);
            config.AllowSubTransition(SubStateId.Cutscene, SubStateId.Explore);
            // 判断记录：03 第 6 节"叠加规则"举例段落原文"叠加规则由游戏层通过状态栈机制
            // 配置...（例如 Combat 中打开 MenuOverlay）"、第 2 节 MenuOverlay 一行"进入条件：
            // 玩家触发'打开菜单'动作"（未限定只能从 Explore 触发）、"回落到触发前的子状态
            // （Explore 或 Combat）"三处合并读，MenuOverlay 明确可以叠加在 Combat 之上；
            // 若默认配置只放行 Explore→MenuOverlay，任务验收要求的
            // "Explore→Combat→(Push MenuOverlay)→Pop 回 Combat→Pop 回 Explore" 场景会在
            // Push 一步直接因未登记的子转移被拒绝。因此默认表额外放行 Combat→MenuOverlay；
            // Dialog/Cutscene→MenuOverlay 03 原文未举例提及，默认表不主动放开，游戏层需要时
            // 可自行 AllowSubTransition 扩展。
            config.AllowSubTransition(SubStateId.Combat, SubStateId.MenuOverlay);

            return config;
        }

        /// <summary>追加一条允许的主状态转移，返回自身以支持链式调用。</summary>
        public AppStateMachineConfig AllowTransition(AppState from, AppState to)
        {
            _mainTransitions.Add((from, to));
            return this;
        }

        /// <summary>查询某主状态转移是否允许。</summary>
        public bool IsTransitionAllowed(AppState from, AppState to) => _mainTransitions.Contains((from, to));

        /// <summary>追加一条允许的 InWorld 子状态转移，返回自身以支持链式调用。</summary>
        public AppStateMachineConfig AllowSubTransition(SubStateId from, SubStateId to)
        {
            _subTransitions.Add((from.Name, to.Name));
            return this;
        }

        /// <summary>查询某子状态转移是否允许。</summary>
        public bool IsSubTransitionAllowed(SubStateId from, SubStateId to) => _subTransitions.Contains((from.Name, to.Name));

        /// <summary>
        /// 登记一个自定义子状态名并返回对应的 <see cref="SubStateId"/>；本方法只登记"这是一个
        /// 已知的自定义子状态名"（供 <see cref="CustomSubStates"/> 枚举查看），不自动附加任何
        /// 转移规则——游戏层仍需调用 <see cref="AllowSubTransition"/> 把它接入转移图（通常至少
        /// 需要 <c>Explore → 自定义子状态</c> 与 <c>自定义子状态 → Explore</c> 两条）。
        /// </summary>
        public SubStateId AddCustomSubState(string name)
        {
            var id = new SubStateId(name);
            _customSubStates.Add(id.Name);
            return id;
        }

        /// <summary>全部经 <see cref="AddCustomSubState"/> 登记的自定义子状态名，只读。</summary>
        public IReadOnlyCollection<string> CustomSubStates => _customSubStates;

        /// <summary>
        /// 从一组转移定义批量构造配置（供数据注册表从 <c>found.game_state</c> 表加载后对接，
        /// 见 schema/found.game_state.md）。空配置起步，逐条按 <see cref="GameStateTransitionDefinition.Kind"/>
        /// 分派到 <see cref="AllowTransition"/>（<c>Main</c>）或 <see cref="AllowSubTransition"/>
        /// （<c>Sub</c>）——不是"在默认表基础上叠加"，调用方若需要 03 第 2 节默认表加自定义
        /// 扩展，应显式合并 <see cref="Default"/> 的定义行与自定义行后一起传入，或对
        /// <see cref="Default"/> 的返回值再调用 <see cref="AllowTransition"/>/<see cref="AllowSubTransition"/>。
        /// <c>kind: main</c> 行的 <c>from</c>/<c>to</c> 必须是 <see cref="AppState"/> 的合法枚举名，
        /// 否则抛 <see cref="ArgumentException"/>；<c>kind: sub</c> 行的 <c>from</c>/<c>to</c>
        /// 按 <see cref="SubStateId(string)"/> 构造（内置子状态名与自定义子状态名同等对待，
        /// 见 <see cref="SubStateId"/> 注释）。
        /// </summary>
        public static AppStateMachineConfig FromDefinitions(IEnumerable<GameStateTransitionDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var config = new AppStateMachineConfig();

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("转移定义列表不能包含 null 元素", nameof(definitions));
                }

                switch (definition.Kind)
                {
                    case GameStateTransitionKind.Main:
                        config.AllowTransition(ParseAppState(definition.From), ParseAppState(definition.To));
                        break;
                    case GameStateTransitionKind.Sub:
                        config.AllowSubTransition(new SubStateId(definition.From), new SubStateId(definition.To));
                        break;
                    default:
                        throw new ArgumentException($"未知的 GameStateTransitionKind：{definition.Kind}", nameof(definitions));
                }
            }

            return config;
        }

        /// <summary>
        /// 收边任务补齐的契约缺口 2（见 <c>schema/FoundGameStateSchema.cs</c>、
        /// <c>schema/found.game_state.md</c>"本模块不做什么"一节此前遗留的"从数据文件读取
        /// 并转换成配置是数据注册表的职责"——本方法就是那个对接点：从
        /// <paramref name="registry"/> 读取 <c>found.game_state</c> 表的全部行，逐行转换成
        /// <see cref="GameStateTransitionDefinition"/> 后交给 <see cref="FromDefinitions"/>。
        /// <para>
        /// 判断记录（缺表时退化为 <see cref="Default"/>，行为不变）：<paramref name="registry"/>
        /// 未注册/未加载 <c>found.game_state</c> 表时 <see cref="IDataRegistryView.GetAll"/>
        /// 返回空列表（见 <c>DataRegistry.GetAll</c>），本方法据此判定"表不存在"并回退到
        /// <see cref="Default"/>——不是"空表=空配置"，因为一个没有任何允许转移的配置会让
        /// <c>AppStateHost</c> 完全无法工作，与"未提供数据时维持此前的内存默认行为"这一集成
        /// 目标相悖。<c>data/_framework/found/found.game_state.json</c> 已提供与
        /// <see cref="Default"/> 等价的默认数据行（见 schema/found.game_state.md"默认表等价于
        /// 03 第 2 节"一节），正常装配路径下两者结果一致，只有测试刻意不加载该表时才会走到
        /// 这条回退分支。
        /// </para>
        /// </summary>
        public static AppStateMachineConfig FromRegistry(IDataRegistryView registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var rows = registry.GetAll(FoundGameStateSchema.Table.Name);
            if (rows.Count == 0)
            {
                return Default();
            }

            var definitions = new List<GameStateTransitionDefinition>(rows.Count);
            foreach (var row in rows)
            {
                var kindText = row.GetString("kind");
                var kind = kindText == "main" ? GameStateTransitionKind.Main : GameStateTransitionKind.Sub;
                row.TryGetString("description", out var description);
                definitions.Add(new GameStateTransitionDefinition(
                    row.GetString("id"), row.GetString("from"), row.GetString("to"), kind,
                    string.IsNullOrEmpty(description) ? null : description));
            }

            return FromDefinitions(definitions);
        }

        private static AppState ParseAppState(string value)
        {
            if (Enum.TryParse<AppState>(value, out var state))
            {
                return state;
            }

            throw new ArgumentException($"无法解析为 AppState 枚举名：\"{value}\"", nameof(value));
        }
    }
}
