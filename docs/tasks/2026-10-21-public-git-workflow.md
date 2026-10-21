# Public repository and isolated Git worktrees (2026-10-21)

Status: review; remote visibility and protection applied, policy changes await task-specific merge approval.
Branch: docs/public-repo-workflow
Current writer: Windows Codex in E:/projects/SurvivalcraftMechDefense-git-policy-20261021.
User-authorized scope: "可以转为公开仓库并修复上面两个问题" — make the repository public, protect main, and replace the temporary-index commit exception with independent worktrees.
Out of scope: gameplay, assets, packages, installation, PR #5, merging this task without its own approval, adding CI.

## Evidence and implementation

- Baseline main: aaf97c0351bb3acfabc5da5a62dea8ac9c502e89. The original checkout stays on feat/progression-robots at 897a601 with a clean working tree at inspection.
- Read-only inspection of tracked paths and locally available all-ref history found no matching common GitHub/AWS token or private-key markers. Filename/history checks found no committed local/, dist/, Mod/Assets/Imported/ or Mod/Assets/Towers/ trees, conventional credential filenames, game executables or packages. This is a limited pattern/path check, not a guarantee that every possible secret has been excluded.
- Replace GIT_INDEX_FILE / commit-tree / manual update-ref task commits with one branch and separate worktree per concurrent task. Editing, verification and commit must refer to that same worktree's inputs.
- Target main protection: PRs, resolved conversations, admin enforcement, force push and deletion disabled; zero required approving reviewers and no required CI checks.
- Keep public visibility explicit in AGENTS.md. CLAUDE.md already imports AGENTS.md, so both agents use the same policy.

## Verification

- Read back public visibility (private=false) and main protected=true. GitHub briefly locked the repository while changing visibility; a subsequent protection request succeeded.
- Protection readback confirms required PRs, zero required approving reviewers, resolved conversations, admin enforcement, force pushes/deletion disabled, and required_status_checks=null. The workflow count remains zero.
- `git diff --check` passed. The policy diff only changes Git workflow and visibility rules; CLAUDE.md still imports AGENTS.md.
- The task branch and its PR carry the policy update; no main merge or auto-merge is authorized yet. Existing PR #5 will inherit the policy after an approved main merge and its next branch update.
- No build/game test is required for these policy-only changes. Existing version tags and local verification tools remain unchanged.
