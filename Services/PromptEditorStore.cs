using System.Text;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Utils;

namespace Voxta.Modules.PromptEditor.Services;

public sealed class PromptEditorStore(ICommonFolders folders, ILogger<PromptEditorStore> logger)
{
    private readonly ILogger<PromptEditorStore> _logger = logger;

    private const string DataFolderName = "PromptEditor";
    private const string OriginalsFolderName = "Originals";
    private const string CollectionsFolderName = "Collections";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string LiveRoot => folders.GetResourceFolder("Prompts", "Default");
    public string DataRoot => folders.GetDataFolder(DataFolderName);
    public string OriginalsRoot => Path.Combine(DataRoot, OriginalsFolderName);
    public string CollectionsRoot => Path.Combine(DataRoot, CollectionsFolderName);

    public void EnsureDataFolders()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CollectionsRoot);
    }

    public string NormalizeSource(string value)
    {
        return value.Equals("collection", StringComparison.OrdinalIgnoreCase) ? "collection" : "live";
    }

    public IReadOnlyList<string> ListLanguages()
    {
        var languages = ListDirectories(LiveRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return languages.Length == 0 ? ["en"] : languages;
    }

    public IReadOnlyList<string> ListCollections()
    {
        EnsureDataFolders();
        return ListDirectories(CollectionsRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListCategories(string source, string? collection, string language)
    {
        var root = ResolveRoot(source, collection);
        var languageRoot = Path.Combine(root, language);
        return ListDirectories(languageRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ListTemplates(string source, string? collection, string language, string category)
    {
        var root = ResolveRoot(source, collection);
        var categoryRoot = Path.Combine(root, language, category);
        return ListTemplateFiles(categoryRoot)
            .Select(x => x.Replace('\\', '/'))
            .ToArray();
    }

    public async Task<(bool Exists, string Content)> ReadTemplateAsync(
        string source,
        string? collection,
        string language,
        string category,
        string templatePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(source, collection);
        var full = TryResolveTemplatePath(root, language, category, templatePath, out var error);
        if (full == null)
        {
            throw new InvalidOperationException(error ?? "Invalid template path.");
        }

        if (!File.Exists(full))
        {
            return (false, string.Empty);
        }

        var content = await File.ReadAllTextAsync(full, cancellationToken);
        return (true, content);
    }

    public async Task WriteTemplateAsync(
        string source,
        string? collection,
        string language,
        string category,
        string templatePath,
        string content,
        CancellationToken cancellationToken)
    {
        EnsureDataFolders();

        source = NormalizeSource(source);
        var root = ResolveRoot(source, collection);
        var full = TryResolveTemplatePath(root, language, category, templatePath, out var error);
        if (full == null)
        {
            throw new InvalidOperationException(error ?? "Invalid template path.");
        }

        if (string.Equals(source, "live", StringComparison.OrdinalIgnoreCase))
        {
            EnsureOriginalsBackupForLanguage(language);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? root);
        await WriteFileTextIfChangedAsync(full, content, cancellationToken);
    }

    public string CreateCollectionFromLive(string name, string language)
    {
        EnsureDataFolders();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Collection name is required.");
        }

        name = SanitizeName(name);
        var srcRoot = Path.Combine(LiveRoot, language);
        if (!Directory.Exists(srcRoot))
        {
            throw new DirectoryNotFoundException($"Live language folder not found: {srcRoot}");
        }

        var destRoot = Path.Combine(CollectionsRoot, name, language);
        if (Directory.Exists(destRoot))
        {
            throw new InvalidOperationException($"Collection already exists: {name}");
        }

        CopyDirectory(srcRoot, destRoot, overwrite: false);
        _logger.LogInformation("Created PromptEditor collection '{Collection}' for '{Language}'", name, language);
        return name;
    }

    public void ApplyCollectionToLive(string name, string language)
    {
        EnsureDataFolders();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Collection name is required.");
        }

        name = SanitizeName(name);
        var collectionLangRoot = Path.Combine(CollectionsRoot, name, language);
        if (!Directory.Exists(collectionLangRoot))
        {
            throw new DirectoryNotFoundException($"Collection language folder not found: {collectionLangRoot}");
        }

        EnsureOriginalsBackupForLanguage(language);

        var liveLangRoot = Path.Combine(LiveRoot, language);
        RestoreLanguageFromBackupOrThrow(language);
        CopyDirectory(collectionLangRoot, liveLangRoot, overwrite: true);
        _logger.LogInformation("Applied PromptEditor collection '{Collection}' to Live for '{Language}'", name, language);
    }

    public void RestoreOriginalsToLive(string language)
    {
        EnsureDataFolders();

        EnsureOriginalsBackupForLanguage(language);
        RestoreLanguageFromBackupOrThrow(language);
        _logger.LogInformation("Restored PromptEditor Originals to Live for '{Language}'", language);
    }

    private string ResolveRoot(string source, string? collection)
    {
        source = NormalizeSource(source);
        if (source == "live")
        {
            return LiveRoot;
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new InvalidOperationException("Collection is required when Editing Source is 'Collection'.");
        }

        EnsureDataFolders();
        return Path.Combine(CollectionsRoot, SanitizeName(collection));
    }

    private void EnsureOriginalsBackupForLanguage(string language)
    {
        var backupLangRoot = Path.Combine(OriginalsRoot, language);
        if (Directory.Exists(backupLangRoot))
        {
            return;
        }

        var liveLangRoot = Path.Combine(LiveRoot, language);
        if (!Directory.Exists(liveLangRoot))
        {
            return;
        }

        Directory.CreateDirectory(OriginalsRoot);
        CopyDirectory(liveLangRoot, backupLangRoot, overwrite: false);
    }

    private void RestoreLanguageFromBackupOrThrow(string language)
    {
        var backupLangRoot = Path.Combine(OriginalsRoot, language);
        if (!Directory.Exists(backupLangRoot))
        {
            throw new DirectoryNotFoundException($"Originals backup missing for '{language}': {backupLangRoot}");
        }

        var liveLangRoot = Path.Combine(LiveRoot, language);
        if (Directory.Exists(liveLangRoot))
        {
            Directory.Delete(liveLangRoot, recursive: true);
        }
        Directory.CreateDirectory(liveLangRoot);

        CopyDirectory(backupLangRoot, liveLangRoot, overwrite: true);
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

    private static IEnumerable<string> ListTemplateFiles(string categoryRoot)
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
            if (string.IsNullOrWhiteSpace(language))
            {
                error = "Language is required.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                error = "Category is required.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(template))
            {
                error = "Template path is required.";
                return null;
            }

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
                error = "Template must end with .scriban.";
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

    private static string SanitizeName(string value)
    {
        var cleaned = value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(c, '_');
        }

        return cleaned.Replace(' ', '-');
    }

    private static async Task WriteFileTextIfChangedAsync(string path, string content, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(path))
            {
                var existing = await File.ReadAllTextAsync(path, cancellationToken);
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

        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);
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
}
