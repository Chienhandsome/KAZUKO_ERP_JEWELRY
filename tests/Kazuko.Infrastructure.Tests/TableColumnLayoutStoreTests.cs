using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Settings;
using Xunit;

namespace Kazuko.Infrastructure.Tests;

public sealed class TableColumnLayoutStoreTests
{
    [Fact]
    public void Legacy_files_and_complete_layouts_round_trip_across_store_instances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kazuko-column-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JsonTableColumnLayoutStore(directory);
            File.WriteAllText(store.GetSettingsPath("legacy"),
                "{\"Columns\":[{\"Key\":\"name\",\"IsVisible\":false,\"DisplayIndex\":0,\"Width\":145}]}");
            var legacy = store.Load("legacy");
            Assert.NotNull(legacy);
            Assert.Equal(1, legacy.SchemaVersion);
            Assert.Equal(145, Assert.Single(legacy.Columns).Width);
            store.Save("new", new DataGridColumnLayoutSettings([
                new("code", true, 1, 200), new("name", false, 0, 145)]));
            var reopened = new JsonTableColumnLayoutStore(directory).Load("new");
            Assert.NotNull(reopened);
            Assert.Equal(new DataGridColumnSetting("name", false, 0, 145), reopened.Columns[1]);
            File.WriteAllText(store.GetSettingsPath("broken"), "{broken");
            Assert.Null(store.Load("broken"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
