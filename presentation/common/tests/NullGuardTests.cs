// T-M15（测试覆盖梳理 2026-10-01，ADR-0125）：presentation 全模块公共构造器的 ArgumentNullException 空参守卫。
//
// 做法：反射枚举 Presentation 程序集的全部公开、非抽象、非泛型类的公共实例构造器；对每个"非可空引用类型、
// 无默认值"的形参，其余形参用合成的合法占位实参（接口 -> DispatchProxy 空实现、委托 -> 空委托、集合 -> 空集合、
// 具体类 -> 递归构造），仅把该形参置 null，断言抛 ArgumentNullException 且 ParamName 等于形参名。
// 每个 (类型, 构造器, 形参) 一行 Theory 数据 = "每处守卫一例"。源码里没有守卫的形参不在范围内（本任务不新增
// 守卫），列在 KnownUnguardedConstructorParameters 里并带原因；另有一条用例保证该清单里的条目确实仍无守卫
// （一旦有人补了守卫，清单条目变陈旧，用例会失败提醒删除）。
// 非构造器的方法/静态工厂守卫、受保护/私有构造器的守卫逐个手写在 NullGuardMethodTests.cs。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace Tests.PresentationCommon
{
    /// <summary>按接口合成"什么都不做、返回类型默认值"的空实现（只服务于把构造器推进到空参守卫那一行）。</summary>
    public class NullGuardStubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod == null ? null : NullGuardSynthesizer.DefaultFor(targetMethod.ReturnType);
    }

    internal static class NullGuardSynthesizer
    {
        public static readonly Assembly PresentationAssembly = typeof(global::Presentation.Common.ResourceReferenceTracker).Assembly;

        /// <summary>合成不出来时抛出，调用方把该行记为"无法合成，需手写"。</summary>
        public sealed class NotSynthesizableException : Exception
        {
            public NotSynthesizableException(string message) : base(message) { }
        }

        public static object? DefaultFor(Type t)
        {
            if (t == typeof(void)) return null;
            if (t.IsValueType) return Nullable.GetUnderlyingType(t) != null ? null : Activator.CreateInstance(t);
            if (t == typeof(string)) return string.Empty;
            return TryCollection(t) ?? (t.IsInterface ? CreateProxy(t) : null);
        }

        private static object? TryCollection(Type t)
        {
            if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);
            if (!t.IsGenericType) return null;
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            if (def == typeof(IReadOnlyList<>) || def == typeof(IReadOnlyCollection<>) || def == typeof(IEnumerable<>)
                || def == typeof(IList<>) || def == typeof(ICollection<>) || def == typeof(List<>))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(args));
            if (def == typeof(IReadOnlyDictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(Dictionary<,>))
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
            if (def == typeof(ISet<>) || def == typeof(HashSet<>))
                return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(args));
            return null;
        }

        private static object CreateProxy(Type iface)
        {
            var create = typeof(DispatchProxy).GetMethods().Single(m => m.Name == "Create" && m.GetParameters().Length == 0);
            return create.MakeGenericMethod(iface, typeof(NullGuardStubProxy)).Invoke(null, null)!;
        }

        private static object CreateDelegate(Type delegateType)
        {
            var invoke = delegateType.GetMethod("Invoke")!;
            var ps = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            Expression body = invoke.ReturnType == typeof(void)
                ? (Expression)Expression.Empty()
                : Expression.Constant(DefaultFor(invoke.ReturnType), invoke.ReturnType);
            return Expression.Lambda(delegateType, body, ps).Compile();
        }

        /// <summary>为形参类型合成一个合法占位值。</summary>
        public static object? Synthesize(Type t, int depth = 0)
        {
            if (t.IsByRef) t = t.GetElementType()!;
            if (t.IsValueType) return DefaultFor(t);
            if (t == typeof(string)) return "x";
            if (t == typeof(object)) return new object();
            var collection = TryCollection(t);
            if (collection != null) return collection;
            if (typeof(Delegate).IsAssignableFrom(t)) return CreateDelegate(t);
            if (t.IsInterface) return CreateProxy(t);
            if (t.IsAbstract || depth >= 3) throw new NotSynthesizableException(t.FullName ?? t.Name);

            foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderBy(c => c.GetParameters().Length))
            {
                try
                {
                    var args = ctor.GetParameters().Select(p => Synthesize(p.ParameterType, depth + 1)).ToArray();
                    return ctor.Invoke(args);
                }
                catch (Exception)
                {
                    // 换下一个构造器。
                }
            }
            // 没有能成功构造的公共构造器（如依赖整套装配的 GameplayAssembly、私有构造器的值对象）：
            // 取一个未初始化的实例充当"非 null 占位"——被测构造器的空参守卫都在使用这些实参之前，
            // 占位实例只需要"不是 null"。
            return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        }
    }

    public class ConstructorNullGuardTests
    {
        /// <summary>源码构造器里确实没有空参守卫的形参（本任务不新增守卫，仅登记；是否补守卫待设计层确认，见汇报）。
        /// 键格式：<c>Namespace.Type(paramName)</c>（同一类型多个构造器重载共用形参名时一并豁免）。</summary>
        private static readonly HashSet<string> KnownUnguardedConstructorParameters = new HashSet<string>(StringComparer.Ordinal)
        {
            "Presentation.Common.ResourceReferenceTracker(loader)",
            "Presentation.Ui.InteractPathProvider(registry)",
            "Presentation.Ui.UiActionInvokedEvent(actionName)",
            "Presentation.Ui.UiLayoutDefinition(fields)",
            "Presentation.Assembly.NumericValidationRuleDescriptor(ruleId)",
            "Presentation.Assembly.NumericValidationRuleDescriptor(checkName)",
            "Presentation.Assembly.NumericValidationRuleDescriptor(gradingItemName)",
            "Presentation.Assembly.NumericValidationRuleDescriptor(group)",
        };

        /// <summary>源码对 null 形参抛的不是 ArgumentNullException 而是 ArgumentException（"不能为空/空白"一类，
        /// 字符串形参 <c>string.IsNullOrWhiteSpace</c> 守卫）：同样是守卫，断言改为 ArgumentException 且 ParamName 一致。</summary>
        private static readonly HashSet<string> NullRejectedAsArgumentException = new HashSet<string>(StringComparer.Ordinal)
        {
            "Presentation.Assembly.SchemaAuditAllowlistEntry(table)",
            "Presentation.Assembly.SchemaAuditAllowlistEntry(field)",
            "Presentation.Assembly.SchemaAuditAllowlistEntry(reason)",
        };

        private static bool IsCandidateType(Type t) =>
            t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && !typeof(Delegate).IsAssignableFrom(t)
            && !(t.IsSealed && t.IsAbstract) && t.Namespace != null && t.Namespace.StartsWith("Presentation", StringComparison.Ordinal);

        private static IEnumerable<(Type Type, ConstructorInfo Ctor, ParameterInfo Param)> NonNullableReferenceParameters()
        {
            var nullability = new NullabilityInfoContext();
            foreach (var type in NullGuardSynthesizer.PresentationAssembly.GetExportedTypes().Where(IsCandidateType).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    foreach (var p in ctor.GetParameters())
                    {
                        if (p.ParameterType.IsValueType || p.HasDefaultValue || p.ParameterType.IsByRef) continue;
                        if (nullability.Create(p).WriteState != NullabilityState.NotNull) continue;
                        yield return (type, ctor, p);
                    }
                }
            }
        }

        private static string Signature(ConstructorInfo c) => string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name));

        public static IEnumerable<object[]> Cases() =>
            NonNullableReferenceParameters().Select(x => new object[] { x.Type.FullName!, Signature(x.Ctor), x.Param.Name! });

        private static (ConstructorInfo Ctor, ParameterInfo Param) Resolve(string typeName, string signature, string paramName)
        {
            var type = NullGuardSynthesizer.PresentationAssembly.GetType(typeName, throwOnError: true)!;
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single(c => Signature(c) == signature);
            return (ctor, ctor.GetParameters().Single(p => p.Name == paramName));
        }

        /// <summary>调用构造器（把 <paramref name="nullIndex"/> 位置置 null），返回抛出的异常（未抛返回 null）。</summary>
        private static Exception? InvokeWithNull(ConstructorInfo ctor, int nullIndex)
        {
            var ps = ctor.GetParameters();
            var args = new object?[ps.Length];
            var nullability = new NullabilityInfoContext();
            for (var i = 0; i < ps.Length; i++)
            {
                // 被测形参置 null；其余形参：声明为可空的引用类型传 null，其余合成合法占位。
                var declaredNullable = !ps[i].ParameterType.IsValueType && nullability.Create(ps[i]).WriteState == NullabilityState.Nullable;
                args[i] = i == nullIndex || declaredNullable ? null : NullGuardSynthesizer.Synthesize(ps[i].ParameterType);
            }
            try
            {
                ctor.Invoke(args);
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException;
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Constructor_NullArgument_ThrowsArgumentNullException_NamingTheParameter(string typeName, string signature, string paramName)
        {
            var (ctor, param) = Resolve(typeName, signature, paramName);
            if (KnownUnguardedConstructorParameters.Contains(typeName + "(" + paramName + ")"))
            {
                return; // 见 KnownUnguardedConstructorParameters_AreStillUnguarded
            }

            var ex = InvokeWithNull(ctor, param.Position);

            if (NullRejectedAsArgumentException.Contains(typeName + "(" + paramName + ")"))
            {
                var plain = Assert.IsType<ArgumentException>(ex);
                Assert.Equal(paramName, plain.ParamName);
                return;
            }

            var argNull = Assert.IsType<ArgumentNullException>(ex);
            Assert.Equal(paramName, argNull.ParamName);
        }

        /// <summary>保证豁免清单没有陈旧条目：清单里的形参传 null 时确实不抛 ArgumentNullException。</summary>
        [Fact]
        public void KnownUnguardedConstructorParameters_AreStillUnguarded()
        {
            var stale = new List<string>();
            foreach (var key in KnownUnguardedConstructorParameters)
            {
                var open = key.LastIndexOf('(');
                var typeName = key.Substring(0, open);
                var paramName = key.Substring(open + 1, key.Length - open - 2);
                var type = NullGuardSynthesizer.PresentationAssembly.GetType(typeName, throwOnError: true)!;
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    var p = ctor.GetParameters().FirstOrDefault(x => x.Name == paramName);
                    if (p == null) continue;
                    if (InvokeWithNull(ctor, p.Position) is ArgumentNullException)
                    {
                        stale.Add(key);
                    }
                }
            }

            Assert.Empty(stale);
        }
    }
}
