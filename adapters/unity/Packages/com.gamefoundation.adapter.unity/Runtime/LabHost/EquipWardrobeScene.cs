#nullable enable
// EquipWardrobeScene：实验室换装场景的屏幕宿主（手感设计/06 第 3.6 节、08 第 3/6 节、ADR-0149）。
//
// 屏幕上有：装备面板（生产类的 EquipmentPanel，绑生产类的 EquipmentViewModel，槽位网格 + 纸娃娃预览区，预览方向可切换）、
// "方向 × 姿势键"轮播区（身体层剪辑 + 当前装备图层剪辑逐帧叠放）、四个按钮（穿下一件 / 全部卸下 / 轮播下一格 / 跑完整报告）与状态行。
// "逐件穿戴"（autoAdvance）按数据清单（EquipWardrobe.ListEquippable）逐件穿上，穿完卸空再来一遍；穿脱走真实背包与装备载体（WardrobeStage），
// 面板里的数量是真实装备状态与数据算出来的。"跑完整报告"调用 EquipWardrobeRunner.Run：在引擎宿主上跑同一份数据生成的衣橱脚本，核对图标与每个
// 方向/姿势键的图层资源，写出本地报告 JSON 与拼图 PNG（lab/out/wardrobe/）。
//
// 判断记录（只读展示 + 本地产物）：场景不改任何数据、不进指纹；游戏接入时把 bodySet 与数据根换成自己的即可（场景不读写死的路径）。
using System;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Ui;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Lab;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.LabHost
{
    public sealed class EquipWardrobeScene : MonoBehaviour
    {
        [SerializeField] private string cell = "2d_action";
        [SerializeField] private string bodySet = "placeholder_hero";
        [SerializeField] private string repoRoot = string.Empty;
        [SerializeField] private string skinRef = string.Empty;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool autoAdvance = true;
        [SerializeField] private float stepSeconds = 0.7f;

        private UiRoot? _ui;
        private TextMeshProUGUI? _status;
        private Image? _carouselBody;
        private Image? _carouselLayer;
        private float _timer;
        private float _clipTime;
        private int _stepIndex;
        private bool _ended;
        private bool _skinInstalled;

        /// <summary>
        /// 界面皮肤包引用（如 <c>skin.reference_fantasy</c>，包在资源内容根的 <c>ui/skin/&lt;名&gt;/</c>）；空 = 占位皮肤。必须在 <see cref="Begin"/> 之前设定。
        /// 判断记录：此前场景写死 <c>UiSkinPack.Load(null)</c>，装备面板与衣橱在实验室里只能看占位皮肤，真皮肤包要等游戏接入才第一次被面板渲染；
        /// 现在非占位皮肤会像生产宿主一样安装皮肤覆盖（面板底图、按钮、主题色），场景销毁时复位。
        /// </summary>
        public string SkinRef
        {
            get => skinRef;
            set => skinRef = value ?? string.Empty;
        }

        public EngineLabHost? Host { get; private set; }

        public WardrobeStage? Stage { get; private set; }

        public EquipmentPanel? Panel { get; private set; }

        public UiVisuals? Visuals { get; private set; }

        public WardrobeCarouselModel? Carousel { get; private set; }

        /// <summary>最近一次"跑完整报告"的结果（没跑过为 null）。</summary>
        public WardrobeEngineResult? Result { get; private set; }

        /// <summary>已穿上的件数（逐件穿戴游标）。</summary>
        public int StepIndex => _stepIndex;

        public string Status => _status != null ? _status.text : string.Empty;

        private void Start()
        {
            if (autoStart)
            {
                Begin();
            }
        }

        /// <summary>搭舞台与界面。<paramref name="host"/> 缺省按仓库根打开；<paramref name="runReport"/> 为真时先跑一遍完整报告（轮播要用它的姿势族与剪辑引用）。</summary>
        public void Begin(EngineLabHost? host = null, bool runReport = false)
        {
            if (Stage != null)
            {
                return;     // 已经搭过（外部先 Begin 了、Start 再来一次）：不重复搭。
            }

            Host = host ?? (repoRoot.Length > 0 ? new EngineLabHost(repoRoot) : EngineLabHost.Open());
            InputScript? template = null;
            foreach (var script in Host.LoadScripts())
            {
                if (string.Equals(script.Meta.ScriptId, EquipWardrobeRunner.TemplateScript, StringComparison.Ordinal))
                {
                    template = script;
                    break;
                }
            }

            if (template == null)
            {
                throw new InvalidOperationException("夹具里没有模板脚本 " + EquipWardrobeRunner.TemplateScript);
            }

            Stage = WardrobeStage.Create(Host.Runner, template);
            var pack = UiSkinPack.Load(skinRef.Length > 0 ? skinRef : null);
            var skinOverride = pack.CreateOverride();
            if (skinOverride != null)
            {
                UiSkin.Install(skinOverride);
                _skinInstalled = true;
            }

            Visuals = new UiVisuals(pack, Stage.Registry, Stage.DisplayInfo);
            Carousel = new WardrobeCarouselModel(Stage.Entries, PaperdollPreview.Directions);

            _ui = UiRoot.Create("EquipWardrobeUi");
            _ui.transform.SetParent(transform, false);

            var panelGo = UiWidgets.CreateRoot("EquipmentPanelHost", _ui.Content);
            Panel = panelGo.gameObject.AddComponent<EquipmentPanel>();
            var layout = Visuals.LayoutOf(global::Presentation.Ui.UiPanel.Equipment, EquipmentPanel.DefaultLayout);
            layout = new UiPanelLayout(layout.Anchor, layout.Columns, layout.CellSize, layout.PreviewScale, bodySet, layout.PreviewDirection);
            Panel.Construct(panelGo, Stage.Panel, Visuals, null, layout);
            Panel.SlotClicked += slot => Stage.Unequip(slot.Value);

            BuildControls();
            if (runReport)
            {
                RunReport();
            }

            Panel.RefreshUi();
        }

        private void BuildControls()
        {
            var bar = UiWidgets.CreatePanelBackground("Controls", _ui!.Content, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(560f, 150f), new Vector2(0f, 90f));
            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(bar, false);
            UiWidgets.SetRect(rowRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -38f), new Vector2(-8f, -8f));
            var group = row.GetComponent<HorizontalLayoutGroup>();
            group.spacing = 6f;
            group.childForceExpandWidth = true;
            group.childControlWidth = true;
            UiWidgets.CreateButton("EquipNext", rowRect, "Equip next", () => StepNext());
            UiWidgets.CreateButton("UnequipAll", rowRect, "Unequip all", () => { Stage!.UnequipAll(); _stepIndex = 0; });
            UiWidgets.CreateButton("CarouselNext", rowRect, "Carousel >", () => AdvanceCarousel());
            UiWidgets.CreateButton("Report", rowRect, "Run report", () => RunReport());

            _status = UiWidgets.CreateLabel("Status", bar, string.Empty, 14, TextAlignmentOptions.TopLeft);
            UiWidgets.SetRect(_status.rectTransform, new Vector2(0f, 0f), new Vector2(0.65f, 1f), new Vector2(10f, 8f), new Vector2(0f, -44f));

            var area = new GameObject("CarouselView", typeof(RectTransform));
            var areaRect = (RectTransform)area.transform;
            areaRect.SetParent(bar, false);
            UiWidgets.SetRect(areaRect, new Vector2(0.66f, 0f), new Vector2(1f, 1f), new Vector2(0f, 8f), new Vector2(-8f, -44f));
            _carouselBody = CreateCarouselImage("Body", areaRect);
            _carouselLayer = CreateCarouselImage("Layer", areaRect);
        }

        private static Image CreateCarouselImage(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            UiWidgets.SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            return image;
        }

        /// <summary>穿下一件（按数据清单次序）；穿完一圈后卸空并回到第一件，返回 false 表示这一步是"卸空回绕"。</summary>
        public bool StepNext()
        {
            var stage = Stage ?? throw new InvalidOperationException("场景还没有 Begin");
            if (_stepIndex >= stage.Entries.Count)
            {
                stage.UnequipAll();
                _stepIndex = 0;
                Panel?.RefreshUi();
                return false;
            }

            stage.Equip(stage.Entries[_stepIndex].ItemId);
            _stepIndex++;
            Panel?.RefreshUi();
            return true;
        }

        public void AdvanceCarousel()
        {
            Carousel?.Next();
            _clipTime = 0f;
        }

        /// <summary>跑完整报告（引擎宿主上的衣橱脚本 + 方向 × 姿势键轮播核对 + 本地产物），状态行显示摘要。</summary>
        public WardrobeEngineResult RunReport()
        {
            var host = Host ?? throw new InvalidOperationException("场景还没有 Begin");
            Result = EquipWardrobeRunner.Run(host, cell, null, bodySet);
            return Result;
        }

        private void Update()
        {
            if (Stage == null || Panel == null || _ended)
            {
                return;
            }

            if (autoAdvance)
            {
                _timer += Time.unscaledDeltaTime;
                if (_timer >= stepSeconds)
                {
                    _timer = 0f;
                    StepNext();
                    AdvanceCarousel();
                }
            }

            Panel.RefreshUi();
            UpdateCarousel();
            if (_status != null)
            {
                var report = Result != null ? EquipWardrobe.Summary(Result.Report) : "(report not run)";
                var cellText = Carousel != null && !Carousel.IsEmpty ? $"{Carousel.Item.ItemId} / {Carousel.Pose} / {Carousel.Direction}" : "-";
                _status.text = $"worn {Stage.Panel.OccupiedCount}/{Stage.SlotCount}  layers {Stage.Panel.PaperdollLayers.Count}\ncarousel {cellText}\n{report}";
            }
        }

        private void UpdateCarousel()
        {
            if (Carousel == null || Carousel.IsEmpty || Result == null || _carouselBody == null || _carouselLayer == null)
            {
                return;
            }

            WardrobeCarouselCell? current = null;
            foreach (var c in Result.Carousel)
            {
                if (c.Item == Carousel.Item.ItemId && c.Pose == Carousel.Pose && c.Direction == Carousel.Direction)
                {
                    current = c;
                    break;
                }
            }

            if (current == null || current.Resource.Length == 0)
            {
                _carouselBody.enabled = false;
                _carouselLayer.enabled = false;
                return;
            }

            // 图层剪辑 id：sprite_anim.<集>__<剪辑>__<方向>__<层>；身体剪辑 = sprite_anim.<剪辑>__<方向>__body。
            var parts = current.Resource.Split(new[] { "__" }, StringSplitOptions.None);
            if (parts.Length != 4)
            {
                return;
            }

            _clipTime += Time.unscaledDeltaTime;
            ApplyClipFrame(_carouselBody, "sprite_anim." + parts[1].Replace("sprite_anim.", string.Empty) + "__" + parts[2] + "__body");
            ApplyClipFrame(_carouselLayer, current.Resource);
        }

        private void ApplyClipFrame(Image image, string resource)
        {
            var loader = UnityEngineHost.Ensure().ResourceLoader;
            var id = new Id(resource);
            if (!loader.TryGetEffect(id, out var asset))
            {
                loader.LoadAsync(id, ResourceKind.Effect, (rid, ok) => { });
                image.enabled = false;
                return;
            }

            if (asset.Frames.Length == 0)
            {
                image.enabled = false;
                return;
            }

            double total = 0;
            foreach (var f in asset.Frames)
            {
                total += f.Duration > 0 ? f.Duration : 0.1;
            }

            var t = total > 0 ? _clipTime % total : 0;
            var index = 0;
            foreach (var f in asset.Frames)
            {
                var d = f.Duration > 0 ? f.Duration : 0.1;
                if (t < d)
                {
                    break;
                }

                t -= d;
                index++;
            }

            image.sprite = asset.Frames[Mathf.Min(index, asset.Frames.Length - 1)].Sprite;
            image.enabled = image.sprite != null;
        }

        private void OnDestroy()
        {
            _ended = true;
            Stage?.Dispose();
            Visuals?.Dispose();
            if (_skinInstalled)
            {
                UiSkin.Reset();
                _skinInstalled = false;
            }

            if (_ui != null)
            {
                Destroy(_ui.gameObject);
            }
        }
    }
}
