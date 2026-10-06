#nullable enable
// SkinTestKit：界面皮肤完整性用例的公用件（手感设计/08、ADR-0149）。
//
// 一切都是确定性的程序生成：按界面资源契约清单（toolchain/asset_import/skin_manifest.json）给每个展开文件写一张图，颜色由"种子 + 相对路径"的散列算出，
// 所以"这张精灵来自哪个皮肤包的哪个文件"可以直接从像素读回来（Identify），不依赖文件名或对象引用。皮肤包写在临时内容根里，
// 占位皮肤（skin.default）是仓库里真实的那一份拷贝——回落链与"换皮肤"都在真实素材之上验证。
using FeelLab.Unity;
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.Ui;
using UnityEngine;

namespace Framework.RealAssetTests
{
    internal static class SkinTestKit
    {
        public const int AltSeed = 7;

        /// <summary>替换皮肤里的数值令牌（取非缺省值，证明运行期读的是 theme.json 而不是写死的缺省）。</summary>
        public const int PanelBorder = 7;
        public const int TooltipBorder = 3;
        public const int ButtonBorder = 6;

        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        public static UiSkinManifest LoadManifest() =>
            UiSkinManifest.Parse(File.ReadAllText(Path.Combine(RepoRoot, "toolchain", "asset_import", "skin_manifest.json")));

        public static Color32 ColorOf(string path, int seed)
        {
            // FNV-1a 64 位散列，取三个字节映射到 40..215，避开透明/纯白/纯黑附近。
            ulong h = 1469598103934665603UL;
            foreach (var ch in seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + path)
            {
                h ^= ch;
                h *= 1099511628211UL;
            }

            return new Color32((byte)(40 + (h & 0xFF) % 176), (byte)(40 + ((h >> 8) & 0xFF) % 176), (byte)(40 + ((h >> 16) & 0xFF) % 176), 255);
        }

        public static string ThemeColorHex(string key, int seed)
        {
            var c = ColorOf("theme/" + key, seed);
            return $"#{c.r:x2}{c.g:x2}{c.b:x2}";
        }

        /// <summary>从精灵读回"它是哪个元素"的颜色：取纹理底边向上第 2 行的水平中点（描边框的 3 像素外沿、实心图的任意处都落在元素颜色上）。</summary>
        public static Color32 Identify(Sprite sprite)
        {
            var t = sprite.texture;
            return t.GetPixel(t.width / 2, 1);
        }

        public static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        public static string NewContentRoot(string label)
        {
            var root = Path.Combine(Application.temporaryCachePath, "skin_tests", label + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            CopyDirectory(
                Path.Combine(RepoRoot, "assets", "_placeholder", "ui", "skin", "default"),
                Path.Combine(root, "ui", "skin", "default"));
            return root;
        }

        public static void Delete(string root)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(from))
            {
                CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
            }
        }

        private static (int W, int H) SizeOf(UiSkinFile file)
        {
            switch (file.Path)
            {
                case "tooltip/divider.png": return (64, 4);
                case "tooltip/row.png": return (96, 16);
                case "paperdoll_preview/background.png": return (160, 192);
            }

            return file.Element.IsNineSlice ? (32, 32) : (48, 48);
        }

        /// <summary>
        /// 写一个"全元素"替换皮肤包到 <c>&lt;root&gt;/ui/skin/&lt;name&gt;</c>：清单展开的每个图片文件一张（框类元素是 3 像素描边的镂空框，其余实心），
        /// 主题令牌各取唯一颜色，数值令牌取非缺省值。<paramref name="skip"/> 返回 true 的文件不写（用来造"缺必备项/缺可选项"）。
        /// </summary>
        public static void WriteAltPack(string root, string name, UiSkinManifest manifest, IReadOnlyList<string> slots, IReadOnlyList<string> qualities, int seed, Func<string, bool>? skip = null)
        {
            var dir = Path.Combine(root, "ui", "skin", name);
            foreach (var file in manifest.Expand(slots, qualities))
            {
                if (!file.Element.IsImage || (skip != null && skip(file.Path)))
                {
                    continue;
                }

                var (w, h) = SizeOf(file);
                var hollow = file.Element.Alpha == "has_transparency" || file.Element.Alpha == "transparent_center";
                var color = ColorOf(file.Path, seed);
                var pixels = new Color32[w * h];
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var ring = x < 3 || y < 3 || x >= w - 3 || y >= h - 3;
                        pixels[y * w + x] = !hollow || ring ? color : new Color32(0, 0, 0, 0);
                    }
                }

                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                var target = Path.Combine(dir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, ImageConversion.EncodeToPNG(texture));
                UnityEngine.Object.DestroyImmediate(texture);
            }

            var colors = new List<string>();
            foreach (var key in manifest.ColorTokens)
            {
                colors.Add($"\"{key}\": \"{ThemeColorHex(key, seed)}\"");
            }

            var theme = "{\"colors\": {" + string.Join(", ", colors) + "}, \"font\": \"fonts/noto_sans_cjk_sc.otf\", "
                + $"\"panel_border\": {PanelBorder}, \"tooltip_border\": {TooltipBorder}, \"button_border\": {ButtonBorder}}}";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "theme.json"), theme);
        }
    }
}
