# SurvivalcraftMechDefense: project instructions

One shared instruction source for Codex and Claude Code (`CLAUDE.md` imports it). Updated 2026-10-10; Git rules follow
`E:/projects/ScCsgoKnives/AGENTS.md`.

## Product

- Target Survivalcraft API 1.9.3.1 and .NET 10. Package `local.mechdefense.sample`, version in `Mod/modinfo.json`.
- The mech is a persistent independent vehicle with a rider, never a replacement player model.
- The ground turret attacks hostile entities only; require line of sight and prevent friendly fire.
- Tower models are the authored blocky models from `E:/projects/endfield-turret-model`; `tools/towers.json` names the
  model version per tower. Details, numbers and the build pipeline: `docs/TOWERS.md`.
- Never substitute a procedural placeholder and describe it as a recovered original game model.

## Scope and current truth

- Follow the user's active request. Explain/review/diagnose requests do not authorize implementation, packaging,
  installation or publication.
- Preserve accepted behavior and unrelated work. Inspect `git status --short` before editing; never reset, restore,
  stash or commit someone else's work as cleanup (other agents keep untracked research files in this tree).
- Do not modify the original game installation, the user's Mods folder or existing worlds. Test in an isolated runtime
  (`tools/run_isolated.py`, `local/runtime-*`) copied from the user's build `D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1`.

## Branch, commit and merge workflow

- Before the first edit of a feature, fix, release or policy change, create a task branch from `main`:
  `feat/…`, `fix/…`, `perf/…`, `refactor/…`, `docs/…`, `build/…`, `chore/…` (short kebab-case). Never implement on `main`.
- This tree has one checked-out branch at a time. Before switching: commit or set aside your own changes and confirm no
  other writer, game or test job is active (uncommitted edits travel with the checkout). A concurrent second task uses
  its own `git worktree` outside this folder. When another agent's branch must stay checked out, a commit to your task
  branch may be written with a temporary index (`GIT_INDEX_FILE` + `commit-tree` + `update-ref`).
- Commits follow Conventional Commits: `<type>(<optional scope>): <description>`, types feat / fix / perf / refactor /
  docs / test / build / chore, scopes such as `towers`, `mech`, `tools`, `docs` (for example
  `feat(towers): ship V5 tower models`). Keep each commit to the task's own files.
- After the scoped change and its verification: commit, push the task branch to `origin`, open a pull request to `main`
  and report the branch, commits, verification and remaining gaps.
- Merge into `main` only after the user explicitly approves merging that task. Approval to develop, commit or push is
  not merge approval, and approval for an earlier task does not carry over.
- Releases: bump `Mod/modinfo.json` (semver), package `dist/MechDefense-<version>.scmod`, and after the approved merge
  tag the merge commit `v<version>` and push the tag. Keep the previous package as rollback until the user accepts the
  new one. Packages go to the user only; installing them is a separate request.
- Git operations that rewrite files (checkout, merge, pull, reset) run on Windows; from the VPS use Git read-only or
  through Windows jobs. Line endings follow `.gitattributes` (C# CRLF in the working tree, Python and JSON LF, stored
  as LF); never let a tool or checkout rewrite the line endings of files you did not change.
- Remote: `https://github.com/zh667/SurvivalcraftMechDefense` (private). No CI: the mod builds against the user's
  local game assemblies, which are not in the repository.

## Files and assets

- Keep source, manifests and verification records in Git.
- Keep game binaries, extracted third-party assets and external tools in ignored `local/` or `Mod/Assets/Imported/`.
  Generated tower assets (`Mod/Assets/Towers/`) and packages (`dist/`) are ignored and rebuilt by the tools. Never
  commit extracted game models, textures, audio or packages.

## Build and verification

- Commands: `docs/TOWERS.md` → “重新生成与验证”. The V5 tower asset build takes about three hours in one process: build
  subsets in parallel jobs (`tools/build_tower_assets.py <ids>`, metadata per tower in `local/tower-meta/`) and assemble
  with `--runtime-only`. Do not rerun it for docs-only or C#-only changes.
- Before a tower or package release: `tools/check_tower_gaps.py` (see-through slits, also on the previous package),
  `tools/check_tower_overlaps.py` (z-fighting pairs), `tools/run_isolated.py lab --mode gallery` (renders, no draw
  errors) and `tools/run_isolated.py perf` for the new and previous package (frame time). Record the numbers in
  `docs/TOWERS.md`.
- Distinguish decoded containers, converted meshes, correct scene assembly, build verification and in-game
  verification; say which layer each result reached and list failed or not-run checks separately.
- Once comparison results are recorded, delete this task's intermediates (`local/claude-work/`, `local/runtime-*`);
  `run_isolated.py` recreates runtimes. Keep packages the user is still using.

## Instruction maintenance

- Keep this file short. Current status goes in `docs/STATUS.md`, task briefs in `docs/tasks/` (template there),
  tower details in `docs/TOWERS.md`.
