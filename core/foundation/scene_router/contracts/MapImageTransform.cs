using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// 消费方反馈第 59 条 / ADR-0036：地图背景图片像素坐标 ↔ 世界坐标的换算——只读纯函数类型，
    /// 从 <c>world.map</c> 记录的可选 <c>image_transform</c> 字段构造（见
    /// <see cref="WorldMapSchema.Table"/> "image_transform" 字段登记）。
    /// <para>
    /// 坐标约定（详见 05_对象模型与世界.md 第 3.1.1 节）：世界坐标 Y 轴正方向向上；地图背景图片
    /// 像素坐标以左上角为原点、列向右为正、行向下为正。二者符号相反是两条约定共同的直接推论。
    /// </para>
    /// </summary>
    public sealed class MapImageTransform
    {
        /// <summary>每个世界单位对应的图片像素数，必须 &gt; 0。</summary>
        public double PixelsPerUnit { get; }

        /// <summary>世界原点 (0,0) 在地图图片中的像素坐标（左上角为 (0,0)）。</summary>
        public Vec2 OriginPx { get; }

        /// <summary>地图背景图片的宽高像素；未声明时为 null（<see cref="WorldBounds"/> 相应返回
        /// null，见该属性判断记录）。</summary>
        public Vec2? ImageSizePx { get; }

        public MapImageTransform(double pixelsPerUnit, Vec2 originPx, Vec2? imageSizePx = null)
        {
            if (!(pixelsPerUnit > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), pixelsPerUnit, "pixelsPerUnit 必须 > 0");
            }

            PixelsPerUnit = pixelsPerUnit;
            OriginPx = originPx;
            ImageSizePx = imageSizePx;
        }

        /// <summary>框架给出的显式命名默认换算：<c>pixels_per_unit = 32</c>，世界原点位于图片像素
        /// <c>(0,0)</c>（左上角）。判断记录：地图未声明 <c>image_transform</c> 时不会自动套用本
        /// 默认值——见 <see cref="FromRecord"/>；调用方需要显式选择"缺省时是否退回本默认值"，本类型
        /// 本身不替调用方做这个选择，避免"这张地图确实声明了换算参数、只是恰好等于默认值"与"这张
        /// 地图压根没声明"两种情形被调用方无法区分。</summary>
        public static readonly MapImageTransform Default = new MapImageTransform(32, new Vec2(0, 0));

        /// <summary>图片像素坐标 → 世界坐标。</summary>
        public Vec2 PixelToWorld(Vec2 px)
        {
            var worldX = (px.X - OriginPx.X) / PixelsPerUnit;
            var worldY = (OriginPx.Y - px.Y) / PixelsPerUnit;
            return new Vec2(worldX, worldY);
        }

        /// <summary>世界坐标 → 图片像素坐标（<see cref="PixelToWorld"/> 的逆变换）。</summary>
        public Vec2 WorldToPixel(Vec2 world)
        {
            var pxX = world.X * PixelsPerUnit + OriginPx.X;
            var pxY = OriginPx.Y - world.Y * PixelsPerUnit;
            return new Vec2(pxX, pxY);
        }

        /// <summary><see cref="ImageSizePx"/> 已声明时，返回图片四角换算到世界坐标后的轴对齐包围盒
        /// （<c>Min</c>/<c>Max</c> 分别取两轴的较小/较大值，不假定 <see cref="OriginPx"/> 一定落在
        /// 图片范围内，也不假定换算不含负缩放）；未声明 <see cref="ImageSizePx"/> 时返回 null（无法
        /// 界定图片范围，供 <see cref="WorldMapPointOutsideImageValidationRule"/> 跳过判定，不是
        /// "包围盒为空"）。</summary>
        public (Vec2 Min, Vec2 Max)? WorldBounds
        {
            get
            {
                if (ImageSizePx == null)
                {
                    return null;
                }

                var size = ImageSizePx.Value;
                var corner1 = PixelToWorld(new Vec2(0, 0));
                var corner2 = PixelToWorld(new Vec2(size.X, size.Y));

                var minX = Math.Min(corner1.X, corner2.X);
                var maxX = Math.Max(corner1.X, corner2.X);
                var minY = Math.Min(corner1.Y, corner2.Y);
                var maxY = Math.Max(corner1.Y, corner2.Y);

                return (new Vec2(minX, minY), new Vec2(maxX, maxY));
            }
        }

        /// <summary>从 <c>world.map</c> 记录读取 <c>image_transform</c>；字段缺失时返回 null——
        /// 刻意不静默退回 <see cref="Default"/>（同类型顶部判断记录，运行时路径不静默降级，见
        /// AGENTS.md 第 3 节）：调用方必须能区分"这张地图没声明换算参数"（返回 null，是否兜底由
        /// 调用方决定）与"声明了且换算参数恰好等于默认值"（返回一个数值等于 <see cref="Default"/>
        /// 的实例）。子字段一旦按 <see cref="WorldMapSchema"/> 登记通过了加载期 <c>required_field</c>/
        /// <c>field_type</c> 校验，这里的解析理应总能成功；仍保留显式异常分支（不吞掉异常返回默认值）
        /// 作为防御性一致性检查。</summary>
        public static MapImageTransform? FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            if (!record.TryGetObject("image_transform", out var obj))
            {
                return null;
            }

            if (!obj.TryGetValue("pixels_per_unit", out var ppuVal) || !(ppuVal is JsonNumber ppuNum))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "image_transform.pixels_per_unit", "期望 Number");
            }

            if (!obj.TryGetValue("origin_px", out var originVal) || !(originVal is JsonObject originObj)
                || !originObj.TryGetValue("x", out var oxVal) || !(oxVal is JsonNumber oxNum)
                || !originObj.TryGetValue("y", out var oyVal) || !(oyVal is JsonNumber oyNum))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "image_transform.origin_px", "期望 Vec2（{\"x\": Number, \"y\": Number}）");
            }

            Vec2? imageSize = null;
            if (obj.TryGetValue("image_size_px", out var sizeVal) && sizeVal is JsonObject sizeObj
                && sizeObj.TryGetValue("x", out var sxVal) && sxVal is JsonNumber sxNum
                && sizeObj.TryGetValue("y", out var syVal) && syVal is JsonNumber syNum)
            {
                imageSize = new Vec2(sxNum.Value, syNum.Value);
            }

            return new MapImageTransform(ppuNum.Value, new Vec2(oxNum.Value, oyNum.Value), imageSize);
        }
    }
}
