# Handoff

## Summary

This change set fixes bonus-rule persistence, improves the discipline workflow on responsive layouts, and makes set-score entry safer and more convenient.

## Changes

- Synchronize bonus-point rules in place so SQLite does not hit the unique index when an existing rule is saved again.
- Add SQLite regression coverage for updating an existing bonus rule.
- Keep navigation buttons beside page titles on narrow screens.
- Repair knockout matchup layout across desktop, tablet, and mobile widths.
- Hide result and set-score controls on the schedule setup page.
- Rename knockout-stage pills from `X přímo` to `X nasazení`.
- Close the random-team confirmation modal after successful reassignment.
- Keep the set-score modal open on validation errors, display the error inside it, and preserve submitted values.
- Derive and save a missing main match score from decisive set results.
- Reject sets played after the configured winning-set threshold has already been reached.
- Propagate automatically derived knockout winners to dependent matches.
- Replace the deployment script's unavailable `Get-FileHash` dependency with a compatible .NET SHA-256 implementation.

## Validation

- `dotnet test Competition.sln -c Release --no-restore`
- 142 tests passed.
- `git diff --check`

## Deployment status

The initial bonus-rule fix and deployment-script compatibility change were deployed successfully to `/subdoms/pohoda-cup`. The later UI and scoring changes in this PR have not been deployed yet.
