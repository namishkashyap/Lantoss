# 2026-10-26: Private repository, license and 1.9.0 merge

Only the items below are in scope for this change.

## Items

- [x] 1. Make `namishkashyap/Lantoss` private. Done: visibility is `PRIVATE`.
- [x] 2. Add a root `LICENSE` (MIT) and a License section in `README.md`.
- [x] 3. Fix the stale README instruction "Update both PCs to 1.5" in the sync section. It now says to use the same Lantoss version on both PCs.
- [x] 4. Mark the resolved open items in the 1.9.0 change record and add this record to the index.
- [ ] 5. Merge PR #1 with a merge commit, delete `release/1.9.0`, update local `main`. This runs after this record is merged.
- [ ] 6. Tag `v1.9.0` and publish the GitHub Release with `Lantoss-Setup-1.9.0-x64.msi`, as planned in the [1.9.0 record](2026-10-26-release-1.9.0.md). This runs after the merge, and the result is on the [v1.9.0 release page](https://github.com/namishkashyap/Lantoss/releases/tag/v1.9.0).

## Decisions

- **Why MIT:** MIT is short, widely understood and compatible with every bundled
  dependency. .NET, ASP.NET Core, QRCoder and BouncyCastle are all MIT. WiX and
  the GCC/MinGW runtime notices only cover the tooling and runtime they ship,
  and their own license files stay in the repo. The copyright holder is
  `namishkashyap`. Replace it with a legal name if preferred.
- **Visibility:** the repository is private, so MIT only grants rights to people
  who are given the code. It becomes relevant if the repository is made public
  again.

## Out of scope (next change)

- Device name: renaming already works under **Settings → General → Device name**.
  A first-run name prompt or a rename shortcut on the sidebar PC card needs
  code changes and a .NET 10 SDK to build.
- New features and error fixes still to be chosen.
