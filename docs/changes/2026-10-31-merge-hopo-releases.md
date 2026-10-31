# 2026-10-31: merge the 2.0.0–2.1.3 pull requests into main

Owner's request: push everything to GitHub and merge the open pull requests. The owner gave permission for the merges.

The pull requests are stacked, and each branch contains the one below it:

| PR | Branch | Base |
|---|---|---|
| #4 | release/2.0.0 | main |
| #5 | release/2.0.1 | main |
| #6 | release/2.0.2 | main |
| #7 | feat/hopo-redesign | release/2.0.2 |
| #8 | release/2.1.0 | feat/hopo-redesign |
| #9 | release/2.1.1 | release/2.1.0 |
| #10 | release/2.1.2 | release/2.1.1 |
| #11 | release/2.1.3 | release/2.1.2 |

## Items

- [ ] 1. Merge from the top of the stack down with merge commits: #11, #10, #9, #8, #7, then #6 into main. Branches are not deleted.
- [ ] 2. Confirm that #4 and #5 show as merged once main contains their commits. Merge either one if it does not.
- [ ] 3. Run git fetch, then fast-forward the local main deliberately. Do not let gh switch branches.
- [ ] 4. Confirm that main contains the v2.1.3 code and that no pull requests are left open.
