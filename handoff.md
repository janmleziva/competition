# Handoff

## Summary

This change set completes the final UI, validation, phase setup, scoring, and standings touchups for the Pohoda Cup workflow. It improves mobile and desktop layouts, clarifies blocked actions, makes group assignment safer, and makes phase results consistent across group and knockout matches.

## User-facing changes

- Restrict discipline participant administration to authenticated users.
- Link competitors to their statistics detail and disable unregistering competitors who are already assigned to teams.
- Improve participant, phase setup, match, modal, and results layouts across desktop and narrow mobile widths.
- Hide participant/team assignment status pills below 410 px where they would crowd names.
- Keep headings and action buttons aligned, including right-aligning the closed-discipline actions on desktop.
- Keep invalid set-score input in its modal, preserve entered values, and display validation errors without closing it.
- Calculate and save a missing main match score from valid set results.
- Allow fixed even numbers of played sets in knockout/final phases and use aggregate set subscores to resolve tied set scores.
- Remove score and subscore controls from matchup setup pages.
- Rename the phase badge from `X přímo` to `X nasazení`.
- Make random group assignment a single compact action above the team list:
  - fully assigned groups are reshuffled completely;
  - partially assigned groups keep existing assignments and fill the remaining places;
  - a confirmation modal explains which behavior will occur.
- Show an explicit notice when points cannot be assigned because no point system is selected; keep the action disabled.
- Show the corresponding notice in the discipline overview in the same summary area as other discipline outcomes.
- Aggregate final-standing scores and subscores from all completed matches, including placement and knockout/final matches, rather than group matches only.
- Wrap long scheduled matchup names instead of truncating meaningful group/stage information.

## Important implementation details

- `PhaseSetupService` owns the revised group-assignment and played-set validation behavior.
- `GroupStandingsService.ApplyAllMatchAggregates` augments final standings with every completed match while preserving the decided placement order.
- Competitor removal availability is exposed before submission through `ICompetitorAdministrationService` and enforced in the UI as well as the service layer.
- Responsive changes are concentrated in `wwwroot/css/site.css`; the desktop phase heading uses an explicit full-width grid track so its two action rows can align to the right edge.

## Validation

- `dotnet test Competition.Tests/Competition.Tests.csproj -c Release --no-restore`
- 156 tests passed after the final CSS-only heading adjustment.
- `git diff --check`

## Deployment status

These changes have not been deployed from this branch.

The local untracked `deployment/database-backups/` directory is intentionally excluded from source control and from the pull request.
