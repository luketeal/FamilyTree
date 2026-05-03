# ADR-003: Separate tables per relationship type

**Date:** 2026-05-03  
**Status:** Decided

## Context

The family tree has four relationship types: biological parent-child, adoptive parent-child, marriage, and stepparent. Each carries different data (adoption date, marriage end reason, stepparent-marriage link) and different business rules (max two biological parents per child, unlimited adoptive parents, marriage is symmetric between two named spouses).

Two structural approaches were considered:

1. **Single polymorphic `Relationship` table** — one table with a `Type` discriminator column and nullable columns for type-specific data (e.g. `AdoptionDate`, `EndReason`).
2. **Separate table per relationship type** — `BiologicalParentChild`, `AdoptiveParentChild`, `Marriage`, and `StepparentRelationship` as distinct tables with columns specific to each.

## Decision

Use separate tables per relationship type: `BiologicalParentChildLinks`, `AdoptiveParentChildLinks`, `Marriages`, and `StepparentRelationships`.

## Reasoning

The polymorphic table approach would require nullable columns for every relationship-specific field, type-discriminator checks throughout every query, and either EF Core inheritance (table-per-hierarchy) or manual discriminator mapping — all of which add complexity without any meaningful benefit here, since none of the relationship types share queryable data beyond `PersonId`.

Separate tables allow:
- Unique index on `(ParentId, ChildId)` on the biological and adoptive tables to enforce duplicate-link prevention at the database level (US-007, US-014).
- Clean EF Core configuration with no discriminators or nullable columns serving dual purposes.
- The "max two biological parents" rule to be enforced at the service layer with a straightforward count query against one table.
- Cascade rules to be set independently per relationship type (`Restrict` on `Marriage.Spouse2Id` to avoid SQLite cascade conflicts; `Cascade` on `StepparentRelationship.MarriageId` so deleting a marriage removes stepparent labels automatically, per US-038).

## Consequences

- Adding a new relationship type requires a new table and a new repository interface — there is no single place to query "all relationships for a person."
- Stepparent is stored, not derived: it is a manually applied label (`StepparentRelationship`) that references the `Marriage` record which creates the connection. Deleting the marriage cascades to delete the stepparent record (US-038).
- Half-sibling and full-sibling detection (US-037, US-051) is a query against `BiologicalParentChildLinks` — find all children sharing one or both parents with the subject. No stored sibling relationship is needed.
- The schema enforces uniqueness at the DB level for biological and adoptive links; duplicate marriage prevention for active marriages is handled at the service layer since the uniqueness condition depends on `EndDate` being null.
