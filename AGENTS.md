# Build and delivery conventions

- The only user-facing executable is `dist/PopBar-win-x64/PopBar.exe`.
- Build user deliveries with `./scripts/publish.ps1`. The distributable archive is always `dist/PopBar-win-x64.zip`.
- Keep normal build and test outputs in their default project `bin/` and `obj/` directories. Do not override `BaseOutputPath` or create executable delivery folders under `artifacts/`.
- Use `artifacts/` only for logs, screenshots, and test fixtures. Do not link users to executables there or in project `bin/` directories.
- If a running process locks an output file, report the exact process/path and ask the user to exit it when necessary. Do not silently switch output directories or stop the user's app.
- Never delete old output folders without checking their resolved paths and whether they contain a running executable or user files.
