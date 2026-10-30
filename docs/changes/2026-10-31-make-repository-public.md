# 2026-10-31: Make the repository public

Owner's request: push the code to GitHub and make `namishkashyap/Lantoss` public, before the UI redesign starts.

## Items

- [x] 1. **Check the full history for secrets before it becomes visible.**
  - Private keys, access tokens, hard-coded passwords, `.pfx`/`.p12`/`.pem`/`.key`/`.env` files, committed installers or binaries.
  - Personal data: e-mail addresses, user names and local paths.
- [ ] 2. **Push this record** to `release/2.0.2` (PR #6). Every other branch is already on GitHub.
- [ ] 3. **Change the visibility to public** with `gh repo edit --visibility public`.

## Not in this change

- Merging the open PRs #4, #5 and #6. They still wait for the owner's review.
- The 20 `previews/windows/*-full.png` files deleted in the working tree. They were not deleted by this change, so they stay committed until the owner decides.

## Rollback

`gh repo edit namishkashyap/Lantoss --visibility private --accept-visibility-change-consequences`. Stars and watchers gained while the repository was public are lost, and forks made in that time remain public.
