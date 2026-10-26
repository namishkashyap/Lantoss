using System.Globalization;

namespace Lantoss.Core;

/// <summary>Names and pictures are self-reported labels for tracking, not proof of identity. Every input is bounded and validated.</summary>
public static class Profiles
{
    public const int MaxPictureBytes = 64 * 1024;
    public static string PicturePath(MetadataStore store) => Path.Combine(store.Root, "profile.png");
    public static byte[]? Picture(MetadataStore store)
    {
        try
        {
            FileInfo file = new(PicturePath(store));
            if (!file.Exists || file.Length > MaxPictureBytes)
                return null;
            byte[] data = File.ReadAllBytes(file.FullName);
            return IsPicture(data) ? data : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    public static bool IsPicture(ReadOnlySpan<byte> data)
        => data.Length is > 8 and <= MaxPictureBytes && (data.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) || data.StartsWith((byte[])[0xFF, 0xD8, 0xFF]));
    public static byte[]? FromDataUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        string[] prefixes = ["data:image/jpeg;base64,", "data:image/png;base64,"];
        string? prefix = prefixes.FirstOrDefault(p => value.StartsWith(p, StringComparison.Ordinal));
        if (prefix == null || value.Length > prefix.Length + (MaxPictureBytes + 2) / 3 * 4)
            throw new InvalidDataException("Use a JPEG or PNG picture up to 64 KB.");
        byte[] data;
        try
        {
            data = Convert.FromBase64String(value[prefix.Length..]);
        }
        catch (FormatException) { throw new InvalidDataException("The picture could not be read."); }
        return IsPicture(data) ? data : throw new InvalidDataException("Use a JPEG or PNG picture up to 64 KB.");
    }
    public static string CleanName(string? name)
    {
        name = (name ?? "").Trim();
        // Bidi and other format characters can make one name display as another.
        if (name.Length > 40 || name.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
            throw new InvalidDataException("Use a name up to 40 characters without control or formatting characters.");
        return name;
    }
    /// <summary>Validates the admin's phone-page name: a single DNS label of letters, digits and inner hyphens, without ".local".</summary>
    public static string AddressName(string? name)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        if (name.EndsWith(".local", StringComparison.Ordinal))
            name = name[..^6];
        if (name.Length is < 1 or > 40 || name[0] == '-' || name[^1] == '-' || !name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            throw new InvalidDataException("Use an address of 1 to 40 letters, digits or hyphens, for example family-share.");
        return name;
    }
    public static string SenderFolder(string inbox, string sender)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new string(sender.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (clean.Length > 60)
            clean = clean[..60];
        clean = clean.TrimEnd('.', ' ');
        try
        {
            FileCatalog.ValidateSegment(clean);
        }
        catch (InvalidDataException) { clean = "Unknown sender"; }
        return Path.Combine(inbox, clean);
    }
    public static string DeviceLabel(string? userAgent)
    {
        string ua = userAgent ?? "";
        string device = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? ua.Contains("Mobile") ? "Android phone" : "Android tablet"
            : ua.Contains("Windows") ? "Windows PC" : ua.Contains("CrOS") ? "Chromebook" : ua.Contains("Macintosh") ? "Mac" : ua.Contains("Linux") ? "Linux PC" : "Browser";
        string browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("SamsungBrowser") ? "Samsung Internet" : ua.Contains("Firefox/") || ua.Contains("FxiOS") ? "Firefox"
            : ua.Contains("Chrome/") || ua.Contains("CriOS") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "";
        return browser.Length == 0 ? device : device + " · " + browser;
    }
}
