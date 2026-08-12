using Competition.Data;
using Competition.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

const string defaultSource =
    "Server=(localdb)\\MSSQLLocalDB;Database=CompetitionDev;Trusted_Connection=True;TrustServerCertificate=True;";

var arguments = args.ToList();
var sourceConnectionString = GetOption(arguments, "--source") ?? defaultSource;
var destinationPath = Path.GetFullPath(
    GetOption(arguments, "--destination") ?? Path.Combine("App_Data", "competition.db"));
var overwrite = arguments.Contains("--overwrite", StringComparer.OrdinalIgnoreCase);

if (File.Exists(destinationPath) && !overwrite)
{
    Console.Error.WriteLine($"Destination already exists: {destinationPath}");
    Console.Error.WriteLine("Pass --overwrite to replace it after creating a recoverable backup.");
    return 2;
}

Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
var temporaryPath = $"{destinationPath}.new-{Guid.NewGuid():N}";

try
{
    var sourceOptions = new DbContextOptionsBuilder<CompetitionDbContext>()
        .UseSqlServer(sourceConnectionString)
        .Options;
    var destinationOptions = new DbContextOptionsBuilder<CompetitionDbContext>()
        .UseSqlite($"Data Source={temporaryPath};Foreign Keys=True;Default Timeout=10")
        .Options;

    await using var source = new CompetitionDbContext(sourceOptions);
    await source.Database.OpenConnectionAsync();

    await using (var destination = new CompetitionDbContext(destinationOptions))
    {
        if (!await destination.Database.EnsureCreatedAsync())
        {
            throw new InvalidOperationException("The temporary SQLite database could not be created.");
        }

        await using var transaction = await destination.Database.BeginTransactionAsync();

        await CopyAsync<AwardPointSystem>(source, destination);
        await CopyAsync<CompetitionEdition>(source, destination);
        await CopyAsync<Competitor>(source, destination);
        await CopyAsync<Discipline>(source, destination);
        await CopyAsync<CompetitionEntry>(source, destination);
        await CopyAsync<CompetitionDiscipline>(source, destination);
        await CopyAsync<RankingPointRule>(source, destination);
        await CopyAsync<DisciplineParticipantAssignment>(source, destination);
        await CopyAsync<DisciplineTeam>(source, destination);
        await CopyAsync<DisciplineTeamMember>(source, destination);
        await CopyAsync<DisciplinePhase>(source, destination);
        await CopyAsync<PhaseGroup>(source, destination);
        await CopyAsync<PhaseGroupTeam>(source, destination);
        await CopyAsync<Match>(source, destination);
        await CopyAsync<MatchSetScore>(source, destination);
        await CopyAsync<DisciplineStanding>(source, destination);

        await transaction.CommitAsync();
        await VerifyCountsAsync(source, destination);
        await destination.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
    }

    SqliteConnection.ClearAllPools();

    if (File.Exists(destinationPath))
    {
        var backupPath = $"{destinationPath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(destinationPath, backupPath);
        MoveSidecarToBackup(destinationPath, backupPath, "-wal");
        MoveSidecarToBackup(destinationPath, backupPath, "-shm");
        Console.WriteLine($"Previous SQLite database backed up to {backupPath}");
    }

    File.Move(temporaryPath, destinationPath);
    Console.WriteLine($"Converted database written to {destinationPath}");
    return 0;
}
catch
{
    SqliteConnection.ClearAllPools();

    if (File.Exists(temporaryPath))
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
            Console.Error.WriteLine($"Temporary database remains at {temporaryPath}");
        }
    }

    TryDeleteTemporarySidecar(temporaryPath, "-wal");
    TryDeleteTemporarySidecar(temporaryPath, "-shm");

    throw;
}

static async Task CopyAsync<TEntity>(CompetitionDbContext source, CompetitionDbContext destination)
    where TEntity : class
{
    var rows = await source.Set<TEntity>().AsNoTracking().ToListAsync();
    await destination.Set<TEntity>().AddRangeAsync(rows);
    await destination.SaveChangesAsync();
    destination.ChangeTracker.Clear();
    Console.WriteLine($"Copied {rows.Count,4} {typeof(TEntity).Name} row(s).");
}

static async Task VerifyCountsAsync(CompetitionDbContext source, CompetitionDbContext destination)
{
    await VerifyCountAsync<AwardPointSystem>(source, destination);
    await VerifyCountAsync<CompetitionEdition>(source, destination);
    await VerifyCountAsync<Competitor>(source, destination);
    await VerifyCountAsync<Discipline>(source, destination);
    await VerifyCountAsync<CompetitionEntry>(source, destination);
    await VerifyCountAsync<CompetitionDiscipline>(source, destination);
    await VerifyCountAsync<RankingPointRule>(source, destination);
    await VerifyCountAsync<DisciplineParticipantAssignment>(source, destination);
    await VerifyCountAsync<DisciplineTeam>(source, destination);
    await VerifyCountAsync<DisciplineTeamMember>(source, destination);
    await VerifyCountAsync<DisciplinePhase>(source, destination);
    await VerifyCountAsync<PhaseGroup>(source, destination);
    await VerifyCountAsync<PhaseGroupTeam>(source, destination);
    await VerifyCountAsync<Match>(source, destination);
    await VerifyCountAsync<MatchSetScore>(source, destination);
    await VerifyCountAsync<DisciplineStanding>(source, destination);
}

static async Task VerifyCountAsync<TEntity>(CompetitionDbContext source, CompetitionDbContext destination)
    where TEntity : class
{
    var sourceCount = await source.Set<TEntity>().LongCountAsync();
    var destinationCount = await destination.Set<TEntity>().LongCountAsync();
    if (sourceCount != destinationCount)
    {
        throw new InvalidOperationException(
            $"Count mismatch for {typeof(TEntity).Name}: SQL Server={sourceCount}, SQLite={destinationCount}.");
    }
}

static string? GetOption(IReadOnlyList<string> arguments, string name)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }

    return null;
}

static void MoveSidecarToBackup(string databasePath, string backupPath, string suffix)
{
    var sidecarPath = databasePath + suffix;
    if (File.Exists(sidecarPath))
    {
        File.Move(sidecarPath, backupPath + suffix);
    }
}

static void TryDeleteTemporarySidecar(string databasePath, string suffix)
{
    try
    {
        File.Delete(databasePath + suffix);
    }
    catch (IOException)
    {
        Console.Error.WriteLine($"Temporary SQLite sidecar remains at {databasePath + suffix}");
    }
}
