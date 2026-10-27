/* =====================================================================
   Vendor Portal - SQL Server schema (ASP.NET Core 8 Identity + business tables)
   Run in SSMS / Azure Data Studio / sqlcmd against (localdb)\MSSQLLocalDB or any local SQL Server.
   Safe to re-run: every table is created only if it does not already exist.
   ===================================================================== */

IF DB_ID(N'VendorPortalDb') IS NULL
    CREATE DATABASE [VendorPortalDb];
GO

USE [VendorPortalDb];
GO

/* ---------------------------- ASP.NET Core Identity ---------------------------- */

IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoles (
        Id               NVARCHAR(450) NOT NULL,
        Name             NVARCHAR(256) NULL,
        NormalizedName   NVARCHAR(256) NULL,
        ConcurrencyStamp NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetRoles PRIMARY KEY (Id)
    );
    CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles (NormalizedName) WHERE NormalizedName IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUsers (
        Id                   NVARCHAR(450)  NOT NULL,
        UserName             NVARCHAR(256)  NULL,
        NormalizedUserName   NVARCHAR(256)  NULL,
        Email                NVARCHAR(256)  NULL,
        NormalizedEmail      NVARCHAR(256)  NULL,
        EmailConfirmed       BIT            NOT NULL,
        PasswordHash         NVARCHAR(MAX)  NULL,
        SecurityStamp        NVARCHAR(MAX)  NULL,
        ConcurrencyStamp     NVARCHAR(MAX)  NULL,
        PhoneNumber          NVARCHAR(MAX)  NULL,
        PhoneNumberConfirmed BIT            NOT NULL,
        TwoFactorEnabled     BIT            NOT NULL,
        LockoutEnd           DATETIMEOFFSET(7) NULL,
        LockoutEnabled       BIT            NOT NULL,
        AccessFailedCount    INT            NOT NULL,
        CONSTRAINT PK_AspNetUsers PRIMARY KEY (Id)
    );
    CREATE INDEX EmailIndex ON dbo.AspNetUsers (NormalizedEmail);
    CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers (NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
END
GO

IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoleClaims (
        Id         INT IDENTITY(1,1) NOT NULL,
        RoleId     NVARCHAR(450) NOT NULL,
        ClaimType  NVARCHAR(MAX) NULL,
        ClaimValue NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY (Id),
        CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims (RoleId);
END
GO

IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserClaims (
        Id         INT IDENTITY(1,1) NOT NULL,
        UserId     NVARCHAR(450) NOT NULL,
        ClaimType  NVARCHAR(MAX) NULL,
        ClaimValue NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetUserClaims PRIMARY KEY (Id),
        CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims (UserId);
END
GO

IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserLogins (
        LoginProvider       NVARCHAR(128) NOT NULL,
        ProviderKey         NVARCHAR(128) NOT NULL,
        ProviderDisplayName NVARCHAR(MAX) NULL,
        UserId              NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
        CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins (UserId);
END
GO

IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserRoles (
        UserId NVARCHAR(450) NOT NULL,
        RoleId NVARCHAR(450) NOT NULL,
        CONSTRAINT PK_AspNetUserRoles PRIMARY KEY (UserId, RoleId),
        CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles (Id) ON DELETE CASCADE,
        CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles (RoleId);
END
GO

IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserTokens (
        UserId        NVARCHAR(450) NOT NULL,
        LoginProvider NVARCHAR(128) NOT NULL,
        Name          NVARCHAR(128) NOT NULL,
        Value         NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
        CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
END
GO

/* ------------------------------ Business tables ------------------------------ */

IF OBJECT_ID(N'dbo.VendorProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VendorProfiles (
        VendorProfileId INT IDENTITY(1,1) NOT NULL,
        UserId          NVARCHAR(450) NOT NULL,
        FullName        NVARCHAR(150) NOT NULL,
        PanNumber       NVARCHAR(10)  NOT NULL,
        MobileNumber    NVARCHAR(15)  NOT NULL,
        GstNumber       NVARCHAR(15)  NOT NULL,
        TradeName       NVARCHAR(150) NULL,
        CreatedAt       DATETIME2(7)  NOT NULL CONSTRAINT DF_VendorProfiles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_VendorProfiles PRIMARY KEY (VendorProfileId),
        CONSTRAINT FK_VendorProfiles_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers (Id)
    );
    CREATE UNIQUE INDEX IX_VendorProfiles_UserId    ON dbo.VendorProfiles (UserId);
    CREATE UNIQUE INDEX IX_VendorProfiles_PanNumber ON dbo.VendorProfiles (PanNumber);
    CREATE UNIQUE INDEX IX_VendorProfiles_GstNumber ON dbo.VendorProfiles (GstNumber);
END
GO

IF OBJECT_ID(N'dbo.VendorApplications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VendorApplications (
        ApplicationId     INT IDENTITY(1,1) NOT NULL,
        VendorProfileId   INT NOT NULL,
        Status            NVARCHAR(30) NOT NULL,   -- Draft, PendingReview, UnderReview, CorrectionsRequired, Approved, Rejected
        SubmittedAt       DATETIME2(7) NULL,
        FinalDecisionById NVARCHAR(450) NULL,
        FinalDecisionAt   DATETIME2(7) NULL,
        CONSTRAINT PK_VendorApplications PRIMARY KEY (ApplicationId),
        CONSTRAINT FK_VendorApplications_VendorProfiles FOREIGN KEY (VendorProfileId) REFERENCES dbo.VendorProfiles (VendorProfileId) ON DELETE CASCADE,
        CONSTRAINT FK_VendorApplications_AspNetUsers_FinalDecisionById FOREIGN KEY (FinalDecisionById) REFERENCES dbo.AspNetUsers (Id)
    );
    CREATE INDEX IX_VendorApplications_VendorProfileId   ON dbo.VendorApplications (VendorProfileId);
    CREATE INDEX IX_VendorApplications_FinalDecisionById ON dbo.VendorApplications (FinalDecisionById);
END
GO

IF OBJECT_ID(N'dbo.VendorDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VendorDocuments (
        VendorDocumentId INT IDENTITY(1,1) NOT NULL,
        ApplicationId    INT NOT NULL,
        DocumentType     NVARCHAR(40) NOT NULL,
        IsRequired       BIT NOT NULL,
        Status           NVARCHAR(20) NOT NULL,    -- NotSubmitted, Pending, Approved, Rejected
        CONSTRAINT PK_VendorDocuments PRIMARY KEY (VendorDocumentId),
        CONSTRAINT FK_VendorDocuments_VendorApplications FOREIGN KEY (ApplicationId) REFERENCES dbo.VendorApplications (ApplicationId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IX_VendorDocuments_ApplicationId_DocumentType ON dbo.VendorDocuments (ApplicationId, DocumentType);
END
GO

IF OBJECT_ID(N'dbo.DocumentVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentVersions (
        DocumentVersionId INT IDENTITY(1,1) NOT NULL,
        VendorDocumentId  INT NOT NULL,
        VersionNumber     INT NOT NULL,
        StorageKey        NVARCHAR(300) NOT NULL,
        OriginalFileName  NVARCHAR(255) NOT NULL,
        ContentType       NVARCHAR(100) NOT NULL,
        SizeBytes         BIGINT NOT NULL,
        UploadedAt        DATETIME2(7) NOT NULL CONSTRAINT DF_DocumentVersions_UploadedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_DocumentVersions PRIMARY KEY (DocumentVersionId),
        CONSTRAINT FK_DocumentVersions_VendorDocuments FOREIGN KEY (VendorDocumentId) REFERENCES dbo.VendorDocuments (VendorDocumentId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IX_DocumentVersions_VendorDocumentId_VersionNumber ON dbo.DocumentVersions (VendorDocumentId, VersionNumber);
END
GO

IF OBJECT_ID(N'dbo.DocumentReviewHistories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentReviewHistories (
        ReviewId          INT IDENTITY(1,1) NOT NULL,
        DocumentVersionId INT NOT NULL,
        ReviewerUserId    NVARCHAR(450) NOT NULL,
        Decision          NVARCHAR(20) NOT NULL,   -- Approved, Rejected
        Remarks           NVARCHAR(1000) NULL,
        ReviewedAt        DATETIME2(7) NOT NULL CONSTRAINT DF_DocumentReviewHistories_ReviewedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_DocumentReviewHistories PRIMARY KEY (ReviewId),
        CONSTRAINT FK_DocumentReviewHistories_DocumentVersions FOREIGN KEY (DocumentVersionId) REFERENCES dbo.DocumentVersions (DocumentVersionId) ON DELETE CASCADE,
        CONSTRAINT FK_DocumentReviewHistories_AspNetUsers_ReviewerUserId FOREIGN KEY (ReviewerUserId) REFERENCES dbo.AspNetUsers (Id)
    );
    CREATE INDEX IX_DocumentReviewHistories_DocumentVersionId ON dbo.DocumentReviewHistories (DocumentVersionId);
    CREATE INDEX IX_DocumentReviewHistories_ReviewerUserId    ON dbo.DocumentReviewHistories (ReviewerUserId);
END
GO

IF OBJECT_ID(N'dbo.ApplicationDecisionHistories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApplicationDecisionHistories (
        DecisionHistoryId INT IDENTITY(1,1) NOT NULL,
        ApplicationId     INT NOT NULL,
        AdminUserId       NVARCHAR(450) NOT NULL,
        PreviousStatus    NVARCHAR(30) NOT NULL,
        NewStatus         NVARCHAR(30) NOT NULL,
        Remarks           NVARCHAR(1000) NULL,
        DecidedAt         DATETIME2(7) NOT NULL CONSTRAINT DF_ApplicationDecisionHistories_DecidedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ApplicationDecisionHistories PRIMARY KEY (DecisionHistoryId),
        CONSTRAINT FK_ApplicationDecisionHistories_VendorApplications FOREIGN KEY (ApplicationId) REFERENCES dbo.VendorApplications (ApplicationId) ON DELETE CASCADE,
        CONSTRAINT FK_ApplicationDecisionHistories_AspNetUsers_AdminUserId FOREIGN KEY (AdminUserId) REFERENCES dbo.AspNetUsers (Id)
    );
    CREATE INDEX IX_ApplicationDecisionHistories_ApplicationId ON dbo.ApplicationDecisionHistories (ApplicationId);
    CREATE INDEX IX_ApplicationDecisionHistories_AdminUserId   ON dbo.ApplicationDecisionHistories (AdminUserId);
END
GO

/* ------------------------------- Seed roles ------------------------------- */

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'ADMIN')
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(NVARCHAR(450), NEWID()), N'Admin', N'ADMIN', CONVERT(NVARCHAR(100), NEWID()));

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'VENDOR')
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(NVARCHAR(450), NEWID()), N'Vendor', N'VENDOR', CONVERT(NVARCHAR(100), NEWID()));
GO

/* The first Admin user is NOT inserted here because Identity must create the password hash.
   The application creates it at startup from appsettings.json / User Secrets (SeedAdmin). */

SELECT name FROM sys.tables ORDER BY name;
GO
