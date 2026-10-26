using System.Text;
using System.Text.Json;

namespace Lantoss.Core;

public sealed record SharedNote(string Id, string Content, string Sender, DateTime CreatedUtc)
{
    public string Kind => Uri.TryCreate(Content, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" && !Content.Any(char.IsWhiteSpace) ? "link" : "text";
}
public sealed record NoteSnapshot(string Revision, SharedNote[] Notes);

/// <summary>A bounded shared board. Plain text is never evaluated, opened or copied automatically.</summary>
public sealed class SharedNotes
{
    public const int MaxCharacters = 16000;
    private readonly object gate = new();
    private readonly string filename;
    private SharedNote[] notes = [];
    private string revision = Guid.NewGuid().ToString("N");
    public event Action? Changed;
    public SharedNotes(string root)
    {
        filename = Path.Combine(root, "shared-notes.json");
        SharedNote[] Read(string path)
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("The shared notes store is too large.");
            return JsonSerializer.Deserialize<SharedNote[]>(File.ReadAllBytes(path), MetadataStore.Json) ?? [];
        }
        try
        {
            if (File.Exists(filename))
                notes = Read(filename);
        }
        catch (Exception error) when (error is IOException or JsonException) { if (!File.Exists(filename + ".bak")) throw; notes = Read(filename + ".bak"); }
        if (notes.Length > 100 || notes.Any(note => note == null || !Guid.TryParseExact(note.Id, "N", out _) || note.Content == null || note.Content.Length > MaxCharacters || note.Sender == null || note.Sender.Length > 80))
            throw new InvalidDataException("Invalid shared notes. Preserve the store for recovery.");
    }
    public NoteSnapshot Snapshot(string? knownRevision = null)
    {
        lock (gate)
            return new(revision, knownRevision == revision ? [] : notes.ToArray());
    }
    public static string Validate(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxCharacters || Encoding.UTF8.GetByteCount(content) > 64000 || content.Contains('\0'))
            throw new InvalidDataException("Enter text or a link, up to 16,000 characters / 64 KB.");
        return content.Trim();
    }
    public SharedNote Add(string? content, string sender, string? id = null)
    {
        string value = Validate(content);
        string key = id ?? Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(key, "N", out _))
            throw new InvalidDataException("Invalid note identity.");
        string label = new(sender.Where(c => !char.IsControl(c)).Take(80).ToArray());
        SharedNote note = new(key, value, label, DateTime.UtcNow);
        lock (gate)
        {
            SharedNote? existing = notes.FirstOrDefault(item => item.Id == key);
            if (existing != null)
            {
                if (existing.Content != value || existing.Sender != label)
                    throw new InvalidDataException("This note identity is already in use.");
                return existing;
            }
            Save([note, .. notes.Take(99)]);
        }
        Changed?.Invoke();
        return note;
    }
    public void Remove(string id)
    {
        lock (gate)
            Save(notes.Where(note => note.Id != id).ToArray());
        Changed?.Invoke();
    }
    private void Save(SharedNote[] next)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, MetadataStore.Json);
        while (bytes.Length > 2 * 1024 * 1024 && next.Length > 1)
        {
            next = next[..^1];
            bytes = JsonSerializer.SerializeToUtf8Bytes(next, MetadataStore.Json);
        }
        FileCatalog.CheckSafeDirectory(Path.GetDirectoryName(filename)!);
        new MetadataStore.AtomicDiskWriter().Write(filename, bytes);
        notes = next;
        revision = Guid.NewGuid().ToString("N");
    }
}
