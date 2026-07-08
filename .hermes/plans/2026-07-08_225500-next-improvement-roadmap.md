# Next Improvement Roadmap

## Main Points

1. Fix GitHub push credentials. Status: blocked until a usable token/SSH key is configured.
2. Add production vector provider: Qdrant. Status: foundation started.
3. Add vector maintenance commands: `/vectorsync`, `/vectorclear`, `/vectorrepair`. Status: complete.
4. Add admin-only bot self-update command.
5. Improve runtime health and diagnostics. Status: complete.
6. Improve voice/image agent harnesses. Status: complete.
7. Add backup/export tools.
8. Run final hardening pass.
9. Add better reasoning features.

## Execution Order

1. GitHub push credentials. Checked HTTPS, SSH, and environment token paths.
2. Qdrant provider foundation. Added provider config, factory path, and HTTP upsert/search/delete store foundation.
3. Vector maintenance commands. Added `/vectorsync`, `/vectorclear`, and `/vectorrepair` with tests.
4. Runtime health. Added compact `/health` diagnostics for vector/Qdrant, media providers, reasoning/runtime flags, and GitHub push readiness.
5. Voice/image harnesses. Added readiness status, provider gates, command coverage, and next safe command candidates to `/harnesses`.
6. Backup/export.
7. Self-update.
8. Better reasoning.
9. Hardening/release.
