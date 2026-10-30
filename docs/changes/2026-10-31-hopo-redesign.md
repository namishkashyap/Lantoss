# 2026-10-31: hopo redesign

Owner's request:
- Rebuild the UI from the design canvas ([Design](https://claude.ai/artifact/PETTHAnoGBq6XJZPnP68ko), 11 screens).
- The app is renamed **hopo**.
- Add a light version designed to match.
- Fake the glass effect where true blur isn't available.
- Cover the Windows app, the macOS/Linux app and the phone page.

## Items

- [x] 1. **Shared hopo palette.** `Core/Appearance.cs` gets the design's dark palette and a matching light palette, keeping System/Light/Dark.
  - Dark:
    - near-black ground `#07080C`, text `#EDEEF2`, muted `#9BA1AE`
    - amber accent `#F5B54A`, mint `#6EE7C5` for online and safe states
    - danger `#FF8A80`
  - Light:
    - cool off-white ground, white glass cards, ink text
    - deep amber `#8A5300` for links and icons; amber `#F5B54A` buttons with dark labels
    - deep mint `#0B7A5F`
  - New tokens for the glass and the glows: `Glass`, `GlassBorder`, `GlowA`, `GlowB`, `Mint`, `Phone`.
  - Text, muted text and buttons keep 4.5:1 contrast. The existing test still checks this.
- [x] 2. **Windows app shell.**
  - Remove the sidebar. The top bar holds the hopo mark, a "hopo / page" breadcrumb, the Receiving chip, search on searchable pages, and a menu button.
  - The menu slides out from the right with:
    - a Share group: Send files, Share a folder, Share text or a link, Phone & browser
    - a Manage group: Transfers, Devices, Sync, History, Settings
    - this PC's card, local storage and appearance
  - Each page opens with an eyebrow, a title and the design's one-line subtitle.
  - Purple and green glows plus faint rings sit behind every page, and cards are translucent "glass".
  - Restyle buttons, inputs, check boxes, badges and cards to the design: 44 px buttons and 12–20 px corners.
- [x] 3. **Windows Home.**
  - Nearby radar: rings around this PC's disc, with each paired PC (mint) and approved phone (purple) as a dot with its name.
  - Clicking a dot shows its card: status, address, trust, Send files, Text or Disconnect.
  - A dock at the bottom: Send files, Share a folder, Share text, Open on your phone.
- [x] 4. **macOS/Linux app.** The same shell, menu, page headings, glass, glows, radar Home and dock, in Avalonia.
- [x] 5. **Phone page.** The same look in both themes: hopo mark, glass cards, glows, amber buttons, mint status.
- [x] 6. **Rename the visible name to hopo.** Window titles, tray, dialogs, notifications, page text, the phone page and the macOS/Linux app name shown in the window.
  - Kept as Lantoss for compatibility, like the legacy `flowshare` names:
    - the executable, data folders, install paths, pipe and lock names, CSRF header and cookie names, namespaces
    - installers and package names, the Explorer menu DLL
    - the repository
- [x] 7. **Tests and previews.**
  - Update the tests whose expected text changes.
  - Run every suite, the UI regression and the format check.
  - Re-render `previews/` in both themes.

## Not in this change

- A version number or release. That comes after the owner reviews the PR.
- Renaming the executable, installer, packages, data folders or repository. Each would break upgrades or links and needs its own migration.
- Bundling the Inter font, which needs a download. The apps use Segoe UI Variable on Windows and the system UI font elsewhere; the phone page asks for Inter and falls back to the system font.
- True background blur on Windows. The cards are translucent over the glows instead.

## Verification

- `build.ps1 -Mode Test`: 279 checks passed, 1 skipped (symbolic links need a privilege this account lacks). No security expectation changed.
- `node source/tests/browser-ui.test.cjs`: 16 passed.
- Windows `--ui-regression`: 26 passed. The new check opens the menu, renders it, then picks Devices and confirms the page opens and the menu closes. The compact PC picker limit moved from 42 to 48 px, because every control is 44 px now.
- `-Mode CheckFormat` passed. Windows and macOS/Linux builds have 0 warnings.
- `previews/` re-rendered: Windows 10 pages in both themes, plus `-full` tall shots; macOS/Linux 10 pages plus the menu in both themes; the phone page connected and waiting in both themes.

## Differences from the design

- No moving glows, ripples or blinking dots on macOS/Linux; on Windows only the device dots pulse and the menu slides in, both off when Windows animations are off.
- The Home dock opens the matching pages instead of the design's rising panels.
- Inter isn't bundled. The phone page's security policy only allows its own fonts, so it uses the system font.
- Text that names the Explorer menu ("Share with Lantoss") and the macOS firewall entry still says Lantoss, because those names come from the installed DLL and the app bundle.
