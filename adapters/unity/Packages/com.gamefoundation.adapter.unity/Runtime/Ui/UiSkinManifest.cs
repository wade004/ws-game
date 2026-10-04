#nullable enable
// UiSkinManifest：界面资源契约清单（toolchain/asset_import/skin_manifest.json，手感设计/08、ADR-0149）在引擎侧的只读视图。
//
// 清单是唯一权威（导入校验、占位皮肤生成器、人读清单、这里的完整性用例读同一份）；本类只把它解析成元素列表并按槽位/品质展开，
// 供"皮肤画廊"（UiSkinGallery）与完整性用例逐元素核对——游戏运行期并不需要读清单（UiSkinPack 的取用路径是同一份约定的代码化形式，
// 用例逐元素比对两者，任何一边漂移都会红）。
//
// 判断记录（只做核对需要的字段）：路径模板、必备/可选、状态归属、回落路径、尺寸与九宫格规则；颜色令牌与数值令牌供用例生成/核对主题。
using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Adapter.Unity.Ui
{
    /// <summary>清单里的一个元素模板（未展开）。</summary>
    public sealed class UiSkinElement
    {
        public string Id = string.Empty;
        public string Path = string.Empty;
        public string Requirement = "required";
        public string? Expand;
        public string? State;
        public string? StateOf;
        public string? Needs;
        public string? Fallback;
        public string? FallbackState;
        public string Kind = "image";
        public string? Alpha;
        public int SizeMin;
        public int SizeMax;
        public bool Square;
        public string? SizeGroup;
        public string? NineSliceToken;
        public int NineSliceDefault;

        public bool IsImage => Kind == "image";

        public bool IsNineSlice => NineSliceToken != null;
    }

    /// <summary>按数据展开后的一个文件。</summary>
    public readonly struct UiSkinFile
    {
        public UiSkinElement Element { get; }

        /// <summary>包内相对路径（<c>/</c> 分隔）。</summary>
        public string Path { get; }

        /// <summary>展开用到的槽位名/品质名（非展开元素为 null）。</summary>
        public string? Name { get; }

        public UiSkinFile(UiSkinElement element, string path, string? name)
        {
            Element = element;
            Path = path;
            Name = name;
        }
    }

    public sealed class UiSkinManifest
    {
        public int Version { get; private set; }

        public IReadOnlyList<UiSkinElement> Elements => _elements;

        /// <summary>必备颜色令牌键（theme.json 的 colors 下）。</summary>
        public IReadOnlyList<string> ColorTokens => _colorTokens;

        /// <summary>数值令牌（键 → 缺省值），如 panel_border / tooltip_border / button_border。</summary>
        public IReadOnlyDictionary<string, int> NumberTokens => _numberTokens;

        private readonly List<UiSkinElement> _elements = new List<UiSkinElement>();
        private readonly List<string> _colorTokens = new List<string>();
        private readonly Dictionary<string, int> _numberTokens = new Dictionary<string, int>(StringComparer.Ordinal);

        public static UiSkinManifest Parse(string json)
        {
            var root = (JsonObject)JsonReader.Parse(json);
            var manifest = new UiSkinManifest { Version = (int)((JsonNumber)root["manifest_version"]).Value };
            var skin = (JsonObject)root["skin"];
            foreach (var item in (JsonArray)skin["elements"])
            {
                manifest._elements.Add(ParseElement((JsonObject)item));
            }

            var tokens = (JsonObject)skin["tokens"];
            foreach (var c in (JsonArray)tokens["colors"])
            {
                var o = (JsonObject)c;
                manifest._colorTokens.Add(((JsonString)o["key"]).Value);
            }

            foreach (var n in (JsonArray)tokens["numbers"])
            {
                var o = (JsonObject)n;
                manifest._numberTokens[((JsonString)o["key"]).Value] = (int)((JsonNumber)o["default"]).Value;
            }

            return manifest;
        }

        private static string? Str(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : null;

        private static UiSkinElement ParseElement(JsonObject o)
        {
            var e = new UiSkinElement
            {
                Id = Str(o, "id") ?? string.Empty,
                Path = Str(o, "path") ?? string.Empty,
                Requirement = Str(o, "requirement") ?? "required",
                Expand = Str(o, "expand"),
                State = Str(o, "state"),
                StateOf = Str(o, "state_of"),
                Needs = Str(o, "needs"),
                Fallback = Str(o, "fallback"),
                FallbackState = Str(o, "fallback_state"),
                Kind = Str(o, "kind") ?? "image",
                Alpha = Str(o, "alpha"),
            };
            if (o.TryGetValue("size", out var sizeValue) && sizeValue is JsonObject size)
            {
                e.SizeMin = (int)((JsonNumber)size["min"]).Value;
                e.SizeMax = (int)((JsonNumber)size["max"]).Value;
                e.Square = Str(size, "aspect") == "1:1";
                e.SizeGroup = Str(size, "group");
            }

            if (o.TryGetValue("nine_slice", out var nineValue) && nineValue is JsonObject nine)
            {
                e.NineSliceToken = Str(nine, "border_token");
                e.NineSliceDefault = (int)((JsonNumber)nine["default"]).Value;
            }

            return e;
        }

        /// <summary>按槽位名/品质名展开成文件清单（顺序同清单次序）。</summary>
        public List<UiSkinFile> Expand(IReadOnlyList<string> slots, IReadOnlyList<string> qualities)
        {
            var files = new List<UiSkinFile>();
            foreach (var e in _elements)
            {
                if (e.Expand == "slot")
                {
                    foreach (var s in slots)
                    {
                        files.Add(new UiSkinFile(e, e.Path.Replace("{slot}", s), s));
                    }
                }
                else if (e.Expand == "quality")
                {
                    foreach (var q in qualities)
                    {
                        files.Add(new UiSkinFile(e, e.Path.Replace("{quality}", q), q));
                    }
                }
                else
                {
                    files.Add(new UiSkinFile(e, e.Path, null));
                }
            }

            return files;
        }

        public UiSkinElement ById(string id)
        {
            foreach (var e in _elements)
            {
                if (e.Id == id)
                {
                    return e;
                }
            }

            throw new KeyNotFoundException("清单里没有元素 " + id);
        }
    }
}
