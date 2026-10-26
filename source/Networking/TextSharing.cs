using Lantoss.Core;

namespace Lantoss.Networking;

public sealed partial class TransferEngine
{
    public async Task SendNoteAsync(Peer peer, string text, CancellationToken token = default)
    {
        string content = SharedNotes.Validate(text);
        WireMessage response = await SpaceRequestAsync(peer, new()
        {
            Kind = "text-post",
            NoteContent = content,
            NoteId = Guid.NewGuid().ToString("N")
        }, token);
        Wire.Require(response, "text-saved");
    }
}
