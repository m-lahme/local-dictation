# Domain Docs

How engineering skills should consume this repository’s domain documentation while exploring the codebase.

## Before exploring, read these

- `CONTEXT.md` at the repository root.
- `CONTEXT-MAP.md` at the repository root if it exists; it points to the relevant context-specific `CONTEXT.md` files.
- Relevant ADRs under `docs/adr/`.
- In a multi-context repository, relevant ADRs under `src/<context>/docs/adr/`.

If these files do not exist, proceed silently. The domain-modeling workflow creates them lazily when terminology or architectural decisions are resolved.

## File structure

This repository uses the single-context layout:

```text
/
├── CONTEXT.md
├── docs/adr/
└── src/
```

A future multi-context layout would use a root `CONTEXT-MAP.md` pointing to context-specific `CONTEXT.md` and ADR directories.

## Use the glossary’s vocabulary

When output names a domain concept—in an issue title, refactoring proposal, hypothesis, or test name—use the term defined in `CONTEXT.md`. Do not drift to synonyms the glossary explicitly avoids.

If a required concept is absent, reconsider whether the language fits the project or note the gap for domain modeling.

## Flag ADR conflicts

If proposed work contradicts an existing ADR, surface the conflict explicitly instead of silently overriding the decision.
