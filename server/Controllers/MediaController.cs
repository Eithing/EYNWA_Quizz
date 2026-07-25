using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Extensions;
using Server.Models;

namespace Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class MediaController(QuizDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private static readonly Dictionary<string, MediaKind> AllowedExtensions = new()
    {
        [".png"] = MediaKind.Image,
        [".jpg"] = MediaKind.Image,
        [".jpeg"] = MediaKind.Image,
        [".webp"] = MediaKind.Image,
        [".gif"] = MediaKind.Image,
        [".mp3"] = MediaKind.Audio,
        [".wav"] = MediaKind.Audio,
        [".ogg"] = MediaKind.Audio,
        [".mp4"] = MediaKind.Video,
        [".webm"] = MediaKind.Video
    };

    private const long MaxSizeBytes = 200 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxSizeBytes)]
    public async Task<ActionResult<MediaAssetDto>> Upload(IFormFile file)
    {
        var ownerId = User.GetGameMasterId();

        if (file.Length == 0)
        {
            return BadRequest("Fichier vide.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.TryGetValue(extension, out var kind))
        {
            return BadRequest("Type de fichier non supporté (image, audio ou vidéo uniquement).");
        }

        var mediaRoot = Path.Combine(env.ContentRootPath, "media", ownerId.ToString());
        Directory.CreateDirectory(mediaRoot);

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(mediaRoot, storedFileName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var asset = new MediaAsset
        {
            OwnerId = ownerId,
            FileName = file.FileName,
            RelativePath = $"{ownerId}/{storedFileName}",
            Kind = kind,
            SizeBytes = file.Length,
            UploadedAtUtc = DateTime.UtcNow
        };

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();

        return Ok(ToDto(asset));
    }

    [HttpGet]
    public async Task<ActionResult<List<MediaAssetDto>>> GetMine()
    {
        var ownerId = User.GetGameMasterId();

        var assets = await db.MediaAssets
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.UploadedAtUtc)
            .ToListAsync();

        return Ok(assets.Select(ToDto).ToList());
    }

    /// <summary>
    /// Accessible sans authentification : les joueurs invités (sans compte) doivent pouvoir charger
    /// les images/sons d'une épreuve directement via &lt;img&gt;/&lt;audio&gt; src pendant une partie.
    /// Le nom de fichier stocké est un GUID non énumérable, ce qui limite l'exposition.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> GetFile(int id)
    {
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
        if (asset is null)
        {
            return NotFound();
        }

        var absolutePath = Path.Combine(env.ContentRootPath, "media", asset.RelativePath);
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

    private static MediaAssetDto ToDto(MediaAsset asset) =>
        new(asset.Id, asset.FileName, $"/api/media/{asset.Id}/file", asset.Kind, asset.SizeBytes);
}
