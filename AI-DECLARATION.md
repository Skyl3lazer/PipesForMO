---
version: "0.1.2"
level: copilot
processes:
  design: hint
  implementation: assist
  testing: copilot
  documentation: assist
  review: hint
  deployment: copilot
components:
  Source/: assist
  "1.6/": assist
  .github/workflows/: copilot
---

This format is based on [AI-DECLARATION.md](https://ai-declaration.md/en/0.1.2).

## Notes

- Claude Code wrote the release workflow under `.github/workflows`.
- The C# under `Source/` and the XML defs under `1.6/` are hand-authored. Claude Code acted on parts of them under direction.
- Test plans and in-game verification were driven by Claude Code.
