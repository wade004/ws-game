using System.Reflection;
using System.Text;

namespace Toolchain.AbiSurface
{
    /// <summary>
    /// 把 <see cref="Type"/> 格式化成一段与"具体加载它的 <see cref="System.Reflection.MetadataLoadContext"/>
    /// 实例/程序集标识"无关、跨基线/当前两次独立 dump 也保持稳定的文本——只用命名空间限定名
    /// （不带程序集限定名，见 <see cref="Format"/> 判断记录），保证同一个签名文本在两次独立进程
    /// dump 出的结果里逐字节相同，才能逐行 diff。
    /// </summary>
    internal static class TypeNameFormatter
    {
        /// <summary>
        /// 判断记录（不用 <c>Type.AssemblyQualifiedName</c>/<c>Type.ToString()</c>）：前者带程序集
        /// 版本号，同一份源码在基线/当前两次编译后版本号必然不同，会把"完全没变的签名"误判成
        /// "变了"；后者对泛型参数会用 <c>T</c>/<c>TResult</c> 这类源码里写的名字，而这些名字本身
        /// 可以改名而不构成物理 ABI 变化（物理签名只认参数位置，见 IL <c>!0</c>/<c>!!0</c> 记法），
        /// 因此改用 <see cref="Type.IsGenericParameter"/> 分支输出位置记法，不输出名字。
        /// </summary>
        public static string Format(Type type)
        {
            if (type.IsByRef)
            {
                var elementType = type.GetElementType();
                return Format(elementType!) + "&";
            }
            if (type.IsPointer)
            {
                var elementType = type.GetElementType();
                return Format(elementType!) + "*";
            }
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var rank = type.GetArrayRank();
                var commas = rank > 1 ? new string(',', rank - 1) : string.Empty;
                return Format(elementType!) + "[" + commas + "]";
            }
            if (type.IsGenericParameter)
            {
                // 方法级泛型参数（DeclaringMethod 非空）用 !!<position>，类型级用 !<position>，
                // 与 ECMA-335 IL 记法一致，且与参数名无关。
                var prefix = type.DeclaringMethod != null ? "!!" : "!";
                return prefix + type.GenericParameterPosition;
            }
            if (type.IsConstructedGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();
                var sb = new StringBuilder();
                sb.Append(StripArityName(def));
                sb.Append('<');
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Format(args[i]));
                }
                sb.Append('>');
                return sb.ToString();
            }
            return StripArityName(type);
        }

        /// <summary>
        /// 泛型类型定义（open generic type definition）的 <c>FullName</c> 自带 <c>`N</c> 元数后缀
        /// （例如 <c>System.Collections.Generic.List`1</c>）——保留这个后缀（元数本身是物理签名
        /// 的一部分，删掉会让不同元数的同名类型撞在一起），只是把它和命名空间/嵌套路径拼接完整。
        /// </summary>
        private static string StripArityName(Type type)
        {
            return type.FullName ?? (type.Namespace != null ? type.Namespace + "." + type.Name : type.Name);
        }

        /// <summary>
        /// 判断记录（不区分 <c>ref</c>/<c>out</c>/<c>in</c>）：三者的物理参数类型都是同一个
        /// byref 类型 <c>T&amp;</c>（<see cref="Format"/> 已经为它加上 <c>&amp;</c> 后缀），
        /// <c>ParameterInfo.IsOut</c>/<c>IsIn</c> 只是源码侧修饰符的元数据标记，不改变 IL 物理
        /// 签名——把 <c>ref</c> 改成 <c>out</c>（或反过来）不会触发 <c>MissingMethodException</c>，
        /// 因此这里故意不把它们计入差异文本，避免把非破坏性改动误报成破坏。
        /// </summary>
        public static string FormatParameters(ParameterInfo[] parameters)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Format(parameters[i].ParameterType));
            }
            return sb.ToString();
        }
    }
}
