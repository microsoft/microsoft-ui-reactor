using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WinUI.Dock;

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext;

internal static class LayoutHelpers
{
    public const string Document = "Document";
    public const string DocumentGroup = "DocumentGroup";
    public const string LayoutPanel = "LayoutPanel";

    public static readonly SourceGenerationContext SerializerContext = new(new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    });

    public static T Deserialize<T>(this JsonNode? node)
    {
        return node is null ? default! : (T)node.Deserialize(typeof(T), SerializerContext)!;
    }

    public static string Path(this DockModule module)
    {
        string name = module is Document document ? document.Title : module.GetType().Name;

        if (module.Owner is null)
        {
            return name;
        }
        else
        {
            string path = module.Owner.Path();

            if (module.Owner is DockContainer container)
            {
                path += $"[{container.Children.IndexOf(module)}]";
            }

            path += $".{name}";

            return path;
        }
    }

    public static void WriteByModuleType(this JsonObject writer, DockModule module)
    {
        writer["Type"] = module switch
        {
            Document _ => Document,
            DocumentGroup _ => DocumentGroup,
            LayoutPanel _ => LayoutPanel,
            _ => throw new NotSupportedException()
        };
    }

    public static DockModule CreateByModuleType(this JsonObject reader)
    {
        return reader["Type"].Deserialize<string>() switch
        {
            Document => new Document(),
            DocumentGroup => new DocumentGroup(),
            LayoutPanel => new LayoutPanel(),
            _ => throw new NotSupportedException()
        };
    }

    public static void WriteDockModuleProperties(this JsonObject writer, DockModule module)
    {
        writer[nameof(DockModule.MinWidth)] = module.MinWidth;
        writer[nameof(DockModule.MaxWidth)] = module.MaxWidth;
        writer[nameof(DockModule.Width)] = module.Width;
        writer[nameof(DockModule.MinHeight)] = module.MinHeight;
        writer[nameof(DockModule.MaxHeight)] = module.MaxHeight;
        writer[nameof(DockModule.Height)] = module.Height;
    }

    public static void ReadDockModuleProperties(this JsonObject reader, DockModule module)
    {
        module.MinWidth = reader[nameof(DockModule.MinWidth)].Deserialize<double>();
        module.MaxWidth = reader[nameof(DockModule.MaxWidth)].Deserialize<double>();
        module.Width = reader[nameof(DockModule.Width)].Deserialize<double>();
        module.MinHeight = reader[nameof(DockModule.MinHeight)].Deserialize<double>();
        module.MaxHeight = reader[nameof(DockModule.MaxHeight)].Deserialize<double>();
        module.Height = reader[nameof(DockModule.Height)].Deserialize<double>();
    }

    public static void WriteDockContainerChildren(this JsonObject writer, DockContainer container)
    {
        writer[nameof(DockContainer.Children)] = new JsonArray([.. container.Children.Select(static item =>
        {
            JsonObject itemWriter = [];
            item.SaveLayout(itemWriter);

            return itemWriter;
        })]);
    }

    public static void ReadDockContainerChildren(this JsonObject reader, DockContainer container)
    {
        foreach (JsonObject childReader in reader[nameof(DockContainer.Children)]!.AsArray().Cast<JsonObject>())
        {
            DockModule child = CreateByModuleType(childReader);
            child.LoadLayout(childReader);

            container.Children.Add(child);
        }
    }

    public static void WriteSideDocuments(this JsonObject writer, string sideName, ObservableCollection<Document> side)
    {
        writer[sideName] = new JsonArray([.. side.Select(static item =>
        {
            JsonObject itemWriter = [];
            item.SaveLayout(itemWriter);

            return itemWriter;
        })]);
    }

    public static void ReadSideDocuments(this JsonObject reader, string sideName, ObservableCollection<Document> side)
    {
        foreach (JsonObject documentReader in reader[sideName]!.AsArray().Cast<JsonObject>())
        {
            Document document = new();
            document.LoadLayout(documentReader);

            side.Add(document);
        }
    }
}
