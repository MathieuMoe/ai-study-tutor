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

-- =====================================================================================
-- Student record ("expediente"): what the platform knows about the student.
-- Demo: one JSON file per student (FileStudentRecordStore, App_Data/records).
-- Production: these tables behind the same IStudentRecordStore interface; on a real
-- platform most of them would be views over the platform's own courses, grades and logs.
-- =====================================================================================

CREATE TABLE dbo.StudentProfiles (
    StudentId    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
                 REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    DisplayName  NVARCHAR(60)     NULL,           -- optional nickname, no real name needed
    SchoolLevel  NVARCHAR(20)     NULL,
    Language     NVARCHAR(20)     NULL,
    Objective    NVARCHAR(300)    NULL,
    ExamDate     DATE             NULL,
    Academy      NVARCHAR(120)    NULL,
    UpdatedAt    DATETIMEOFFSET   NOT NULL
);

CREATE TABLE dbo.Enrollments (
    Id         INT IDENTITY      NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    Course     NVARCHAR(150)     NOT NULL,
    Subject    NVARCHAR(40)      NOT NULL,
    Teacher    NVARCHAR(100)     NULL
);

CREATE TABLE dbo.EnrollmentTopics (
    Id            INT IDENTITY  NOT NULL PRIMARY KEY,
    EnrollmentId  INT           NOT NULL REFERENCES dbo.Enrollments (Id) ON DELETE CASCADE,
    Position      SMALLINT      NOT NULL,         -- syllabus order
    Name          NVARCHAR(150) NOT NULL,
    Status        NVARCHAR(20)  NOT NULL CHECK (Status IN ('NotStarted', 'InProgress', 'Completed')),
    Mastery       TINYINT       NULL CHECK (Mastery BETWEEN 0 AND 10),
    LastActivity  DATE          NULL
);

CREATE TABLE dbo.Assessments (
    Id         BIGINT IDENTITY   NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    Date       DATE              NOT NULL,
    Subject    NVARCHAR(40)      NOT NULL,
    Title      NVARCHAR(150)     NOT NULL,
    Topic      NVARCHAR(150)     NULL,
    Score      DECIMAL(7,2)      NOT NULL,
    MaxScore   DECIMAL(7,2)      NOT NULL CHECK (MaxScore > 0),
    Kind       NVARCHAR(20)      NOT NULL,        -- Test, Exam, Homework, MockExam, LevelTest
    CONSTRAINT CK_Assessments_Score CHECK (Score BETWEEN 0 AND MaxScore)
);
CREATE INDEX IX_Assessments_Student ON dbo.Assessments (StudentId, Subject, Date DESC);

CREATE TABLE dbo.AssessmentMistakes (
    Id            BIGINT IDENTITY NOT NULL PRIMARY KEY,
    AssessmentId  BIGINT          NOT NULL REFERENCES dbo.Assessments (Id) ON DELETE CASCADE,
    Text          NVARCHAR(300)   NOT NULL
);

-- Written automatically by the tutor after each answer (RecordRules.TrackTutorTurn):
-- answers in the same subject and topic within 30 minutes extend the same row.
CREATE TABLE dbo.ActivityLog (
    Id           BIGINT IDENTITY   NOT NULL PRIMARY KEY,
    StudentId    UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    StartAt      DATETIMEOFFSET    NOT NULL,
    EndAt        DATETIMEOFFSET    NOT NULL,
    Kind         NVARCHAR(20)      NOT NULL,      -- TutorSession, Lesson, Test, Video, Assignment
    Subject      NVARCHAR(40)      NOT NULL,
    Topic        NVARCHAR(150)     NULL,
    Mode         NVARCHAR(20)      NULL,
    Turns        INT               NOT NULL DEFAULT 0,
    LevelBefore  TINYINT           NULL,
    LevelAfter   TINYINT           NULL,
    Summary      NVARCHAR(300)     NULL
);
CREATE INDEX IX_ActivityLog_Student ON dbo.ActivityLog (StudentId, Subject, EndAt DESC);

CREATE TABLE dbo.Goals (
    Id         INT IDENTITY      NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    Text       NVARCHAR(300)     NOT NULL,
    Target     DATE              NULL,
    Done       BIT               NOT NULL DEFAULT 0
);

CREATE TABLE dbo.TeacherNotes (
    Id         INT IDENTITY      NOT NULL PRIMARY KEY,
    StudentId  UNIQUEIDENTIFIER  NOT NULL REFERENCES dbo.Students (StudentId) ON DELETE CASCADE,
    Date       DATE              NOT NULL,
    Author     NVARCHAR(100)     NOT NULL,
    Text       NVARCHAR(600)     NOT NULL
);
