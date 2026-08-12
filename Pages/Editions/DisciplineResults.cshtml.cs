using Competition.Services;

namespace Competition.Pages.Editions;

public sealed class DisciplineResultsModel(
    IPhaseSetupService phases,
    IGroupStandingsService groupStandings,
    ICompetitionScoringService? scoring = null)
    : DisciplinePhasesModel(phases, groupStandings, scoring)
{
    public override bool IsResultsPage => true;
}
