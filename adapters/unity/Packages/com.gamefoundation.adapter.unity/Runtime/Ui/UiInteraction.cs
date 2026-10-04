#nullable enable
// UiInteraction：背包与装备面板共用的最小运行期交互——悬停提示框（UiTooltip）与拖放穿脱（UiDragController）（手感设计/08 第 3 节、ADR-0152）。
//
// 数据与美术的来源和面板一致：提示框内容来自 item.template 数据（ItemTooltipBuilder），外观来自皮肤包 tooltip/*；拖拽的影子与目标叠层来自皮肤包 drag/*；
// 没有声明皮肤（占位皮肤）时同一条路径取框架占位包里的同名文件，永远画得出来。穿脱走注入的 UiEquipActions（生产宿主接 UiIntents，实验室换装场景接 WardrobeStage）。
//
// 判断记录（交互只做"最小可用"）：悬停出提示框、从背包拖到装备槽位穿上、把已装备的槽位拖回背包卸下；不做拖拽排序、不做槽位间互换、不做堆叠拆分、不做触屏长按。
// 判断记录（合法性的两层）：目标叠层的 target_ok/target_blocked 只按数据判断"物品模板的槽位 = 目标槽位"（ItemTooltipBuilder.FitsSlot）；
// 真正落下时的结果以装备载体返回的 EquipResult 为准（等级需求、占位置换等规则不在界面里重复实现），失败时原样带出失败原因，装备状态不变。
using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using UnityEngine;

namespace Adapter.Unity.Ui
{
    /// <summary>穿脱动作（宿主注入）：背包物品穿到槽位、卸下槽位。</summary>
    public sealed class UiEquipActions
    {
        public Func<Id, Id, EquipResult> Equip { get; }

        /// <summary>卸下槽位，成功返回 true。</summary>
        public Func<Id, bool> Unequip { get; }

        public UiEquipActions(Func<Id, Id, EquipResult> equip, Func<Id, bool> unequip)
        {
            Equip = equip ?? throw new ArgumentNullException(nameof(equip));
            Unequip = unequip ?? throw new ArgumentNullException(nameof(unequip));
        }
    }

    /// <summary>提示框与拖放的共同宿主：持有画布内容节点、皮肤取用入口与穿脱动作；皮肤切换时重建提示框与拖拽影子的外观。</summary>
    public sealed class UiInteraction : IDisposable
    {
        public RectTransform Content { get; }

        public UiVisuals Visuals { get; }

        public UiEquipActions? Actions { get; }

        public UiTooltip Tooltip { get; }

        public UiDragController Drag { get; }

        public UiInteraction(RectTransform content, UiVisuals visuals, UiEquipActions? actions)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Visuals = visuals ?? throw new ArgumentNullException(nameof(visuals));
            Actions = actions;
            Tooltip = new UiTooltip(this);
            Drag = new UiDragController(this);
            Visuals.SkinChanged += OnSkinChanged;
        }

        private void OnSkinChanged()
        {
            Tooltip.Rebuild();
            Drag.RebuildVisuals();
        }

        /// <summary>画布使用的相机：Overlay 画布为 null，否则取画布的 worldCamera。</summary>
        public Camera? Camera
        {
            get
            {
                var canvas = Content.GetComponentInParent<Canvas>();
                if (canvas == null)
                {
                    return null;
                }

                canvas = canvas.rootCanvas;
                return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }
        }

        /// <summary>屏幕坐标 → 内容节点的局部坐标（以内容节点的轴心为原点；内容节点是铺满画布的拉伸节点，轴心在中心）。</summary>
        public Vector2 ScreenToLocal(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Content, screen, Camera, out var local);
            return local;
        }

        /// <summary>内容节点局部坐标 → 屏幕坐标（测试/截图用：由控件位置反推指针位置）。</summary>
        public Vector2 ScreenOf(RectTransform rect) =>
            RectTransformUtility.WorldToScreenPoint(Camera, rect.TransformPoint(rect.rect.center));

        public void Dispose()
        {
            Visuals.SkinChanged -= OnSkinChanged;
            Tooltip.Dispose();
            Drag.Dispose();
        }
    }
}
