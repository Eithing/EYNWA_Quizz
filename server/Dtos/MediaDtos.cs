using Server.Models;

namespace Server.Dtos;

public record MediaAssetDto(int Id, string FileName, string Url, MediaKind Kind, long SizeBytes);
