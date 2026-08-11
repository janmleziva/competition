using Competition.Domain;

namespace Competition.Services;

public static class TeamNameFormatter
{
    public static IReadOnlyDictionary<long, string> CreateEntryLabels(IEnumerable<CompetitionEntry> entries)
    {
        var participants = entries
            .Where(entry => entry.Competitor is not null)
            .GroupBy(entry => entry.Id)
            .Select(group => group.First())
            .ToList();
        var result = new Dictionary<long, string>();

        foreach (var surnameGroup in participants.GroupBy(
                     entry => entry.Competitor.LastName.Trim(),
                     StringComparer.CurrentCultureIgnoreCase))
        {
            var members = surnameGroup.ToList();
            foreach (var entry in members)
            {
                var firstName = entry.Competitor.FirstName.Trim();
                var prefixLength = FindUniquePrefixLength(entry, members);
                var prefix = firstName.Length == 0
                    ? string.Empty
                    : firstName[..Math.Min(prefixLength, firstName.Length)];
                result[entry.Id] = prefix.Length == 0
                    ? entry.Competitor.LastName.Trim()
                    : $"{entry.Competitor.LastName.Trim()} {prefix}.";
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<long, string> CreateEntryLabels(IEnumerable<DisciplineTeam> teams) =>
        CreateEntryLabels(teams.SelectMany(team => team.Members).Select(member => member.CompetitionEntry));

    public static string Format(
        DisciplineTeam team,
        IReadOnlyDictionary<long, string> entryLabels) =>
        string.Join("/", team.Members
            .OrderBy(member => member.Order)
            .Select(member => entryLabels.GetValueOrDefault(
                member.CompetitionEntryId,
                Fallback(member))));

    private static int FindUniquePrefixLength(CompetitionEntry entry, IReadOnlyList<CompetitionEntry> surnameGroup)
    {
        var firstName = entry.Competitor.FirstName.Trim();
        if (firstName.Length == 0)
        {
            return 0;
        }

        var maxLength = surnameGroup
            .Select(item => item.Competitor.FirstName.Trim().Length)
            .DefaultIfEmpty(1)
            .Max();
        for (var length = 1; length <= maxLength; length++)
        {
            var prefix = Prefix(firstName, length);
            if (surnameGroup.Where(item => item.Id != entry.Id).All(item =>
                    !string.Equals(prefix, Prefix(item.Competitor.FirstName.Trim(), length),
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                return length;
            }
        }

        return firstName.Length;
    }

    private static string Prefix(string value, int length) =>
        value[..Math.Min(value.Length, length)];

    private static string Fallback(DisciplineTeamMember member)
    {
        var competitor = member.CompetitionEntry?.Competitor;
        if (competitor is null)
        {
            return $"#{member.CompetitionEntryId}";
        }

        var firstName = competitor.FirstName.Trim();
        return firstName.Length == 0
            ? competitor.LastName.Trim()
            : $"{competitor.LastName.Trim()} {firstName[0]}.";
    }
}
