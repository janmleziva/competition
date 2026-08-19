# Handoff

## Summary

This branch adds phase-specific set rules to edition disciplines and completes the related setup, scoring, standings, and team-result workflows.

Disciplines can be marked as set-based when assigned to an edition. Each phase can then use either a fixed number of played sets or a number of sets required to win. Group phases also support configurable points for a win, draw, and loss. Match outcomes for fixed-set draws are resolved by the aggregate set subscore when it differs.

## User-facing changes

- Add a `Hraje se na sety` option when assigning a discipline to an edition; new set-based disciplines default to two sets.
- Configure the set rule independently for every phase:
  - fixed number of played sets;
  - number of sets required to win.
- Save phase set-rule and group-point changes automatically when their values change.
- Configure group-stage points for a win, draw, and loss.
- Display Czech labels and responsive controls for phase settings.
- Enter and edit exactly the applicable number of set results for the selected phase rule.
- Resolve a tied fixed-set match using the aggregate points scored across its sets.
- Use the resolved outcome consistently in group standings, statistics, scoring, and knockout progression.
- Link team names on discipline results and standings to a new team-results page.
- Show every match for the selected team in that edition discipline, with scores oriented to the team and opposing teams linked to their own result pages.

## Data and compatibility

- Add `SetRule`, `SetCount`, `PointsForWin`, `PointsForDraw`, and `PointsForLoss` phase fields.
- Include EF Core migration `20260819075703_AddPhaseSetRules`.
- Update the SQLite schema upgrader and initial SQL schema for existing local and deployed databases.
- Preserve the existing discipline-level set defaults as the initial values for newly created phases.

## Important implementation details

- `MatchOutcomeResolver` is the common source for win, draw, and loss resolution.
- Fixed-set matches may have an equal set score; aggregate set points break that tie when unequal.
- Winning-set matches stop accepting additional sets once either side reaches the target.
- Automatically resolved knockout winners continue to propagate into dependent matches.
- Team histories include completed, in-progress, and scheduled matches scoped to the requested edition and discipline.

## Validation

- `dotnet test Competition.Tests/Competition.Tests.csproj -c Release --no-restore`
- 151 tests passed.
- `git diff --check`

## Deployment status

The changes have not been deployed. Apply the EF migration, or allow the SQLite schema upgrader to update the local `App_Data/competition.db`, after pulling the branch.

The untracked `deployment/database-backups/` directory is intentionally excluded from this branch.
