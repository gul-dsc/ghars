using GharsPlatform.Models.Core;

namespace GharsPlatform.Helpers;

/// <summary>
/// Where a page should load a DSC gallery album's files from. Uploaded files are under
/// /uploads/gallery, which is not served statically, so they go through the authorized
/// <c>/protected-files/album-cover|album-media/{id}</c> endpoints. Anything else (a site image such
/// as the brand logo, an external video link) is used as stored.
/// </summary>
public static class GalleryMediaUrls
{
    /// <summary>True for a file this application stored under wwwroot/uploads.</summary>
    public static bool IsStoredUpload(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase);

    public static string? AlbumCover(MediaAlbum album)
        => string.IsNullOrWhiteSpace(album.CoverImagePath) ? null
            : IsStoredUpload(album.CoverImagePath) ? $"/protected-files/album-cover/{album.Id}"
            : album.CoverImagePath;

    public static string? AlbumItem(MediaItem item)
        => !string.IsNullOrWhiteSpace(item.FilePath)
            ? (IsStoredUpload(item.FilePath) ? $"/protected-files/album-media/{item.Id}" : item.FilePath)
            : item.VideoUrl;
}
