using System;
using Core.Foundation.Common;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// 单个挂载点的登记信息：对应数据表 <c>found.hook</c> 的一行（见
    /// 04_数据与内容管线.md 第 1.1 节"脚本钩子注册表：钩子 id、触发时机、参数签名"、
    /// schema/found.hook.md）。
    /// </summary>
    public sealed class HookPointDefinition
    {
        /// <summary>挂载点 id，格式 <c>found.hook.&lt;name&gt;</c>（见 schema/found.hook.md）。</summary>
        public Id HookId { get; }

        /// <summary>参数签名说明文本（供校验与文档化，例如 <c>"sceneId: Id"</c>），不做机器可读的强类型校验。</summary>
        public string Signature { get; }

        /// <summary>本挂载点是否允许注册多个回调；为 false 时第二次 <see cref="IHookRegistry.Register"/> 抛异常。默认 true。</summary>
        public bool AllowMultiple { get; }

        /// <summary>可选的挂载点说明。</summary>
        public string? Description { get; }

        public HookPointDefinition(Id hookId, string signature, bool allowMultiple = true, string? description = null)
        {
            if (string.IsNullOrEmpty(signature))
            {
                throw new ArgumentException("signature 不能为空", nameof(signature));
            }

            HookId = hookId;
            Signature = signature;
            AllowMultiple = allowMultiple;
            Description = description;
        }
    }
}
