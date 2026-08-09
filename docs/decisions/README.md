# Architecture Decision Records

This folder captures significant technical decisions made during the project — things where more than one reasonable approach existed and the reasoning behind the choice is worth preserving.

## When to write one

Write an ADR when:
- You're choosing between two or more non-trivial technical approaches
- The decision will be hard or expensive to reverse later
- A future reader looking at the code would wonder "why did they do it this way?"

Spike results (throwaway prototypes to evaluate options) should always produce an ADR here.

## Format

Use the template below. Keep it short — the goal is to capture the decision and the reasoning, not to write an essay.

```
# ADR-NNN: Title

**Date:** YYYY-MM-DD  
**Status:** Decided | Superseded by ADR-NNN

## Context
What problem were we solving? What options did we consider?

## Decision
What did we choose?

## Reasoning
Why this option over the others? Include any prototype findings, benchmarks, or constraints that drove the call.

## Consequences
What does this decision make easier? What does it make harder or foreclose?
```

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-001](ADR-001-storage-agnostic-design.md) | Storage-agnostic domain design | Decided |
| [ADR-002](ADR-002-partial-date-value-object.md) | PartialDate value object for approximate and partial dates | Decided |
| [ADR-003](ADR-003-relationship-schema.md) | Separate tables per relationship type | Decided |
| [ADR-004](ADR-004-client-only-wasm-local-first.md) | Client-only Blazor WebAssembly with local-first storage | Decided |
| [ADR-007](ADR-007-export-file-delivery.md) | Anchor download for export, not the File System Access API | Decided |
