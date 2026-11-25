using System.Text.Json;

namespace MultiVRC.Core;

public static class BundledTemplates
{
    private const string TemplatePrefix = "MultiVRC.Bundled.";

    public static List<Template> All()
    {
        var assembly = typeof(BundledTemplates).Assembly;
        var templates = new List<Template>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(TemplatePrefix, StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var template = JsonSerializer.Deserialize<Template>(stream, JsonFiles.Options);
            if (template is null) continue;
            template.Bundled = true;
            templates.Add(template);
        }
        return templates;
    }
}
