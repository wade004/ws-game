#nullable enable
// SkillBarInteraction：技能栏的鼠标交互（ADR-0180）——悬停显示技能详情（SkillTooltipView）、左键点击施放，并且让技能栏自己接收鼠标、不穿透到世界。
//
// 判断记录（谁拥有技能栏）：技能栏的外观由各游戏自己画（格子数量、皮肤、热键文字都不同），框架只提供交互这一层：游戏把每个格子的底框交给 Register，
// 并提供“某格当前的提示框内容”和“点击某格”两个回调。本类不认识技能、热键或输入映射，所以任何游戏、任何栏位布局都能用。
// 判断记录（点击 = 热键）：点击不另开施法路径——游戏的点击回调应当把对应热键经 UiInputBridge.PressKey 注入输入流，之后走与真实按键完全相同的
// 输入映射 → 输入缓冲 → 技能绑定 → 施法管线，冷却、资源、读条中、目标等限制与提示和按键一致。
// 判断记录（不穿透到世界）：HUD 画布通常没有 GraphicRaycaster，鼠标永远“不在界面上”；本类只给技能栏自己挂一个嵌套画布 + 射线检测器，
// 格子底框接收射线，其余图形（标签、提示框）一律不接收，HUD 的其它部分仍然不挡世界点击。世界侧用 UiInputBridge.PointerOverUi 判断指针是否在技能栏上。
// 判断记录（缺省行为不变）：不安装本组件的游戏没有任何变化；安装是游戏显式调用 Install 的结果。
using System;
using System.Collections.Generic;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public sealed class SkillBarInteraction : MonoBehaviour
    {
        private readonly List<RectTransform> _slotRoots = new List<RectTransform>();
        private SkillTooltipView? _tip;
        private Func<int, SkillTooltipContent?>? _contentFor;
        private Action<int>? _onClick;
        private int _hovered = -1;

        /// <summary>在 <paramref name="bar"/> 上安装交互：挂嵌套画布与射线检测器，创建提示框（父节点取 <paramref name="tipParent"/>，通常是 HUD 画布根）。</summary>
        public static SkillBarInteraction Install(
            RectTransform bar, RectTransform tipParent, Func<int, SkillTooltipContent?> contentFor, Action<int> onClick, SkillTooltipView.Style? style = null)
        {
            if (bar.GetComponent<Canvas>() == null)
            {
                // 先挂画布且不覆盖排序：与 HUD 画布同层、绘制在 HUD 其它元素之上，射线只对技能栏生效。
                bar.gameObject.AddComponent<Canvas>();
            }

            if (bar.GetComponent<GraphicRaycaster>() == null)
            {
                bar.gameObject.AddComponent<GraphicRaycaster>();
            }

            var interaction = bar.gameObject.AddComponent<SkillBarInteraction>();
            interaction._contentFor = contentFor;
            interaction._onClick = onClick;
            interaction._tip = new SkillTooltipView(tipParent, style);
            return interaction;
        }

        /// <summary>登记第 <paramref name="index"/> 格：底框接收射线，悬停进出与左键点击挂到它上面。序号要连续从 0 开始登记。</summary>
        public void Register(Image frame, int index)
        {
            // 格子里的标签、图标等其余图形不接收射线，否则指针落在它们上面时底框收不到进出和点击；底框自己接收。
            foreach (var graphic in frame.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            frame.raycastTarget = true;
            while (_slotRoots.Count <= index)
            {
                _slotRoots.Add(null!);
            }

            _slotRoots[index] = frame.rectTransform;
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ => Hover(index));
            AddTrigger(trigger, EventTriggerType.PointerExit, _ => Unhover(index));
            AddTrigger(trigger, EventTriggerType.PointerClick, data =>
            {
                if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                {
                    Click(index);
                }
            });
        }

        public SkillTooltipView Tooltip => _tip ?? throw new InvalidOperationException("技能栏交互未安装");

        /// <summary>当前悬停的格子序号（0 起；-1 = 没有悬停）。</summary>
        public int HoveredSlot => _hovered;

        /// <summary>左键点击第 <paramref name="index"/> 格（鼠标事件和测试都走这里）。</summary>
        public void Click(int index) => _onClick?.Invoke(index);

        public void Hover(int index)
        {
            _hovered = index;
            Refresh();
        }

        public void Unhover(int index)
        {
            if (_hovered != index)
            {
                return;
            }

            _hovered = -1;
            _tip?.Hide();
        }

        /// <summary>悬停期间每帧刷新提示框内容（冷却时长、属性变了数字跟着变）；没有悬停或格子空着就隐藏。</summary>
        public void Refresh()
        {
            if (_tip == null || _contentFor == null)
            {
                return;
            }

            if (_hovered < 0 || _hovered >= _slotRoots.Count || _slotRoots[_hovered] == null)
            {
                _tip.Hide();
                return;
            }

            var content = _contentFor(_hovered);
            if (content == null)
            {
                _tip.Hide();
                return;
            }

            _tip.Show(content, _slotRoots[_hovered]);
        }

        private void Update()
        {
            if (_hovered >= 0)
            {
                Refresh();
            }
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data));
            trigger.triggers.Add(entry);
        }
    }
}
