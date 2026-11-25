using System.Text.Json.Nodes;
using JsonConfigEditor.Core;
using Xunit;

namespace JsonConfigEditor.Tests;

/// <summary>Listen sortieren mit ▲ ▼ (SPEC "Listen sortieren").</summary>
public class ListReorderTests
{
    private const string Devices = """
        { "Audio": { "PreferredDevices": [
            { "Name": "Jabra EVOLVE LINK", "Id": "{0.0.1}.{4b02}", "LastSeen": "2026-11-25 22:34" },
            { "Name": "Mikrofonarray Intel", "Id": "{0.0.1}.{b19f}", "LastSeen": "2026-11-25 22:34" }
        ] } }
        """;

    private static string[] Names(JsonNode? list) =>
        list!.AsArray().Select(n => (string)n!["Name"]!).ToArray();

    [Fact]
    public void Label_is_the_first_string_property_or_itemLabel()
    {
        using var t = new TempDir().WithBase("""{ "Audio": { "PreferredDevices": [] } }""").WithUser(Devices);
        var f = t.Load().Find("Audio:PreferredDevices")!;
        Assert.Equal(["Jabra EVOLVE LINK", "Mikrofonarray Intel"], ConfigList.Items(f).Select(i => i.Label));

        Assert.Equal("{0.0.1}.{4b02}", ConfigList.LabelOf(JsonNode.Parse("""{ "Name": "x", "Id": "{0.0.1}.{4b02}" }"""), "id", 0));
        Assert.Equal("abc", ConfigList.LabelOf(JsonValue.Create("abc"), null, 0));
        Assert.Equal("7", ConfigList.LabelOf(JsonValue.Create(7), null, 0));
        Assert.Equal("Eintrag 3", ConfigList.LabelOf(JsonNode.Parse("""{ "n": 1 }"""), null, 2));
    }

    [Fact]
    public void Moving_writes_the_whole_list_and_keeps_every_property()
    {
        using var t = new TempDir().WithBase("""{ "Audio": { "PreferredDevices": [] } }""").WithUser(Devices);
        var doc = t.Load();
        var f = doc.Find("Audio:PreferredDevices")!;
        var second = ConfigList.Items(f)[1];

        ConfigWriter.MoveItem(doc, f.Key, second.Index, second.Node, -1);

        var written = JsonNode.Parse(t.UserText)!["Audio"]!["PreferredDevices"]!;
        Assert.Equal(["Mikrofonarray Intel", "Jabra EVOLVE LINK"], Names(written));
        Assert.Equal("{0.0.1}.{b19f}", (string)written[0]!["Id"]!);
        Assert.Equal("2026-11-25 22:34", (string)written[1]!["LastSeen"]!);
        Assert.Equal(["Mikrofonarray Intel", "Jabra EVOLVE LINK"], Names(f.EffectiveList));
    }

    [Fact]
    public void Effective_list_merges_by_index_like_IConfiguration()
    {
        using var t = new TempDir().WithBase("""{ "L": ["a", "b", "c"] }""").WithUser("""{ "L": ["x"] }""");
        var f = t.Load().Find("L")!;
        Assert.Equal(["x", "b", "c"], f.EffectiveList!.Select(n => (string)n!));
        Assert.Equal(3, f.ArrayCount);
    }

    [Fact]
    public void After_moving_a_shorter_user_list_the_effective_list_is_right()
    {
        using var t = new TempDir().WithBase("""{ "L": ["a", "b", "c"] }""").WithUser("""{ "L": ["x"] }""");
        var doc = t.Load();
        var f = doc.Find("L")!;
        var c = ConfigList.Items(f)[2];

        ConfigWriter.MoveItem(doc, "L", c.Index, c.Node, -1);

        Assert.Equal(["x", "c", "b"], JsonNode.Parse(t.UserText)!["L"]!.AsArray().Select(n => (string)n!));
        doc.Reload();
        Assert.Equal(["x", "c", "b"], doc.Find("L")!.EffectiveList!.Select(n => (string)n!));
    }

    [Fact]
    public void Moving_back_to_the_default_order_removes_the_key()
    {
        using var t = new TempDir().WithBase("""{ "L": ["a", "b"], "Other": 1 }""");
        var doc = t.Load();
        var f = doc.Find("L")!;
        var b = ConfigList.Items(f)[1];
        ConfigWriter.MoveItem(doc, "L", b.Index, b.Node, -1);
        Assert.True(f.HasUser);

        var b2 = ConfigList.Items(f)[0];
        ConfigWriter.MoveItem(doc, "L", b2.Index, b2.Node, +1);
        Assert.False(f.HasUser);
        Assert.False(JsonNode.Parse(t.UserText)!.AsObject().ContainsKey("L"));
    }

    [Fact]
    public void App_refreshed_LastSeen_meanwhile_item_is_found_by_its_label()
    {
        using var t = new TempDir().WithBase("""{ "Audio": { "PreferredDevices": [] } }""").WithUser(Devices);
        var doc = t.Load();
        var f = doc.Find("Audio:PreferredDevices")!;
        var second = ConfigList.Items(f)[1];

        // App schreibt inzwischen: LastSeen neu, drittes Mikro hinten dran
        File.WriteAllText(t.User, Devices.Replace("2026-11-25 22:34", "2026-11-26 08:00")
            .Replace("] } }", """, { "Name": "PowerConf", "Id": "x" } ] } }"""));

        ConfigWriter.MoveItem(doc, f.Key, second.Index, second.Node, -1);

        var written = JsonNode.Parse(t.UserText)!["Audio"]!["PreferredDevices"]!;
        Assert.Equal(["Mikrofonarray Intel", "Jabra EVOLVE LINK", "PowerConf"], Names(written));
        Assert.Equal("2026-11-26 08:00", (string)written[0]!["LastSeen"]!);
    }

    [Fact]
    public void Item_gone_meanwhile_nothing_is_moved()
    {
        using var t = new TempDir().WithBase("""{ "Audio": { "PreferredDevices": [] } }""").WithUser(Devices);
        var doc = t.Load();
        var f = doc.Find("Audio:PreferredDevices")!;
        var second = ConfigList.Items(f)[1];

        var onlyJabra = """{ "Audio": { "PreferredDevices": [ { "Name": "Jabra EVOLVE LINK" }, { "Name": "PowerConf" } ] } }""";
        File.WriteAllText(t.User, onlyJabra);

        Assert.Throws<ListChangedException>(() => ConfigWriter.MoveItem(doc, f.Key, second.Index, second.Node, -1));
        Assert.Equal(onlyJabra, t.UserText);
    }

    [Fact]
    public void Locked_list_cannot_be_moved()
    {
        using var t = new TempDir().WithBase("""{ "L": ["a", "b"] }""").WithMachine("""{ "Locked": { "L": ["b", "a"] } }""");
        var doc = t.Load();
        var f = doc.Find("L")!;
        Assert.False(f.CanReorder);
        var item = ConfigList.Items(f)[1];
        Assert.Throws<FieldLockedException>(() => ConfigWriter.MoveItem(doc, "L", item.Index, item.Node, -1));
    }

    [Fact]
    public void Descriptor_list_sets_label_detail_and_can_switch_reorder_off()
    {
        using var t = new TempDir()
            .WithBase("""{ "Audio": { "PreferredDevices": [] }, "Fixed": ["a", "b"] }""")
            .WithUser(Devices)
            .WithDescriptor("""
                { "fields": {
                    "Audio:PreferredDevices": { "list": { "itemLabel": "Id", "itemDetail": ["LastSeen"] } },
                    "Fixed": { "list": { "reorder": false } }
                } }
                """);
        var doc = t.Load();
        var f = doc.Find("Audio:PreferredDevices")!;
        var first = ConfigList.Items(f)[0];
        Assert.Equal("{0.0.1}.{4b02}", first.Label);
        Assert.Equal("LastSeen: 2026-11-25 22:34", first.Detail);
        Assert.Contains("Name: Jabra EVOLVE LINK", first.ToolTip);
        Assert.True(f.CanReorder);

        var fixedList = doc.Find("Fixed")!;
        Assert.False(fixedList.CanReorder);
        var b = ConfigList.Items(fixedList)[1];
        Assert.Throws<InvalidOperationException>(() => ConfigWriter.MoveItem(doc, "Fixed", b.Index, b.Node, -1));
        Assert.DoesNotContain(doc.Warnings, w => w.Contains(".list"));
    }

    [Fact]
    public void Wrong_type_in_list_costs_only_that_attribute()
    {
        using var t = new TempDir()
            .WithBase("""{ "L": ["a"] }""")
            .WithDescriptor("""{ "fields": { "L": { "label": "Liste", "list": { "itemLabel": 3 } } } }""");
        var doc = t.Load();
        var f = doc.Find("L")!;
        Assert.Equal("Liste", f.Label);
        Assert.True(f.CanReorder);
        Assert.Contains(doc.Warnings, w => w.Contains("L.list"));
    }

    [Fact]
    public void Moving_off_the_ends_does_nothing()
    {
        using var t = new TempDir().WithBase("""{ "L": ["a", "b"] }""");
        var doc = t.Load();
        var a = ConfigList.Items(doc.Find("L")!)[0];
        ConfigWriter.MoveItem(doc, "L", a.Index, a.Node, -1);
        Assert.False(File.Exists(t.User));
    }
}
