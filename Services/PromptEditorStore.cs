using System.IO.Compression;
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

    public sealed record ExportZipResult(string FileName, byte[] ZipBytes);

    public sealed record ImportZipResult(string CollectionName, IReadOnlyList<string> Languages, int FilesImported);

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

    public async Task<ExportZipResult> ExportLanguageZipAsync(
        string source,
        string? collection,
        string language,
        CancellationToken cancellationToken)
    {
        source = NormalizeSource(source);
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidOperationException("Language is required.");
        }

        var root = ResolveRoot(source, collection);
        var languageRoot = Path.Combine(root, language);
        if (!Directory.Exists(languageRoot))
        {
            throw new DirectoryNotFoundException($"Language folder not found: {languageRoot}");
        }

        var safeCollection = source == "collection" ? SanitizeName(collection ?? string.Empty) : null;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = source == "collection"
            ? $"PromptEditor_collection_{safeCollection}_{language}_{stamp}.zip"
            : $"PromptEditor_live_{language}_{stamp}.zip";

        await using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddManifestAsync(archive, source, safeCollection, [language], cancellationToken);

            var files = Directory.GetFiles(languageRoot, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(languageRoot, file).Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(rel) || rel.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                var entryPath = $"{language}/{rel}";
                var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var fs = File.OpenRead(file);
                await fs.CopyToAsync(entryStream, cancellationToken);
            }
        }

        return new ExportZipResult(fileName, ms.ToArray());
    }

    public async Task<ImportZipResult> ImportZipToCollectionAsync(
        Stream zipStream,
        string collectionName,
        string fallbackLanguage,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        EnsureDataFolders();

        if (zipStream == null)
        {
            throw new InvalidOperationException("Missing ZIP file.");
        }

        if (!zipStream.CanRead)
        {
            throw new InvalidOperationException("ZIP stream is not readable.");
        }

        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new InvalidOperationException("Collection name is required.");
        }

        if (string.IsNullOrWhiteSpace(fallbackLanguage))
        {
            fallbackLanguage = "en";
        }

        var safeName = SanitizeName(collectionName);
        var destRoot = Path.Combine(CollectionsRoot, safeName);
        if (Directory.Exists(destRoot))
        {
            if (!overwrite)
            {
                throw new InvalidOperationException($"Collection already exists: {safeName}");
            }

            Directory.Delete(destRoot, recursive: true);
        }
        Directory.CreateDirectory(destRoot);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var fileEntries = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new ImportEntry(e, NormalizeZipPath(e.FullName)))
            .Where(x => x.PathSegments.Length > 0)
            .ToArray();

        if (fileEntries.Length == 0)
        {
            throw new InvalidOperationException("ZIP contains no files.");
        }

        var knownLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "en",
            fallbackLanguage,
        };
        foreach (var l in ListLanguages())
        {
            knownLanguages.Add(l);
        }

        var firstSegments = new HashSet<string>(fileEntries.Select(x => x.PathSegments[0]), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in fileEntries)
        {
            if (LooksLikeLanguageCode(entry.PathSegments[0]))
            {
                knownLanguages.Add(entry.PathSegments[0]);
            }

            if (entry.PathSegments.Length >= 2 && LooksLikeLanguageCode(entry.PathSegments[1]))
            {
                knownLanguages.Add(entry.PathSegments[1]);
            }
        }

        var stripPrefix = false;
        string? prefixToStrip = null;

        if (firstSegments.Count == 1)
        {
            var prefix = firstSegments.First();
            var allHaveSecondSegment = fileEntries.All(x => x.PathSegments.Length >= 2);
            var allSecondAreLanguages = allHaveSecondSegment && fileEntries.All(x => knownLanguages.Contains(x.PathSegments[1]));
            if (allSecondAreLanguages)
            {
                stripPrefix = true;
                prefixToStrip = prefix;
            }
        }

        var importedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedFiles = 0;

        foreach (var item in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segments = item.PathSegments;
            if (stripPrefix && prefixToStrip != null && segments.Length >= 2 && segments[0].Equals(prefixToStrip, StringComparison.OrdinalIgnoreCase))
            {
                segments = segments.Skip(1).ToArray();
            }

            if (segments.Length == 0)
            {
                continue;
            }

            var hasLanguagePrefix = knownLanguages.Contains(segments[0]);
            var destSegments = hasLanguagePrefix
                ? segments
                : [fallbackLanguage, .. segments];

            var relativePath = Path.Combine(destSegments);

            if (!relativePath.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase)
                && !relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destPath = Path.GetFullPath(Path.Combine(destRoot, relativePath));
            var destRootFull = Path.GetFullPath(destRoot);
            if (!destRootFull.EndsWith(Path.DirectorySeparatorChar))
            {
                destRootFull += Path.DirectorySeparatorChar;
            }
            if (!destPath.StartsWith(destRootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invalid ZIP entry path: {item.Entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? destRoot);
            await using var entryStream = item.Entry.Open();
            await using var outStream = File.Create(destPath);
            await entryStream.CopyToAsync(outStream, cancellationToken);
            importedFiles++;

            importedLanguages.Add(destSegments[0]);
        }

        if (importedFiles == 0)
        {
            throw new InvalidOperationException("No supported files found in ZIP.");
        }

        _logger.LogInformation(
            "Imported {Files} prompt files into collection '{Collection}' ({Languages})",
            importedFiles,
            safeName,
            string.Join(", ", importedLanguages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));

        return new ImportZipResult(safeName, importedLanguages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), importedFiles);
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

    private sealed record ImportEntry(ZipArchiveEntry Entry, string[] PathSegments);

    private static string[] NormalizeZipPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var cleaned = path.Replace('\\', '/');
        while (cleaned.StartsWith("./", StringComparison.Ordinal))
        {
            cleaned = cleaned[2..];
        }

        return cleaned
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x != "." && x != "..")
            .ToArray();
    }

    private static bool LooksLikeLanguageCode(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        segment = segment.Trim().Replace('_', '-');
        if (segment.Length < 2 || segment.Length > 16)
        {
            return false;
        }

        var parts = segment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        var primary = parts[0];
        if (primary.Length is < 2 or > 3)
        {
            return false;
        }
        if (!primary.All(ch => ch is >= 'a' and <= 'z'))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        var region = parts[1];
        if (region.Length is < 2 or > 8)
        {
            return false;
        }
        if (!region.All(char.IsLetterOrDigit))
        {
            return false;
        }

        return true;
    }

    private static async Task AddManifestAsync(
        ZipArchive archive,
        string source,
        string? collection,
        IReadOnlyList<string> languages,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("prompteditor-manifest.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();

        var json =
            $$"""
              {
                "format": "voxta-prompteditor",
                "version": 1,
                "createdUtc": "{{DateTime.UtcNow:O}}",
                "source": "{{source}}",
                "collection": {{(collection == null ? "null" : $"\"{collection}\"")}},
                "languages": [{{string.Join(", ", languages.Select(l => $"\"{l}\""))}}]
              }
              """;

        var bytes = Utf8NoBom.GetBytes(json);
        await stream.WriteAsync(bytes, cancellationToken);
    }
}
