using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Voxta.Modules.PromptEditor.Services;

/// <summary>
/// Sidecar HTTP server for the Prompt Editor PWA.
/// Serves static files AND handles API endpoints directly (no dependency on Voxta API).
/// Reads/writes templates and presets directly to disk.
/// </summary>
public sealed class StaticFileServer : IDisposable
{
    private readonly ILogger<StaticFileServer> _logger;
    private readonly object _lock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    public int Port { get; private set; }
    public bool IsRunning => _listener?.IsListening == true;

    // Paths resolved at startup
    private string _voxtaRoot = "";
    private string _liveRoot = "";
    private string _dataRoot = "";
    private string _originalsRoot = "";
    private string _collectionsRoot = "";
    private string _presetsRoot = "";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".webmanifest"] = "application/manifest+json",
    };

    public StaticFileServer(ILogger<StaticFileServer> logger)
    {
        _logger = logger;
    }

    public string GetPublicRoot()
    {
        // First try the module folder relative to Voxta root (most reliable for deployed modules)
        if (!string.IsNullOrEmpty(_voxtaRoot))
        {
            var modulePublic = Path.Combine(_voxtaRoot, "Modules", "Voxta.Modules.PromptEditor", "public");
            if (Directory.Exists(modulePublic))
                return modulePublic;
        }

        // Fallback to assembly location (for dev environments)
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? Environment.CurrentDirectory;
        return Path.Combine(assemblyDir, "public");
    }

    public int EnsureRunning()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StaticFileServer));
            if (_listener?.IsListening == true) return Port;
            Start();
            return Port;
        }
    }

    private void Start()
    {
        ResolvePaths();

        var publicRoot = GetPublicRoot();
        if (!Directory.Exists(publicRoot))
        {
            Directory.CreateDirectory(publicRoot);
            _logger.LogWarning("Public folder created at {Path}", publicRoot);
        }

        Port = FindAvailablePort();
        _listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{Port}/";
        _listener.Prefixes.Add(prefix);
        _cts = new CancellationTokenSource();

        try
        {
            _listener.Start();
            _logger.LogInformation("Prompt Editor sidecar started at {Url}", prefix);
            _logger.LogInformation("  Public root: {Path}", publicRoot);
            _logger.LogInformation("  Live templates: {Path}", _liveRoot);
            _logger.LogInformation("  Data folder: {Path}", _dataRoot);
            _serverTask = Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start sidecar on port {Port}", Port);
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            throw;
        }
    }

    private void ResolvePaths()
    {
        // Find Voxta root by going up from the module's location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? Environment.CurrentDirectory;

        // Module is in: VoxtaRoot/Modules/Voxta.Modules.PromptEditor/
        // So go up 2 levels to get VoxtaRoot
        _voxtaRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", ".."));

        // If we're in a dev environment, paths might be different - check for Resources folder
        if (!Directory.Exists(Path.Combine(_voxtaRoot, "Resources")))
        {
            // Try current directory
            _voxtaRoot = Environment.CurrentDirectory;
        }

        _liveRoot = Path.Combine(_voxtaRoot, "Resources", "Prompts", "Default");
        _dataRoot = Path.Combine(_voxtaRoot, "Data", "PromptEditor");
        _originalsRoot = Path.Combine(_dataRoot, "Originals");
        _collectionsRoot = Path.Combine(_dataRoot, "Collections");
        _presetsRoot = Path.Combine(_dataRoot, "Presets");

        // Ensure data folders exist
        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(_collectionsRoot);
        Directory.CreateDirectory(_presetsRoot);
    }

    private static int FindAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Listener error");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.LocalPath ?? "/";
            var method = request.HttpMethod;

            _logger.LogDebug("Request: {Method} {Path}", method, path);

            // CORS headers
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

            if (method == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // Route to API or static files
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Equals("/api", StringComparison.OrdinalIgnoreCase))
            {
                var apiPath = path.Length > 4 ? path[4..] : "/";
                await HandleApiRequest(apiPath, method, request, response);
            }
            else
            {
                await HandleStaticFile(path, response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Request error: {Path}", request.Url?.LocalPath);
            try
            {
                await WriteJsonResponse(response, 500, new { error = ex.Message });
            }
            catch
            {
                // Response may already be closed
            }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    #region API Routing

    private async Task HandleApiRequest(string path, string method, HttpListenerRequest request, HttpListenerResponse response)
    {
        // Parse query string
        var query = request.QueryString;

        // Normalize path (remove trailing slash if present)
        path = path.TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        _logger.LogDebug("API request: {Method} {Path}", method, path);

        try
        {
            // Templates API
            if (path == "/languages" && method == "GET")
            {
                var items = ListLanguages();
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path == "/collections" && method == "GET")
            {
                var items = ListCollections();
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path == "/categories" && method == "GET")
            {
                var source = query["source"] ?? "live";
                var collection = query["collection"];
                var language = query["language"] ?? "en";
                var items = ListCategories(source, collection, language);
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path == "/templates" && method == "GET")
            {
                var source = query["source"] ?? "live";
                var collection = query["collection"];
                var language = query["language"] ?? "en";
                var category = query["category"] ?? "";
                var items = ListTemplates(source, collection, language, category);
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path == "/template" && method == "GET")
            {
                var source = query["source"] ?? "live";
                var collection = query["collection"];
                var language = query["language"] ?? "en";
                var category = query["category"] ?? "";
                var templatePath = query["path"] ?? "";
                var (exists, content) = await ReadTemplateAsync(source, collection, language, category, templatePath);
                await WriteJsonResponse(response, 200, new { exists, content });
            }
            else if (path == "/template" && method == "PUT")
            {
                var body = await ReadJsonBody<SaveTemplateRequest>(request);
                await WriteTemplateAsync(body);
                await WriteJsonResponse(response, 200, new { ok = true, message = "Saved." });
            }
            else if (path == "/collections/create" && method == "POST")
            {
                var body = await ReadJsonBody<CreateCollectionRequest>(request);
                var name = CreateCollectionFromLive(body.Name ?? "", body.Language ?? "en");
                await WriteJsonResponse(response, 200, new { ok = true, message = $"Created '{name}'.", value = name });
            }
            else if (path == "/collections/apply" && method == "POST")
            {
                var body = await ReadJsonBody<ApplyCollectionRequest>(request);
                ApplyCollectionToLive(body.Name ?? "", body.Language ?? "en");
                await WriteJsonResponse(response, 200, new { ok = true, message = "Applied." });
            }
            else if (path == "/originals/restore" && method == "POST")
            {
                var body = await ReadJsonBody<RestoreOriginalsRequest>(request);
                RestoreOriginalsToLive(body.Language ?? "en");
                await WriteJsonResponse(response, 200, new { ok = true, message = "Restored." });
            }
            else if (path.StartsWith("/collections/") && method == "DELETE")
            {
                var name = Uri.UnescapeDataString(path[13..]);
                DeleteCollection(name);
                await WriteJsonResponse(response, 200, new { ok = true, message = "Collection deleted." });
            }
            // Presets API
            else if (path == "/presets" && method == "GET")
            {
                var items = ListPresets();
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path.StartsWith("/presets/") && method == "GET" && !path.Contains("/convert"))
            {
                var name = Uri.UnescapeDataString(path[9..]);
                var preset = await ReadPresetAsync(name);
                if (preset == null)
                {
                    await WriteJsonResponse(response, 404, new { error = "Preset not found" });
                }
                else
                {
                    await WriteJsonResponse(response, 200, preset);
                }
            }
            else if (path == "/presets" && method == "POST")
            {
                var body = await ReadJsonBody<CreatePresetRequest>(request);
                await WritePresetAsync(body.Name ?? "unnamed", body.Data ?? new SillyTavernPreset());
                await WriteJsonResponse(response, 200, new { ok = true, message = "Created." });
            }
            else if (path.StartsWith("/presets/") && path.EndsWith("/convert") && method == "POST")
            {
                var name = Uri.UnescapeDataString(path[9..^8]);
                var body = await ReadJsonBody<ConvertPresetRequest>(request);
                var files = await ConvertPresetToScriban(name, body.Language ?? "en", body.TargetLive);
                var target = body.TargetLive ? "Live templates" : "Collection";
                await WriteJsonResponse(response, 200, new { ok = true, message = $"Converted {files.Count} files to {target}.", files });
            }
            else if (path.StartsWith("/presets/") && method == "PUT")
            {
                var name = Uri.UnescapeDataString(path[9..]);
                var preset = await ReadJsonBody<SillyTavernPreset>(request);
                await WritePresetAsync(name, preset);
                await WriteJsonResponse(response, 200, new { ok = true, message = "Saved." });
            }
            else if (path.StartsWith("/presets/") && method == "DELETE")
            {
                var name = Uri.UnescapeDataString(path[9..]);
                DeletePreset(name);
                await WriteJsonResponse(response, 200, new { ok = true, message = "Deleted." });
            }
            // Converted presets (directories in Live/TextGen/Includes/Presets/)
            else if (path == "/converted-presets" && method == "GET")
            {
                var items = ListConvertedPresets();
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path.StartsWith("/converted-presets/") && path.EndsWith("/config") && method == "PUT")
            {
                var name = Uri.UnescapeDataString(path[19..^7]);
                var config = await ReadJsonBody<ConvertedPresetConfig>(request);
                await SaveConvertedPresetConfig(name, config);
                await WriteJsonResponse(response, 200, new { ok = true, message = "Config saved and Main.scriban regenerated." });
            }
            else if (path.StartsWith("/converted-presets/") && path.Contains("/prompt/") && method == "PUT")
            {
                var parts = path[19..].Split("/prompt/");
                var presetName = Uri.UnescapeDataString(parts[0]);
                var promptName = Uri.UnescapeDataString(parts[1]);
                var body = await ReadJsonBody<PromptUpdateRequest>(request);
                await UpdateConvertedPrompt(presetName, promptName, body.Content ?? "");
                await WriteJsonResponse(response, 200, new { ok = true, message = "Prompt saved." });
            }
            else if (path.StartsWith("/converted-presets/") && path.EndsWith("/auto-insert") && method == "POST")
            {
                var name = Uri.UnescapeDataString(path[19..^12]);
                var body = await ReadJsonBody<AutoInsertRequest>(request);
                var result = await AutoInsertIncludeIntoTemplate(name, body.Language ?? "en", body.Template ?? "TextGen/ChatInstructUserMessage.scriban");
                await WriteJsonResponse(response, 200, result);
            }
            else if (path.StartsWith("/converted-presets/") && path.EndsWith("/remove-include") && method == "POST")
            {
                var name = Uri.UnescapeDataString(path[19..^15]);
                var body = await ReadJsonBody<AutoInsertRequest>(request);
                var result = await RemoveIncludeFromTemplate(name, body.Language ?? "en", body.Template ?? "TextGen/ChatInstructUserMessage.scriban");
                await WriteJsonResponse(response, 200, result);
            }
            else if (path == "/injection-templates" && method == "GET")
            {
                var language = query["language"] ?? "en";
                var items = ListInjectionTemplates(language);
                await WriteJsonResponse(response, 200, new { items });
            }
            else if (path.StartsWith("/converted-presets/") && method == "GET")
            {
                var name = Uri.UnescapeDataString(path[19..]);
                var preset = await GetConvertedPresetDetails(name);
                await WriteJsonResponse(response, 200, preset);
            }
            else
            {
                _logger.LogWarning("Unknown API endpoint: {Method} {Path}", method, path);
                await WriteJsonResponse(response, 404, new { error = $"Unknown API endpoint: {method} {path}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API error: {Method} {Path}", method, path);
            await WriteJsonResponse(response, 400, new { error = ex.Message });
        }
    }

    #endregion

    #region Template Operations

    private string[] ListLanguages()
    {
        if (!Directory.Exists(_liveRoot)) return ["en"];
        var dirs = Directory.GetDirectories(_liveRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return dirs.Length == 0 ? ["en"] : dirs;
    }

    private string[] ListCollections()
    {
        if (!Directory.Exists(_collectionsRoot)) return [];
        return Directory.GetDirectories(_collectionsRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] ListCategories(string source, string? collection, string language)
    {
        var root = ResolveRoot(source, collection);
        var langRoot = Path.Combine(root, language);
        if (!Directory.Exists(langRoot)) return [];
        return Directory.GetDirectories(langRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] ListTemplates(string source, string? collection, string language, string category)
    {
        var root = ResolveRoot(source, collection);
        var catRoot = Path.Combine(root, language, category);
        if (!Directory.Exists(catRoot)) return [];
        return Directory.GetFiles(catRoot, "*.scriban", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(catRoot, p).Replace('\\', '/'))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<(bool Exists, string Content)> ReadTemplateAsync(string source, string? collection, string language, string category, string templatePath)
    {
        var root = ResolveRoot(source, collection);
        var fullPath = ResolveSafeTemplatePath(root, language, category, templatePath);
        if (!File.Exists(fullPath)) return (false, "");
        var content = await File.ReadAllTextAsync(fullPath);
        return (true, content);
    }

    private async Task WriteTemplateAsync(SaveTemplateRequest req)
    {
        var source = req.Source ?? "live";
        var root = ResolveRoot(source, req.Collection);
        var fullPath = ResolveSafeTemplatePath(root, req.Language ?? "en", req.Category ?? "", req.TemplatePath ?? "");

        if (source.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            EnsureOriginalsBackup(req.Language ?? "en");
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, req.Content ?? "", Utf8NoBom);
    }

    private string CreateCollectionFromLive(string name, string language)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name required");
        name = SanitizeName(name);
        var srcRoot = Path.Combine(_liveRoot, language);
        if (!Directory.Exists(srcRoot)) throw new DirectoryNotFoundException($"Language folder not found: {language}");
        var destRoot = Path.Combine(_collectionsRoot, name, language);
        if (Directory.Exists(destRoot)) throw new InvalidOperationException($"Collection exists: {name}");
        CopyDirectory(srcRoot, destRoot);
        return name;
    }

    private void ApplyCollectionToLive(string name, string language)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name required");
        name = SanitizeName(name);
        var collectionLangRoot = Path.Combine(_collectionsRoot, name, language);
        if (!Directory.Exists(collectionLangRoot)) throw new DirectoryNotFoundException($"Collection language folder not found");
        EnsureOriginalsBackup(language);
        var liveLangRoot = Path.Combine(_liveRoot, language);
        RestoreFromBackup(language);
        CopyDirectory(collectionLangRoot, liveLangRoot, overwrite: true);
    }

    private void RestoreOriginalsToLive(string language)
    {
        EnsureOriginalsBackup(language);
        RestoreFromBackup(language);
    }

    private void DeleteCollection(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name required");
        name = SanitizeName(name);
        var collectionPath = Path.Combine(_collectionsRoot, name);
        if (Directory.Exists(collectionPath))
        {
            Directory.Delete(collectionPath, true);
            _logger.LogInformation("Deleted collection: {Name}", name);
        }
    }

    private string ResolveRoot(string source, string? collection)
    {
        if (source.Equals("collection", StringComparison.OrdinalIgnoreCase))
        {
            // If no collection specified, fall back to live
            if (string.IsNullOrWhiteSpace(collection)) return _liveRoot;
            return Path.Combine(_collectionsRoot, SanitizeName(collection));
        }
        return _liveRoot;
    }

    private string ResolveSafeTemplatePath(string root, string language, string category, string template)
    {
        if (string.IsNullOrWhiteSpace(language)) throw new InvalidOperationException("Language required");
        if (string.IsNullOrWhiteSpace(category)) throw new InvalidOperationException("Category required");
        if (string.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("Template path required");

        var safeTemplate = template.Replace('/', Path.DirectorySeparatorChar);
        var baseDir = Path.GetFullPath(Path.Combine(root, language, category));
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, safeTemplate));

        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected");
        if (!fullPath.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Template must be .scriban");

        return fullPath;
    }

    private void EnsureOriginalsBackup(string language)
    {
        var backupLangRoot = Path.Combine(_originalsRoot, language);
        if (Directory.Exists(backupLangRoot)) return;
        var liveLangRoot = Path.Combine(_liveRoot, language);
        if (!Directory.Exists(liveLangRoot)) return;
        Directory.CreateDirectory(_originalsRoot);
        CopyDirectory(liveLangRoot, backupLangRoot);
    }

    private void RestoreFromBackup(string language)
    {
        var backupLangRoot = Path.Combine(_originalsRoot, language);
        if (!Directory.Exists(backupLangRoot)) throw new DirectoryNotFoundException($"Backup missing for {language}");
        var liveLangRoot = Path.Combine(_liveRoot, language);
        if (Directory.Exists(liveLangRoot)) Directory.Delete(liveLangRoot, true);
        Directory.CreateDirectory(liveLangRoot);
        CopyDirectory(backupLangRoot, liveLangRoot);
    }

    #endregion

    #region Preset Operations

    private string[] ListPresets()
    {
        if (!Directory.Exists(_presetsRoot)) return [];
        return Directory.GetFiles(_presetsRoot, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SillyTavernPreset?> ReadPresetAsync(string name)
    {
        var path = Path.Combine(_presetsRoot, SanitizeName(name) + ".json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<SillyTavernPreset>(json, JsonOptions);
    }

    private async Task WritePresetAsync(string name, SillyTavernPreset preset)
    {
        Directory.CreateDirectory(_presetsRoot);
        var path = Path.Combine(_presetsRoot, SanitizeName(name) + ".json");
        var json = JsonSerializer.Serialize(preset, JsonOptions);
        await File.WriteAllTextAsync(path, json, Utf8NoBom);
    }

    private void DeletePreset(string name)
    {
        var path = Path.Combine(_presetsRoot, SanitizeName(name) + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    private List<string> ListConvertedPresets()
    {
        // List directories in Live/en/TextGen/Includes/Presets/
        var presetsDir = Path.Combine(_liveRoot, "en", "TextGen", "Includes", "Presets");
        if (!Directory.Exists(presetsDir)) return [];

        return Directory.GetDirectories(presetsDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n)
            .ToList()!;
    }

    private async Task<object> GetConvertedPresetDetails(string presetName)
    {
        var presetsDir = Path.Combine(_liveRoot, "en", "TextGen", "Includes", "Presets", SanitizeName(presetName));
        if (!Directory.Exists(presetsDir))
            throw new DirectoryNotFoundException($"Preset not found: {presetName}");

        // Read config if exists
        var configPath = Path.Combine(presetsDir, "_config.json");
        var config = new ConvertedPresetConfig();
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            config = JsonSerializer.Deserialize<ConvertedPresetConfig>(json, JsonOptions) ?? new();
        }

        // List all .scriban files except Main.scriban
        var prompts = new List<object>();
        foreach (var file in Directory.GetFiles(presetsDir, "*.scriban").OrderBy(f => f))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.Equals("Main", StringComparison.OrdinalIgnoreCase)) continue;

            var content = await File.ReadAllTextAsync(file);
            var enabled = config.EnabledPrompts?.Contains(fileName, StringComparer.OrdinalIgnoreCase) ?? true;
            var order = config.PromptOrder?.IndexOf(fileName) ?? -1;

            prompts.Add(new
            {
                name = fileName,
                content,
                enabled,
                order = order >= 0 ? order : 999,
                role = "system" // Default role for converted prompts
            });
        }

        // Sort by order then name
        prompts = prompts.OrderBy(p => ((dynamic)p).order).ThenBy(p => ((dynamic)p).name).ToList();

        return new
        {
            name = presetName,
            prompts,
            totalCount = prompts.Count,
            enabledCount = prompts.Count(p => ((dynamic)p).enabled)
        };
    }

    private async Task SaveConvertedPresetConfig(string presetName, ConvertedPresetConfig config)
    {
        var presetsDir = Path.Combine(_liveRoot, "en", "TextGen", "Includes", "Presets", SanitizeName(presetName));
        if (!Directory.Exists(presetsDir))
            throw new DirectoryNotFoundException($"Preset not found: {presetName}");

        // Save config
        var configPath = Path.Combine(presetsDir, "_config.json");
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, Utf8NoBom);

        // Regenerate Main.scriban with only enabled prompts
        await RegenerateMainScriban(presetName, config);

        _logger.LogInformation("Saved config for preset '{Preset}' with {Enabled}/{Total} prompts enabled",
            presetName, config.EnabledPrompts?.Count ?? 0, config.PromptOrder?.Count ?? 0);
    }

    private async Task RegenerateMainScriban(string presetName, ConvertedPresetConfig config)
    {
        var safeName = SanitizeName(presetName);
        var presetsDir = Path.Combine(_liveRoot, "en", "TextGen", "Includes", "Presets", safeName);
        var mainPath = Path.Combine(presetsDir, "Main.scriban");

        var sb = new StringBuilder();
        sb.AppendLine($"{{{{~ # Main include for: {presetName} ~}}}}");
        sb.AppendLine($"{{{{~ # Auto-generated - do not edit directly ~}}}}");
        sb.AppendLine();

        var enabledSet = new HashSet<string>(config.EnabledPrompts ?? [], StringComparer.OrdinalIgnoreCase);
        var orderedPrompts = config.PromptOrder ?? [];

        foreach (var promptName in orderedPrompts)
        {
            if (!enabledSet.Contains(promptName)) continue;

            var includeName = $"Presets/{safeName}/{promptName}";
            sb.AppendLine($"{{{{~ # {promptName} ~}}}}");
            sb.AppendLine($"{{{{ include '{includeName}' }}}}");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(mainPath, sb.ToString(), Utf8NoBom);
    }

    private async Task UpdateConvertedPrompt(string presetName, string promptName, string content)
    {
        var presetsDir = Path.Combine(_liveRoot, "en", "TextGen", "Includes", "Presets", SanitizeName(presetName));
        if (!Directory.Exists(presetsDir))
            throw new DirectoryNotFoundException($"Preset not found: {presetName}");

        var filePath = Path.Combine(presetsDir, SanitizeName(promptName) + ".scriban");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Prompt not found: {promptName}");

        await File.WriteAllTextAsync(filePath, content, Utf8NoBom);
        _logger.LogInformation("Updated prompt '{Prompt}' in preset '{Preset}'", promptName, presetName);
    }

    private string[] ListInjectionTemplates(string language)
    {
        // List templates where presets can be injected
        // Main candidates are ChatInstructUserMessage.scriban and ChatInstructSystemMessage.scriban
        var textGenDir = Path.Combine(_liveRoot, language, "TextGen");
        if (!Directory.Exists(textGenDir)) return [];

        var templates = new List<string>();
        foreach (var file in Directory.GetFiles(textGenDir, "*.scriban", SearchOption.TopDirectoryOnly))
        {
            var name = "TextGen/" + Path.GetFileName(file);
            templates.Add(name);
        }
        return templates.OrderBy(t => t).ToArray();
    }

    private async Task<object> AutoInsertIncludeIntoTemplate(string presetName, string language, string templatePath)
    {
        var safeName = SanitizeName(presetName);
        var includeStatement = $"{{{{ include 'Presets/{safeName}/Main' }}}}";

        // Resolve template path
        var fullPath = Path.Combine(_liveRoot, language, templatePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Template not found: {templatePath}");

        // Backup originals
        EnsureOriginalsBackup(language);

        // Read current content
        var content = await File.ReadAllTextAsync(fullPath);

        // Check if already included
        if (content.Contains($"Presets/{safeName}/Main", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = true, message = "Include already exists in template.", alreadyExists = true };
        }

        // Find a good injection point
        // Strategy: Insert after the initial comments/includes but before the main content
        // Look for existing include statements and add after the last one,
        // or add after the opening {{ if/else block
        string newContent;

        // Try to find a good insertion point
        var lines = content.Split('\n');
        var insertIndex = -1;

        // Find the line with "{{ include 'Intro'" or similar system intro include
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Look for existing include statements or intro patterns
            if (line.Contains("{{ include 'Intro'", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("{{ system_intro }}", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("{{ system_prompt }}", StringComparison.OrdinalIgnoreCase))
            {
                insertIndex = i + 1;
            }
            // Also check for ReplyHeader/ReplyInstructions as a fallback insertion point
            if (line.Contains("include 'ReplyHeader'", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("include 'ReplyInstructions'", StringComparison.OrdinalIgnoreCase))
            {
                // Insert BEFORE reply instructions
                if (insertIndex < 0) insertIndex = i;
                break;
            }
        }

        if (insertIndex < 0)
        {
            // Fallback: add at the beginning after any opening comments
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("{{~") && !line.StartsWith("{{#") && !string.IsNullOrEmpty(line))
                {
                    insertIndex = i;
                    break;
                }
            }
        }

        if (insertIndex < 0) insertIndex = 0;

        // Build the new content with proper formatting
        var presetComment = $"{{{{~ # Injected preset: {presetName} ~}}}}";
        var linesList = lines.ToList();
        linesList.Insert(insertIndex, "");
        linesList.Insert(insertIndex + 1, presetComment);
        linesList.Insert(insertIndex + 2, includeStatement);
        linesList.Insert(insertIndex + 3, "");

        newContent = string.Join("\n", linesList);

        // Write back
        await File.WriteAllTextAsync(fullPath, newContent, Utf8NoBom);
        _logger.LogInformation("Auto-inserted preset '{Preset}' into template '{Template}'", presetName, templatePath);

        return new { ok = true, message = $"Inserted include for '{presetName}' into {templatePath}", insertedAt = insertIndex };
    }

    private async Task<object> RemoveIncludeFromTemplate(string presetName, string language, string templatePath)
    {
        var safeName = SanitizeName(presetName);

        // Resolve template path
        var fullPath = Path.Combine(_liveRoot, language, templatePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Template not found: {templatePath}");

        // Read current content
        var content = await File.ReadAllTextAsync(fullPath);

        // Check if included
        if (!content.Contains($"Presets/{safeName}/Main", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = true, message = "Include not found in template.", notFound = true };
        }

        // Remove the include and its comment
        var lines = content.Split('\n').ToList();
        var indicesToRemove = new List<int>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Remove the preset comment
            if (line.Contains($"# Injected preset: {presetName}", StringComparison.OrdinalIgnoreCase))
            {
                indicesToRemove.Add(i);
            }
            // Remove the include statement
            if (line.Contains($"Presets/{safeName}/Main", StringComparison.OrdinalIgnoreCase))
            {
                indicesToRemove.Add(i);
                // Also remove empty line before if exists
                if (i > 0 && string.IsNullOrWhiteSpace(lines[i - 1]) && !indicesToRemove.Contains(i - 1))
                {
                    indicesToRemove.Add(i - 1);
                }
                // Also remove empty line after if exists
                if (i < lines.Count - 1 && string.IsNullOrWhiteSpace(lines[i + 1]) && !indicesToRemove.Contains(i + 1))
                {
                    indicesToRemove.Add(i + 1);
                }
            }
        }

        // Remove lines in reverse order to preserve indices
        foreach (var idx in indicesToRemove.OrderByDescending(i => i))
        {
            if (idx >= 0 && idx < lines.Count)
                lines.RemoveAt(idx);
        }

        var newContent = string.Join("\n", lines);

        // Write back
        await File.WriteAllTextAsync(fullPath, newContent, Utf8NoBom);
        _logger.LogInformation("Removed preset '{Preset}' include from template '{Template}'", presetName, templatePath);

        return new { ok = true, message = $"Removed include for '{presetName}' from {templatePath}" };
    }

    private async Task<List<string>> ConvertPresetToScriban(string presetName, string language, bool targetLive)
    {
        var preset = await ReadPresetAsync(presetName)
            ?? throw new InvalidOperationException($"Preset not found: {presetName}");

        var safeName = SanitizeName(presetName);
        var basePath = $"Presets/{safeName}";
        var category = "TextGen/Includes";

        // Get enabled prompts in order
        var globalOrder = preset.PromptOrder?.FirstOrDefault(po => po.CharacterId == 100001);
        var orderMap = (globalOrder?.Order ?? [])
            .Where(o => !string.IsNullOrEmpty(o.Identifier))
            .Select((o, i) => new { Identifier = o.Identifier!, o.Enabled, Index = i })
            .ToDictionary(x => x.Identifier);

        var orderedPrompts = (preset.Prompts ?? [])
            .Where(p => !p.Marker && !string.IsNullOrEmpty(p.Identifier) && orderMap.TryGetValue(p.Identifier, out var o) && o.Enabled)
            .OrderBy(p => orderMap.TryGetValue(p.Identifier!, out var o) ? o.Index : 999)
            .ToList();

        var files = new List<string>();

        // Target Live folder directly or Collections folder
        var outputRoot = targetLive ? _liveRoot : Path.Combine(_collectionsRoot, $"preset-{safeName}");

        foreach (var prompt in orderedPrompts)
        {
            var templatePath = $"{basePath}/{SanitizeName(prompt.Name ?? "unnamed")}.scriban";
            var content = ConvertPromptContent(prompt.Content ?? "", prompt.Name ?? "", presetName);

            var fullPath = Path.Combine(outputRoot, language, category, templatePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(fullPath, content, Utf8NoBom);
            files.Add(templatePath);
        }

        // Generate main include file
        var mainContent = GenerateMainInclude(orderedPrompts, basePath, presetName);
        var mainPath = $"{basePath}/Main.scriban";
        var mainFullPath = Path.Combine(outputRoot, language, category, mainPath);
        var mainDir = Path.GetDirectoryName(mainFullPath);
        if (!string.IsNullOrEmpty(mainDir)) Directory.CreateDirectory(mainDir);
        await File.WriteAllTextAsync(mainFullPath, mainContent, Utf8NoBom);
        files.Add(mainPath);

        // Create _config.json with all prompts enabled by default
        var promptNames = orderedPrompts
            .Select(p => SanitizeName(p.Name ?? "unnamed"))
            .Where(n => !n.Equals("Main", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var config = new ConvertedPresetConfig
        {
            EnabledPrompts = promptNames,
            PromptOrder = promptNames
        };
        var configPath = Path.Combine(outputRoot, language, category, basePath, "_config.json");
        var configJson = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, configJson, Utf8NoBom);

        var targetDesc = targetLive ? "Live templates" : $"Collection 'preset-{safeName}'";
        _logger.LogInformation("Converted preset '{Preset}' to {Count} files in {Target}", presetName, files.Count, targetDesc);
        return files;
    }

    private static string ConvertPromptContent(string content, string promptName, string presetName)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";

        var c = content;

        // Remove SillyTavern comments {{// ... }} - use .*? non-greedy to handle multi-line comments
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{//.*?\}\}", "", System.Text.RegularExpressions.RegexOptions.Singleline);

        // Remove {{trim}} markers
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{trim\}\}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Extract content from {{setvar::name::value}} - keep ONLY the value part (the actual content)
        // Use non-greedy match with lookahead to properly capture content up to the closing }}
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{setvar::[^:]+::(.*?)\}\}", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        // Replace {{getvar::name}} with Scriban variable placeholder (use ?? for null coalescing)
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{getvar::([^}]+)\}\}", "{{ $1 ?? \"\" }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Character/user variables - map to Voxta Scriban variables
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{char\}\}|\{\{charname\}\}", "{{ char }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{user\}\}|\{\{username\}\}", "{{ user }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{group\}\}", "{{ other_chars | array.join \", \" }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{scenario\}\}", "{{ scenario }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{personality\}\}", "{{ char_personality | join_newlines }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{description\}\}", "{{ char_description | join_newlines }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{persona\}\}", "{{ user_description }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{summary\}\}", "{{ summary }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{mesExamples\}\}|\{\{message_examples\}\}", "{{ char_message_examples | join_newlines }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // SillyTavern special tokens - map to Voxta equivalents
        c = System.Text.RegularExpressions.Regex.Replace(c, @"<BOT>|<bot>", "{{ char }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"<USER>|<user>", "{{ user }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Time variables - map to Voxta's now variable
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{time\}\}", "{{ now }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{date\}\}", "{{ now }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{weekday\}\}", "{{ now }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{isotime\}\}", "{{ now }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{isodate\}\}", "{{ now }}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove other unsupported macros but leave a comment
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{roll:[^}]*\}\}", "{{~ # dice roll macro not supported ~}}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{random::[^}]*\}\}", "{{~ # random selection macro not supported ~}}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{newline\}\}", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean up remaining unsupported ST macros (anything in double braces that looks like a macro)
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{[a-z_]+\}\}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Clean up empty braces and excessive whitespace
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\{\{\s*\}\}", "");
        c = System.Text.RegularExpressions.Regex.Replace(c, @"\n{3,}", "\n\n");

        var result = c.Trim();
        if (string.IsNullOrWhiteSpace(result)) return "";

        return $"{{{{~ # From: {presetName} / {promptName} ~}}}}\n{result}";
    }

    private static string GenerateMainInclude(List<PromptEntry> prompts, string basePath, string presetName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{{{{~ # Main include for: {presetName} ~}}}}");
        sb.AppendLine();

        var includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prompt in prompts)
        {
            var safeName = SanitizeName(prompt.Name ?? "unnamed");

            // Skip "Main" to avoid infinite recursion (Main.scriban including itself)
            if (string.Equals(safeName, "Main", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{{{{~ # [{prompt.Role}] {prompt.Name} - SKIPPED (would cause recursion) ~}}}}");
                sb.AppendLine();
                continue;
            }

            // Skip duplicates (e.g., opening/closing XML-style tags that resolve to same filename)
            if (!includedFiles.Add(safeName))
            {
                sb.AppendLine($"{{{{~ # [{prompt.Role}] {prompt.Name} - SKIPPED (duplicate of {safeName}) ~}}}}");
                sb.AppendLine();
                continue;
            }

            var includeName = $"{basePath}/{safeName}";
            sb.AppendLine($"{{{{~ # [{prompt.Role}] {prompt.Name} ~}}}}");
            sb.AppendLine($"{{{{ include '{includeName}' }}}}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Static File Serving

    private async Task HandleStaticFile(string path, HttpListenerResponse response)
    {
        var publicRoot = GetPublicRoot();
        if (path == "/") path = "/index.html";

        var relativePath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(publicRoot, relativePath));

        if (!fullPath.StartsWith(publicRoot, StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 403;
            return;
        }

        if (!File.Exists(fullPath))
        {
            // SPA fallback
            fullPath = Path.Combine(publicRoot, "index.html");
            if (!File.Exists(fullPath))
            {
                response.StatusCode = 404;
                var msg = Encoding.UTF8.GetBytes("404 Not Found");
                response.ContentType = "text/plain";
                response.ContentLength64 = msg.Length;
                await response.OutputStream.WriteAsync(msg);
                return;
            }
        }

        var ext = Path.GetExtension(fullPath);
        response.ContentType = MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
        response.Headers["Cache-Control"] = ext is ".html" ? "no-cache" : "public, max-age=3600";

        var content = await File.ReadAllBytesAsync(fullPath);
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content);
    }

    #endregion

    #region Helpers

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unnamed";

        // Remove leading/trailing whitespace
        var cleaned = value.Trim();

        // Remove ALL non-ASCII characters first (Unicode decorative chars, box drawing, symbols, etc.)
        // This handles: ┌┐└┘├┤┬┴┼│─|✎✏✐✑☞☛➊➋➌➍➎➀➁➂➃➄➅⌈⌉⌊⌋⌜⌝⌞⌟∑✱⚲━‒✉✓✗♠♣♥♦★☆→←↑↓⇒⇐⇑⇓┎┖┒┚「」『』【】etc.
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^\x00-\x7F]", "");

        // Keep only ASCII alphanumeric, spaces, and basic punctuation
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^a-zA-Z0-9\s\-_]", "");

        // Replace multiple spaces with single space first
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");

        // Trim spaces
        cleaned = cleaned.Trim();

        // Replace spaces with hyphens
        cleaned = cleaned.Replace(' ', '-');

        // Remove consecutive hyphens
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"-+", "-");

        // Remove leading/trailing hyphens
        cleaned = cleaned.Trim('-');

        // Ensure we have something
        if (string.IsNullOrWhiteSpace(cleaned)) return "unnamed";

        return cleaned;
    }

    private static void CopyDirectory(string src, string dest, bool overwrite = false)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var destPath = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? dest);
            File.Copy(file, destPath, overwrite);
        }
    }

    private static async Task<T> ReadJsonBody<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid JSON body");
    }

    private static async Task WriteJsonResponse(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    #endregion

    #region Request/Response DTOs

    private sealed record SaveTemplateRequest(
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("collection")] string? Collection,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("templatePath")] string? TemplatePath,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record CreateCollectionRequest(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("language")] string? Language);

    private sealed record ApplyCollectionRequest(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("language")] string? Language);

    private sealed record RestoreOriginalsRequest(
        [property: JsonPropertyName("language")] string? Language);

    private sealed record CreatePresetRequest(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("data")] SillyTavernPreset? Data);

    private sealed record ConvertPresetRequest(
        [property: JsonPropertyName("collection")] string? Collection,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("targetLive")] bool TargetLive = false);

    private sealed record PromptUpdateRequest(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record AutoInsertRequest(
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("template")] string? Template);

    private sealed class ConvertedPresetConfig
    {
        [JsonPropertyName("enabledPrompts")] public List<string>? EnabledPrompts { get; set; }
        [JsonPropertyName("promptOrder")] public List<string>? PromptOrder { get; set; }
    }

    #endregion

    #region Preset Models (embedded)

    private sealed class SillyTavernPreset
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; } = 1.0;
        [JsonPropertyName("frequency_penalty")] public double FrequencyPenalty { get; set; }
        [JsonPropertyName("presence_penalty")] public double PresencePenalty { get; set; }
        [JsonPropertyName("top_p")] public double TopP { get; set; } = 1.0;
        [JsonPropertyName("top_k")] public int TopK { get; set; }
        [JsonPropertyName("min_p")] public double MinP { get; set; }
        [JsonPropertyName("repetition_penalty")] public double RepetitionPenalty { get; set; } = 1.0;
        [JsonPropertyName("openai_max_tokens")] public int OpenAiMaxTokens { get; set; } = 2048;
        [JsonPropertyName("prompts")] public List<PromptEntry>? Prompts { get; set; }
        [JsonPropertyName("prompt_order")] public List<PromptOrderGroup>? PromptOrder { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; set; }
    }

    private sealed class PromptEntry
    {
        [JsonPropertyName("identifier")] public string? Identifier { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("system_prompt")] public bool SystemPrompt { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("marker")] public bool Marker { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; } = "system";
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("injection_position")] public int InjectionPosition { get; set; }
        [JsonPropertyName("injection_depth")] public int InjectionDepth { get; set; }
    }

    private sealed class PromptOrderEntry
    {
        [JsonPropertyName("identifier")] public string? Identifier { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    private sealed class PromptOrderGroup
    {
        [JsonPropertyName("character_id")] public int CharacterId { get; set; }
        [JsonPropertyName("order")] public List<PromptOrderEntry>? Order { get; set; }
    }

    #endregion

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            try { _serverTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts?.Dispose();
        }
    }
}
