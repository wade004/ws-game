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
// 字体判断记录（缺口 1 已解决，约定见包 README"资源 id → 路径规则"）：DrawText 的 fontId 参数
// 现按 id 区分具体字体资源——UnityResourceLoader 已经把 Font 种类的 LoadAsync 改为在主线程调用
// Resources.Load<Font>("Fonts/<name>")（<name> 为 fontId 去掉 "font." 前缀、点号换下划线，见
// UnityResourceLoader.cs 顶部"判断记录（Font 资源种类）"）；本类型对每个 fontId 各自缓存一份
// 由 TMPro.TMP_FontAsset.CreateFontAsset 生成的 TMP 字体资产（TMP_FontAsset 本身不能跨字体共享，
// 每个不同的底层 UnityEngine.Font 需要各自生成一次并缓存复用）。ResolveFontAsset 不依赖调用方
// 事先调用 IResourceLoader.LoadAsync（DrawText 契约本身没有"资源必须已加载"这一前置要求）——
// 直接同步调用 Resources.Load 判定该 fontId 对应的资产是否存在：存在则生成/复用对应
// TMP_FontAsset；不存在（未导入该字体资产，或 fontId 是未知/占位测试值）则回退到包内默认占位
// 字体（原有的单一路径，见下 ResolveDefaultFontAsset），仍找不到才最终回退 TMP 内置默认字体
// （TMP_Settings.defaultFontAsset）；每一次回退都 Debug.LogWarning 一次诊断，不抛异常。这是
// 一个已知限制（字体资产必须先被 Unity 资产管线导入，运行期无法从任意字节数组生成），已记录在
// 包 README。
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
        /// <summary>无法按 fontId 解析出具体字体资产时的最终回退路径（缺口 1 解决前的唯一路径，
        /// 现降级为"回退"角色，见类型顶部字体判断记录）。</summary>
        private const string PlaceholderFontResourcePath = "Fonts/noto_sans_cjk_sc";

        private readonly Transform _root;
        private readonly Dictionary<Id, Canvas> _surfaces = new Dictionary<Id, Canvas>();
        private readonly Dictionary<Id, string> _layouts = new Dictionary<Id, string>();
        private readonly Dictionary<Id, GameObject> _focusableElements = new Dictionary<Id, GameObject>();
        private Id? _focusedElement;

        /// <summary>按 fontId 缓存生成好的 TMP 字体资产（缺口 1 已解决：fontId 现在真的区分具体
        /// 字体资源，见类型顶部字体判断记录）。</summary>
        private readonly Dictionary<Id, TMP_FontAsset> _fontAssetsByFontId = new Dictionary<Id, TMP_FontAsset>();

        /// <summary>fontId 找不到对应字体资产时记过一次诊断的集合，避免同一个未知 fontId 每次
        /// DrawText 都刷一遍警告日志。</summary>
        private readonly HashSet<Id> _missingFontIdsWarned = new HashSet<Id>();

        private TMP_FontAsset? _placeholderFontAsset;
        private bool _placeholderFontLoadAttempted;

        public UnityUISurface(Transform root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            // 判断记录（引擎侧收口任务，根治"There are 2 event systems in the scene"）：此前用
            // EventSystem.current == null 判定"场景里还没有 EventSystem"——但 EventSystem.current
            // 只在某个 EventSystem 实例的 OnEnable() 真正跑过之后才会被赋值，与"场景里是否已经
            // 存在一个 EventSystem GameObject（如 GreyBox.unity/Shell.unity 场景自带的那个，见
            // Assets/Editor/GreyBoxSceneBuilder.cs/ShellSceneBuilder.cs）"是两回事——Unity 不保证
            // 不同 GameObject 的 Awake/OnEnable 执行顺序，若本类型（随 UnityEngineHost.Awake 一并
            // 构造）先于场景自带 EventSystem 的 OnEnable 执行，EventSystem.current 此时仍是 null，
            // 本构造函数因此会再建一个 DontDestroyOnLoad 的 "GameFoundation.EventSystem"，随后
            // 场景自带的那个也完成 OnEnable，两者同时存在，UGUI 记一条
            // "There are 2 event systems in the scene" 警告（实测复现：VerticalSliceTests 恢复
            // LogAssert.NoUnexpectedReceived() 收尾检查后被记成"未预期日志"判为失败）。改用
            // FindFirstObjectByType 直接扫描场景对象图本身是否已经挂着一个 EventSystem（不依赖
            // 它的 OnEnable 是否已经跑过），才是与"场景是否已经有一个"这句字面意图相符的判定，
            // 消除这条时序竞争。
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
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

        /// <summary>按 fontId 解析出对应的 TMP 字体资产（缺口 1 已解决，见类型顶部字体判断记录）：
        /// 先查本实例缓存；未缓存则按"font.&lt;name&gt; -&gt; Resources/Fonts/&lt;name&gt;"规则同步
        /// 尝试 <c>Resources.Load</c> + <c>TMP_FontAsset.CreateFontAsset</c>；解析不到（字体资产
        /// 未导入、fontId 本身是未知/占位测试值等）则记一条诊断（同一 fontId 只记一次，避免刷屏）并
        /// 回退到包内默认占位字体，仍失败才最终回退 TMP 内置默认字体。</summary>
        private TMP_FontAsset ResolveFontAsset(Id fontId)
        {
            if (_fontAssetsByFontId.TryGetValue(fontId, out var cached))
            {
                return cached;
            }

            var resolved = TryCreateFontAssetForId(fontId);
            if (resolved != null)
            {
                _fontAssetsByFontId[fontId] = resolved;
                return resolved;
            }

            if (_missingFontIdsWarned.Add(fontId))
            {
                Debug.LogWarning(
                    $"[UnityUISurface] fontId \"{fontId}\" 未解析到可用字体资产" +
                    $"（Resources/Fonts/{ResourcesFontName(fontId)} 不存在或生成失败），回退到默认占位字体。");
            }

            return ResolveDefaultFontAsset();
        }

        /// <summary>按 fontId 尝试解析字体资产，解析不到返回 null（不记诊断——由调用方
        /// <see cref="ResolveFontAsset"/> 统一决定是否记录，避免重复日志）。</summary>
        private static TMP_FontAsset? TryCreateFontAssetForId(Id fontId)
        {
            var osFont = Resources.Load<Font>("Fonts/" + ResourcesFontName(fontId));
            if (osFont == null)
            {
                return null;
            }

            try
            {
                return TMP_FontAsset.CreateFontAsset(osFont);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[UnityUISurface] fontId \"{fontId}\" 对应字体资产 TMP_FontAsset.CreateFontAsset 抛出异常：{ex.Message}。");
                return null;
            }
        }

        /// <summary>"font.&lt;name&gt;" -&gt; "&lt;name&gt;"：去掉 id 第一个点分段（类别前缀）、
        /// 剩余点号换下划线，与 UnityResourceLoader.StripCategoryPrefix 同一套规则（04 文档"逻辑 id
        /// 与底层资源解耦"约定）。两处各自维护一份是因为二者分处不同文件、职责独立，逻辑本身足够
        /// 简单，重复一次比新增跨文件私有依赖更简单。</summary>
        private static string ResourcesFontName(Id fontId)
        {
            var value = fontId.Value;
            var dotIndex = value.IndexOf('.');
            var withoutCategory = dotIndex < 0 ? value : value.Substring(dotIndex + 1);
            return withoutCategory.Replace('.', '_');
        }

        /// <summary>无法按 fontId 解析出字体资产时的最终回退：包内默认占位字体
        /// （<see cref="PlaceholderFontResourcePath"/>），仍失败才回退 TMP 内置默认字体
        /// （缺口 1 解决前的唯一路径，现降级为兜底角色）。</summary>
        private TMP_FontAsset ResolveDefaultFontAsset()
        {
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
