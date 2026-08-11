# Competition application implementation plan

## Domain and persistence decisions

- A `CompetitionEdition` is one annual event and owns its dates, name, city, registered competitors, and configured disciplines. Each configured discipline declares its required team size; individual disciplines use `1`, while the expected pair disciplines use `2`.
- `Competitor` and `Discipline` are reusable catalog records. `CompetitionEntry` registers a person in one edition. A `DisciplineTeam` is the playing participant in one discipline and has one or more ordered `DisciplineTeamMember` records pointing to edition entries. Solo disciplines therefore use a one-person team instead of a separate data path.
- Teams are formed independently for every discipline, so the same competitor may have different teammates in each discipline. A competitor may belong to at most one team within a discipline.
- Team names are derived, never stored: ordered members' last names are joined with `/` (for example, `Smith/Jones`). Changing a competitor's last name or a team's membership therefore cannot leave a stale team name in the database.
- A competition discipline has an ordered set of phases. A phase may contain groups and matches; classification matches such as the final, bronze-medal match, or fifth-place match are ordinary named matches in a final-standing phase.
- Group membership is explicit at team level, so a standings table can show a team before it plays a match.
- A match stores its primary score. `MatchSetScore` stores optional ordered component scores, such as beach-volleyball set results.
- Group standings are calculated from completed matches, not persisted. This avoids stale wins, draws, losses, table points, scores for/against, and score difference after anonymous result edits.
- Final discipline team rankings are persisted in `DisciplineStanding`. `PointsAwarded` is a per-member snapshot of the applicable `RankingPointRule`, preserving historical totals if rules are later edited. Every member receives the full value; points are not divided by team size.
- Overall edition standings are individual. They are calculated by joining each finalized team standing to all team members and summing that team's full `PointsAwarded` value per `CompetitionEntry`.
- Match `Version` is an optimistic-concurrency token. Result forms must submit it and handle `DbUpdateConcurrencyException`, preventing one anonymous editor from silently overwriting another.

## Database structure

1. `CompetitionEditions` -> event identity and inclusive date range.
2. `Competitors` -> reusable person record with first name, last name, and optional date of birth.
3. `CompetitionEntries` -> competitor registration and edition seed.
4. `Disciplines` -> reusable discipline name.
5. `CompetitionDisciplines` -> edition-specific discipline, order, playing-system type, and required team size.
6. `DisciplineTeams` -> discipline-specific playing participants and their seeds; every solo entrant is represented as a one-member team.
7. `DisciplineTeamMembers` -> ordered links from a team to edition competitors, used to derive the `/`-separated team name.
8. `DisciplinePhases` -> ordered group, knockout, or final-standing phases and their win/draw/loss table-point rules.
9. `PhaseGroups` and `PhaseGroupTeams` -> named groups and explicit team assignments.
10. `Matches` -> phase/group, two nullable teams (to allow an undecided bracket), label, order, state, primary score, and concurrency metadata.
11. `MatchSetScores` -> ordered subscores belonging to a match.
12. `RankingPointRules` -> edition-discipline mapping from final team rank to full points awarded to each member.
13. `DisciplineStandings` -> finalized team rank and per-member awarded-points snapshot.

Database checks cover valid date ranges, positive ordering/seeding/ranks, nonnegative points and scores, paired match scores, completed matches requiring a score, and a team not playing itself. Unique indexes protect registrations, team membership (one team per person per discipline), member order, assignments, ordering, and point-rule positions. Cross-aggregate rules such as “both match teams belong to the match discipline” and “all team members are registered in the discipline's edition” remain service-level validation where enforcing them would require further duplicated parent keys.

## Incremental delivery plan

### Step 1 - Persistence foundation (completed)

- Add EF Core SQL Server runtime and design-time tooling.
- Define the domain entities, relationships, indexes, constraints, and enum conversions.
- Produce a rerunnable MSSQL `001_initial_schema.sql` script for host-side execution.
- Remove automatic startup migration so application startup no longer depends on direct SQL connectivity from the developer machine.
- Verify a clean build, script/model alignment, and connection-string based startup configuration.

Acceptance: a clean checkout can run `dotnet build`; a host operator can apply `scripts/sql/001_initial_schema.sql` through the Forpsi MSSQL web interface; and the app can start once `ConnectionStrings:CompetitionDb` points at that prepared database.

### Step 2 - Edition administration (completed)

- Replace the placeholder CreateComp page with create/edit forms for name, city, start date, and end date.
- Add edition list/detail pages and choose the active edition explicitly rather than assuming the current year.
- Validate required text, `EndDate >= StartDate`, and duplicate submissions.
- Add service tests for create/update and Razor Page tests for validation.

Acceptance: an authenticated admin can create an edition spanning one or more days and reopen it for editing.

### Step 3 - Competitors and edition registration

- Add competitor catalog create/edit/search with first name, last name, and optional date of birth.
- Add/remove competitors from an edition and manage their unique edition seeds.
- Prevent removal once dependent match/results data exists; offer a clear validation message rather than a database error.
- Test duplicate registration, duplicate seed, and dependent-data behavior.

Acceptance: an admin can configure the usual 8-10 competitors once and reuse them in later editions.

### Step 4 - Discipline setup

- Add discipline catalog management.
- Attach ordered disciplines to an edition and choose a playing-system preset or custom system.
- Configure the required team size for each discipline; use two where appropriate and represent individual play as teams of one.
- Draw/form teams independently for each discipline, store member order, and assign discipline-specific team seeds.
- Display the derived team name as ordered member last names separated by `/`; never accept it as editable input.
- Prevent a competitor from appearing in two teams in the same discipline while allowing different teammates across disciplines.
- Allow a redraw only before dependent results exist. Once matches have results or standings are finalized, changing membership must require an explicit reset of that discipline so historical points cannot silently move to another person.
- Configure final-rank point rules and validate full coverage of the expected ranks.
- Test uniqueness and cross-edition/cross-discipline validation.

Acceptance: each edition has its ordered discipline list, independently formed teams, format, and per-member point allocation; changing teams in one discipline does not affect another.

### Step 5 - Phase, group, and fixture setup

- Build phase management for group, knockout, and final-standing phases.
- Configure group names/membership and phase table-point rules (defaults 2/1/0).
- Generate round-robin fixtures, including correct handling for an odd team count; allow manual fixture editing.
- Create playoff/classification match slots with nullable teams and later advancement assignment.
- Validate that groups and teams belong to the same discipline and that group matches use teams assigned to that group.
- Unit-test fixture generation for the expected team counts without duplicate pairings.

Acceptance: an admin can produce every match slot for a discipline before results are known.

### Step 6 - Public match views and anonymous result editing (completed)

- Add public edition/discipline pages, grouped by phase and group.
- Add an intentionally anonymous result-edit endpoint limited to score/status fields; do not expose general entity binding.
- Validate nonnegative scores, two distinct assigned teams, set numbering, and consistency between primary and set scores.
- Require the match concurrency `Version`; display a conflict and current result when another edit wins.
- Add antiforgery protection, rate limiting, structured result-change logging, and a configurable edit-open/edit-closed switch.
- Test authorization boundaries, over-posting resistance, concurrent edits, and invalid score combinations.

Acceptance: any visitor can edit an allowed match result without signing in, but cannot change its teams, phase, discipline, or other administration data.

### Step 7 - Group standings (completed)

- Implement a query service over completed group matches.
- Return played, wins, draws, losses, table points, score for, score against, and score difference for every assigned group team.
- Use this deterministic tie-break order: table points; points, score difference, and (when applicable) subscore difference in a mini-table among every team tied on table points; overall score difference; applicable overall subscore difference; score ratio; competition seed; then team ID as a stable final fallback.
- Show progressive final standings once completed group or knockout stages determine a team's final placement.
- Recalculate on every read at this scale; add caching only if measurements justify it.
- Test empty groups, draws, incomplete matches, tied tables, edits, and the default 2/1/0 calculation.

Acceptance: standings immediately reflect every result edit and include teams with zero matches.

### Step 8 - Progression and discipline finalization

- Add explicit advancement/placement commands that assign teams to playoff and classification slots.
- Present a proposed final ranking, allow an admin to adjust it, then finalize it transactionally.
- Copy the full per-member points from `RankingPointRule` into `DisciplineStanding.PointsAwarded`; reject missing/duplicate teams, teams without members, and ranks without a rule.
- Define reopening behavior: reopening removes or supersedes the finalized standings in one transaction; finalized team membership remains immutable unless the discipline is explicitly reset.
- Test bracket advancement, finalization, rollback on invalid data, and reopening.

Acceptance: every discipline produces one durable final rank and full per-member awarded-points value per team.

### Step 9 - Overall edition standings

- Expand each finalized team standing to its members and sum the full awarded points by edition entry with one projection query; never divide points by member count.
- Show each individual's team and points per discipline plus total individual points, with a clear indicator for disciplines not yet finalized.
- Agree on total-standing tie behavior; initially show equal totals as tied and use competitor name only for display ordering.
- Test differently composed teams across disciplines, one-person teams, partially completed editions, nonparticipants, ties, and amended discipline results. Explicitly assert that both members of a two-person team receive the full points.

Acceptance: every competitor's edition total reconciles exactly with the sum of that individual's full team points across disciplines.

### Step 10 - Operational hardening and deployment

- Add realistic seed data for local development only.
- Add database backup/restore instructions for the SQL Server host.
- Verify SQL Server connectivity, migration permissions, and production connection-string management.
- Add migration checks to CI, integration tests against a temporary SQL Server instance, health checks, and production-safe error handling.
- Move the temporary plaintext admin credential to environment/host secrets before production use.

Acceptance: deployment preserves data, migrations are repeatable, backups are tested, and secrets are absent from committed production configuration.

## Decisions needed before their implementation step

- Exact group tie-break rules.
- Whether a discipline rank may be tied; the schema currently permits tied ranks.
- Whether all competitors must participate in every discipline.
- Whether primary set-based scores must always equal the number of sets won, or may be manually overridden.
- How playoff advancement is determined for each playing-system preset.
- When anonymous editing opens/closes and whether a lightweight edit PIN is desirable later.
