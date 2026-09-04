using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.Events
{
    public class EventCatalogTests
    {
        [Fact]
        public void FromDefinitions_RegistersAllGivenKeys()
        {
            var defA = new EventDefinition(new Id("test.a"), "test", new[] { "x" });
            var defB = new EventDefinition(new Id("test.b"), "test", Array.Empty<string>(), "示例描述");

            var catalog = EventCatalog.FromDefinitions(new List<EventDefinition> { defA, defB });

            Assert.True(catalog.IsRegistered(new Id("test.a")));
            Assert.True(catalog.IsRegistered(new Id("test.b")));
            Assert.False(catalog.IsRegistered(new Id("test.c")));
            Assert.Equal(2, catalog.All.Count);
        }

        [Fact]
        public void Get_ReturnsDefinitionForRegisteredKey_AndNullOtherwise()
        {
            var def = new EventDefinition(new Id("test.a"), "test", new[] { "x", "y" }, "desc");
            var catalog = EventCatalog.FromDefinitions(new[] { def });

            var found = catalog.Get(new Id("test.a"));
            Assert.NotNull(found);
            Assert.Equal("test", found!.Domain);
            Assert.Equal(new[] { "x", "y" }, found.Fields);
            Assert.Equal("desc", found.Description);

            Assert.Null(catalog.Get(new Id("test.missing")));
        }

        [Fact]
        public void FromDefinitions_DuplicateKey_Throws()
        {
            var defA = new EventDefinition(new Id("test.a"), "test", Array.Empty<string>());
            var defB = new EventDefinition(new Id("test.a"), "test", Array.Empty<string>());

            Assert.Throws<InvalidOperationException>(() =>
                EventCatalog.FromDefinitions(new[] { defA, defB }));
        }
    }
}
