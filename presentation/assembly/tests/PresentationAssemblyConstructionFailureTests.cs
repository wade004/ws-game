using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 数据源包装：把指定表名的表从 <see cref="ListTables"/> 里去掉——模拟"数据集缺某张表"，
    /// 用来走装配根构造期的数据缺失路径（<see cref="InMemoryDataSource"/> 只能追加、不能删除）。
    /// </summary>
    internal sealed class OmittingDataSource : IDataSource
    {
        private readonly IReadOnlyList<DataTableSource> _tables;

        public OmittingDataSource(IDataSource inner, IEnumerable<string> omittedTableNames)
        {
            var omitted = new HashSet<string>(omittedTableNames, StringComparer.Ordinal);
            _tables = inner.ListTables().Where(t => !omitted.Contains(t.TableName)).ToList();
        }

        public IReadOnlyList<DataTableSource> ListTables() => _tables;
    }

    /// <summary>
    /// T-M41（构造期失败路径）：必填构造参数为 null 时的参数名；数据集缺表时装配根的退化行为——
    /// 被其它表引用的表（如 <c>feedback.floating_text_style</c>）缺失由数据注册表的引用完整性校验在装配之前拦截，不属本类。
    /// 契约是"可选表缺失不阻断装配"（菜单退化为空、相机不自动配置），
    /// 而不是在后续某个宿主里抛出空引用。期望值一律由数据/选项自己算出。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        public static IEnumerable<object[]> RequiredConstructorArguments() => new[]
        {
            "gameplay", "world", "registry", "bus", "rng", "viewFactory", "renderer2D", "camera", "audio", "fileSystem", "sceneRouter",
        }.Select(n => new object[] { n });

        [Theory]
        [MemberData(nameof(RequiredConstructorArguments))]
        public void Construct_RequiredArgumentIsNull_ThrowsArgumentNullExceptionNamingIt(string argument)
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                Build(out _, out _, out _, out _, nullArgument: argument));

            Assert.Equal(argument, ex.ParamName);
        }

        [Fact]
        public void Construct_WithoutShellMenuDefinitionTable_DegradesToEmptyMenu()
        {
            var presentation = Build(out _, out _, out _, out _, omitTables: new[] { "shell_menu_definition" });

            Assert.Empty(presentation.ShellViewModel.MenuEntries);
        }

        [Fact]
        public void Construct_WithoutCameraProfileTable_DoesNotAutoConfigureCamera()
        {
            var presentation = Build(out _, out _, out _, out _, omitTables: new[] { "camera_profile" });

            Assert.NotNull(presentation.Camera);
            Assert.Null(presentation.Camera.CurrentProfile);
        }

        [Theory]
        [InlineData("vfx.def")]
        [InlineData("sfx.def")]
        [InlineData("display.weapon_style")]
        [InlineData("feedback.binding")]
        public void Construct_WithoutAnOptionalPresentationTable_StillConstructsEveryHost(string omittedTable)
        {
            var presentation = Build(out _, out _, out _, out _, omitTables: new[] { omittedTable });

            Assert.NotNull(presentation.Vfx);
            Assert.NotNull(presentation.Sfx);
            Assert.NotNull(presentation.Feedback);
            Assert.NotNull(presentation.Shell);
            Assert.NotNull(presentation.PauseMenu);
            // 能正常 Dispose（缺表不留下半构造状态）。
            presentation.Dispose();
        }
    }
}
