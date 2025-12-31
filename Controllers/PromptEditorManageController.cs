using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Voxta.Modules.PromptEditor.Services;

namespace Voxta.Modules.PromptEditor.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("manage/prompt-editor")]
public sealed class PromptEditorManageController(StaticFileServer fileServer) : Controller
{
    private readonly StaticFileServer _fileServer = fileServer;

    [HttpGet("")]
    public IActionResult Index()
    {
        // Start the static file server if not running and get the port
        var port = _fileServer.EnsureRunning();

        // Return an iframe wrapper that loads the PWA from the local server
        var html = string.Format(IframeWrapperHtml, port);
        return Content(html, "text/html");
    }

    private const string IframeWrapperHtml =
        // language=html
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Voxta Prompt Editor</title>
          <style>
            * {{ margin: 0; padding: 0; }}
            html, body {{ height: 100%; overflow: hidden; background: #0b0f14; }}
            iframe {{
              width: 100%;
              height: 100%;
              border: none;
            }}
          </style>
        </head>
        <body>
          <iframe src="http://127.0.0.1:{0}/" allowfullscreen></iframe>
        </body>
        </html>
        """;
}
