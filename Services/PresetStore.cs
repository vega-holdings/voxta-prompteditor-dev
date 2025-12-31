using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Utils;
using Voxta.Modules.PromptEditor.Models;

namespace Voxta.Modules.PromptEditor.Services;

/// <summary>
/// Storage and conversion service for SillyTavern presets
/// </summary>
public sealed partial class PresetStore(ICommonFolders folders, PromptEditorStore templateStore, ILogger<PresetStore> logger)
{
    private readonly ILogger<PresetStore> _logger = logger;

    private const string PresetsFolderName = "Presets";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public string PresetsRoot => Path.Combine(folders.GetDataFolder("PromptEditor"), PresetsFolderName);

    /// <summary>
    /// Ensure presets folder exists
    /// </summary>
    private void EnsurePresetsFolder()
    {
        Directory.CreateDirectory(PresetsRoot);
    }

    /// <summary>
    /// List all preset names
    /// </summary>
    public IReadOnlyList<string> ListPresets()
    {
        EnsurePresetsFolder();

        if (!Directory.Exists(PresetsRoot))
            return [];

        return Directory.GetFiles(PresetsRoot, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Read a preset by name
    /// </summary>
    public async Task<SillyTavernPreset?> ReadPresetAsync(string name, CancellationToken cancellationToken)
    {
        var path = GetPresetPath(name);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<SillyTavernPreset>(json, JsonOptions);
    }

    /// <summary>
    /// Write a preset
    /// </summary>
    public async Task WritePresetAsync(string name, SillyTavernPreset preset, CancellationToken cancellationToken)
    {
        EnsurePresetsFolder();
        var path = GetPresetPath(name);
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        await File.WriteAllTextAsync(path, json, Utf8NoBom, cancellationToken);
        _logger.LogInformation("Saved preset '{Name}'", name);
    }

    /// <summary>
    /// Delete a preset
    /// </summary>
    public void DeletePreset(string name)
    {
        var path = GetPresetPath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted preset '{Name}'", name);
        }
    }

    /// <summary>
    /// Convert a preset to Scriban include files in a collection
    /// </summary>
    public async Task<ConversionResult> ConvertToScribanAsync(
        string presetName,
        string targetCollection,
        string language,
        CancellationToken cancellationToken)
    {
        var preset = await ReadPresetAsync(presetName, cancellationToken);
        if (preset == null)
            throw new InvalidOperationException($"Preset not found: {presetName}");

        var safeName = SanitizeName(presetName);
        var basePath = $"Presets/{safeName}";

        // Get enabled prompts in order
        var globalOrder = preset.PromptOrder.FirstOrDefault(po => po.CharacterId == 100001);
        var orderMap = (globalOrder?.Order ?? [])
            .Select((o, i) => new { o.Identifier, o.Enabled, Index = i })
            .ToDictionary(x => x.Identifier);

        var orderedPrompts = preset.Prompts
            .Where(p => !p.Marker && orderMap.TryGetValue(p.Identifier, out var o) && o.Enabled)
            .OrderBy(p => orderMap.TryGetValue(p.Identifier, out var o) ? o.Index : 999)
            .ToList();

        var files = new List<string>();
        var category = "TextGen/Includes";

        // Generate individual Scriban files for each prompt
        foreach (var prompt in orderedPrompts)
        {
            var templatePath = $"{basePath}/{SanitizeName(prompt.Name)}.scriban";
            var content = ConvertPromptContent(prompt.Content, prompt.Name, presetName);

            await templateStore.WriteTemplateAsync(
                "collection",
                targetCollection,
                language,
                category,
                templatePath,
                content,
                cancellationToken);

            files.Add(templatePath);
        }

        // Generate main include file
        var mainContent = GenerateMainInclude(orderedPrompts, basePath, presetName);
        var mainPath = $"{basePath}/Main.scriban";
        await templateStore.WriteTemplateAsync(
            "collection",
            targetCollection,
            language,
            category,
            mainPath,
            mainContent,
            cancellationToken);
        files.Add(mainPath);

        _logger.LogInformation(
            "Converted preset '{Preset}' to {Count} Scriban files in collection '{Collection}'",
            presetName, files.Count, targetCollection);

        return new ConversionResult(targetCollection, files);
    }

    private string GetPresetPath(string name)
    {
        var safeName = SanitizeName(name);
        return Path.Combine(PresetsRoot, safeName + ".json");
    }

    private static string SanitizeName(string value)
    {
        var cleaned = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(c, '_');
        }
        return cleaned.Replace(' ', '-');
    }

    /// <summary>
    /// Convert SillyTavern prompt content to Scriban format
    /// </summary>
    private static string ConvertPromptContent(string content, string promptName, string presetName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var converted = content;

        // Replace SillyTavern variables with Scriban equivalents
        converted = CharNameRegex().Replace(converted, "{{ char }}");
        converted = UserNameRegex().Replace(converted, "{{ user }}");
        converted = ScenarioRegex().Replace(converted, "{{ scenario | default: \"\" }}");
        converted = PersonalityRegex().Replace(converted, "{{ personality | default: \"\" }}");
        converted = DescriptionRegex().Replace(converted, "{{ description | default: \"\" }}");
        converted = PersonaRegex().Replace(converted, "{{ persona | default: \"\" }}");

        // Time variables
        converted = TimeRegex().Replace(converted, "{{ date.now | date.to_string \"%H:%M\" }}");
        converted = DateRegex().Replace(converted, "{{ date.now | date.to_string \"%Y-%m-%d\" }}");
        converted = WeekdayRegex().Replace(converted, "{{ date.now | date.to_string \"%A\" }}");

        // Remove SillyTavern-specific macros
        converted = CommentRegex().Replace(converted, "");
        converted = TrimRegex().Replace(converted, "");
        converted = SetVarRegex().Replace(converted, "");
        converted = GetVarRegex().Replace(converted, "");
        converted = RollRegex().Replace(converted, "");
        converted = EmptyBracesRegex().Replace(converted, "");

        // Add header comment
        var header = $"{{{{~ # Generated from SillyTavern preset: {presetName} ~}}}}\n" +
                     $"{{{{~ # Prompt: {promptName} ~}}}}\n";

        return header + converted.Trim();
    }

    /// <summary>
    /// Generate main include file that chains all prompts
    /// </summary>
    private static string GenerateMainInclude(List<PromptEntry> prompts, string basePath, string presetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{{{{~ # Main include for SillyTavern preset: {presetName} ~}}}}");
        sb.AppendLine($"{{{{~ # Generated on {DateTime.UtcNow:O} ~}}}}");
        sb.AppendLine();
        sb.AppendLine("{{~ # This file chains all enabled prompts from the preset ~}}");
        sb.AppendLine();

        foreach (var prompt in prompts)
        {
            var includeName = $"{basePath}/{SanitizeName(prompt.Name)}";
            var roleBadge = !string.IsNullOrEmpty(prompt.Role) ? $"[{prompt.Role}] " : "";
            sb.AppendLine($"{{{{~ # {roleBadge}{prompt.Name} ~}}}}");
            sb.AppendLine($"{{{{ include '{includeName}' }}}}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // Regex patterns for variable replacement
    [GeneratedRegex(@"\{\{char\}\}|\{\{charname\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex CharNameRegex();

    [GeneratedRegex(@"\{\{user\}\}|\{\{username\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex UserNameRegex();

    [GeneratedRegex(@"\{\{scenario\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex ScenarioRegex();

    [GeneratedRegex(@"\{\{personality\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PersonalityRegex();

    [GeneratedRegex(@"\{\{description\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionRegex();

    [GeneratedRegex(@"\{\{persona\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PersonaRegex();

    [GeneratedRegex(@"\{\{time\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\{\{date\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\{\{weekday\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"\{\{//[^}]*\}\}")]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"\{\{trim\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex TrimRegex();

    [GeneratedRegex(@"\{\{setvar::[^}]*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex SetVarRegex();

    [GeneratedRegex(@"\{\{getvar::[^}]*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex GetVarRegex();

    [GeneratedRegex(@"\{\{roll:[^}]*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex RollRegex();

    [GeneratedRegex(@"\{\{\s*\}\}")]
    private static partial Regex EmptyBracesRegex();
}

/// <summary>
/// Result of preset-to-Scriban conversion
/// </summary>
public sealed record ConversionResult(string CollectionName, IReadOnlyList<string> Files);
