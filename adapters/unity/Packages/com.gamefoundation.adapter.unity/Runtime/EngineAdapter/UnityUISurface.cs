#nullable enable
// UnityUISurface：IUISurface 的 Unity 引擎实现，基于 uGUI Canvas + TextMeshPro。
//
// CreateSurface 创建一个 Screen Space - Overlay 的 Canvas（承载面），SetLayout 只记录传入的
// 不透明布局字符串（02 第 1.10 节"layoutData 是结构化布局描述，格式待技术选型阶段确定，
// 本层只当作不透明字符串传递"——本实现忠实照做，不解析）。
//
// DrawText 判断记录：契约没有给"一段绘制的文本"返回任何句柄或标识（不同于 IRenderer2D/
// IRenderer3D 的保留句柄模式），逐帧调用即"新画一段文字"是最贴近字面语义的解释；本实现按
// 调用顺序累加创建 TMP_Text 子元素，不去重、不跨帧清空——上层如需要每帧刷新的动态文本，应自行
// 在合适时机调用非契约方法 ClearSurface 复位（同 adapters/stub 系"契约之外的协作方法"惯例）。
//
// 字体判断记录（对应任务书"若批处理下生成字体资产困难，用 Resources/StreamingAssets 路径运行期
// 创建 TMP_FontAsset.CreateFontAsset"）：DrawText 的 fontId 参数目前不区分不同字体资源——
// UnityResourceLoader 的 Font 资源种类只能提供原始字节，无法在运行期从任意字节数组产出可用于
// TMP 渲染的字体资产（见 UnityResourceLoader.cs 顶部判断记录）；本实现改为使用包内已经被 Unity
// 资产管线正常导入过的占位字体（adapters/unity/Assets/Framework/Resources/Fonts/
// NotoSansCJKsc-Regular.otf，导入后是一个可以被 Resources.Load<Font> 取到的 UnityEngine.Font
// 对象），在首次使用时调用 TMPro.TMP_FontAsset.CreateFontAsset 动态生成一份运行期 TMP 字体资产
// 并缓存复用；找不到该 Font 资源或生成失败时回退到 TMP 内置默认字体（TMP_Settings.
// defaultFontAsset），并 Debug.LogWarning 一次。这是一个已知限制，已记录在包 README。
//
// 判断记录（TMP 运行期依赖）：实测 TMP_FontAsset.CreateFontAsset 与 TMP_Settings.
// defaultFontAsset 在"一个全新 Unity 工程从未打开过 TextMeshPro 相关窗口"的批处理环境下都会
// 因为 TMP_Settings 单例不存在、SDF 着色器未被资产管线导入而抛异常——这两项内容
// （TMP_Settings.asset、TMP_SDF*.shader 等）在 com.unity.ugui 包里并非普通会被自动导入的
// 资产，只打包在包内 "Package Resources/TMP Essential Resources.unitypackage" 中，需要通过
// 官方"Import TMP Essential Resources"流程解包；由于
// UnityEditor.AssetDatabase.ImportPackage 在无窗口批处理下是异步的、无法可靠等到其真正完成
// 就已经 -quit 退出，因此改为一次性把该 .unitypackage 解包后的标准内容直接提交进仓库
// （adapters/unity/Assets/TextMesh Pro/，与手动执行"Import TMP Essential Resources"菜单项
// 产生的文件完全一致，含相同 GUID），使其成为像其它 Assets 资产一样的普通版本库内容，
// 不再需要任何运行期或工具链导入步骤。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityUISurface : IUISurface
    {
        private const string PlaceholderFontResourcePath = "Fonts/NotoSansCJKsc-Regular";

        private readonly Transform _root;
        private readonly Dictionary<Id, Canvas> _surfaces = new Dictionary<Id, Canvas>();
        private readonly Dictionary<Id, string> _layouts = new Dictionary<Id, string>();
        private readonly Dictionary<Id, GameObject> _focusableElements = new Dictionary<Id, GameObject>();
        private Id? _focusedElement;
        private TMP_FontAsset? _placeholderFontAsset;
        private bool _placeholderFontLoadAttempted;

        public UnityUISurface(Transform root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            if (EventSystem.current == null)
            {
                var esGo = new GameObject("GameFoundation.EventSystem");
                esGo.transform.SetParent(_root, worldPositionStays: false);
                esGo.AddComponent<EventSystem>();
                // 判断记录：工程已把 Active Input Handling 切到 Input System Package
                // （见 Editor/ProjectSetup.cs ConfigureInputSystem），旧版 StandaloneInputModule
                // 内部读取 UnityEngine.Input.mousePosition 会直接抛
                // InvalidOperationException；必须用 Input System 包自带的
                // InputSystemUIInputModule 驱动 UGUI 的事件系统。
                esGo.AddComponent<InputSystemUIInputModule>();
            }
        }

        public void CreateSurface(Id surfaceId, int width, int height)
        {
            if (_surfaces.TryGetValue(surfaceId, out var existing))
            {
                var existingRect = existing.GetComponent<RectTransform>();
                existingRect.sizeDelta = new Vector2(width, height);
                return;
            }

            var go = new GameObject($"Surface_{surfaceId.Value}");
            go.transform.SetParent(_root, worldPositionStays: false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);

            go.AddComponent<GraphicRaycaster>();

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            _surfaces[surfaceId] = canvas;
        }

        public void SetLayout(Id surfaceId, string layoutData)
        {
            _layouts[surfaceId] = layoutData;
        }

        public void DrawText(Id surfaceId, string text, Vec2 position, Id fontId, double size)
        {
            if (!_surfaces.TryGetValue(surfaceId, out var canvas))
            {
                throw new InvalidOperationException($"UISurface \"{surfaceId}\" 尚未 CreateSurface");
            }

            var go = new GameObject("Text");
            go.transform.SetParent(canvas.transform, worldPositionStays: false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = (float)size;
            tmp.font = ResolveFontAsset(fontId);
            tmp.raycastTarget = false;

            var rect = tmp.rectTransform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2((float)position.X, (float)position.Y);
            rect.sizeDelta = new Vector2(1000, 200);
        }

        public void SetFocus(Id elementId)
        {
            _focusedElement = elementId;
            if (_focusableElements.TryGetValue(elementId, out var go) && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(go);
            }
        }

        public Id? GetFocusedElement() => _focusedElement;

        /// <summary>非契约方法：清空一个 surface 下用 DrawText 累积创建的全部文本元素，
        /// 供逐帧刷新型的调用方在重绘前复位（见类型顶部"DrawText 判断记录"）。</summary>
        public void ClearSurface(Id surfaceId)
        {
            if (!_surfaces.TryGetValue(surfaceId, out var canvas)) return;

            for (var i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(canvas.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>非契约方法：登记一个可获得输入焦点的 UI 元素，使 SetFocus 能驱动
        /// EventSystem 的实际选中状态（IUISurface 契约本身没有"创建可交互控件"的方法，
        /// 具体控件由 09 表现层在其之上搭建）。</summary>
        public void RegisterFocusable(Id elementId, GameObject element)
        {
            _focusableElements[elementId] = element;
        }

        public string? GetLayoutForTest(Id surfaceId) => _layouts.TryGetValue(surfaceId, out var layout) ? layout : null;

        private TMP_FontAsset ResolveFontAsset(Id fontId)
        {
            // fontId 目前不区分具体字体资源，见类型顶部"字体判断记录"。
            if (_placeholderFontAsset != null) return _placeholderFontAsset;

            if (!_placeholderFontLoadAttempted)
            {
                _placeholderFontLoadAttempted = true;
                _placeholderFontAsset = TryCreatePlaceholderFontAsset();
            }

            if (_placeholderFontAsset != null) return _placeholderFontAsset;

            var fallback = TMP_Settings.defaultFontAsset;
            if (fallback == null)
            {
                throw new InvalidOperationException(
                    "找不到任何可用的 TMP 字体资产：占位字体生成失败且 TMP_Settings.defaultFontAsset 未配置");
            }

            return fallback;
        }

        private static TMP_FontAsset? TryCreatePlaceholderFontAsset()
        {
            var osFont = Resources.Load<Font>(PlaceholderFontResourcePath);
            if (osFont == null)
            {
                Debug.LogWarning(
                    $"[UnityUISurface] 未找到占位字体资源 Resources/{PlaceholderFontResourcePath}，回退到 TMP 内置默认字体。");
                return null;
            }

            try
            {
                var fontAsset = TMP_FontAsset.CreateFontAsset(osFont);
                if (fontAsset == null)
                {
                    Debug.LogWarning("[UnityUISurface] TMP_FontAsset.CreateFontAsset 返回空，回退到 TMP 内置默认字体。");
                }
                return fontAsset;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityUISurface] TMP_FontAsset.CreateFontAsset 抛出异常：{ex.Message}，回退到 TMP 内置默认字体。");
                return null;
            }
        }
    }
}
