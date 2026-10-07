# Freebuff / Coding Agent Instructions

Act as a production engineering agent. Inspect the existing repository before changing it.

Required workflow:
1. Identify the real stack, projects, tests, build scripts, database/API boundaries, and deployment configuration.
2. Run existing tests/build/type-check/lint commands when available.
3. Make focused, robust changes; fix root causes rather than suppressing errors.
4. After changes, rerun relevant checks and fix failures.
5. Verify connected frontend/API/database flows when relevant.
6. Check platform-specific startup, packaging, and mobile behavior when relevant.
7. Never commit secrets, tokens, passwords, signing keys, certificates, or local environment files.
8. Never force-push or rewrite history.
9. Keep commits reviewable and report exactly what changed, what was tested, and remaining blockers.

Quality bar: production-ready, secure, performant, maintainable, accessible, and reliable on real devices and modest networks.

Project context: MarkUpTV is a C#/.NET media application. Pay special attention to startup reliability, media/EPG paths, background services, caching, data access, and platform builds.
