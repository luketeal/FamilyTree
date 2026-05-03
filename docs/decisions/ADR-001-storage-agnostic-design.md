# ADR-001: Storage-agnostic domain design

**Date:** 2026-05-03  
**Status:** Decided

## Context

Early planning assumed SQLite for development and PostgreSQL for production, which implied maintaining dual-database configuration from the start and letting deployment constraints influence application design decisions. We considered whether that distinction needed to be locked in early.

## Decision

Keep the domain and application layers completely ignorant of the storage provider. All data access goes through repository interfaces (e.g. `IPersonRepository`). EF Core lives exclusively in an `Infrastructure` project. The current and only configured provider is SQLite. No production database provider is mandated.

## Reasoning

The repository abstraction already isolates the domain from EF Core specifics. Carrying a dual-provider setup from day one adds configuration complexity and can subtly push design choices toward what the database handles well rather than what the domain needs. Deferring the production storage decision costs at most a migration rewrite later — a small, bounded task — while keeping the design cleaner now.

## Consequences

- Domain models and application services are portable across any EF Core provider (SQLite, PostgreSQL, SQL Server) with no changes above the `Infrastructure` layer.
- EF Core migrations are written for SQLite only; switching providers requires regenerating them and verifying any provider-specific query behavior (case sensitivity, date handling).
- The production storage choice remains open and can be made on deployment criteria (cost, hosting environment, operational familiarity) rather than being baked into the codebase.
