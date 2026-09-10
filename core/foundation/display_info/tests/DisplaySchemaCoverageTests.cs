using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>
    /// ADR-0019 / F1c：<see cref="Core.Foundation.DisplayInfo.DisplaySchemas.Map"/> 的
    /// <c>mirror_pairs</c>/<c>paperdoll_layers</c> 子结构登记（<c>Item</c>）；ADR-0024 第二批登记
    /// 起 <c>anchor_points</c>/<c>default_slot_meshes</c>/<c>material_params</c>
    /// （<see cref="Core.Foundation.DisplayInfo.DisplaySchemas.Map"/>）与
    /// <c>Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet</c> 的 <c>clips</c> 均改用
    /// <c>MapSchema</c> 登记，覆盖范围：子结构命中/坏形状各一例。
    /// </summary>
    public sealed class DisplaySchemaCoverageTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        [Fact]
        public void MirrorPairsAndPaperdollLayers_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.cov_sample\",\"category\":\"creature\",\"logical_id\":\"creature.cov_sample\"," +
                "\"kind\":\"sprite\"," +
                "\"mirror_pairs\":[{\"direction_slot\":\"dir.west\",\"mirror_of\":\"dir.east\",\"flip_x\":true}]," +
                "\"paperdoll_layers\":[\"base\",\"armor\"]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void MirrorPairs_MissingMirrorOf_ReportsRequiredField()
        {
            var rows = "[{\"id\":\"display.cov_bad\",\"category\":\"creature\",\"logical_id\":\"creature.cov_bad\"," +
                "\"kind\":\"sprite\",\"mirror_pairs\":[{\"direction_slot\":\"dir.west\"}]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "mirror_pairs[0].mirror_of");
        }

        [Fact]
        public void PaperdollLayers_NonStringElement_ReportsFieldType()
        {
            var rows = "[{\"id\":\"display.cov_bad_layer\",\"category\":\"creature\",\"logical_id\":\"creature.cov_bad_layer\"," +
                "\"kind\":\"sprite\",\"paperdoll_layers\":[123]}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_type" && i.Field == "paperdoll_layers[0]");
        }

        // -----------------------------------------------------------------
        // ADR-0024 第二批登记：anchor_points / default_slot_meshes / material_params（MapSchema.FreeKeyed）
        // -----------------------------------------------------------------

        [Fact]
        public void AnchorPoints_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.cov_anchor\",\"category\":\"creature\",\"logical_id\":\"creature.cov_anchor\"," +
                "\"kind\":\"sprite\",\"paperdoll_layers\":[\"hand_main\"]," +
                "\"anchor_points\":{\"hand_main\":{\"parent_layer\":\"hand_main\",\"offset\":{\"x\":1,\"y\":2}," +
                "\"offset_by_direction\":{\"dir.side_r\":{\"x\":3,\"y\":4}}}}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void AnchorPoints_MissingParentLayer_ReportsRequiredFieldWithBracketPath()
        {
            var rows = "[{\"id\":\"display.cov_anchor_bad\",\"category\":\"creature\",\"logical_id\":\"creature.cov_anchor_bad\"," +
                "\"kind\":\"sprite\",\"anchor_points\":{\"hand_main\":{\"offset\":{\"x\":1,\"y\":2}}}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "required_field" && i.Field == "anchor_points[hand_main].parent_layer");
        }

        [Fact]
        public void DefaultSlotMeshesAndMaterialParams_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.cov_model\",\"category\":\"creature\",\"logical_id\":\"creature.cov_model\"," +
                "\"kind\":\"model\",\"model_ref\":\"model.cov_sample\"," +
                "\"default_slot_meshes\":{\"slot.head\":\"mesh.cov_helmet\"}," +
                "\"material_params\":{\"emission_strength\":0.5}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void DefaultSlotMeshes_ValueNotValidId_ReportsFieldType()
        {
            var rows = "[{\"id\":\"display.cov_model_bad\",\"category\":\"creature\",\"logical_id\":\"creature.cov_model_bad\"," +
                "\"kind\":\"model\",\"model_ref\":\"model.cov_sample\"," +
                "\"default_slot_meshes\":{\"slot.head\":\"not a valid id\"}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_type" && i.Field == "default_slot_meshes[slot.head]");
        }

        [Fact]
        public void MaterialParams_ValueNotNumber_ReportsFieldType()
        {
            var rows = "[{\"id\":\"display.cov_model_bad2\",\"category\":\"creature\",\"logical_id\":\"creature.cov_model_bad2\"," +
                "\"kind\":\"model\",\"model_ref\":\"model.cov_sample\"," +
                "\"material_params\":{\"emission_strength\":\"not_a_number\"}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.Map.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.Map);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "field_type" && i.Field == "material_params[emission_strength]");
        }

        // -----------------------------------------------------------------
        // ADR-0024 第二批登记：display.anim_set.clips（MapSchema.FreeKeyed）
        // -----------------------------------------------------------------

        [Fact]
        public void AnimSetClips_WellFormed_LoadsWithoutErrors()
        {
            var rows = "[{\"id\":\"display.anim_set.cov_sample\",\"clips\":{\"idle\":{\"resource_ref\":\"anim.cov_idle\"," +
                "\"events\":[{\"name\":\"hit_frame\",\"time_pct\":0.5}]}}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void AnimSetClips_MissingResourceRef_ReportsRequiredFieldWithBracketPath()
        {
            var rows = "[{\"id\":\"display.anim_set.cov_bad\",\"clips\":{\"idle\":{}}}]";

            var source = new InMemoryDataSource().Add(
                Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet.Name,
                Envelope(Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet.Name, rows));
            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(Core.Foundation.DisplayInfo.DisplaySchemas.AnimSet);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i =>
                i.Check == "required_field" && i.Field == "clips[idle].resource_ref");
        }
    }
}
