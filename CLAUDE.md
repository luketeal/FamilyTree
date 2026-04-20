# FamilyTree — Claude Instructions

## Project
React + Node.js/Express family tree app. Tech stack: React (frontend), Node.js/Express (API), SQLite (dev), PostgreSQL (prod).

## Branch Naming
Always use: `claude/<short-kebab-description>-<4-char-random-suffix>`
Example: `claude/add-person-form-k9xQ`

## Commit Messages
- Imperative mood, sentence case, no trailing period
- No conventional-commit prefixes (no `feat:`, `fix:`, etc.)
- Single line for simple changes; add a blank line + body for context if needed
- Good: `Add birth date validation to person form`
- Bad: `feat(form): add birth date validation`

## Pull Requests

### When to create vs update
- Create a new PR when starting work on a new branch
- Update an existing PR (via `mcp__github__update_pull_request`) when:
  - New commits are pushed to the same branch
  - The scope of the work changes
  - Reviewer feedback is addressed
- Never close and re-open a PR to make edits — always update it

### Updating PRs
- When updating a PR body, preserve all already-checked Test Plan items (`- [x]`) — never uncheck them
- Only add or revise unchecked items or new sections

### Title format
- Imperative mood, sentence case, no trailing period
- No ticket numbers or prefixes
- Describe the change, not the files touched
- 72 characters max
- Good: `Add sibling display to person profile page`
- Bad: `feat(profile): US-051 sibling view changes`

### Required body sections
Every PR body must contain these sections in this order:

```
## Summary
One sentence describing what this PR does and why.

## Changes
Bullet list of concrete changes (not file names).
- Add birth place field to person creation form (US-001)
- Guard against duplicate biological parent relationships (US-007)

## Test Plan
Checklist of manual steps to verify the change works.
- [ ] Create a new person with birth place filled in — confirm it saves
- [ ] Attempt to add a second biological father — confirm error appears

## User Stories
List any user story IDs touched (omit section if none).
- US-001, US-007
```

### Session URL
Always append the Claude session URL as the last line of the PR body, on its own line with no label. Claude Code appends this automatically.

### Draft PRs
Open as draft when the branch is not yet ready for review. Convert to ready with `mcp__github__update_pull_request` when complete.
