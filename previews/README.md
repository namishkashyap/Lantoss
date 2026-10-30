# hopo screens

This folder holds screenshots of every section in the hopo design. They were rendered from the real
apps with sample data: "Family PC", the paired PCs "Studio PC" and "Priya's laptop", and one shared
space, "Family photos".

| Folder | What | Size |
|---|---|---|
| `windows/` | Windows app (WPF), 10 pages in Light and Dark. `-full` files show the whole page without scrolling. | 1440 × 900 and 1440 × 2000 |
| `macos-linux/` | macOS / Linux app (Avalonia), the same 10 pages plus the open menu in Light and Dark | 1280 × 840 |
| `phone/` | The phone page in a mobile browser: connected, and waiting for the PC to allow it | 500 wide |

## Pages

1. **Home:** a nearby radar with this PC in the middle, paired PCs (mint) and phones (purple) around it, and a dock with Send files, Share a folder, Share text and Open on your phone. The top bar holds the hopo mark, the Receiving chip and the menu button; the menu groups the Share and Manage pages.
2. **Send:** the chosen files and folders, plus the recipient.
   - PCs come from a list.
   - Approved phones appear under "Phones and browsers", each with its own Send button.
3. **Transfers:** live jobs with speed and remaining time.
4. **Devices:** paired PCs (trust badge, key, Send, Shared folders, Rename, Unpair, Block) and phones
   connected through the web.
   - Waiting phones show **Allow** and **Block**.
   - Blocked addresses show **Unblock**.
5. **Phone & browser:** the QR code and link, the HTTPS certificate code, waiting phones with Allow,
   the optional access code, and files offered to phones.
6. **Text & links:** the shared notes board. "Open link" asks first and shows the host.
7. **Shared Spaces:** folders on other PCs, folders shared from this PC, the access level, and
   **Who can open** (all paired PCs or chosen PCs).
8. **Sync:** linked folders and two-way sync status.
9. **History:** finished transfers with sender details and CSV export.
10. **Settings:** General, Transfers, Network, Security & access, and Advanced.

## Not in the screenshots

Preview mode does not start the sharing server, so these states need a live session:
- the QR code
- phones waiting for Allow
- phone recipients on Send
- live transfer progress

Dialogs are not included either:
- pairing comparison
- incoming files, including the program/script and look-alike warnings
- sync approval
- Open link confirmation

## Recreate

1. Build with `.\source\build.ps1 -Mode Build` and `.\source\build.ps1 -Mode Desktop`.
2. Run `Lantoss.exe --preview <png> --page "<page>" --theme Light|Dark --width 1440 --height 900 --data-dir <folder>`
   for each Windows page. The macOS/Linux app renders every page with `--render <folder> --data-dir <folder>`.
3. For the phone page:
   - Start the test server: `dotnet Lantoss.Tests.dll <folder> --serve-browser`, adding `--waiting` for
     the waiting state.
   - Capture it with headless Chrome at 500 px wide, using `--ignore-certificate-errors`.
