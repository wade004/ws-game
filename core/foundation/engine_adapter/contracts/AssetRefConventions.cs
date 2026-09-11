using System;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 32 条（[ADR-0025](../../../../architecture/adr/0025-资源引用标识到资产相对路径的约定纳入公开契约.md)）：
    /// 把"资源引用 id → 资产相对路径"这条此前只存在于各处私有实现里的约定（见下方"判断记录（此前的
    /// 重复实现）"）收口为单一公开静态类，供引擎适配层与内容工具共同引用，不再各自维护一份、容易
    /// 漂移的拷贝。
    /// <para>
    /// 覆盖范围：本类型只收口 <c>display.map</c> 的 <c>sprite_set_id</c>/<c>icon_id</c> 两个字段
    /// （04 第 7.1 节，架构文档 14_资产规格书模板.md 第 1.2 节"命名规则"给出的规格书口径）目前
    /// 已有真实消费方（<c>Core.Foundation.EngineAdapter.IResourceLoader</c> 实现、
    /// <c>presentation/render</c>、<c>toolchain/asset_import</c>）在用的两条具体规则；14 第 1.2
    /// 节描述的"通用扁平文件名模板"覆盖更广的资源引用 id 集合（<c>model_ref</c>/<c>resource_ref</c>
    /// 等），本次不展开收口——那些字段目前各自的调用点（<c>UnityResourceLoader.ResolveModelResourcesPath</c>
    /// 等）已各自独立、无重复实现，不构成消费方反馈第 32 条指出的"无公开函数"问题（该问题原文特指
    /// <c>sprite_set_id</c>/<c>icon_id</c> 两个字段，见回复文档）。
    /// </para>
    /// <para>
    /// 判断记录（此前的重复实现，本次收口的对象）：在新增本类型之前，"去掉资源引用 id 的类别前缀
    /// （第一个点分段），剩余部分把点号换成下划线"这条规则在仓库里至少有两处相互独立的私有实现——
    /// <c>Adapter.Unity.EngineAdapter.UnityResourceLoader.StripCategoryPrefix</c>（引擎适配层，消费
    /// 侧）与 <c>Presentation.Render.Core.SpriteViewBase.StripCategoryPrefix</c>（框架表现层，产出
    /// 纸娃娃层资源 id 时用来编码 <c>spriteSetName</c> 片段）；两者字节级相同但各自维护，任何一处
    /// 修改都不会自动同步到另一处。<c>icon_id → 资产相对路径</c>这条规则则完全没有公开实现——
    /// 只存在于 <c>toolchain/asset_import/sprite_cmd.py</c>/<c>icon_cmd.py</c> 两个 Python 脚本里，
    /// C# 侧从未表达过，编辑器等消费方只能照抄 <c>data/_sample</c> 的具体样例反推（见回复文档"现行
    /// 约定原文"一节）。本类型新增后，上述两处 <c>StripCategoryPrefix</c> 私有实现均已改为转发到
    /// <see cref="StripCategoryPrefix"/>，不再各自持有独立副本；Python 侧新增
    /// <c>toolchain/asset_import/ref_conventions.py</c> 按同一规则独立实现（两种语言无法共享同一份
    /// 源码，靠 <c>toolchain/tests/test_ref_conventions.py</c> 与本类型对应的 C# 单元测试用同一组
    /// 样例互相对照，任一侧改动规则而另一侧未同步会被测试捕获，见该测试文件判断记录）。
    /// </para>
    /// </summary>
    public static class AssetRefConventions
    {
        /// <summary>去掉资源引用 id 的"类别前缀"（第一个点分段，如 <c>sprite.creature.wolf_grey</c>
        /// 的 <c>sprite</c>、<c>icon.item.sample_blade</c> 的 <c>icon</c>），剩余部分把点号换成
        /// 下划线（架构文档 14 第 1.2 节命名模板"资源引用 id 去掉类别前缀，点号换下划线"）。id 不含
        /// 点号时（不满足 04 第 2.1 节 id 格式，理论不应发生）原样返回，不抛异常——与此前两处私有实现
        /// 的既有容错行为一致，调用方按后续解析结果是否合法自行处理。</summary>
        public static string StripCategoryPrefix(string resourceRefId)
        {
            if (resourceRefId == null) throw new ArgumentNullException(nameof(resourceRefId));
            var dotIndex = resourceRefId.IndexOf('.');
            var withoutCategory = dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
            return withoutCategory.Replace('.', '_');
        }

        /// <summary>把 <c>display.map.sprite_set_id</c>（形如 <c>sprite.&lt;category&gt;.&lt;name&gt;</c>，
        /// <c>category</c> 取值见 <c>Core.Foundation.DisplayInfo.DisplaySchemas.Categories</c>）解析为
        /// 该精灵集在资产根目录下的相对目录路径（正斜杠分隔，不以 <c>/</c> 结尾，不含扩展名——
        /// 目录下按方向档位/层名进一步展开，见 14 第 1.2 节"磁盘布局"）：
        /// <c>"sprites/&lt;category&gt;_&lt;name&gt;"</c>。与
        /// <c>toolchain/asset_import/sprite_cmd.py</c> 的 <c>sprite_out_dir</c> 计算逐字节一致
        /// （该脚本判断记录"输出路径：目录名与运行时资源 id 解析规则对齐"）。</summary>
        public static string SpriteSetDirectory(Id spriteSetId) =>
            "sprites/" + StripCategoryPrefix(spriteSetId.Value);

        /// <summary>把 <c>display.map.icon_id</c>（形如 <c>icon.&lt;category&gt;.&lt;name&gt;</c>）
        /// 解析为该图标在资产根目录下的相对文件路径（正斜杠分隔，含 <c>.png</c> 扩展名）：
        /// <c>"icons/&lt;category&gt;/&lt;name&gt;.png"</c>——与
        /// <see cref="SpriteSetDirectory"/> 不同，<c>category</c> 段保留为独立子目录、不与
        /// <c>name</c> 拼接扁平化。与 <c>toolchain/asset_import/icon_cmd.py</c>/<c>sprite_cmd.py</c>
        /// （随精灵集登记的 <c>icon.png</c>）的 <c>icon_out_path</c> 计算逐字节一致（两脚本判断
        /// 记录一致，见回复文档"现行约定原文"一节）；<c>toolchain/import_sample_assets.py</c> 产出的
        /// <c>data/_sample</c> 全部 <c>icon_id</c> 样例（如 <c>icon.creature.sample_beast</c> →
        /// <c>icons/creature/sample_beast.png</c>）已用本方法逐一核对一致，见对应单元测试。</summary>
        /// <exception cref="FormatException"><paramref name="iconId"/> 去掉类别前缀后段数不足 2
        /// （即不满足 <c>icon.&lt;category&gt;.&lt;name&gt;</c> 至少三段的形状），无法拆出
        /// <c>category</c>/<c>name</c> 两部分。</exception>
        public static string IconFile(Id iconId)
        {
            var remainder = StripCategoryPrefixDotted(iconId.Value);
            var dotIndex = remainder.IndexOf('.');
            if (dotIndex < 0)
            {
                throw new FormatException(
                    $"icon_id \"{iconId.Value}\" 去掉类别前缀后剩余 \"{remainder}\" 不含点号，" +
                    "无法拆出 category/name 两段（期望形如 icon.<category>.<name>）");
            }
            var category = remainder.Substring(0, dotIndex);
            var name = remainder.Substring(dotIndex + 1).Replace('.', '_');
            return "icons/" + category + "/" + name + ".png";
        }

        /// <summary>见 <see cref="IconFile"/>：与 <see cref="StripCategoryPrefix"/> 的区别是不把
        /// 剩余部分的点号替换成下划线——<see cref="IconFile"/> 需要先按第一个剩余点号切出
        /// <c>category</c>，不能提前把该分隔符本身也替换掉。</summary>
        private static string StripCategoryPrefixDotted(string resourceRefId)
        {
            var dotIndex = resourceRefId.IndexOf('.');
            return dotIndex < 0 ? resourceRefId : resourceRefId.Substring(dotIndex + 1);
        }

        /// <summary><see cref="SpriteSetDirectory"/> 的反向解析：给定形如
        /// <c>"sprites/&lt;category&gt;_&lt;name&gt;"</c> 的相对目录路径（允许前导/末尾多余的
        /// <c>/</c>），尝试还原出 <c>sprite_set_id</c>。判断记录（有损逆运算的边界）：正向编码把
        /// <c>category</c> 与 <c>name</c> 之间的点号也换成了下划线，与 <c>name</c> 本身可能含有的
        /// 下划线无法从字符串本身区分——本方法假定 <c>category</c> 是 04 已登记域名清单之外的一个
        /// "显示类别"枚举值（<see cref="Core.Foundation.DisplayInfo.DisplaySchemas.Categories"/>：
        /// <c>skill</c>/<c>aura</c>/<c>item</c>/<c>creature</c>/<c>gobj</c>/<c>projectile</c>，均不含
        /// 下划线），按"第一个下划线之前的部分是 category"切分——与
        /// <c>toolchain/asset_import/ref_conventions.py</c> 的
        /// <c>try_parse_sprite_set_id</c> 同一假设、同一切分规则。不满足
        /// <c>"sprites/"</c> 前缀，或去掉前缀后不含下划线（无法切出 category/name 两段）时返回
        /// <c>false</c>。</summary>
        public static bool TryParseSpriteSetId(string relativeDirectory, out Id spriteSetId)
        {
            spriteSetId = default;
            if (string.IsNullOrEmpty(relativeDirectory))
            {
                return false;
            }

            var trimmed = relativeDirectory.Trim('/');
            const string prefix = "sprites/";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var flattened = trimmed.Substring(prefix.Length);
            var underscoreIndex = flattened.IndexOf('_');
            if (underscoreIndex <= 0 || underscoreIndex >= flattened.Length - 1)
            {
                return false;
            }

            var category = flattened.Substring(0, underscoreIndex);
            var name = flattened.Substring(underscoreIndex + 1);
            return Id.TryParse($"sprite.{category}.{name}", out spriteSetId);
        }

        /// <summary><see cref="IconFile"/> 的反向解析：给定形如
        /// <c>"icons/&lt;category&gt;/&lt;name&gt;.png"</c> 的相对文件路径（允许前导 <c>/</c>，
        /// 扩展名大小写不敏感、允许缺省），尝试还原出 <c>icon_id</c>。不满足
        /// <c>"icons/"</c> 前缀、路径段数不是恰好 <c>category/name.ext</c> 两段，或还原出的 id 不满足
        /// <see cref="Id"/> 格式时返回 <c>false</c>。</summary>
        public static bool TryParseIconId(string relativeFilePath, out Id iconId)
        {
            iconId = default;
            if (string.IsNullOrEmpty(relativeFilePath))
            {
                return false;
            }

            var trimmed = relativeFilePath.TrimStart('/');
            const string prefix = "icons/";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var remainder = trimmed.Substring(prefix.Length);
            var segments = remainder.Split('/');
            if (segments.Length != 2 || segments[0].Length == 0 || segments[1].Length == 0)
            {
                return false;
            }

            var category = segments[0];
            var fileName = segments[1];
            var extDot = fileName.LastIndexOf('.');
            var name = extDot < 0 ? fileName : fileName.Substring(0, extDot);
            if (name.Length == 0)
            {
                return false;
            }

            return Id.TryParse($"icon.{category}.{name}", out iconId);
        }
    }
}
