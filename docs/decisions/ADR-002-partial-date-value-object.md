# ADR-002: PartialDate value object for approximate and partial dates

**Date:** 2026-05-03  
**Status:** Decided

## Context

US-045 requires date fields to accept year-only ("1920"), year+month ("March 1920"), full dates ("15 March 1920"), and a "circa" flag for approximation (displayed as "c. 1920"). This requirement applies to birth date, death date, marriage start/end date, and adoption date — every date in the system.

Two implementation approaches were considered:

1. **`DateTime?` everywhere, retrofit later** — use standard .NET `DateTime?` for all date fields now, add partial-date support as a separate pass when US-045 is prioritised.
2. **`PartialDate` value object from the start** — define a purpose-built value object with nullable `Year`, `Month`, `Day` integers and an `IsApproximate` flag, stored via EF Core owned-type columns.

## Decision

Implement `PartialDate` as a sealed value object from day one, stored as four flat columns per date field via EF Core owned types (`{Prefix}_Year`, `{Prefix}_Month`, `{Prefix}_Day`, `{Prefix}_IsApproximate`).

## Reasoning

Retrofitting from `DateTime?` to a partial-date representation later would require a data migration touching every date column in the schema and every query, display, and sorting site in the codebase — a disruptive cross-cutting change. Introducing `PartialDate` upfront costs only a small amount of additional model complexity, and that complexity pays for itself immediately: `DateTime?` cannot represent "year 1880" without an arbitrary sentinel value (e.g. Jan 1), which would corrupt sorting and age calculations throughout the app.

The owned-type flat-column approach was chosen over a JSON column because it keeps date parts queryable and sortable at the database level without deserialisation, which is important for the date-range search in US-035.

## Consequences

- Every date field in the domain is `PartialDate?` (or `PartialDate` for required fields like `Marriage.StartDate`). No `DateTime` fields exist in domain entities.
- `PartialDate` sorts correctly: year-only before year+month, year+month before full date, all within the same year — handled by `CompareTo`.
- Age calculations that involve an approximate date must prefix their result with "~" (UI concern, enforced at the display layer).
- The EF Core schema uses four columns per date field. Adding a new date field means adding four columns in a migration, which is slightly more verbose but entirely mechanical.
- `PartialDate` is immutable; all construction goes through static factory methods (`FromYear`, `FromYearMonth`, `FromYearMonthDay`) that validate inputs eagerly.
