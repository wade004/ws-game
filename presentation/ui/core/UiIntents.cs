using System;
using Core.Carriers.Common;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
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

        public void UseItem(Id instanceId)
        {
            var args = new JsonObjectBuilder().Add("instance_id", new JsonString(instanceId.Value)).Build();
            _worldSim.SubmitIntent(new Intent(_playerId, "use_item", args));
        }

        public EquipResult Equip(Id instanceId, Id slot) => _equipment.Equip(_playerId, instanceId, slot);

        public ItemInstanceRef? Unequip(Id slot) => _equipment.Unequip(_playerId, slot);

        public void CastSkill(Id skillId, Id? targetId)
        {
            var builder = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value));
            if (targetId.HasValue)
            {
                builder = builder.Add("target_id", new JsonString(targetId.Value.Value));
            }
            _worldSim.SubmitIntent(new Intent(_playerId, "cast", builder.Build()));
        }

        public void Move(Vec2 direction)
        {
            var args = new JsonObjectBuilder()
                .Add("dir_x", new JsonNumber(direction.X))
                .Add("dir_y", new JsonNumber(direction.Y))
                .Build();
            _worldSim.SubmitIntent(new Intent(_playerId, "move", args));
        }

        public bool AcceptQuest(Id questId) => _quest.Accept(_playerId, questId);

        public bool TurnInQuest(Id questId) => _quest.TurnIn(_playerId, questId);

        public bool ChooseDialogOption(int index) => _dialog.ChooseOption(_playerId, index);

        public PurchaseResult Buy(Id vendorId, Id itemId, int count) => _economy.Buy(_playerId, vendorId, itemId, count);

        public SellResult Sell(Id vendorId, Id itemInstanceId, int count) => _economy.Sell(_playerId, vendorId, itemInstanceId, count);

        public bool Rebind(string action, string binding) => _inputMap.Rebind(action, binding);

        public void SetLocale(Id locale) => _l10n.SetLocale(locale);

        public void SetLayerVolume(string layer, double volume) => _audioVolume.SetVolume(layer, volume);

        /// <summary>缺口 4：技能书面板拖放/点击绑定的意图入口——把 <paramref name="skillId"/> 绑定到
        /// 动作条 <paramref name="slot"/> 号槽位（<see cref="ActionBarViewModel.SlotKey"/> 换算槽位键）。
        /// 绑定被拒绝（<paramref name="skillId"/> 不是玩家已知技能）时原样返回 false，不抛异常。</summary>
        public bool BindActionBarSlot(int slot, Id skillId) =>
            _skillBindings.Bind(_playerId, ActionBarViewModel.SlotKey(slot), skillId);

        /// <summary>缺口 4：清空动作条 <paramref name="slot"/> 号槽位的绑定。</summary>
        public bool UnbindActionBarSlot(int slot) =>
            _skillBindings.Unbind(_playerId, ActionBarViewModel.SlotKey(slot));

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

        public bool Pause() => _appState.RequestTransition(AppState.Pause);

        public bool Resume() => _appState.RequestTransition(AppState.InWorld);

        public bool OpenMenu() => _appState.PushSubState(InWorldSubState.MenuOverlay);

        public bool CloseMenu() => _appState.PopSubState();
    }
}
