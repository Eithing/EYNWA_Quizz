using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using QuizParty.Api.Dtos;

namespace QuizParty.Api.Controllers;

[ApiController]
[Route("api/media")]
public class MediaController(IWebHostEnvironment env) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    private const long MaxSizeBytes = 20 * 1024 * 1024;

    /// <summary>Upload d'image pour une question (ex: zoom-image). Réservé aux GM authentifiés.</summary>
    [Authorize]
    [HttpPost]
    [RequestSizeLimit(MaxSizeBytes)]
    public async Task<ActionResult<MediaUploadResponse>> Upload(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest("Fichier vide.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest("Type de fichier non supporté (image uniquement : png, jpg, webp, gif).");
        }

        var mediaRoot = Path.Combine(env.ContentRootPath, "media");
        Directory.CreateDirectory(mediaRoot);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(mediaRoot, fileName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new MediaUploadResponse($"/api/media/file/{fileName}"));
    }

    /// <summary>
    /// Public (les joueurs invités sans compte doivent pouvoir charger l'image directement via &lt;img src&gt;).
    /// Le nom de fichier est un GUID non énumérable, ce qui limite l'exposition.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("file/{fileName}")]
    public IActionResult GetFile(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var absolutePath = Path.Combine(env.ContentRootPath, "media", safeFileName);

        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        if (!contentTypeProvider.TryGetContentType(absolutePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return PhysicalFile(absolutePath, contentType);
    }
}
