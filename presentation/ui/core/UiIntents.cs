using System;
using Core.Carriers.Common;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SimLoop;
using Core.Gameplay.Dialog;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Carriers.Unit;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Ui
{
    /// <summary>
    /// UI 控件对用户输入的响应统一出口（见 09_表现层.md 第 7.2 节"UI 控件对用户输入的响应，一律
    /// 封装为'意图请求'经窄契约提交...不直接改变背包数据"）。本类型本身不持有、不修改任何逻辑
    /// 状态（铁律 P3），每个公开方法都只是把一次用户操作转发给规则层/玩法层的窄契约入口
    /// （<see cref="IWorldSim.SubmitIntent"/>、<see cref="IEquipmentHost"/>、<see cref="IQuestHost"/>、
    /// <see cref="IDialogHost"/>、<see cref="IEconomyHost"/>、<see cref="IInputMapHost"/>、
    /// <see cref="IL10nHost"/>、<see cref="IAppStateHost"/>、<see cref="IAudioLayerVolumeHost"/>
    /// （缺口 12）、<see cref="ISkillBindingHost"/>（缺口 4）），裁决结果由被调用方给出，本类型只
    /// 原样透传返回值。
    /// <para>
    /// 判断记录（哪些操作走 <c>SubmitIntent</c>、哪些直接调用窄契约方法）：任务书拍板"装备穿脱是
    /// 玩法层直接裁决的窄契约调用"——<see cref="Equip"/>/<see cref="Unequip"/> 因此直接调用
    /// <see cref="IEquipmentHost"/>，不经 <see cref="IWorldSim.SubmitIntent"/>；<see cref="UseItem"/>
    /// 按任务书"技能管线/物品系统的消费由 L4 处理：本模块只提交"经 <c>SubmitIntent(player,
    /// "use_item", {instance_id})</c>；<see cref="CastSkill"/>/<see cref="Move"/> 同样是需要规则层
    /// 校验（射程、资源、冷却、导航）的动作，经 <c>SubmitIntent</c>。
    /// </para>
    /// </summary>
    public sealed class UiIntents
    {
        private readonly Id _playerId;
        private readonly IWorldSim _worldSim;
        private readonly IEquipmentHost _equipment;
        private readonly IQuestHost _quest;
        private readonly IDialogHost _dialog;
        private readonly IEconomyHost _economy;
        private readonly IInputMapHost _inputMap;
        private readonly IL10nHost _l10n;
        private readonly IAppStateHost _appState;
        private readonly IAudioLayerVolumeHost _audioVolume;
        private readonly ISkillBindingHost _skillBindings;

        /// <summary>ADR-0013 离散时间模型："结束回合"意图的窄契约出口（见 <see cref="EndTurn"/>）。
        /// 具体类型（而不是 <see cref="Core.Foundation.SimLoop.ITurnScheduler"/>）：只有具体类型才
        /// 暴露 <c>EndTurn(Id)</c>/<c>GetCurrentActor()</c> 这两个便利成员，惯例同
        /// <c>Core.Gameplay.Assembly.GameplayAssembly.TurnScheduler</c> 属性判断记录。可为空：未装配
        /// 离散模式（调用方未构造 <c>GameplayAssembly(clockHost:)</c>）的既有调用方不受影响，
        /// <see cref="EndTurn"/> 此时恒返回 <c>false</c>，不抛异常。</summary>
        private readonly Core.Foundation.SimLoop.TurnScheduler? _turnScheduler;

        /// <summary>ADR-0077：可选事件总线，非空时携带 <c>panelId</c> 参数的重载会
        /// <c>PublishImmediate</c> 一个 <see cref="UiActionInvokedEvent"/>（见类型注释"判断记录
        /// （ui.action_invoked）"）；未经新增重载构造（恒为 null）的既有调用方不受影响，本类型其余
        /// 全部行为不变。</summary>
        private readonly IEventBus? _eventBus;

        public UiIntents(
            Id playerId,
            IWorldSim worldSim,
            IEquipmentHost equipment,
            IQuestHost quest,
            IDialogHost dialog,
            IEconomyHost economy,
            IInputMapHost inputMap,
            IL10nHost l10n,
            IAppStateHost appState,
            IAudioLayerVolumeHost audioVolume,
            ISkillBindingHost skillBindings,
            Core.Foundation.SimLoop.TurnScheduler? turnScheduler = null)
        {
            _playerId = playerId;
            _worldSim = worldSim ?? throw new ArgumentNullException(nameof(worldSim));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _quest = quest ?? throw new ArgumentNullException(nameof(quest));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _inputMap = inputMap ?? throw new ArgumentNullException(nameof(inputMap));
            _l10n = l10n ?? throw new ArgumentNullException(nameof(l10n));
            _appState = appState ?? throw new ArgumentNullException(nameof(appState));
            _audioVolume = audioVolume ?? throw new ArgumentNullException(nameof(audioVolume));
            _skillBindings = skillBindings ?? throw new ArgumentNullException(nameof(skillBindings));
            _turnScheduler = turnScheduler;
        }

        /// <summary>ADR-0077 新增重载（新增参数不改既有构造签名，同类型内既有"新增重载、旧重载原样
        /// 转发"惯例）：<paramref name="eventBus"/> 非空时接入 ui.action_invoked 发布能力，见
        /// <see cref="_eventBus"/> 字段注释。与旧 12 参重载的参数个数不同（本重载多一个必填
        /// <paramref name="eventBus"/>），调用方按参数个数即可无歧义选中对应重载，不产生重载决议
        /// 二义性。</summary>
        public UiIntents(
            Id playerId,
            IWorldSim worldSim,
            IEquipmentHost equipment,
            IQuestHost quest,
            IDialogHost dialog,
            IEconomyHost economy,
            IInputMapHost inputMap,
            IL10nHost l10n,
            IAppStateHost appState,
            IAudioLayerVolumeHost audioVolume,
            ISkillBindingHost skillBindings,
            Core.Foundation.SimLoop.TurnScheduler? turnScheduler,
            IEventBus eventBus)
            : this(playerId, worldSim, equipment, quest, dialog, economy, inputMap, l10n, appState,
                audioVolume, skillBindings, turnScheduler)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        /// <summary>ADR-0077 判断记录（ui.action_invoked）：本类型新增的一批携带 <c>panelId</c> 参数
        /// 的重载（<see cref="CastSkill(Id,Id,Id?)"/> 等）在委派给对应旧方法之前，先调用本辅助方法
        /// <c>PublishImmediate</c> 一次 <see cref="UiActionInvokedEvent"/>。<paramref name="actionName"/>
        /// 是框架自持的固定 UI 意图词汇（每个重载各自的方法名语义，如 <c>"cast_skill"</c>），不是
        /// 调用方传入的自由字符串——不采纳消费方原话里的 <c>buttonId</c>：框架的 UI 核心层不知道、
        /// 也不该知道某个具体皮肤上有哪些按钮，只知道"哪个面板上触发了哪个 UI 意图"（<paramref
        /// name="panelId"/> 由调用方给出，与 <see cref="UiPanelRegistry.Open"/>/<see cref="UiPanelRegistry.Close"/>
        /// 同一套 <c>ui_layout_definition.id</c> 空间，见 <c>architecture/adr/0077-ui交互域事件.md</c>
        /// "决策"一节）。<see cref="_eventBus"/> 为 null（调用方未经新增构造函数重载接入）时静默跳过，
        /// 不抛异常——本类型其余"未装配的能力静默降级"一贯风格。</summary>
        private void PublishActionInvoked(Id panelId, string actionName) =>
            _eventBus?.PublishImmediate(new UiActionInvokedEvent(panelId, actionName));

        public void UseItem(Id instanceId)
        {
            var args = new JsonObjectBuilder().Add("instance_id", new JsonString(instanceId.Value)).Build();
            _worldSim.SubmitIntent(new Intent(_playerId, "use_item", args));
        }

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "use_item"}</c>
        /// （见 <see cref="PublishActionInvoked"/>），再转发 <see cref="UseItem(Id)"/>。</summary>
        public void UseItem(Id panelId, Id instanceId)
        {
            PublishActionInvoked(panelId, "use_item");
            UseItem(instanceId);
        }

        public EquipResult Equip(Id instanceId, Id slot) => _equipment.Equip(_playerId, instanceId, slot);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "equip"}</c>，
        /// 再转发 <see cref="Equip(Id,Id)"/>。</summary>
        public EquipResult Equip(Id panelId, Id instanceId, Id slot)
        {
            PublishActionInvoked(panelId, "equip");
            return Equip(instanceId, slot);
        }

        public ItemInstanceRef? Unequip(Id slot) => _equipment.Unequip(_playerId, slot);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "unequip"}</c>，
        /// 再转发 <see cref="Unequip(Id)"/>。</summary>
        public ItemInstanceRef? Unequip(Id panelId, Id slot)
        {
            PublishActionInvoked(panelId, "unequip");
            return Unequip(slot);
        }

        public void CastSkill(Id skillId, Id? targetId)
        {
            var builder = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value));
            if (targetId.HasValue)
            {
                builder = builder.Add("target_id", new JsonString(targetId.Value.Value));
            }
            _worldSim.SubmitIntent(new Intent(_playerId, "cast", builder.Build()));
        }

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "cast_skill"}</c>，
        /// 再转发 <see cref="CastSkill(Id,Id?)"/>。</summary>
        public void CastSkill(Id panelId, Id skillId, Id? targetId)
        {
            PublishActionInvoked(panelId, "cast_skill");
            CastSkill(skillId, targetId);
        }

        /// <summary>
        /// GP-PRES-02 收口（<c>architecture/落地计划/audit-20260907/gameplay-presentation.md</c>）：
        /// 参数键改为 <c>dx</c>/<c>dy</c>，与 <see cref="Core.Carriers.Unit.MovementHost.Request"/>
        /// 内部方向移动编码、<c>MovementTickHandler.TryReadDirection</c> 的实际读取键一致（此前本方法
        /// 手写 <c>dir_x</c>/<c>dir_y</c>，与消费端字段名不一致，移动处理器读不到方向、按"缺失
        /// target/direction 参数"的诊断分支忽略整条意图，位置恒不变——该错误只影响本便利 API，不
        /// 影响模板默认键盘路径，因为那条路径走的是 <see cref="Core.Carriers.Unit.MovementHost.Request"/>
        /// 而不是本方法，见类型顶部审计记录）。窄契约统一为 <c>dx</c>/<c>dy</c> 一套，不新增第三套
        /// 字段名。
        /// </summary>
        public void Move(Vec2 direction)
        {
            var args = new JsonObjectBuilder()
                .Add("dx", new JsonNumber(direction.X))
                .Add("dy", new JsonNumber(direction.Y))
                .Build();
            _worldSim.SubmitIntent(new Intent(_playerId, "move", args));
        }

        public bool AcceptQuest(Id questId) => _quest.Accept(_playerId, questId);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "accept_quest"}</c>，
        /// 再转发 <see cref="AcceptQuest(Id)"/>。</summary>
        public bool AcceptQuest(Id panelId, Id questId)
        {
            PublishActionInvoked(panelId, "accept_quest");
            return AcceptQuest(questId);
        }

        public bool TurnInQuest(Id questId) => _quest.TurnIn(_playerId, questId);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "turn_in_quest"}</c>，
        /// 再转发 <see cref="TurnInQuest(Id)"/>。</summary>
        public bool TurnInQuest(Id panelId, Id questId)
        {
            PublishActionInvoked(panelId, "turn_in_quest");
            return TurnInQuest(questId);
        }

        /// <summary>
        /// 消费方反馈（游戏接入方第十五批，阻塞，框架缺陷）修复：本方法此前恒转发
        /// <see cref="IDialogHost.ChooseOption"/>——<see cref="IDialogHost.StartStory"/> 会把会话的
        /// gossip 菜单态清空（<c>DialogHost.Session.GossipMenuId</c>），<see cref="IDialogHost.ChooseOption"/>
        /// 要求该字段非空，于是剧情会话里经原生对白面板（<c>DialogPanel.RefreshUi</c>）点任何分支都
        /// 被拒绝，节点恒不推进——玩家只有绕开 UI 直调 <see cref="IDialogHost.AdvanceStory"/> 才能走完
        /// 剧情。
        /// <para>
        /// 判断记录（按当前会话类型分派，一个入口不拆两个方法）：<see cref="DialogHost"/> 的会话模型
        /// 里 <c>GossipMenuId</c>/<c>StoryTreeId</c> 互斥——<c>StartStory</c> 进入剧情时清空
        /// <c>GossipMenuId</c>，<c>OpenGossip</c> 打开菜单时清空 <c>StoryTreeId</c>（见两方法实现），
        /// 因此"当前是否在剧情会话"与"当前是否在 gossip 会话"这两个只读查询在任一时刻至多一个为真，
        /// 不存在需要裁决优先级的真正并存情形。分派顺序（先查 <see cref="IDialogHost.GetStoryView"/>
        /// 再查 <see cref="IDialogHost.GetGossipView"/>）与唯一消费方 <c>DialogPanel.RefreshUi</c>
        /// 决定渲染哪一种视图的顺序（<c>if (_vm.Story != null) ... else if (_vm.Gossip != null)</c>）
        /// 保持一致，UI 显示的是哪种会话，点击就转发到哪种会话的推进方法，不会出现"看到剧情分支、
        /// 点击却按 gossip 语义处理"的错位。两个会话都未打开时返回 <c>false</c>，不抛异常（与本类型
        /// 其它意图方法"被拒绝时静默返回失败"一贯风格一致）。不改
        /// <see cref="IDialogHost.ChooseOption"/>/<see cref="IDialogHost.AdvanceStory"/> 本身的语义——
        /// 两个宿主方法各自语义清楚，缺陷只在本方法这一层"UI 意图未按会话类型分派"。
        /// </para>
        /// </summary>
        public bool ChooseDialogOption(int index)
        {
            if (_dialog.GetStoryView(_playerId) != null)
            {
                return _dialog.AdvanceStory(_playerId, index);
            }
            if (_dialog.GetGossipView(_playerId) != null)
            {
                return _dialog.ChooseOption(_playerId, index);
            }
            return false;
        }

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "choose_dialog_option"}</c>，
        /// 再转发 <see cref="ChooseDialogOption(int)"/>。</summary>
        public bool ChooseDialogOption(Id panelId, int index)
        {
            PublishActionInvoked(panelId, "choose_dialog_option");
            return ChooseDialogOption(index);
        }

        public PurchaseResult Buy(Id vendorId, Id itemId, int count) => _economy.Buy(_playerId, vendorId, itemId, count);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "buy"}</c>，
        /// 再转发 <see cref="Buy(Id,Id,int)"/>。</summary>
        public PurchaseResult Buy(Id panelId, Id vendorId, Id itemId, int count)
        {
            PublishActionInvoked(panelId, "buy");
            return Buy(vendorId, itemId, count);
        }

        public SellResult Sell(Id vendorId, Id itemInstanceId, int count) => _economy.Sell(_playerId, vendorId, itemInstanceId, count);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "sell"}</c>，
        /// 再转发 <see cref="Sell(Id,Id,int)"/>。</summary>
        public SellResult Sell(Id panelId, Id vendorId, Id itemInstanceId, int count)
        {
            PublishActionInvoked(panelId, "sell");
            return Sell(vendorId, itemInstanceId, count);
        }

        public bool Rebind(string action, string binding) => _inputMap.Rebind(action, binding);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "rebind"}</c>，
        /// 再转发 <see cref="Rebind(string,string)"/>。</summary>
        public bool Rebind(Id panelId, string action, string binding)
        {
            PublishActionInvoked(panelId, "rebind");
            return Rebind(action, binding);
        }

        public void SetLocale(Id locale) => _l10n.SetLocale(locale);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "set_locale"}</c>，
        /// 再转发 <see cref="SetLocale(Id)"/>。</summary>
        public void SetLocale(Id panelId, Id locale)
        {
            PublishActionInvoked(panelId, "set_locale");
            SetLocale(locale);
        }

        public void SetLayerVolume(string layer, double volume) => _audioVolume.SetVolume(layer, volume);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "set_layer_volume"}</c>，
        /// 再转发 <see cref="SetLayerVolume(string,double)"/>。</summary>
        public void SetLayerVolume(Id panelId, string layer, double volume)
        {
            PublishActionInvoked(panelId, "set_layer_volume");
            SetLayerVolume(layer, volume);
        }

        /// <summary>缺口 4：技能书面板拖放/点击绑定的意图入口——把 <paramref name="skillId"/> 绑定到
        /// 动作条 <paramref name="slot"/> 号槽位（<see cref="ActionBarViewModel.SlotKey"/> 换算槽位键）。
        /// 绑定被拒绝（<paramref name="skillId"/> 不是玩家已知技能）时原样返回 false，不抛异常。</summary>
        public bool BindActionBarSlot(int slot, Id skillId) =>
            _skillBindings.Bind(_playerId, ActionBarViewModel.SlotKey(slot), skillId);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "bind_action_bar_slot"}</c>，
        /// 再转发 <see cref="BindActionBarSlot(int,Id)"/>。</summary>
        public bool BindActionBarSlot(Id panelId, int slot, Id skillId)
        {
            PublishActionInvoked(panelId, "bind_action_bar_slot");
            return BindActionBarSlot(slot, skillId);
        }

        /// <summary>缺口 4：清空动作条 <paramref name="slot"/> 号槽位的绑定。</summary>
        public bool UnbindActionBarSlot(int slot) =>
            _skillBindings.Unbind(_playerId, ActionBarViewModel.SlotKey(slot));

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "unbind_action_bar_slot"}</c>，
        /// 再转发 <see cref="UnbindActionBarSlot(int)"/>。</summary>
        public bool UnbindActionBarSlot(Id panelId, int slot)
        {
            PublishActionInvoked(panelId, "unbind_action_bar_slot");
            return UnbindActionBarSlot(slot);
        }

        /// <summary>ADR-0013 离散时间模型（03 第 3.2 节步骤 3"玩家……或调用 endTurn 提交'结束回合'
        /// 意图"）：把 HUD"结束回合"按钮的点击转发到
        /// <see cref="Core.Foundation.SimLoop.TurnScheduler.EndTurn(Id)"/> 这一窄契约，解除
        /// <c>awaiting_input</c> 子态，让 <c>GameplayAssembly.Advance</c> 推进到下一行动者。未装配
        /// 离散模式（构造期未传入 <c>turnScheduler</c>）或当前不轮到玩家行动时返回 <c>false</c>，
        /// 不抛异常（与本类型其它意图方法"被拒绝时静默返回失败"一贯风格一致）。</summary>
        public bool EndTurn()
        {
            if (_turnScheduler == null)
            {
                return false;
            }

            var current = _turnScheduler.GetCurrentActor();
            if (current == null || !current.Value.Equals(_playerId))
            {
                return false;
            }

            _turnScheduler.EndTurn(_playerId);
            return true;
        }

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "end_turn"}</c>，
        /// 再转发 <see cref="EndTurn()"/>。</summary>
        public bool EndTurn(Id panelId)
        {
            PublishActionInvoked(panelId, "end_turn");
            return EndTurn();
        }

        public bool Pause() => _appState.RequestTransition(AppState.Pause);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "pause"}</c>，
        /// 再转发 <see cref="Pause()"/>。</summary>
        public bool Pause(Id panelId)
        {
            PublishActionInvoked(panelId, "pause");
            return Pause();
        }

        public bool Resume() => _appState.RequestTransition(AppState.InWorld);

        /// <summary>ADR-0077 新增重载：先发布 <c>ui.action_invoked{panelId, actionName: "resume"}</c>，
        /// 再转发 <see cref="Resume()"/>。</summary>
        public bool Resume(Id panelId)
        {
            PublishActionInvoked(panelId, "resume");
            return Resume();
        }

        public bool OpenMenu() => _appState.PushSubState(InWorldSubState.MenuOverlay);

        public bool CloseMenu() => _appState.PopSubState();
    }
}
