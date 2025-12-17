using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Voxta.Modules.PromptEditor.Services;

namespace Voxta.Modules.PromptEditor.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/extensions/prompt-editor")]
public sealed class PromptEditorApiController(PromptEditorStore store, ILogger<PromptEditorApiController> logger) : ControllerBase
{
    private readonly PromptEditorStore _store = store;
    private readonly ILogger<PromptEditorApiController> _logger = logger;

    [HttpGet("languages")]
    public ActionResult<ListResponse> GetLanguages()
    {
        return Ok(new ListResponse(_store.ListLanguages()));
    }

    [HttpGet("collections")]
    public ActionResult<ListResponse> GetCollections()
    {
        return Ok(new ListResponse(_store.ListCollections()));
    }

    [HttpGet("categories")]
    public ActionResult<ListResponse> GetCategories(
        [FromQuery] string source = "live",
        [FromQuery] string? collection = null,
        [FromQuery] string language = "en")
    {
        try
        {
            source = _store.NormalizeSource(source);
            return Ok(new ListResponse(_store.ListCategories(source, collection, language)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list categories");
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("templates")]
    public ActionResult<ListResponse> GetTemplates(
        [FromQuery] string source = "live",
        [FromQuery] string? collection = null,
        [FromQuery] string language = "en",
        [FromQuery] string category = "")
    {
        try
        {
            source = _store.NormalizeSource(source);
            return Ok(new ListResponse(_store.ListTemplates(source, collection, language, category)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list templates");
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("template")]
    public async Task<ActionResult<TemplateResponse>> GetTemplate(
        [FromQuery] string source = "live",
        [FromQuery] string? collection = null,
        [FromQuery] string language = "en",
        [FromQuery] string category = "",
        [FromQuery(Name = "path")] string templatePath = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            source = _store.NormalizeSource(source);
            var (exists, content) = await _store.ReadTemplateAsync(
                source,
                collection,
                language,
                category,
                templatePath,
                cancellationToken);
            return Ok(new TemplateResponse(exists, content));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read template");
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("template")]
    public async Task<ActionResult<ActionResponse>> SaveTemplate(
        [FromBody] SaveTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.WriteTemplateAsync(
                request.Source ?? "live",
                request.Collection,
                request.Language ?? "en",
                request.Category ?? "",
                request.TemplatePath ?? "",
                request.Content ?? "",
                cancellationToken);
            return Ok(new ActionResponse(true, "Saved."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save template");
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("collections/create")]
    public ActionResult<ActionResponse> CreateCollection([FromBody] CreateCollectionRequest request)
    {
        try
        {
            var created = _store.CreateCollectionFromLive(request.Name ?? "", request.Language ?? "en");
            return Ok(new ActionResponse(true, $"Created collection '{created}' for '{request.Language ?? "en"}'.", created));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create collection");
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("collections/apply")]
    public ActionResult<ActionResponse> ApplyCollection([FromBody] ApplyCollectionRequest request)
    {
        try
        {
            _store.ApplyCollectionToLive(request.Name ?? "", request.Language ?? "en");
            return Ok(new ActionResponse(true, $"Applied collection '{request.Name}' to Live."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply collection");
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("originals/restore")]
    public ActionResult<ActionResponse> RestoreOriginals([FromBody] RestoreOriginalsRequest request)
    {
        try
        {
            _store.RestoreOriginalsToLive(request.Language ?? "en");
            return Ok(new ActionResponse(true, $"Restored Originals to Live for '{request.Language}'."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore originals");
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportZip(
        [FromQuery] string source = "live",
        [FromQuery] string? collection = null,
        [FromQuery] string language = "en",
        CancellationToken cancellationToken = default)
    {
        try
        {
            source = _store.NormalizeSource(source);
            var zip = await _store.ExportLanguageZipAsync(source, collection, language, cancellationToken);
            return File(zip.ZipBytes, "application/zip", zip.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export ZIP");
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<ImportZipResponse>> ImportZip(
        [FromForm] ImportZipRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.File == null || request.File.Length <= 0)
            {
                return BadRequest("Missing ZIP file.");
            }

            if (request.File.Length > 50_000_000)
            {
                return BadRequest("ZIP is too large.");
            }

            var name = (request.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(request.File.FileName);
            }

            var language = (request.Language ?? "en").Trim();

            await using var stream = request.File.OpenReadStream();
            var result = await _store.ImportZipToCollectionAsync(
                stream,
                name,
                language,
                request.Overwrite,
                cancellationToken);

            return Ok(new ImportZipResponse(
                true,
                $"Imported {result.FilesImported} files into collection '{result.CollectionName}'.",
                result.CollectionName,
                result.Languages,
                result.FilesImported));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import ZIP");
            return BadRequest(ex.Message);
        }
    }

    public sealed record ListResponse(IReadOnlyList<string> Items);

    public sealed record TemplateResponse(bool Exists, string Content);

    public sealed record ActionResponse(bool Ok, string Message, string? Value = null);

    public sealed record ImportZipResponse(
        bool Ok,
        string Message,
        string CollectionName,
        IReadOnlyList<string> Languages,
        int FilesImported);

    public sealed record SaveTemplateRequest(
        string? Source,
        string? Collection,
        string? Language,
        string? Category,
        string? TemplatePath,
        string? Content);

    public sealed record CreateCollectionRequest(string? Name, string? Language);

    public sealed record ApplyCollectionRequest(string? Name, string? Language);

    public sealed record RestoreOriginalsRequest(string? Language);

    public sealed record ImportZipRequest(
        IFormFile? File,
        string? Name,
        string? Language,
        bool Overwrite);
}
