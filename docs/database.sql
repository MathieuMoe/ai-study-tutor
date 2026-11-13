-- TutorForge: production schema for the student progress (SQL Server).
-- The demo stores the same data as JSON files (FileStudentProgressStore).
-- To move to a database: implement IStudentProgressStore with these tables
-- (EF Core or Dapper) and change one line in DependencyInjection.AddTutorForge.

CREATE TABLE dbo.Students (
    StudentId    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, -- anonymous id from the cookie (or the user id with real login)
    LastSubject  NVARCHAR(40)     NULL,
    CreatedAt    DATETIMEOFFSET   NOT NULL,
    UpdatedAt    DATETIMEOFFSET   NOT NULL
);
CREATE INDEX IX_Students_UpdatedAt ON dbo.Students (UpdatedAt); -- retention purge

CREATE TABLE dbo.SubjectProgress (
    StudentId       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    Subject         NVARCHAR(40)     NOT NULL,
    Topic           NVARCHAR(120)    NULL,
    SchoolLevel     NVARCHAR(20)     NOT NULL,
    Mode            NVARCHAR(20)     NOT NULL,
    Language        NVARCHAR(20)     NOT NULL,
    EstimatedLevel  TINYINT          NULL CHECK (EstimatedLevel BETWEEN 0 AND 10),
    UpdatedAt       DATETIMEOFFSET   NOT NULL,
    CONSTRAINT PK_SubjectProgress PRIMARY KEY (StudentId, Subject)
);

CREATE TABLE dbo.LevelHistory (
    Id         BIGINT IDENTITY   NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL,
    Subject    NVARCHAR(40)      NOT NULL,
    Level      TINYINT           NOT NULL CHECK (Level BETWEEN 0 AND 10),
    At         DATETIMEOFFSET    NOT NULL,
    CONSTRAINT FK_LevelHistory_SubjectProgress FOREIGN KEY (StudentId, Subject)
        REFERENCES dbo.SubjectProgress (StudentId, Subject) ON DELETE CASCADE
);
CREATE INDEX IX_LevelHistory_Student ON dbo.LevelHistory (StudentId, Subject, At);

CREATE TABLE dbo.ConversationMessages (
    Id         BIGINT IDENTITY   NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL,
    Subject    NVARCHAR(40)      NOT NULL,
    Sequence   INT               NOT NULL,
    Role       NVARCHAR(10)      NOT NULL CHECK (Role IN ('User', 'Assistant')),
    Content    NVARCHAR(4000)    NOT NULL,
    CreatedAt  DATETIMEOFFSET    NOT NULL,
    CONSTRAINT FK_Messages_SubjectProgress FOREIGN KEY (StudentId, Subject)
        REFERENCES dbo.SubjectProgress (StudentId, Subject) ON DELETE CASCADE,
    CONSTRAINT UQ_Messages_Order UNIQUE (StudentId, Subject, Sequence)
);

-- Upsert of one subject (what SaveSubjectAsync does), inside a transaction:
--   MERGE dbo.Students ... ; MERGE dbo.SubjectProgress ... ;
--   INSERT dbo.LevelHistory when the level changed;
--   DELETE + INSERT dbo.ConversationMessages for that subject (the browser sends the last 30 messages).
-- Retention (ProgressCleanupService): DELETE dbo.Students WHERE UpdatedAt < DATEADD(day, -90, SYSDATETIMEOFFSET());
