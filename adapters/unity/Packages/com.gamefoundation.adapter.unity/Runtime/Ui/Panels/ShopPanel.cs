#nullable enable
// ShopPanel：商店面板（W3b 收边，拍板 7"商店 UI：补 ShopViewModel + Unity ShopPanel + PlayMode
// 用例"）。绑定 Presentation.Ui.ShopViewModel（商人出售清单/库存/价格）与 UiIntents.Buy/Sell，
// 布局/交互惯例同 GameplayPanels.cs 六个既有面板（每帧把视图模型当前属性刷到控件上，见
// IUiPanel.cs 顶部判断记录）。
//
// 判断记录（Sell 区块为什么额外接 InventoryViewModel）：ShopViewModel（09 第 7.1 节"商店"）只读
// Core.Gameplay.Economy.EconomyHost.GetVendorDef 的出售清单——它回答"商人在卖什么"，不回答"玩家
// 自己持有什么可以卖给商人"（那是 InventoryViewModel 已有的职责，见该类型判断记录）。UiIntents.Sell
// 需要玩家侧的 itemInstanceId，本面板因此在 Construct 期额外接一份 InventoryViewModel 引用，只用它
// 读 Slots（不重复其 Use 交互），换算出"当前打开的商人 + 玩家持有的物品实例"两者的乘积作为可点击
// 的卖出候选——不做"该商人是否收购这件物品"的过滤（09/04 均未给出 vendor 收购白名单字段），卖出
// 请求本身仍经 EconomyHost.Sell 的既有校验兜底，被拒绝时 UiIntents.Sell 返回失败结果，本面板不用
// 关心具体拒绝原因（同 InventoryViewModel.OnUseClicked 一贯"提交意图，不代为预判"的风格）。
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Ui;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Ui.Panels
{
    public sealed class ShopPanel : UiPanelBehaviour
    {
        private ShopViewModel _vm = null!;
        private InventoryViewModel _inventory = null!;
        private UiIntents _intents = null!;
        private TextMeshProUGUI _balanceLabel = null!;
        private RectTransform _buyList = null!;
        private RectTransform _sellList = null!;
        private readonly List<GameObject> _buyRows = new List<GameObject>();
        private readonly List<GameObject> _sellRows = new List<GameObject>();

        public void Construct(RectTransform parent, ShopViewModel vm, InventoryViewModel inventory, UiIntents intents)
        {
            _vm = vm;
            _inventory = inventory;
            _intents = intents;

            var root = UiWidgets.CreatePanelBackground(
                "ShopPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(360f, 320f), Vector2.zero);

            _balanceLabel = UiWidgets.CreateLabel("Balance", root, "-", 16);
            UiWidgets.SetRect(_balanceLabel.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -28), new Vector2(-8, -8));

            var buyHeader = UiWidgets.CreateLabel("BuyHeader", root, "出售清单", 14);
            UiWidgets.SetRect(buyHeader.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(8, -52), new Vector2(-8, -36));

            _buyList = UiWidgets.CreateVerticalList("BuyList", root, 2f);
            UiWidgets.SetRect(_buyList, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(8, -140), new Vector2(-8, -56));

            var sellHeader = UiWidgets.CreateLabel("SellHeader", root, "背包（可卖出）", 14);
            UiWidgets.SetRect(sellHeader.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(8, -18), new Vector2(-8, 2));

            _sellList = UiWidgets.CreateVerticalList("SellList", root, 2f);
            UiWidgets.SetRect(_sellList, Vector2.zero, new Vector2(1, 0.5f), new Vector2(8, 8), new Vector2(-8, -18));
        }

        /// <summary>打开商人货架并刷新一次（供交互层——通常是 NPC 对话/进店判定——在决定"打开商店"
        /// 后调用，同 <see cref="ShopViewModel.OpenVendor"/> 判断记录）。</summary>
        public void OpenVendor(Id vendorId)
        {
            _vm.OpenVendor(vendorId);
            Show();
            RefreshUi();
        }

        public void CloseVendorAndHide()
        {
            _vm.CloseVendor();
            Hide();
        }

        public override void RefreshUi()
        {
            _balanceLabel.text = _vm.CurrentVendorId.HasValue
                ? $"货币：{FormatBalances()}"
                : "（未打开商店）";

            RefreshBuyRows();
            RefreshSellRows();
        }

        private string FormatBalances()
        {
            // ShopViewModel.GetPlayerBalance 按币种查询（09 第 7.1 节"商店"面板"展示……玩家各货币
            // 余额"）；本面板只展示当前出售清单实际用到的币种，避免枚举一份未知的全局货币表。
            var seen = new HashSet<Id>();
            var sb = new System.Text.StringBuilder();
            foreach (var item in _vm.SellItems)
            {
                if (seen.Add(item.PriceCurrencyId))
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(ShortId(item.PriceCurrencyId)).Append(':').Append(_vm.GetPlayerBalance(item.PriceCurrencyId));
                }
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }

        private void RefreshBuyRows()
        {
            var count = _vm.SellItems.Count;
            EnsureRowCount(_buyRows, _buyList, count, isBuyRow: true);

            for (var i = 0; i < count; i++)
            {
                var item = _vm.SellItems[i];
                var label = _buyRows[i].transform.Find("Label").GetComponent<TextMeshProUGUI>();
                var stockText = item.Stock.HasValue ? item.Stock.Value.ToString() : "不限";
                label.text = $"{ShortId(item.ItemId)}  {item.PriceAmount}{ShortId(item.PriceCurrencyId)}  库存:{stockText}";

                var button = _buyRows[i].transform.Find("Buy").GetComponent<UnityEngine.UI.Button>();
                button.interactable = !item.Stock.HasValue || item.Stock.Value > 0;
            }
        }

        private void RefreshSellRows()
        {
            var slots = _inventory.Slots;
            EnsureRowCount(_sellRows, _sellList, slots.Count, isBuyRow: false);

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var label = _sellRows[i].transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = $"{ShortId(slot.TemplateId)} x{slot.Count}";
            }
        }

        private void EnsureRowCount(List<GameObject> rows, RectTransform list, int desired, bool isBuyRow)
        {
            while (rows.Count < desired)
            {
                var rowIndex = rows.Count;
                var row = new GameObject($"Row{rowIndex}", typeof(RectTransform));
                var rect = (RectTransform)row.transform;
                rect.SetParent(list, false);
                var hl = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hl.spacing = 6f; hl.childControlWidth = false; hl.childControlHeight = true; hl.childForceExpandWidth = false;
                var label = UiWidgets.CreateLabel("Label", rect, "-", 14);
                label.rectTransform.sizeDelta = new Vector2(220f, 20f);

                var capturedIndex = rowIndex;
                if (isBuyRow)
                {
                    UiWidgets.CreateButton("Buy", rect, "购买", () => OnBuyClicked(capturedIndex));
                }
                else
                {
                    UiWidgets.CreateButton("Sell", rect, "卖出", () => OnSellClicked(capturedIndex));
                }
                rows.Add(row);
            }
            while (rows.Count > desired)
            {
                var last = rows[rows.Count - 1];
                rows.RemoveAt(rows.Count - 1);
                Destroy(last);
            }
        }

        private void OnBuyClicked(int index)
        {
            if (!_vm.CurrentVendorId.HasValue || index < 0 || index >= _vm.SellItems.Count)
            {
                return;
            }
            var item = _vm.SellItems[index];
            _intents.Buy(_vm.CurrentVendorId.Value, item.ItemId, count: 1);
        }

        private void OnSellClicked(int index)
        {
            if (!_vm.CurrentVendorId.HasValue || index < 0 || index >= _inventory.Slots.Count)
            {
                return;
            }
            var slot = _inventory.Slots[index];
            _intents.Sell(_vm.CurrentVendorId.Value, slot.InstanceId, count: 1);
        }

        private static string ShortId(Id id)
        {
            var v = id.Value;
            var idx = v.LastIndexOf('.');
            return idx >= 0 ? v.Substring(idx + 1) : v;
        }
    }
}
