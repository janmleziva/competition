SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CompetitionEditions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionEditions
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        City NVARCHAR(120) NOT NULL,
        StartDate DATE NOT NULL,
        EndDate DATE NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_CompetitionEditions_IsActive DEFAULT 0,
        CreationToken UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_CompetitionEditions_CreationToken DEFAULT NEWID(),
        CONSTRAINT PK_CompetitionEditions PRIMARY KEY (Id),
        CONSTRAINT CK_CompetitionEditions_DateRange CHECK (EndDate >= StartDate)
    );
END;

IF COL_LENGTH(N'dbo.CompetitionEditions', N'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionEditions
    ADD IsActive BIT NOT NULL CONSTRAINT DF_CompetitionEditions_IsActive DEFAULT 0 WITH VALUES;
END;

IF COL_LENGTH(N'dbo.CompetitionEditions', N'CreationToken') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionEditions
    ADD CreationToken UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_CompetitionEditions_CreationToken DEFAULT NEWID() WITH VALUES;
END;

IF OBJECT_ID(N'dbo.Competitors', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Competitors
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        FirstName NVARCHAR(100) NOT NULL,
        LastName NVARCHAR(100) NOT NULL,
        DateOfBirth DATE NULL,
        CONSTRAINT PK_Competitors PRIMARY KEY (Id)
    );
END;

IF OBJECT_ID(N'dbo.Disciplines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Disciplines
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        Name NVARCHAR(120) NOT NULL,
        CONSTRAINT PK_Disciplines PRIMARY KEY (Id)
    );
END;

IF OBJECT_ID(N'dbo.CompetitionEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionEntries
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionEditionId BIGINT NOT NULL,
        CompetitorId BIGINT NOT NULL,
        Seed INT NOT NULL,
        CONSTRAINT PK_CompetitionEntries PRIMARY KEY (Id),
        CONSTRAINT CK_CompetitionEntries_Seed CHECK (Seed > 0)
    );
END;

IF OBJECT_ID(N'dbo.AwardPointSystems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AwardPointSystems
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        Name NVARCHAR(120) NOT NULL,
        CONSTRAINT PK_AwardPointSystems PRIMARY KEY (Id)
    );
END;

IF OBJECT_ID(N'dbo.CompetitionDisciplines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionDisciplines
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionEditionId BIGINT NOT NULL,
        DisciplineId BIGINT NOT NULL,
        PlayingSystem NVARCHAR(40) NOT NULL,
        TeamSize INT NOT NULL,
        [Order] INT NOT NULL,
        ScheduledAt DATETIME2 NULL,
        UsesSetScores BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_UsesSetScores DEFAULT (0),
        SetsToWin INT NULL,
        [Description] NVARCHAR(2000) NULL,
        IsLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsLocked DEFAULT (0),
        IsScheduleLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsScheduleLocked DEFAULT (0),
        AreResultsLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_AreResultsLocked DEFAULT (0),
        IsClosed BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsClosed DEFAULT (0),
        ClosedAtUtc DATETIME2 NULL,
        AwardPointSystemId BIGINT NULL,
        CONSTRAINT PK_CompetitionDisciplines PRIMARY KEY (Id),
        CONSTRAINT CK_CompetitionDisciplines_Order CHECK ([Order] > 0),
        CONSTRAINT CK_CompetitionDisciplines_TeamSize CHECK (TeamSize > 0),
        CONSTRAINT CK_CompetitionDisciplines_SetsToWin CHECK (SetsToWin IS NULL OR SetsToWin > 0)
    );
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'ScheduledAt') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines ADD ScheduledAt DATETIME2 NULL;
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'UsesSetScores') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD UsesSetScores BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_UsesSetScores DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'IsScheduleLocked') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD IsScheduleLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsScheduleLocked DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'AreResultsLocked') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD AreResultsLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_AreResultsLocked DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'SetsToWin') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines ADD SetsToWin INT NULL;
    UPDATE dbo.CompetitionDisciplines SET SetsToWin = 2 WHERE UsesSetScores = 1;
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines ADD [Description] NVARCHAR(2000) NULL;
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'IsLocked') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD IsLocked BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsLocked DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'IsClosed') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD IsClosed BIT NOT NULL CONSTRAINT DF_CompetitionDisciplines_IsClosed DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'ClosedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines ADD ClosedAtUtc DATETIME2 NULL;
END;

IF COL_LENGTH(N'dbo.CompetitionDisciplines', N'AwardPointSystemId') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines ADD AwardPointSystemId BIGINT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_CompetitionDisciplines_SetsToWin')
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
        ADD CONSTRAINT CK_CompetitionDisciplines_SetsToWin CHECK (SetsToWin IS NULL OR SetsToWin > 0);
END;

IF OBJECT_ID(N'dbo.DisciplinePhases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DisciplinePhases
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionDisciplineId BIGINT NOT NULL,
        Name NVARCHAR(120) NOT NULL,
        [Type] NVARCHAR(30) NOT NULL,
        [Order] INT NOT NULL,
        PointsForWin INT NOT NULL,
        PointsForDraw INT NOT NULL,
        PointsForLoss INT NOT NULL,
        CONSTRAINT PK_DisciplinePhases PRIMARY KEY (Id),
        CONSTRAINT CK_DisciplinePhases_Order CHECK ([Order] > 0),
        CONSTRAINT CK_DisciplinePhases_Points CHECK (PointsForWin >= 0 AND PointsForDraw >= 0 AND PointsForLoss >= 0)
    );
END;

IF OBJECT_ID(N'dbo.DisciplineTeams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DisciplineTeams
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionDisciplineId BIGINT NOT NULL,
        Seed INT NOT NULL,
        CONSTRAINT PK_DisciplineTeams PRIMARY KEY (Id),
        CONSTRAINT CK_DisciplineTeams_Seed CHECK (Seed > 0)
    );
END;

IF OBJECT_ID(N'dbo.RankingPointRules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RankingPointRules
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionDisciplineId BIGINT NOT NULL,
        [Rank] INT NOT NULL,
        Points INT NOT NULL,
        CONSTRAINT PK_RankingPointRules PRIMARY KEY (Id),
        CONSTRAINT CK_RankingPointRules_Rank CHECK ([Rank] > 0),
        CONSTRAINT CK_RankingPointRules_Points CHECK (Points >= 0)
    );
END;

IF OBJECT_ID(N'dbo.PhaseGroups', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PhaseGroups
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        DisciplinePhaseId BIGINT NOT NULL,
        Name NVARCHAR(80) NOT NULL,
        [Order] INT NOT NULL,
        CONSTRAINT PK_PhaseGroups PRIMARY KEY (Id),
        CONSTRAINT CK_PhaseGroups_Order CHECK ([Order] > 0)
    );
END;

IF OBJECT_ID(N'dbo.DisciplineStandings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DisciplineStandings
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionDisciplineId BIGINT NOT NULL,
        DisciplineTeamId BIGINT NOT NULL,
        [Rank] INT NOT NULL,
        PointsAwarded INT NOT NULL,
        CONSTRAINT PK_DisciplineStandings PRIMARY KEY (Id),
        CONSTRAINT CK_DisciplineStandings_Rank CHECK ([Rank] > 0),
        CONSTRAINT CK_DisciplineStandings_Points CHECK (PointsAwarded >= 0)
    );
END;

IF OBJECT_ID(N'dbo.DisciplineTeamMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DisciplineTeamMembers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        CompetitionDisciplineId BIGINT NOT NULL,
        DisciplineTeamId BIGINT NOT NULL,
        CompetitionEntryId BIGINT NOT NULL,
        [Order] INT NOT NULL,
        CONSTRAINT PK_DisciplineTeamMembers PRIMARY KEY (Id),
        CONSTRAINT CK_DisciplineTeamMembers_Order CHECK ([Order] > 0)
    );
END;

IF OBJECT_ID(N'dbo.DisciplineParticipantAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DisciplineParticipantAssignments
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        AwardPointSystemId BIGINT NOT NULL,
        CompetitionEntryId BIGINT NOT NULL,
        CONSTRAINT PK_DisciplineParticipantAssignments PRIMARY KEY (Id)
    );
END;

IF COL_LENGTH(N'dbo.RankingPointRules', N'AwardPointSystemId') IS NULL
   AND COL_LENGTH(N'dbo.RankingPointRules', N'CompetitionDisciplineId') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId')
        ALTER TABLE dbo.RankingPointRules DROP CONSTRAINT FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RankingPointRules_CompetitionDisciplineId_Rank' AND object_id = OBJECT_ID(N'dbo.RankingPointRules'))
        DROP INDEX IX_RankingPointRules_CompetitionDisciplineId_Rank ON dbo.RankingPointRules;

    EXEC sp_rename N'dbo.RankingPointRules.CompetitionDisciplineId', N'AwardPointSystemId', N'COLUMN';

    INSERT INTO dbo.AwardPointSystems (Name)
    SELECT DISTINCT CONCAT(N'Migrated point system #', AwardPointSystemId)
    FROM dbo.RankingPointRules;

    UPDATE discipline
    SET AwardPointSystemId = system.Id
    FROM dbo.CompetitionDisciplines discipline
    INNER JOIN dbo.AwardPointSystems system
        ON system.Name = CONCAT(N'Migrated point system #', discipline.Id)
    WHERE discipline.AwardPointSystemId IS NULL;

    UPDATE pointRule
    SET AwardPointSystemId = system.Id
    FROM dbo.RankingPointRules pointRule
    INNER JOIN dbo.AwardPointSystems system
        ON system.Name = CONCAT(N'Migrated point system #', pointRule.AwardPointSystemId);
END;

IF OBJECT_ID(N'dbo.Matches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Matches
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        DisciplinePhaseId BIGINT NOT NULL,
        PhaseGroupId BIGINT NULL,
        HomeTeamId BIGINT NULL,
        AwayTeamId BIGINT NULL,
        Name NVARCHAR(120) NOT NULL,
        [Order] INT NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        HomeScore INT NULL,
        AwayScore INT NULL,
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Matches_UpdatedAtUtc DEFAULT CURRENT_TIMESTAMP,
        Version INT NOT NULL,
        CONSTRAINT PK_Matches PRIMARY KEY (Id),
        CONSTRAINT CK_Matches_Order CHECK ([Order] > 0),
        CONSTRAINT CK_Matches_DifferentTeams CHECK (HomeTeamId IS NULL OR AwayTeamId IS NULL OR HomeTeamId <> AwayTeamId),
        CONSTRAINT CK_Matches_Score CHECK ((HomeScore IS NULL AND AwayScore IS NULL) OR (HomeScore >= 0 AND AwayScore >= 0)),
        CONSTRAINT CK_Matches_CompletedHasScore CHECK (Status <> 'Completed' OR (HomeScore IS NOT NULL AND AwayScore IS NOT NULL))
    );
END;

IF OBJECT_ID(N'dbo.PhaseGroupTeams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PhaseGroupTeams
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        DisciplinePhaseId BIGINT NOT NULL,
        PhaseGroupId BIGINT NOT NULL,
        DisciplineTeamId BIGINT NOT NULL,
        Seed INT NOT NULL,
        CONSTRAINT PK_PhaseGroupTeams PRIMARY KEY (Id),
        CONSTRAINT CK_PhaseGroupTeams_Seed CHECK (Seed > 0)
    );
END;

IF OBJECT_ID(N'dbo.MatchSetScores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MatchSetScores
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        MatchId BIGINT NOT NULL,
        SetNumber INT NOT NULL,
        HomeScore INT NOT NULL,
        AwayScore INT NOT NULL,
        CONSTRAINT PK_MatchSetScores PRIMARY KEY (Id),
        CONSTRAINT CK_MatchSetScores_SetNumber CHECK (SetNumber > 0),
        CONSTRAINT CK_MatchSetScores_Score CHECK (HomeScore >= 0 AND AwayScore >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'AK_DisciplineTeams_CompetitionDisciplineId_Id')
BEGIN
    ALTER TABLE dbo.DisciplineTeams
    ADD CONSTRAINT AK_DisciplineTeams_CompetitionDisciplineId_Id UNIQUE (CompetitionDisciplineId, Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'AK_PhaseGroups_DisciplinePhaseId_Id')
BEGIN
    ALTER TABLE dbo.PhaseGroups
    ADD CONSTRAINT AK_PhaseGroups_DisciplinePhaseId_Id UNIQUE (DisciplinePhaseId, Id);
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionEntries_CompetitionEditions_CompetitionEditionId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.CompetitionEntries DROP CONSTRAINT FK_CompetitionEntries_CompetitionEditions_CompetitionEditionId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionDisciplines_CompetitionEditions_CompetitionEditionId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines DROP CONSTRAINT FK_CompetitionDisciplines_CompetitionEditions_CompetitionEditionId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplinePhases_CompetitionDisciplines_CompetitionDisciplineId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.DisciplinePhases DROP CONSTRAINT FK_DisciplinePhases_CompetitionDisciplines_CompetitionDisciplineId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineTeams_CompetitionDisciplines_CompetitionDisciplineId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.DisciplineTeams DROP CONSTRAINT FK_DisciplineTeams_CompetitionDisciplines_CompetitionDisciplineId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.RankingPointRules DROP CONSTRAINT FK_RankingPointRules_CompetitionDisciplines_CompetitionDisciplineId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PhaseGroups_DisciplinePhases_DisciplinePhaseId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.PhaseGroups DROP CONSTRAINT FK_PhaseGroups_DisciplinePhases_DisciplinePhaseId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineStandings_CompetitionDisciplines_CompetitionDisciplineId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.DisciplineStandings DROP CONSTRAINT FK_DisciplineStandings_CompetitionDisciplines_CompetitionDisciplineId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineStandings_DisciplineTeams_DisciplineTeamId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.DisciplineStandings DROP CONSTRAINT FK_DisciplineStandings_DisciplineTeams_DisciplineTeamId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineTeamMembers_DisciplineTeams_CompetitionDisciplineId_DisciplineTeamId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.DisciplineTeamMembers DROP CONSTRAINT FK_DisciplineTeamMembers_DisciplineTeams_CompetitionDisciplineId_DisciplineTeamId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_DisciplinePhases_DisciplinePhaseId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.Matches DROP CONSTRAINT FK_Matches_DisciplinePhases_DisciplinePhaseId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PhaseGroupTeams_PhaseGroups_DisciplinePhaseId_PhaseGroupId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.PhaseGroupTeams DROP CONSTRAINT FK_PhaseGroupTeams_PhaseGroups_DisciplinePhaseId_PhaseGroupId;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MatchSetScores_Matches_MatchId' AND delete_referential_action_desc = N'CASCADE')
BEGIN
    ALTER TABLE dbo.MatchSetScores DROP CONSTRAINT FK_MatchSetScores_Matches_MatchId;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionEntries_CompetitionEditions_CompetitionEditionId')
BEGIN
    ALTER TABLE dbo.CompetitionEntries
    ADD CONSTRAINT FK_CompetitionEntries_CompetitionEditions_CompetitionEditionId
        FOREIGN KEY (CompetitionEditionId) REFERENCES dbo.CompetitionEditions (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionEntries_Competitors_CompetitorId')
BEGIN
    ALTER TABLE dbo.CompetitionEntries
    ADD CONSTRAINT FK_CompetitionEntries_Competitors_CompetitorId
        FOREIGN KEY (CompetitorId) REFERENCES dbo.Competitors (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionDisciplines_CompetitionEditions_CompetitionEditionId')
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
    ADD CONSTRAINT FK_CompetitionDisciplines_CompetitionEditions_CompetitionEditionId
        FOREIGN KEY (CompetitionEditionId) REFERENCES dbo.CompetitionEditions (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionDisciplines_Disciplines_DisciplineId')
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
    ADD CONSTRAINT FK_CompetitionDisciplines_Disciplines_DisciplineId
        FOREIGN KEY (DisciplineId) REFERENCES dbo.Disciplines (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CompetitionDisciplines_AwardPointSystems_AwardPointSystemId')
BEGIN
    ALTER TABLE dbo.CompetitionDisciplines
    ADD CONSTRAINT FK_CompetitionDisciplines_AwardPointSystems_AwardPointSystemId
        FOREIGN KEY (AwardPointSystemId) REFERENCES dbo.AwardPointSystems (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplinePhases_CompetitionDisciplines_CompetitionDisciplineId')
BEGIN
    ALTER TABLE dbo.DisciplinePhases
    ADD CONSTRAINT FK_DisciplinePhases_CompetitionDisciplines_CompetitionDisciplineId
        FOREIGN KEY (CompetitionDisciplineId) REFERENCES dbo.CompetitionDisciplines (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineTeams_CompetitionDisciplines_CompetitionDisciplineId')
BEGIN
    ALTER TABLE dbo.DisciplineTeams
    ADD CONSTRAINT FK_DisciplineTeams_CompetitionDisciplines_CompetitionDisciplineId
        FOREIGN KEY (CompetitionDisciplineId) REFERENCES dbo.CompetitionDisciplines (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_RankingPointRules_AwardPointSystems_AwardPointSystemId')
BEGIN
    ALTER TABLE dbo.RankingPointRules
    ADD CONSTRAINT FK_RankingPointRules_AwardPointSystems_AwardPointSystemId
        FOREIGN KEY (AwardPointSystemId) REFERENCES dbo.AwardPointSystems (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PhaseGroups_DisciplinePhases_DisciplinePhaseId')
BEGIN
    ALTER TABLE dbo.PhaseGroups
    ADD CONSTRAINT FK_PhaseGroups_DisciplinePhases_DisciplinePhaseId
        FOREIGN KEY (DisciplinePhaseId) REFERENCES dbo.DisciplinePhases (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineStandings_CompetitionDisciplines_CompetitionDisciplineId')
BEGIN
    ALTER TABLE dbo.DisciplineStandings
    ADD CONSTRAINT FK_DisciplineStandings_CompetitionDisciplines_CompetitionDisciplineId
        FOREIGN KEY (CompetitionDisciplineId) REFERENCES dbo.CompetitionDisciplines (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineStandings_DisciplineTeams_DisciplineTeamId')
BEGIN
    ALTER TABLE dbo.DisciplineStandings
    ADD CONSTRAINT FK_DisciplineStandings_DisciplineTeams_DisciplineTeamId
        FOREIGN KEY (DisciplineTeamId) REFERENCES dbo.DisciplineTeams (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineTeamMembers_CompetitionEntries_CompetitionEntryId')
BEGIN
    ALTER TABLE dbo.DisciplineTeamMembers
    ADD CONSTRAINT FK_DisciplineTeamMembers_CompetitionEntries_CompetitionEntryId
        FOREIGN KEY (CompetitionEntryId) REFERENCES dbo.CompetitionEntries (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineParticipantAssignments_CompetitionDisciplines_CompetitionDisciplineId')
BEGIN
    ALTER TABLE dbo.DisciplineParticipantAssignments
    ADD CONSTRAINT FK_DisciplineParticipantAssignments_CompetitionDisciplines_CompetitionDisciplineId
        FOREIGN KEY (CompetitionDisciplineId) REFERENCES dbo.CompetitionDisciplines (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineParticipantAssignments_CompetitionEntries_CompetitionEntryId')
BEGIN
    ALTER TABLE dbo.DisciplineParticipantAssignments
    ADD CONSTRAINT FK_DisciplineParticipantAssignments_CompetitionEntries_CompetitionEntryId
        FOREIGN KEY (CompetitionEntryId) REFERENCES dbo.CompetitionEntries (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DisciplineTeamMembers_DisciplineTeams_CompetitionDisciplineId_DisciplineTeamId')
BEGIN
    ALTER TABLE dbo.DisciplineTeamMembers
    ADD CONSTRAINT FK_DisciplineTeamMembers_DisciplineTeams_CompetitionDisciplineId_DisciplineTeamId
        FOREIGN KEY (CompetitionDisciplineId, DisciplineTeamId)
        REFERENCES dbo.DisciplineTeams (CompetitionDisciplineId, Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_DisciplinePhases_DisciplinePhaseId')
BEGIN
    ALTER TABLE dbo.Matches
    ADD CONSTRAINT FK_Matches_DisciplinePhases_DisciplinePhaseId
        FOREIGN KEY (DisciplinePhaseId) REFERENCES dbo.DisciplinePhases (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_DisciplineTeams_AwayTeamId')
BEGIN
    ALTER TABLE dbo.Matches
    ADD CONSTRAINT FK_Matches_DisciplineTeams_AwayTeamId
        FOREIGN KEY (AwayTeamId) REFERENCES dbo.DisciplineTeams (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_DisciplineTeams_HomeTeamId')
BEGIN
    ALTER TABLE dbo.Matches
    ADD CONSTRAINT FK_Matches_DisciplineTeams_HomeTeamId
        FOREIGN KEY (HomeTeamId) REFERENCES dbo.DisciplineTeams (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_PhaseGroups_DisciplinePhaseId_PhaseGroupId')
BEGIN
    ALTER TABLE dbo.Matches
    ADD CONSTRAINT FK_Matches_PhaseGroups_DisciplinePhaseId_PhaseGroupId
        FOREIGN KEY (DisciplinePhaseId, PhaseGroupId)
        REFERENCES dbo.PhaseGroups (DisciplinePhaseId, Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PhaseGroupTeams_DisciplineTeams_DisciplineTeamId')
BEGIN
    ALTER TABLE dbo.PhaseGroupTeams
    ADD CONSTRAINT FK_PhaseGroupTeams_DisciplineTeams_DisciplineTeamId
        FOREIGN KEY (DisciplineTeamId) REFERENCES dbo.DisciplineTeams (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_PhaseGroupTeams_PhaseGroups_DisciplinePhaseId_PhaseGroupId')
BEGIN
    ALTER TABLE dbo.PhaseGroupTeams
    ADD CONSTRAINT FK_PhaseGroupTeams_PhaseGroups_DisciplinePhaseId_PhaseGroupId
        FOREIGN KEY (DisciplinePhaseId, PhaseGroupId)
        REFERENCES dbo.PhaseGroups (DisciplinePhaseId, Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MatchSetScores_Matches_MatchId')
BEGIN
    ALTER TABLE dbo.MatchSetScores
    ADD CONSTRAINT FK_MatchSetScores_Matches_MatchId
        FOREIGN KEY (MatchId) REFERENCES dbo.Matches (Id);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionDisciplines_CompetitionEditionId_DisciplineId' AND object_id = OBJECT_ID(N'dbo.CompetitionDisciplines'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionDisciplines_CompetitionEditionId_DisciplineId
        ON dbo.CompetitionDisciplines (CompetitionEditionId, DisciplineId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionEditions_CreationToken' AND object_id = OBJECT_ID(N'dbo.CompetitionEditions'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionEditions_CreationToken
        ON dbo.CompetitionEditions (CreationToken);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionEditions_IsActive' AND object_id = OBJECT_ID(N'dbo.CompetitionEditions'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionEditions_IsActive
        ON dbo.CompetitionEditions (IsActive)
        WHERE IsActive = 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionDisciplines_CompetitionEditionId_Order' AND object_id = OBJECT_ID(N'dbo.CompetitionDisciplines'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionDisciplines_CompetitionEditionId_Order
        ON dbo.CompetitionDisciplines (CompetitionEditionId, [Order]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionDisciplines_DisciplineId' AND object_id = OBJECT_ID(N'dbo.CompetitionDisciplines'))
BEGIN
    CREATE INDEX IX_CompetitionDisciplines_DisciplineId
        ON dbo.CompetitionDisciplines (DisciplineId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionEntries_CompetitionEditionId_CompetitorId' AND object_id = OBJECT_ID(N'dbo.CompetitionEntries'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionEntries_CompetitionEditionId_CompetitorId
        ON dbo.CompetitionEntries (CompetitionEditionId, CompetitorId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionEntries_CompetitionEditionId_Seed' AND object_id = OBJECT_ID(N'dbo.CompetitionEntries'))
BEGIN
    CREATE UNIQUE INDEX IX_CompetitionEntries_CompetitionEditionId_Seed
        ON dbo.CompetitionEntries (CompetitionEditionId, Seed);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionEntries_CompetitorId' AND object_id = OBJECT_ID(N'dbo.CompetitionEntries'))
BEGIN
    CREATE INDEX IX_CompetitionEntries_CompetitorId
        ON dbo.CompetitionEntries (CompetitorId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Competitors_LastName_FirstName' AND object_id = OBJECT_ID(N'dbo.Competitors'))
BEGIN
    CREATE INDEX IX_Competitors_LastName_FirstName
        ON dbo.Competitors (LastName, FirstName);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplinePhases_CompetitionDisciplineId_Order' AND object_id = OBJECT_ID(N'dbo.DisciplinePhases'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplinePhases_CompetitionDisciplineId_Order
        ON dbo.DisciplinePhases (CompetitionDisciplineId, [Order]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Disciplines_Name' AND object_id = OBJECT_ID(N'dbo.Disciplines'))
BEGIN
    CREATE UNIQUE INDEX IX_Disciplines_Name
        ON dbo.Disciplines (Name);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineStandings_CompetitionDisciplineId_DisciplineTeamId' AND object_id = OBJECT_ID(N'dbo.DisciplineStandings'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplineStandings_CompetitionDisciplineId_DisciplineTeamId
        ON dbo.DisciplineStandings (CompetitionDisciplineId, DisciplineTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineStandings_CompetitionDisciplineId_Rank' AND object_id = OBJECT_ID(N'dbo.DisciplineStandings'))
BEGIN
    CREATE INDEX IX_DisciplineStandings_CompetitionDisciplineId_Rank
        ON dbo.DisciplineStandings (CompetitionDisciplineId, [Rank]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineStandings_DisciplineTeamId' AND object_id = OBJECT_ID(N'dbo.DisciplineStandings'))
BEGIN
    CREATE INDEX IX_DisciplineStandings_DisciplineTeamId
        ON dbo.DisciplineStandings (DisciplineTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineTeamMembers_CompetitionDisciplineId_CompetitionEntryId' AND object_id = OBJECT_ID(N'dbo.DisciplineTeamMembers'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplineTeamMembers_CompetitionDisciplineId_CompetitionEntryId
        ON dbo.DisciplineTeamMembers (CompetitionDisciplineId, CompetitionEntryId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineParticipantAssignments_CompetitionDisciplineId_CompetitionEntryId' AND object_id = OBJECT_ID(N'dbo.DisciplineParticipantAssignments'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplineParticipantAssignments_CompetitionDisciplineId_CompetitionEntryId
        ON dbo.DisciplineParticipantAssignments (CompetitionDisciplineId, CompetitionEntryId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineTeamMembers_CompetitionDisciplineId_DisciplineTeamId' AND object_id = OBJECT_ID(N'dbo.DisciplineTeamMembers'))
BEGIN
    CREATE INDEX IX_DisciplineTeamMembers_CompetitionDisciplineId_DisciplineTeamId
        ON dbo.DisciplineTeamMembers (CompetitionDisciplineId, DisciplineTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineTeamMembers_CompetitionEntryId' AND object_id = OBJECT_ID(N'dbo.DisciplineTeamMembers'))
BEGIN
    CREATE INDEX IX_DisciplineTeamMembers_CompetitionEntryId
        ON dbo.DisciplineTeamMembers (CompetitionEntryId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineTeamMembers_DisciplineTeamId_Order' AND object_id = OBJECT_ID(N'dbo.DisciplineTeamMembers'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplineTeamMembers_DisciplineTeamId_Order
        ON dbo.DisciplineTeamMembers (DisciplineTeamId, [Order]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DisciplineTeams_CompetitionDisciplineId_Seed' AND object_id = OBJECT_ID(N'dbo.DisciplineTeams'))
BEGIN
    CREATE UNIQUE INDEX IX_DisciplineTeams_CompetitionDisciplineId_Seed
        ON dbo.DisciplineTeams (CompetitionDisciplineId, Seed);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Matches_AwayTeamId' AND object_id = OBJECT_ID(N'dbo.Matches'))
BEGIN
    CREATE INDEX IX_Matches_AwayTeamId
        ON dbo.Matches (AwayTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Matches_DisciplinePhaseId_Order' AND object_id = OBJECT_ID(N'dbo.Matches'))
BEGIN
    CREATE UNIQUE INDEX IX_Matches_DisciplinePhaseId_Order
        ON dbo.Matches (DisciplinePhaseId, [Order]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Matches_DisciplinePhaseId_PhaseGroupId' AND object_id = OBJECT_ID(N'dbo.Matches'))
BEGIN
    CREATE INDEX IX_Matches_DisciplinePhaseId_PhaseGroupId
        ON dbo.Matches (DisciplinePhaseId, PhaseGroupId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Matches_HomeTeamId' AND object_id = OBJECT_ID(N'dbo.Matches'))
BEGIN
    CREATE INDEX IX_Matches_HomeTeamId
        ON dbo.Matches (HomeTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MatchSetScores_MatchId_SetNumber' AND object_id = OBJECT_ID(N'dbo.MatchSetScores'))
BEGIN
    CREATE UNIQUE INDEX IX_MatchSetScores_MatchId_SetNumber
        ON dbo.MatchSetScores (MatchId, SetNumber);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroups_DisciplinePhaseId_Name' AND object_id = OBJECT_ID(N'dbo.PhaseGroups'))
BEGIN
    CREATE UNIQUE INDEX IX_PhaseGroups_DisciplinePhaseId_Name
        ON dbo.PhaseGroups (DisciplinePhaseId, Name);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroups_DisciplinePhaseId_Order' AND object_id = OBJECT_ID(N'dbo.PhaseGroups'))
BEGIN
    CREATE UNIQUE INDEX IX_PhaseGroups_DisciplinePhaseId_Order
        ON dbo.PhaseGroups (DisciplinePhaseId, [Order]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroupTeams_DisciplinePhaseId_DisciplineTeamId' AND object_id = OBJECT_ID(N'dbo.PhaseGroupTeams'))
BEGIN
    CREATE UNIQUE INDEX IX_PhaseGroupTeams_DisciplinePhaseId_DisciplineTeamId
        ON dbo.PhaseGroupTeams (DisciplinePhaseId, DisciplineTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroupTeams_DisciplinePhaseId_PhaseGroupId' AND object_id = OBJECT_ID(N'dbo.PhaseGroupTeams'))
BEGIN
    CREATE INDEX IX_PhaseGroupTeams_DisciplinePhaseId_PhaseGroupId
        ON dbo.PhaseGroupTeams (DisciplinePhaseId, PhaseGroupId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroupTeams_DisciplineTeamId' AND object_id = OBJECT_ID(N'dbo.PhaseGroupTeams'))
BEGIN
    CREATE INDEX IX_PhaseGroupTeams_DisciplineTeamId
        ON dbo.PhaseGroupTeams (DisciplineTeamId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PhaseGroupTeams_PhaseGroupId_Seed' AND object_id = OBJECT_ID(N'dbo.PhaseGroupTeams'))
BEGIN
    CREATE UNIQUE INDEX IX_PhaseGroupTeams_PhaseGroupId_Seed
        ON dbo.PhaseGroupTeams (PhaseGroupId, Seed);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RankingPointRules_AwardPointSystemId_Rank' AND object_id = OBJECT_ID(N'dbo.RankingPointRules'))
BEGIN
    CREATE UNIQUE INDEX IX_RankingPointRules_AwardPointSystemId_Rank
        ON dbo.RankingPointRules (AwardPointSystemId, [Rank]);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AwardPointSystems_Name' AND object_id = OBJECT_ID(N'dbo.AwardPointSystems'))
BEGIN
    CREATE UNIQUE INDEX IX_AwardPointSystems_Name ON dbo.AwardPointSystems (Name);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CompetitionDisciplines_AwardPointSystemId' AND object_id = OBJECT_ID(N'dbo.CompetitionDisciplines'))
BEGIN
    CREATE INDEX IX_CompetitionDisciplines_AwardPointSystemId ON dbo.CompetitionDisciplines (AwardPointSystemId);
END;
