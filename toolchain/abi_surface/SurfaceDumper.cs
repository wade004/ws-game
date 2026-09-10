using System.Reflection;

namespace Toolchain.AbiSurface
{
    /// <summary>
    /// 用 <see cref="System.Reflection.MetadataLoadContext"/>（只读元数据反射，不执行被加载程序集
    /// 任何代码，见 AbiSurface.csproj 头判断记录）把一份或多份 DLL 的公开/受保护 API 表面 dump 成
    /// 排序、确定性的文本行——每次独立进程运行对同一份 DLL 输出逐字节相同的结果，才能拿两次 dump
    /// 做逐行 diff（<see cref="SurfaceCompare"/>）。
    /// </summary>
    internal static class SurfaceDumper
    {
        public static List<string> Dump(IReadOnlyList<string> dllPaths)
        {
            var resolvedPaths = dllPaths.Select(p => Path.GetFullPath(p)).ToList();
            foreach (var p in resolvedPaths)
            {
                if (!File.Exists(p)) throw new FileNotFoundException("dump 目标 DLL 不存在：" + p, p);
            }

            // 判断记录（解析器候选集合）：MetadataLoadContext 不像正常 AssemblyLoadContext 那样能
            // 按需探测磁盘，必须预先给出一份"可能用到的程序集路径"全集（PathAssemblyResolver）。
            // 被检查的 core/* 程序集是 netstandard2.1 类库，引用的 BCL 类型（System.String、
            // System.Collections.Generic.* 等）通过 netstandard.dll 门面转发到当前 .NET 运行时的
            // 具体实现程序集——两者都要能找到：1）当前运行时目录（GetRuntimeDirectory，含
            // netstandard.dll 门面与 System.Private.CoreLib 等）；2）每个传入 DLL 所在目录（同目录
            // 下的兄弟 DLL，例如 dump Core.Rules.dll 时解析它引用的 Core.Foundation.dll）。两者按
            // 文件名去重后一起交给 PathAssemblyResolver，同名時以传入 DLL 所在目录优先（收集顺序：
            // 先兄弟目录，后运行时目录，Dictionary 用"先出现的赢"语义）。
            var candidateAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var siblingDirs = resolvedPaths.Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in siblingDirs)
            {
                foreach (var dll in Directory.GetFiles(dir, "*.dll"))
                {
                    var name = Path.GetFileNameWithoutExtension(dll);
                    if (!candidateAssemblies.ContainsKey(name)) candidateAssemblies[name] = dll;
                }
            }
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (!candidateAssemblies.ContainsKey(name)) candidateAssemblies[name] = dll;
            }

            var resolver = new PathAssemblyResolver(candidateAssemblies.Values);
            using var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "System.Private.CoreLib");

            var lines = new List<string>();
            foreach (var dllPath in resolvedPaths)
            {
                var asm = mlc.LoadFromAssemblyPath(dllPath);
                // 判断记录：Assembly.GetTypes() 返回该程序集声明的全部类型，含嵌套类型；DumpType
                // 自己会递归 GetNestedTypes() 下钻，这里只从顶层类型（DeclaringType == null）
                // 开始，否则每个嵌套类型会被 GetTypes() 直接枚举到一次、又被外层递归枚举到一次，
                // 重复写出两条一模一样的行（实测复现：EntitySpatialSyncHost+KindConfig 的每条
                // 成员在 dump 输出里都出现了两遍）。
                foreach (var type in asm.GetTypes().Where(t => t.DeclaringType == null))
                {
                    DumpType(type, lines);
                }
            }

            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        private static bool IsSurfaceVisible(Type type)
        {
            if (type.IsPublic || type.IsNestedPublic) return true;
            if (type.IsNestedFamily || type.IsNestedFamORAssem) return true;
            return false;
        }

        private static void DumpType(Type type, List<string> lines)
        {
            if (!IsSurfaceVisible(type)) return;

            var kind = ClassifyKind(type);
            var flags = new List<string>();
            bool isStaticClass = type.IsAbstract && type.IsSealed && type.IsClass;
            if (kind == "class")
            {
                if (isStaticClass) flags.Add("static");
                else if (type.IsAbstract) flags.Add("abstract");
                else if (type.IsSealed) flags.Add("sealed");
            }
            if (type.IsGenericTypeDefinition) flags.Add("generic:" + type.GetGenericArguments().Length);
            var flagsText = flags.Count > 0 ? string.Join(",", flags) : "-";

            lines.Add(string.Join("\t", "TYPE", TypeNameFormatter.Format(type), kind, flagsText));

            foreach (var ctor in type.GetConstructors(Bindings))
            {
                if (!IsMemberVisible(ctor.Attributes)) continue;
                var sig = ".ctor(" + TypeNameFormatter.FormatParameters(ctor.GetParameters()) + ")";
                var mflags = MethodFlags(ctor.IsStatic, false, false, false);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "ctor", sig, mflags));
            }

            foreach (var method in type.GetMethods(Bindings))
            {
                if (!IsMemberVisible(method.Attributes)) continue;
                if (method.IsSpecialName) continue; // 属性/事件的 get_/set_/add_/remove_ 访问器另按 property/event 记录，避免重复。
                var arity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
                var name = arity > 0 ? method.Name + "`" + arity : method.Name;
                var sig = name + "(" + TypeNameFormatter.FormatParameters(method.GetParameters()) + "):" + TypeNameFormatter.Format(method.ReturnType);
                var mflags = MethodFlags(method.IsStatic, method.IsVirtual && !method.IsFinal, method.IsAbstract, method.IsFinal && method.IsVirtual);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "method", sig, mflags));
            }

            foreach (var prop in type.GetProperties(Bindings))
            {
                var getter = prop.GetMethod;
                var setter = prop.SetMethod;
                var getVis = getter != null && IsMemberVisible(getter.Attributes) ? Visibility(getter.Attributes) : "none";
                var setVis = setter != null && IsMemberVisible(setter.Attributes) ? Visibility(setter.Attributes) : "none";
                if (getVis == "none" && setVis == "none") continue; // 访问器都不是 public/protected，属性本身不算表面成员。
                var sig = prop.Name + ":" + TypeNameFormatter.Format(prop.PropertyType);
                // 判断记录：本 dumper 不单独为属性的 get_/set_ 访问器方法输出 method 行（上面
                // GetMethods 循环用 IsSpecialName 过滤掉了它们，避免与这里的 property 行重复记同
                // 一处签名两遍）。接口属性是否 abstract（没有默认实现体）因此只能记在这一行的
                // flags 里——SurfaceCompareLogic 的"既有接口新增 abstract 成员"规则要靠这个标记
                // 识别属性访问器，不是只识别 method 行。
                bool isAbstractProp = (getter != null && getter.IsAbstract) || (setter != null && setter.IsAbstract);
                var mflags = "get:" + getVis + ",set:" + setVis + (isAbstractProp ? ",abstract" : "");
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "property", sig, mflags));
            }

            foreach (var field in type.GetFields(Bindings))
            {
                if (!IsMemberVisible(field.Attributes)) continue;
                if (field.IsSpecialName) continue;
                if (type.IsEnum && field.IsLiteral)
                {
                    var raw = field.GetRawConstantValue();
                    var sig = field.Name + "=" + Convert.ToInt64(raw);
                    lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "enumvalue", sig, "-"));
                    continue;
                }
                if (type.IsEnum) continue; // value__ 实例字段：不是可用的枚举成员，跳过。
                var fsig = field.Name + ":" + TypeNameFormatter.Format(field.FieldType);
                var fflagsList = new List<string>();
                if (field.IsStatic) fflagsList.Add("static");
                if (field.IsInitOnly) fflagsList.Add("readonly");
                if (field.IsLiteral) fflagsList.Add("literal");
                var fflags = fflagsList.Count > 0 ? string.Join(",", fflagsList) : "-";
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "field", fsig, fflags));
            }

            foreach (var evt in type.GetEvents(Bindings))
            {
                var adder = evt.AddMethod;
                if (adder == null || !IsMemberVisible(adder.Attributes)) continue;
                var esig = evt.Name + ":" + TypeNameFormatter.Format(evt.EventHandlerType!);
                var eflags = adder.IsStatic ? "static" : "-";
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "event", esig, eflags));
            }

            foreach (var nested in type.GetNestedTypes(Bindings))
            {
                DumpType(nested, lines);
            }
        }

        private const BindingFlags Bindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static bool IsMemberVisible(MethodAttributes attrs)
        {
            var access = attrs & MethodAttributes.MemberAccessMask;
            return access == MethodAttributes.Public || access == MethodAttributes.Family || access == MethodAttributes.FamORAssem;
        }

        private static bool IsMemberVisible(FieldAttributes attrs)
        {
            var access = attrs & FieldAttributes.FieldAccessMask;
            return access == FieldAttributes.Public || access == FieldAttributes.Family || access == FieldAttributes.FamORAssem;
        }

        private static string Visibility(MethodAttributes attrs)
        {
            var access = attrs & MethodAttributes.MemberAccessMask;
            if (access == MethodAttributes.Public) return "public";
            if (access == MethodAttributes.Family) return "protected";
            if (access == MethodAttributes.FamORAssem) return "protected-internal";
            return "none";
        }

        private static string MethodFlags(bool isStatic, bool isVirtualNonFinal, bool isAbstract, bool isSealedOverride)
        {
            var flags = new List<string>();
            if (isStatic) flags.Add("static");
            if (isAbstract) flags.Add("abstract");
            else if (isVirtualNonFinal) flags.Add("virtual");
            else if (isSealedOverride) flags.Add("sealed-override");
            return flags.Count > 0 ? string.Join(",", flags) : "-";
        }

        private static string ClassifyKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate") return "delegate";
            if (type.IsValueType) return "struct";
            return "class";
        }
    }
}
