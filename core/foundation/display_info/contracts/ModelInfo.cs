using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// <c>kind: model</c> 型外形的专属字段（见 04_数据与内容管线.md 第 7.1 节"<c>kind: model</c>
    /// 型专属字段"表）。不可变值对象，由 <see cref="Core.Foundation.DisplayInfo.DisplayInfo.FromRecord"/>
    /// 从 <c>display.map</c> 记录构造。
    /// </summary>
    public sealed class ModelInfo
    {
        /// <summary>三维模型资源引用（不含路径，由引擎适配层解析），必填。</summary>
        public Id ModelRef { get; }

        /// <summary>指向 <c>display.anim_set</c> 的动画集引用（见 7.1.1 节），必填。</summary>
        public Id AnimSetRef { get; }

        /// <summary>该模型声明的挂点 id 列表，供 <c>IRenderer3D.attachToSocket</c> 使用，
        /// 可选，默认空列表。</summary>
        public IReadOnlyList<Id> Sockets { get; }

        /// <summary>该模型声明的可换装槽位 id 列表，供 <c>IRenderer3D.setSlotMesh</c> 使用，
        /// 可选，默认空列表。</summary>
        public IReadOnlyList<Id> Slots { get; }

        /// <summary>槽位 id 到默认网格资源引用的映射，未被 <c>display.equip_visual</c> 覆盖时使用，
        /// 可选，默认空字典。</summary>
        public IReadOnlyDictionary<Id, Id> DefaultSlotMeshes { get; }

        /// <summary>材质参数默认值（闪白、溶解等），经 <c>IRenderer3D.setMaterialParam</c> 应用，
        /// 可选，默认空字典。</summary>
        public IReadOnlyDictionary<string, double> MaterialParams { get; }

        public ModelInfo(
            Id modelRef,
            Id animSetRef,
            IReadOnlyList<Id>? sockets = null,
            IReadOnlyList<Id>? slots = null,
            IReadOnlyDictionary<Id, Id>? defaultSlotMeshes = null,
            IReadOnlyDictionary<string, double>? materialParams = null)
        {
            ModelRef = modelRef;
            AnimSetRef = animSetRef;
            Sockets = sockets ?? System.Array.Empty<Id>();
            Slots = slots ?? System.Array.Empty<Id>();
            DefaultSlotMeshes = defaultSlotMeshes ?? EmptyIdMap;
            MaterialParams = materialParams ?? EmptyMaterialParams;
        }

        private static readonly IReadOnlyDictionary<Id, Id> EmptyIdMap = new Dictionary<Id, Id>();
        private static readonly IReadOnlyDictionary<string, double> EmptyMaterialParams = new Dictionary<string, double>();
    }
}
