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
            // ABI-116-01 根治（codex 第十六轮，audit-24a11fe-20260910）：TYPE 行第一个 flag 固定是
            // 类型可见性（public/nested-public/nested-protected/nested-protected-internal），供
            // SurfaceCompareLogic 的可见性放宽豁免逻辑按"去掉可见性 token 后其余 flags 是否相同"
            // 识别——放在首位是那段逻辑的硬约定，不能挪到其它位置。
            var flags = new List<string> { TypeVisibility(type) };
            bool isStaticClass = type.IsAbstract && type.IsSealed && type.IsClass;
            if (kind == "class")
            {
                if (isStaticClass) flags.Add("static");
                else if (type.IsAbstract) flags.Add("abstract");
                else if (type.IsSealed) flags.Add("sealed");
            }
            if (type.IsGenericTypeDefinition)
            {
                var genericArgs = type.GetGenericArguments();
                flags.Add("generic:" + genericArgs.Length);
                var constraints = FormatGenericConstraints(genericArgs);
                if (constraints.Length > 0) flags.Add("constraints:" + constraints);
            }
            var flagsText = string.Join(",", flags);

            lines.Add(string.Join("\t", "TYPE", TypeNameFormatter.Format(type), kind, flagsText));

            foreach (var ctor in type.GetConstructors(Bindings))
            {
                if (!IsMemberVisible(ctor.Attributes)) continue;
                var sig = ".ctor(" + TypeNameFormatter.FormatParameters(ctor.GetParameters()) + ")";
                var mflags = MethodFlags(Visibility(ctor.Attributes), ctor.IsStatic, false, false, false, null);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "ctor", sig, mflags));
            }

            foreach (var method in type.GetMethods(Bindings))
            {
                if (!IsMemberVisible(method.Attributes)) continue;
                // ABI-1162-01 根治（codex 第十七轮，audit-4faab73-20260910）：此前 `IsSpecialName`
                // 整体跳过，误伤了运算符重载/转换运算符（`op_Addition`/`op_Implicit`/`op_Explicit`
                // 等——C# 编译器同样把它们标记 IsSpecialName=true，物理上是普通 static 方法，删除
                // 或改签名会让旧调用方 `MissingMethodException`）。只应跳过属性/事件的
                // `get_`/`set_`/`add_`/`remove_` 访问器（它们另按 property/event 记录，避免与这里
                // 重复记同一签名两遍）——按访问器方法名前缀 + IsSpecialName 双重判定，不再用
                // IsSpecialName 单独判定，运算符类方法（`op_` 前缀）不在这四个前缀里，照常记录。
                if (method.IsSpecialName && IsAccessorMethodName(method.Name)) continue;
                var arity = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
                var name = arity > 0 ? method.Name + "`" + arity : method.Name;
                var sig = name + "(" + TypeNameFormatter.FormatParameters(method.GetParameters()) + "):" + TypeNameFormatter.Format(method.ReturnType);
                string? constraints = arity > 0 ? FormatGenericConstraints(method.GetGenericArguments()) : null;
                var mflags = MethodFlags(Visibility(method.Attributes), method.IsStatic, method.IsVirtual && !method.IsFinal, method.IsAbstract, method.IsFinal && method.IsVirtual, constraints);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "method", sig, mflags));
            }

            foreach (var prop in type.GetProperties(Bindings))
            {
                var getter = prop.GetMethod;
                var setter = prop.SetMethod;
                var getVis = getter != null && IsMemberVisible(getter.Attributes) ? Visibility(getter.Attributes) : "none";
                var setVis = setter != null && IsMemberVisible(setter.Attributes) ? Visibility(setter.Attributes) : "none";
                if (getVis == "none" && setVis == "none") continue; // 访问器都不是 public/protected，属性本身不算表面成员。
                // ABI-1162-01 根治（codex 第十七轮，audit-4faab73-20260910）：此前 sig 只编码
                // `Name:PropertyType`，完全不含 `PropertyInfo.GetIndexParameters()`——public
                // indexer（`this[int]`）的索引参数类型变化（例如改成 `this[string]`）dump 前后
                // 逐字节相同，compare `breaks=0`，但物理上是完全不同的 `get_Item`/`set_Item`
                // 方法签名，旧编译消费方运行期 `MissingMethodException`（见
                // delivery/run5-console.log 的 indexer oracle 与 AUDIT_REPORT.md ABI-1162-01）。
                // 索引参数与方法参数共用 TypeNameFormatter.FormatParameters，非索引器
                // （Length==0）时不追加 `[...]`，签名文本与旧格式保持一致，不产生无谓的格式噪音。
                var indexParams = prop.GetIndexParameters();
                var indexSig = indexParams.Length > 0 ? "[" + TypeNameFormatter.FormatParameters(indexParams) + "]" : "";
                var sig = prop.Name + indexSig + ":" + TypeNameFormatter.Format(prop.PropertyType);
                // 判断记录：本 dumper 不单独为属性的 get_/set_ 访问器方法输出 method 行（上面
                // GetMethods 循环用 IsSpecialName+前缀 过滤掉了它们，避免与这里的 property 行重复
                // 记同一处签名两遍）。接口属性是否 abstract（没有默认实现体）因此只能记在这一行的
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
                // ABI-116-01 根治：非枚举 const（IsLiteral）字段把内联常量值编进 sig（旧行为只记
                // 类型不记值）——旧 consumer 在编译期把 const 的值内联进 IL，值变化即使字段签名
                // 物理上没变，重编译前旧 consumer 用的还是旧值，属于契约破坏，必须让新旧两行不同。
                string fsig;
                if (field.IsLiteral)
                {
                    var raw = field.GetRawConstantValue();
                    fsig = field.Name + ":" + TypeNameFormatter.Format(field.FieldType) + "=" + FormatConstValue(raw);
                }
                else
                {
                    fsig = field.Name + ":" + TypeNameFormatter.Format(field.FieldType);
                }
                var fflagsList = new List<string> { Visibility(field.Attributes) };
                if (field.IsStatic) fflagsList.Add("static");
                if (field.IsInitOnly) fflagsList.Add("readonly");
                if (field.IsLiteral) fflagsList.Add("literal");
                var fflags = string.Join(",", fflagsList);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "field", fsig, fflags));
            }

            foreach (var evt in type.GetEvents(Bindings))
            {
                var adder = evt.AddMethod;
                if (adder == null || !IsMemberVisible(adder.Attributes)) continue;
                var esig = evt.Name + ":" + TypeNameFormatter.Format(evt.EventHandlerType!);
                var eflagsList = new List<string> { Visibility(adder.Attributes) };
                if (adder.IsStatic) eflagsList.Add("static");
                // ABI-1162-01 根治（codex 第十七轮，audit-4faab73-20260910）：此前只记 add 访问器
                // 可见性——remove 访问器单独收窄（例如 add 仍 public，remove 从 public 收窄成
                // internal/private）时事件这一行 flags 不变，compare `breaks=0`，但旧编译消费方
                // 的 `obj.Event -= handler;` 物理上调用 `remove_Event`，收窄后同样
                // `MissingMethodException`（与 get/set 分别判定同一治理口径，见属性分支）。首
                // token 仍固定是 add 可见性（SurfaceCompareLogic 的可见性放宽豁免按首 token 识别，
                // 不能挪位——见该处判断记录），remove 可见性追加为独立 token，narrowing 时这条
                // token 变化会让整行"身份"不同，走规则 1 的"行消失即破坏"判定。
                var remover = evt.RemoveMethod;
                var removeVis = remover != null && IsMemberVisible(remover.Attributes) ? Visibility(remover.Attributes) : "none";
                eflagsList.Add("remove:" + removeVis);
                var eflags = string.Join(",", eflagsList);
                lines.Add(string.Join("\t", "MEMBER", TypeNameFormatter.Format(type), "event", esig, eflags));
            }

            foreach (var nested in type.GetNestedTypes(Bindings))
            {
                DumpType(nested, lines);
            }
        }

        private const BindingFlags Bindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly string[] AccessorMethodNamePrefixes = { "get_", "set_", "add_", "remove_" };

        /// <summary>
        /// ABI-1162-01 根治：判断一个 `IsSpecialName=true` 的方法是否是属性/事件访问器（这些已经
        /// 另按 property/event 记录，method 循环要跳过避免重复）——按前缀区分，运算符重载/转换
        /// 运算符（`op_Addition`/`op_Implicit` 等）同样 `IsSpecialName=true` 但不匹配这四个前缀，
        /// 因此不会被这里过滤，会正常按普通 method 记录（见调用点判断记录）。
        /// </summary>
        private static bool IsAccessorMethodName(string methodName)
        {
            foreach (var prefix in AccessorMethodNamePrefixes)
            {
                if (methodName.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

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

        // ABI-116-01 根治：字段可见性判定，与上面 MethodAttributes 版本同构（FieldAttributes 是
        // 独立的位域类型，不能复用同一个重载）。
        private static string Visibility(FieldAttributes attrs)
        {
            var access = attrs & FieldAttributes.FieldAccessMask;
            if (access == FieldAttributes.Public) return "public";
            if (access == FieldAttributes.Family) return "protected";
            if (access == FieldAttributes.FamORAssem) return "protected-internal";
            return "none";
        }

        /// <summary>
        /// 类型自身的可见性——只对已经通过 <see cref="IsSurfaceVisible"/> 的类型调用，因此
        /// 兜底分支（各判定都不成立）实际不可达；分类只覆盖顶层 public 与三档嵌套可见性
        /// （nested-public/nested-protected/nested-protected-internal），供
        /// <see cref="SurfaceCompareLogic"/> 的可见性放宽豁免识别嵌套类型的可见性收窄
        /// （例如 nested-public 收窄为 nested-protected：旧格式两次 dump 该 TYPE 行的 flags 完全
        /// 相同、看不出变化，只有把可见性单独记一个 token 才能让这处收窄体现为行差异）。
        /// </summary>
        private static string TypeVisibility(Type type)
        {
            if (type.IsNestedPublic) return "nested-public";
            if (type.IsNestedFamily) return "nested-protected";
            if (type.IsNestedFamORAssem) return "nested-protected-internal";
            if (type.IsPublic) return "public";
            return "public";
        }

        private static string MethodFlags(string visibility, bool isStatic, bool isVirtualNonFinal, bool isAbstract, bool isSealedOverride, string? constraints)
        {
            // ABI-116-01 根治：visibility 固定放在第一个 token——理由与 TYPE 行相同，见 DumpType
            // 判断记录；SurfaceCompareLogic 的放宽豁免逻辑按"首 token 是否已知可见性词汇"识别，
            // 挪到别处会让豁免逻辑失效（退化为普通全行 diff，narrowing/widening 都判破坏）。
            var flags = new List<string> { visibility };
            if (isStatic) flags.Add("static");
            if (isAbstract) flags.Add("abstract");
            else if (isVirtualNonFinal) flags.Add("virtual");
            else if (isSealedOverride) flags.Add("sealed-override");
            if (!string.IsNullOrEmpty(constraints)) flags.Add("constraints:" + constraints);
            return string.Join(",", flags);
        }

        /// <summary>
        /// 泛型参数约束（类型或方法级泛型定义）——覆盖 variance（out/in）、特殊约束
        /// （class/struct/new()）与显式基类/接口约束，按参数位置、约束内部按 ordinal 排序后拼接，
        /// 保证同一份 DLL 两次独立 dump 逐字节相同。约束改变（增/删/换）不做放宽豁免，任何变化都
        /// 让整条 TYPE/MEMBER 行的 flags 不同，走默认的"行消失即破坏"规则——已编译调用方虽然不会
        /// 因为约束变化在运行期直接崩，但源码重新编译会报"类型/方法不满足约束"，属于治理目标里
        /// "未声明的破坏性变更"（与既有接口新增 abstract 成员同一治理逻辑，见 SurfaceCompare.cs
        /// 规则 2 判断记录）。
        /// </summary>
        private static string FormatGenericConstraints(Type[] genericParams)
        {
            var parts = new List<string>();
            foreach (var gp in genericParams)
            {
                var special = new List<string>();
                var varAttrs = gp.GenericParameterAttributes & GenericParameterAttributes.VarianceMask;
                if (varAttrs == GenericParameterAttributes.Covariant) special.Add("out");
                else if (varAttrs == GenericParameterAttributes.Contravariant) special.Add("in");
                var constraintAttrs = gp.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
                if ((constraintAttrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0) special.Add("class");
                if ((constraintAttrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) special.Add("struct");
                if ((constraintAttrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0) special.Add("new()");
                var baseConstraints = gp.GetGenericParameterConstraints()
                    .Select(TypeNameFormatter.Format)
                    .OrderBy(s => s, StringComparer.Ordinal);
                special.AddRange(baseConstraints);
                parts.Add("!" + gp.GenericParameterPosition + ":" + (special.Count > 0 ? string.Join("&", special) : "-"));
            }
            return string.Join(";", parts);
        }

        /// <summary>
        /// 非枚举 const 字段的内联常量值——文本化用于 dump 行，与
        /// <see cref="TypeNameFormatter.FormatLiteralValue"/> 共用同一套格式（判断记录见该处：
        /// const 字段值真正内联进调用方 IL，与可选参数默认值的编译器语法糖性质不同，仍要编码）。
        /// </summary>
        private static string FormatConstValue(object? raw) => TypeNameFormatter.FormatLiteralValue(raw);

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
