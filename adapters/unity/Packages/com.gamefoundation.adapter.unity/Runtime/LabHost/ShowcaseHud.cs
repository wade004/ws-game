#nullable enable
// ShowcaseHud：演示场景（ADR-0154）的游戏内 HUD——玩家生命/体力条、敌人头顶血条、伤害飘字、技能栏（含扫光）、连击计数。
//
// 判断记录（走框架 UI + 皮肤契约）：HUD 全部是运行期 uGUI（UiWidgets 造控件、UiSkin 取色/字体/九宫格），技能栏槽位框走皮肤包的槽位框契约
// （UiSkinPack.SlotFrameDefault）；皮肤包引用 skin.reference_fantasy，目录不存在时框架的回退链自动退回占位皮肤（本分支不带皮肤包，也不复制）。
// 判断记录（只读读模型）：HUD 只读 ShowcaseHudModel（由 ShowcaseDirector 从逻辑事件推演），不读也不写逻辑世界。
// 判断记录（定位）：飘字与头顶血条按舞台相机把世界坐标投到屏幕再换算到画布坐标；画布是屏幕空间叠加，随屏幕尺寸缩放（参考 1920x1080）。
using System;
using System.Collections.Generic;
using Adapter.Unity.Ui;
using Core.Foundation.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.LabHost
{
    public sealed class ShowcaseHud : MonoBehaviour
    {
        public const string SkinRef = "skin.reference_fantasy";

        private sealed class EnemyBar
        {
            public RectTransform Root = null!;
            public Image Fill = null!;
            public TextMeshProUGUI Name = null!;
            public Image Back = null!;
        }

        private sealed class SlotView
        {
            public RectTransform Root = null!;
            public Image Overlay = null!;
            public TextMeshProUGUI Key = null!;
            public TextMeshProUGUI Name = null!;
            public TextMeshProUGUI Sub = null!;
        }

        private EngineLabStage? _stage;
        private Canvas? _canvas;
        private RectTransform? _canvasRect;
        private UiSkinPack? _pack;
        private Image? _hpFill;
        private Image? _stFill;
        private TextMeshProUGUI? _hpText;
        private TextMeshProUGUI? _stText;
        private TextMeshProUGUI? _comboText;
        private TextMeshProUGUI? _comboSub;
        private readonly List<SlotView> _slots = new List<SlotView>();
        private readonly Dictionary<Id, EnemyBar> _enemyBars = new Dictionary<Id, EnemyBar>();

        /// <summary>飘字起点相对头顶的抬高（画布单位）：高过敌人名字（28）与血条（24）两层再留一点空。</summary>
        private const float NumberBaseLift = 80f;
        private readonly List<TextMeshProUGUI> _numberPool = new List<TextMeshProUGUI>();
        private bool _built;

        /// <summary>HUD 可见控件数（测试用）。</summary>
        public int VisibleEnemyBars { get; private set; }

        public int VisibleNumbers { get; private set; }

        public bool Built => _built;

        public bool UsesPlaceholderSkin => _pack == null || _pack.IsPlaceholder || _pack.RefInvalid || !_pack.PackDirectoryExists;

        public static ShowcaseHud Create(EngineLabStage stage, Transform parent)
        {
            var go = new GameObject("ShowcaseHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<ShowcaseHud>();
            hud._stage = stage;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            _pack = UiSkinPack.Load(SkinRef);
            var skin = _pack.CreateOverride();
            if (skin != null)
            {
                UiSkin.Install(skin);
            }

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)canvasGo.transform;

            BuildPlayerBars(_canvasRect);
            BuildSkillBar(_canvasRect);
            BuildCombo(_canvasRect);
            _built = true;
        }

        private static (RectTransform Root, Image Fill) Bar(string name, Transform parent, Color color, Vector2 pos, Vector2 size)
        {
            var (root, fill) = UiWidgets.CreateProgressBar(name, parent, color);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = size;
            root.anchoredPosition = pos;
            return (root, fill);
        }

        private void BuildPlayerBars(RectTransform canvas)
        {
            var panel = UiWidgets.CreatePanelBackground("PlayerPanel", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(430f, 120f), new Vector2(24f, -24f));
            panel.pivot = new Vector2(0f, 1f);
            var (hpRoot, hpFill) = Bar("HpBar", panel, new Color(0.78f, 0.16f, 0.16f, 1f), new Vector2(18f, -18f), new Vector2(394f, 34f));
            _hpFill = hpFill;
            _hpText = UiWidgets.CreateLabel("HpText", hpRoot, "生命", 22, TextAlignmentOptions.Center);
            var (stRoot, stFill) = Bar("StaminaBar", panel, new Color(0.88f, 0.72f, 0.2f, 1f), new Vector2(18f, -66f), new Vector2(394f, 26f));
            _stFill = stFill;
            _stText = UiWidgets.CreateLabel("StaminaText", stRoot, "体力", 18, TextAlignmentOptions.Center);
        }

        private void BuildSkillBar(RectTransform canvas)
        {
            var model = _stage?.Showcase?.Model;
            var count = model == null ? 4 : model.Slots.Length;
            const float slotSize = 112f;
            const float gap = 16f;
            var total = count * slotSize + (count - 1) * gap;
            var bar = UiWidgets.CreateRoot("SkillBar", canvas);
            bar.anchorMin = new Vector2(0.5f, 0f);
            bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(total, slotSize);
            bar.anchoredPosition = new Vector2(0f, 28f);
            for (var i = 0; i < count; i++)
            {
                var view = new SlotView();
                var slotGo = new GameObject("Slot" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)slotGo.transform;
                rect.SetParent(bar, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(slotSize, slotSize);
                rect.anchoredPosition = new Vector2(i * (slotSize + gap), 0f);
                var frame = slotGo.GetComponent<Image>();
                frame.sprite = _pack != null ? _pack.SlotFrameDefault() : UiSkin.PanelSprite;
                frame.type = Image.Type.Sliced;
                frame.color = Color.white;
                view.Root = rect;

                var inner = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var innerRect = (RectTransform)inner.transform;
                innerRect.SetParent(rect, false);
                innerRect.anchorMin = Vector2.zero;
                innerRect.anchorMax = Vector2.one;
                innerRect.offsetMin = new Vector2(6f, 6f);
                innerRect.offsetMax = new Vector2(-6f, -6f);
                var innerImg = inner.GetComponent<Image>();
                innerImg.sprite = UiSkin.FlatSprite;
                innerImg.color = new Color(0.12f, 0.12f, 0.16f, 0.65f);
                innerImg.raycastTarget = false;

                view.Name = UiWidgets.CreateLabel("Name", rect, model == null ? string.Empty : model.Slots[i].Name, 26, TextAlignmentOptions.Center);
                view.Name.rectTransform.anchorMin = new Vector2(0f, 0.3f);
                view.Name.rectTransform.anchorMax = new Vector2(1f, 0.75f);
                view.Name.rectTransform.offsetMin = view.Name.rectTransform.offsetMax = Vector2.zero;
                view.Key = UiWidgets.CreateLabel("Key", rect, model == null ? string.Empty : model.Slots[i].Key, 24, TextAlignmentOptions.TopLeft);
                view.Key.rectTransform.anchorMin = Vector2.zero;
                view.Key.rectTransform.anchorMax = Vector2.one;
                view.Key.rectTransform.offsetMin = new Vector2(12f, 0f);
                view.Key.rectTransform.offsetMax = new Vector2(0f, -8f);
                view.Key.color = UiSkin.AccentColor;
                view.Sub = UiWidgets.CreateLabel("Sub", rect, string.Empty, 18, TextAlignmentOptions.Bottom);
                view.Sub.rectTransform.anchorMin = Vector2.zero;
                view.Sub.rectTransform.anchorMax = new Vector2(1f, 0.35f);
                view.Sub.rectTransform.offsetMin = view.Sub.rectTransform.offsetMax = Vector2.zero;

                var overlay = new GameObject("Sweep", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var overlayRect = (RectTransform)overlay.transform;
                overlayRect.SetParent(rect, false);
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = new Vector2(6f, 6f);
                overlayRect.offsetMax = new Vector2(-6f, -6f);
                view.Overlay = overlay.GetComponent<Image>();
                view.Overlay.sprite = UiSkin.FlatSprite;
                view.Overlay.type = Image.Type.Filled;
                view.Overlay.fillMethod = Image.FillMethod.Radial360;
                view.Overlay.fillOrigin = (int)Image.Origin360.Top;
                view.Overlay.fillClockwise = false;
                view.Overlay.color = new Color(0f, 0f, 0f, 0.62f);
                view.Overlay.fillAmount = 0f;
                view.Overlay.raycastTarget = false;
                _slots.Add(view);
            }
        }

        private void BuildCombo(RectTransform canvas)
        {
            _comboText = UiWidgets.CreateLabel("ComboCount", canvas, string.Empty, 96, TextAlignmentOptions.Right);
            var r = _comboText.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.sizeDelta = new Vector2(420f, 120f);
            r.anchoredPosition = new Vector2(-48f, 120f);
            _comboText.color = new Color(1f, 0.82f, 0.3f, 1f);
            _comboText.fontStyle = FontStyles.Bold;
            _comboSub = UiWidgets.CreateLabel("ComboLabel", canvas, string.Empty, 34, TextAlignmentOptions.Right);
            var s = _comboSub.rectTransform;
            s.anchorMin = s.anchorMax = new Vector2(1f, 0.5f);
            s.pivot = new Vector2(1f, 0.5f);
            s.sizeDelta = new Vector2(420f, 50f);
            s.anchoredPosition = new Vector2(-48f, 52f);
        }

        private Vector2 ToCanvas(Vector3 world)
        {
            var cam = _stage?.StageCamera;
            if (cam == null || _canvasRect == null)
            {
                return Vector2.zero;
            }

            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local);
            return local;
        }

        private EnemyBar CreateEnemyBar()
        {
            var bar = new EnemyBar();
            var (root, fill) = UiWidgets.CreateProgressBar("EnemyBar", _canvasRect!, new Color(0.85f, 0.2f, 0.18f, 1f));
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(168f, 24f);
            bar.Root = root;
            bar.Fill = fill;
            bar.Back = root.GetComponent<Image>();
            bar.Name = UiWidgets.CreateLabel("EnemyName", root, string.Empty, 22, TextAlignmentOptions.Bottom);
            bar.Name.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.Name.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.Name.rectTransform.pivot = new Vector2(0.5f, 0f);
            bar.Name.rectTransform.sizeDelta = new Vector2(0f, 28f);
            bar.Name.rectTransform.anchoredPosition = Vector2.zero;
            return bar;
        }

        private void LateUpdate()
        {
            var director = _stage?.Showcase;
            if (!_built || director == null)
            {
                return;
            }

            var m = director.Model;
            if (_hpFill != null)
            {
                _hpFill.fillAmount = (float)(m.PlayerMaxHp <= 0 ? 0 : m.PlayerHp / m.PlayerMaxHp);
                _hpText!.text = "生命  " + Math.Ceiling(m.PlayerHp).ToString("0") + " / " + Math.Ceiling(m.PlayerMaxHp).ToString("0");
                _stFill!.fillAmount = (float)(m.PlayerStamina / m.PlayerMaxStamina);
                _stText!.text = "体力";
            }

            for (var i = 0; i < _slots.Count && i < m.Slots.Length; i++)
            {
                var s = m.Slots[i];
                _slots[i].Overlay.fillAmount = (float)s.Fraction;
                _slots[i].Sub.text = s.ComboIndex > 0 && s.LockRemaining > 0 ? "第" + s.ComboIndex + "段" : string.Empty;
            }

            if (_comboText != null)
            {
                if (m.Combo >= 2)
                {
                    _comboText.text = m.Combo.ToString();
                    _comboSub!.text = "连击";
                    var pulse = 1f + Mathf.Max(0f, 0.25f - (float)m.ComboAge) * 1.6f;
                    _comboText.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
                }
                else
                {
                    _comboText.text = string.Empty;
                    _comboSub!.text = string.Empty;
                }
            }

            // 敌人头顶血条。
            var shown = 0;
            var cam = _stage!.StageCamera;
            foreach (var pair in m.Bars)
            {
                if (!_enemyBars.TryGetValue(pair.Key, out var view))
                {
                    view = CreateEnemyBar();
                    _enemyBars[pair.Key] = view;
                }

                var bar = pair.Value;
                var e = director.PositionOf(pair.Key);
                var visible = cam != null && !bar.Hidden && bar.Alive && e.HasValue && bar.SinceHit < 6.0;
                view.Root.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                shown++;
                var head = ToCanvas(new Vector3(e!.Value.x, e.Value.y + director.HeadHeightOf(pair.Key) + 0.1f, 0f));
                view.Root.anchoredPosition = head;
                view.Fill.fillAmount = (float)bar.Fraction;
                view.Name.text = bar.Label;
                view.Fill.color = bar.SinceHit < 0.25 ? new Color(1f, 0.9f, 0.5f, 1f) : new Color(0.85f, 0.2f, 0.18f, 1f);
            }

            foreach (var pair in _enemyBars)
            {
                if (!m.Bars.ContainsKey(pair.Key))
                {
                    pair.Value.Root.gameObject.SetActive(false);
                }
            }

            VisibleEnemyBars = shown;

            // 飘字。
            // 同一目标连续命中的数字按出现时的道次（导演在出字时数同目标在场数字）分道：左中右轮换并逐道抬高，且整体起在名字与血条之上，不压住它们。
            var used = 0;
            foreach (var n in m.Numbers)
            {
                var lane = n.Lane;
                TextMeshProUGUI label;
                if (used < _numberPool.Count)
                {
                    label = _numberPool[used];
                }
                else
                {
                    label = UiWidgets.CreateLabel("Number", _canvasRect!, string.Empty, 40, TextAlignmentOptions.Center);
                    label.rectTransform.sizeDelta = new Vector2(220f, 70f);
                    label.fontStyle = FontStyles.Bold;
                    label.enableWordWrapping = false;
                    _numberPool.Add(label);
                }

                label.gameObject.SetActive(true);
                var t = (float)n.Age;
                var rise = Mathf.Min(1f, t * 3f);
                var p = ToCanvas(new Vector3((float)n.WorldPos.X, (float)n.WorldPos.Y, 0f)) + new Vector2(((lane % 3) - 1) * 56f, NumberBaseLift + 80f * rise + 22f * lane);
                label.rectTransform.anchoredPosition = p;
                label.text = n.Text;
                label.fontSize = n.IsCrit ? 66 : (n.ImpactClass == "heavy" || n.ImpactClass == "massive" ? 54 : 42);
                var a = Mathf.Clamp01(1.4f - t * 1.2f);
                label.color = n.IsCrit ? new Color(1f, 0.82f, 0.25f, a) : (n.IsKill ? new Color(1f, 0.4f, 0.3f, a) : new Color(1f, 1f, 1f, a));
                used++;
            }

            for (var i = used; i < _numberPool.Count; i++)
            {
                _numberPool[i].gameObject.SetActive(false);
            }

            VisibleNumbers = used;
        }

        private void OnDestroy()
        {
            _pack?.Dispose();
            UiSkin.Reset();
        }
    }
}
