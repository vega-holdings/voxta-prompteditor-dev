using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Utils;
using Voxta.Model.ApiMessages.Requests;
using Voxta.Model.ApiMessages.Responses;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.PromptEditor.Configuration;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global", Justification = "Field names are reused in module registration.")]
public class ModuleConfigurationProvider(
    ICommonFolders folders,
    ILogger<ModuleConfigurationProvider> logger
) : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    private const string EditingSourceLive = "live";
    private const string EditingSourceCollection = "collection";

    public const string EditingSource = "EditingSource";
    public const string CollectionName = "CollectionName";
    public const string NewCollectionName = "NewCollectionName";
    public const string Language = "Language";
    public const string Category = "Category";
    public const string Template = "Template";
    public const string LoadedTemplateKey = "LoadedTemplateKey";
    public const string TemplateText = "TemplateText";

    public const string ActionCreateCollection = "ActionCreateCollection";
    public const string ActionApplyCollection = "ActionApplyCollection";
    public const string ActionRestoreOriginal = "ActionRestoreOriginal";

    private const string DataFolderName = "PromptEditor";
    private const string OriginalsFolderName = "Originals";
    private const string CollectionsFolderName = "Collections";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string[] FieldsRequiringReload =>
    [
        EditingSource,
        CollectionName,
        Language,
        Category,
        Template,
    ];

    public async Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(auth.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return FormBuilder.Build(
                FormTitleField.Create("Prompt Editor", "Admin-only.", false)
            );
        }

        var liveRoot = GetLivePromptsRoot();
        var dataRoot = folders.GetDataFolder(DataFolderName);
        var originalsRoot = Path.Combine(dataRoot, OriginalsFolderName);
        var collectionsRoot = Path.Combine(dataRoot, CollectionsFolderName);

        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(collectionsRoot);

        var source = NormalizeSource(settings.GetRawValue(EditingSource));
        var selectedCollection = settings.GetRawValue(CollectionName).Trim();
        var hasCollection = source == EditingSourceCollection && !string.IsNullOrWhiteSpace(selectedCollection);

        var activeRoot = source == EditingSourceLive
            ? liveRoot
            : hasCollection
                ? Path.Combine(collectionsRoot, SanitizeName(selectedCollection))
                : Path.Combine(collectionsRoot, "_no_collection_selected");

        var languages = ListDirectories(liveRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (languages.Length == 0)
        {
            languages = ["en"];
        }

        var selectedLanguage = settings.GetRawValue(Language).Trim();
        if (string.IsNullOrWhiteSpace(selectedLanguage) || !languages.Contains(selectedLanguage, StringComparer.OrdinalIgnoreCase))
        {
            selectedLanguage = languages.FirstOrDefault(x => string.Equals(x, "en", StringComparison.OrdinalIgnoreCase)) ?? languages[0];
        }

        var categoriesRoot = Path.Combine(activeRoot, selectedLanguage);
        var categories = ListDirectories(categoriesRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectedCategory = settings.GetRawValue(Category).Trim();
        if (categories.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(selectedCategory) || !categories.Contains(selectedCategory, StringComparer.OrdinalIgnoreCase))
            {
                selectedCategory = categories[0];
            }
        }
        else
        {
            selectedCategory = string.Empty;
        }

        var templates = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(selectedCategory))
        {
            templates = ListTemplates(Path.Combine(categoriesRoot, selectedCategory))
                .Select(x => x.Replace('\\', '/'))
                .ToArray();
        }

        var selectedTemplate = settings.GetRawValue(Template).Trim();
        if (templates.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(selectedTemplate) || !templates.Contains(selectedTemplate, StringComparer.OrdinalIgnoreCase))
            {
                selectedTemplate = templates[0];
            }
        }
        else
        {
            selectedTemplate = string.Empty;
        }

        var collections = ListDirectories(collectionsRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectedKey = (!string.IsNullOrWhiteSpace(selectedCategory) && !string.IsNullOrWhiteSpace(selectedTemplate))
            ? BuildLoadedKey(source, selectedCollection, selectedLanguage, selectedCategory, selectedTemplate)
            : string.Empty;
        var loadedKey = settings.GetRawValue(LoadedTemplateKey).Trim();
        var loadedMatchesSelection = !string.IsNullOrWhiteSpace(selectedKey)
            && !string.IsNullOrWhiteSpace(loadedKey)
            && string.Equals(selectedKey, loadedKey, StringComparison.OrdinalIgnoreCase);

        var status = new List<string>
        {
            $"Live root: {liveRoot}",
            $"Data root: {dataRoot}",
            $"Originals backup: {originalsRoot}",
            $"Collections root: {collectionsRoot}",
            $"Editing source: {source}",
            $"Editing root: {activeRoot}",
            $"Language: {selectedLanguage}",
            $"Category: {(string.IsNullOrWhiteSpace(selectedCategory) ? "(none)" : selectedCategory)}",
            $"Template: {(string.IsNullOrWhiteSpace(selectedTemplate) ? "(none)" : selectedTemplate)}",
            $"Loaded matches selection: {loadedMatchesSelection}",
        };

        if (source == EditingSourceCollection)
        {
            status.Add($"Collection: {(string.IsNullOrWhiteSpace(selectedCollection) ? "(none) - select one to enable editing" : selectedCollection)}");
        }

        var sourceField = new FormChoicesField
        {
            Name = EditingSource,
            Label = "Editing Source",
            Text = "Live edits affect Voxta immediately. Collections are stored under Data and can be applied to Live.",
            Choices =
            [
                new FormChoice { Label = "Live (Resources/Prompts/Default)", Value = EditingSourceLive },
                new FormChoice { Label = "Collection (Data/PromptEditor/Collections/<name>)", Value = EditingSourceCollection },
            ],
            DefaultValue = EditingSourceLive,
        };

        var collectionField = new FormChoicesField
        {
            Name = CollectionName,
            Label = "Collection",
            Text = "Only used when Editing Source = Collection.",
            Choices = (collections.Length == 0)
                ? [new FormChoice { Label = "(none)", Value = "" }]
                : collections.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = collections.Length == 0 ? "" : collections[0],
            Advanced = false,
        };

        var newCollectionField = new FormTextField
        {
            Name = NewCollectionName,
            Label = "New Collection Name",
            Text = "Type a new name, Save, then click “Create Collection from Live”.",
            Placeholder = "my-prompts",
            Advanced = true,
        };

        var languageField = new FormChoicesField
        {
            Name = Language,
            Label = "Language",
            Choices = languages.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = selectedLanguage,
        };

        var categoryField = new FormChoicesField
        {
            Name = Category,
            Label = "Category Folder",
            Text = "Top-level folder under the selected language.",
            Choices = (categories.Length == 0)
                ? [new FormChoice { Label = "(none)", Value = "" }]
                : categories.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = selectedCategory,
            Large = true,
        };

        var templateField = new FormChoicesField
        {
            Name = Template,
            Label = "Template",
            Choices = (templates.Length == 0)
                ? [new FormChoice { Label = "(none)", Value = "" }]
                : templates.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = selectedTemplate,
            Large = true,
        };

        var loadedKeyField = new FormTextField
        {
            Name = LoadedTemplateKey,
            Label = "Loaded Template Key (internal)",
            Text = "Used to avoid overwriting a newly-selected template with the previously-loaded editor content. Do not edit.",
            Advanced = true,
        };

        var editorField = new FormMultilineField
        {
            Name = TemplateText,
            Label = "Template Content",
            Rows = 26,
            Text = "Change selection, then Save once to load it into the editor. Edit, then Save again to write changes to disk. Tip: don’t edit before loading a new selection.",
            Advanced = false,
        };

        if (!settings.HasValue(loadedKeyField) && !string.IsNullOrWhiteSpace(selectedKey))
        {
            loadedKeyField.StartValue = selectedKey;
        }

        // Provide initial editor content when the module is first added (no saved TemplateText yet).
        if (!settings.HasValue(editorField) && !string.IsNullOrWhiteSpace(selectedCategory) && !string.IsNullOrWhiteSpace(selectedTemplate))
        {
            var maybePath = TryResolveTemplatePath(activeRoot, selectedLanguage, selectedCategory, selectedTemplate, out _);
            if (maybePath != null && File.Exists(maybePath))
            {
                editorField.StartValue = await File.ReadAllTextAsync(maybePath, cancellationToken);
            }
        }

        return FormBuilder.Build(
            FormTitleField.Create("Prompt Editor (alpha)", "Save writes the currently loaded template. If you changed selection, Save also loads the newly-selected template into the editor.", false),
            FormDocumentationField.Create(string.Join(Environment.NewLine, status), "Status"),
            sourceField,
            collectionField,
            newCollectionField,
            new FormInvokeActionField { Name = ActionCreateCollection, Label = "", ButtonText = "Create Collection from Live" },
            new FormInvokeActionField { Name = ActionApplyCollection, Label = "", ButtonText = "Apply Collection to Live (selected language)" },
            new FormInvokeActionField { Name = ActionRestoreOriginal, Label = "", ButtonText = "Restore Originals to Live (selected language)" },
            FormTitleField.Create("Template", null, false),
            languageField,
            categoryField,
            templateField,
            loadedKeyField,
            editorField
        );
    }

    public override Task UpdateModuleConfigurationAsync(ISettingsSource settings)
    {
        try
        {
            var dict = TryGetSettingsDictionary(settings);
            if (dict == null)
            {
                return Task.CompletedTask;
            }

            var source = NormalizeSource(dict.GetValueOrDefault(EditingSource) ?? EditingSourceLive);
            var collection = (dict.GetValueOrDefault(CollectionName) ?? string.Empty).Trim();

            var liveRoot = GetLivePromptsRoot();
            var collectionsRoot = Path.Combine(folders.GetDataFolder(DataFolderName), CollectionsFolderName);
            if (source == EditingSourceCollection && string.IsNullOrWhiteSpace(collection))
            {
                return Task.CompletedTask;
            }

            var activeRoot = source == EditingSourceCollection
                ? Path.Combine(collectionsRoot, SanitizeName(collection))
                : liveRoot;

            var lang = (dict.GetValueOrDefault(Language) ?? "en").Trim();
            var category = (dict.GetValueOrDefault(Category) ?? string.Empty).Trim();
            var template = (dict.GetValueOrDefault(Template) ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(lang) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(template))
            {
                return Task.CompletedTask;
            }

            var selectedKey = BuildLoadedKey(source, collection, lang, category, template);
            var loadedKey = (dict.GetValueOrDefault(LoadedTemplateKey) ?? string.Empty).Trim();

            var templateText = dict.GetValueOrDefault(TemplateText) ?? string.Empty;

            // Selection changed: save the previously loaded template (if any), then load the selected template.
            if (!string.Equals(selectedKey, loadedKey, StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseLoadedKey(loadedKey, out var loaded))
                {
                    var loadedRoot = loaded.Source == EditingSourceCollection
                        ? Path.Combine(collectionsRoot, SanitizeName(loaded.Collection))
                        : liveRoot;
                    var loadedPath = TryResolveTemplatePath(loadedRoot, loaded.Language, loaded.Category, loaded.Template, out _);
                    if (loadedPath != null)
                    {
                        if (loaded.Source == EditingSourceLive)
                        {
                            EnsureOriginalsBackupForLanguage(liveRoot, loaded.Language);
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(loadedPath) ?? loadedRoot);
                        WriteFileTextIfChanged(loadedPath, templateText);
                    }
                }

                var path = TryResolveTemplatePath(activeRoot, lang, category, template, out _);
                var content = path != null && File.Exists(path) ? File.ReadAllText(path) : string.Empty;

                dict[TemplateText] = content;
                dict[LoadedTemplateKey] = selectedKey;
                return Task.CompletedTask;
            }

            // Editing the loaded template: write to disk.
            var targetPath = TryResolveTemplatePath(activeRoot, lang, category, template, out _);
            if (targetPath == null)
            {
                return Task.CompletedTask;
            }

            if (source == EditingSourceLive)
            {
                EnsureOriginalsBackupForLanguage(liveRoot, lang);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? activeRoot);
            WriteFileTextIfChanged(targetPath, templateText);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PromptEditor UpdateModuleConfigurationAsync failed");
        }

        return Task.CompletedTask;
    }

    public override Task<FormInvokeActionResponse> InvokeAction(
        IAuthenticationContext auth,
        StaticSettingsSource settings,
        FormInvokeActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(auth.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new FormInvokeActionResponse { Text = "Admin-only." });
        }

        try
        {
            var dict = TryGetSettingsDictionary(settings) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var liveRoot = GetLivePromptsRoot();
            var dataRoot = folders.GetDataFolder(DataFolderName);
            var originalsRoot = Path.Combine(dataRoot, OriginalsFolderName);
            var collectionsRoot = Path.Combine(dataRoot, CollectionsFolderName);

            var lang = (dict.GetValueOrDefault(Language) ?? "en").Trim();
            var selectedCollection = (dict.GetValueOrDefault(CollectionName) ?? string.Empty).Trim();

            if (request.FieldName == ActionCreateCollection)
            {
                var name = (dict.GetValueOrDefault(NewCollectionName) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = "Set New Collection Name, Save, then click again." });
                }

                name = SanitizeName(name);
                var destRoot = Path.Combine(collectionsRoot, name, lang);
                var srcRoot = Path.Combine(liveRoot, lang);

                if (!Directory.Exists(srcRoot))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = $"Live language folder not found: {srcRoot}" });
                }

                if (Directory.Exists(destRoot))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = $"Collection already exists: {name} ({destRoot})" });
                }

                CopyDirectory(srcRoot, destRoot, overwrite: false);
                return Task.FromResult(new FormInvokeActionResponse { Text = $"Created collection '{name}' for '{lang}': {destRoot}" });
            }

            if (request.FieldName == ActionApplyCollection)
            {
                if (string.IsNullOrWhiteSpace(selectedCollection))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = "Select a Collection, Save, then click again." });
                }

                var collectionRoot = Path.Combine(collectionsRoot, SanitizeName(selectedCollection), lang);
                if (!Directory.Exists(collectionRoot))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = $"Collection language folder not found: {collectionRoot}" });
                }

                EnsureOriginalsBackupForLanguage(liveRoot, lang);

                var liveLangRoot = Path.Combine(liveRoot, lang);
                RestoreLanguageFromBackupOrThrow(originalsRoot, liveLangRoot, lang);
                CopyDirectory(collectionRoot, liveLangRoot, overwrite: true);

                return Task.FromResult(new FormInvokeActionResponse { Text = $"Applied collection '{selectedCollection}' to Live for '{lang}'." });
            }

            if (request.FieldName == ActionRestoreOriginal)
            {
                EnsureOriginalsBackupForLanguage(liveRoot, lang);

                var liveLangRoot = Path.Combine(liveRoot, lang);
                RestoreLanguageFromBackupOrThrow(originalsRoot, liveLangRoot, lang);
                return Task.FromResult(new FormInvokeActionResponse { Text = $"Restored Originals to Live for '{lang}'." });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PromptEditor InvokeAction failed");
            return Task.FromResult(new FormInvokeActionResponse { Text = $"Error: {ex.Message}" });
        }

        return Task.FromResult(new FormInvokeActionResponse { Text = "No action." });
    }

    private string GetLivePromptsRoot() => folders.GetResourceFolder("Prompts", "Default");

    private static string NormalizeSource(string value)
    {
        if (value.Equals(EditingSourceCollection, StringComparison.OrdinalIgnoreCase))
        {
            return EditingSourceCollection;
        }

        return EditingSourceLive;
    }

    private static IEnumerable<string> ListDirectories(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .Select(path => Path.GetFileName(path) ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ListTemplates(string categoryRoot)
    {
        try
        {
            if (!Directory.Exists(categoryRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(categoryRoot, "*.scriban", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(categoryRoot, path))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? TryResolveTemplatePath(string root, string language, string category, string template, out string? error)
    {
        error = null;
        try
        {
            var safeTemplate = template.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var baseDir = Path.GetFullPath(Path.Combine(root, language, category));
            var full = Path.GetFullPath(Path.Combine(baseDir, safeTemplate));

            var baseDirWithSep = baseDir.EndsWith(Path.DirectorySeparatorChar)
                ? baseDir
                : baseDir + Path.DirectorySeparatorChar;

            if (!full.StartsWith(baseDirWithSep, StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid template path (path traversal).";
                return null;
            }

            if (!full.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase))
            {
                error = "Template must end with .scriban";
                return null;
            }

            return full;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string BuildLoadedKey(string source, string collection, string language, string category, string template)
    {
        var c = source == EditingSourceCollection ? SanitizeName(collection) : "";
        return $"{source}|{c}|{language}|{category}|{template.Replace('\\', '/').Trim()}";
    }

    private sealed record LoadedKeyParts(string Source, string Collection, string Language, string Category, string Template);

    private static bool TryParseLoadedKey(string loadedKey, [NotNullWhen(true)] out LoadedKeyParts? parts)
    {
        parts = null;
        if (string.IsNullOrWhiteSpace(loadedKey))
        {
            return false;
        }

        var split = loadedKey.Split('|');
        if (split.Length != 5)
        {
            return false;
        }

        var source = NormalizeSource(split[0]);
        var collection = (split[1] ?? string.Empty).Trim();
        if (source == EditingSourceCollection && string.IsNullOrWhiteSpace(collection))
        {
            return false;
        }

        var language = (split[2] ?? string.Empty).Trim();
        var category = (split[3] ?? string.Empty).Trim();
        var template = (split[4] ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        parts = new LoadedKeyParts(source, collection, language, category, template);
        return true;
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

    private static void WriteFileTextIfChanged(string path, string content)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }
        catch
        {
            // Best-effort compare; fall through to write.
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }

    private void EnsureOriginalsBackupForLanguage(string liveRoot, string lang)
    {
        var dataRoot = folders.GetDataFolder(DataFolderName);
        var originalsRoot = Path.Combine(dataRoot, OriginalsFolderName);
        var backupLangRoot = Path.Combine(originalsRoot, lang);
        if (Directory.Exists(backupLangRoot))
        {
            return;
        }

        var liveLangRoot = Path.Combine(liveRoot, lang);
        if (!Directory.Exists(liveLangRoot))
        {
            return;
        }

        Directory.CreateDirectory(originalsRoot);
        CopyDirectory(liveLangRoot, backupLangRoot, overwrite: false);
    }

    private static void RestoreLanguageFromBackupOrThrow(string originalsRoot, string liveLangRoot, string lang)
    {
        var backupLangRoot = Path.Combine(originalsRoot, lang);
        if (!Directory.Exists(backupLangRoot))
        {
            throw new DirectoryNotFoundException($"Originals backup missing for '{lang}': {backupLangRoot}");
        }

        if (Directory.Exists(liveLangRoot))
        {
            Directory.Delete(liveLangRoot, recursive: true);
        }
        Directory.CreateDirectory(liveLangRoot);

        CopyDirectory(backupLangRoot, liveLangRoot, overwrite: true);
    }

    private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? destDir);
            File.Copy(file, dest, overwrite);
        }
    }

    private static Dictionary<string, string>? TryGetSettingsDictionary(ISettingsSource settings)
    {
        try
        {
            var prop = settings.GetType().GetProperty("Settings", BindingFlags.Instance | BindingFlags.NonPublic);
            return prop?.GetValue(settings) as Dictionary<string, string>;
        }
        catch
        {
            return null;
        }
    }
}
