/*
    Gives an existing user a role (Editor or Admin) in the local PortfolioDb database.

    The user registers first via POST /api/auth/register (and gets the role Gast). Run this
    script with sqlcmd from the repository root:

        sqlcmd -S .\SQLEXPRESS -E -C -b -W -d PortfolioDb -i scripts/assign-role.sql -v Email="editor@example.test" Role="Editor"

    (-E: Windows login, -C: trust the local certificate, -b: stop with an error code on failure,
    -W: no padding in the output.)

    In SSMS or Azure Data Studio: turn on SQLCMD mode (Query > SQLCMD Mode) and add these two lines
    at the top before running it:
        :setvar Email "editor@example.test"
        :setvar Role "Editor"

    The script also gives the user a new SecurityStamp. The API checks that stamp on every request,
    so the user's current cookie stops working (401) and they have to sign in again: the new role
    is in the cookie they get then. No passwords are involved or stored here.
*/

-- AspNetUsers has filtered indexes; SQL Server refuses to update the table without this.
-- (sqlcmd turns it off by default, SSMS turns it on.)
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
-- Any error rolls the whole transaction back: never a new role without a new stamp.
SET XACT_ABORT ON;

DECLARE @Email nvarchar(256) = N'$(Email)';
DECLARE @Role nvarchar(256) = N'$(Role)';

-- Identity looks users and roles up by their normalized (upper case) name.
DECLARE @UserId nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = UPPER(@Email));
DECLARE @RoleId nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE NormalizedName = UPPER(@Role));

IF @UserId IS NULL
BEGIN
    RAISERROR(N'No user with e-mail address %s. Register first via POST /api/auth/register.', 16, 1, @Email);
    RETURN;
END;

IF @RoleId IS NULL
BEGIN
    RAISERROR(N'No role %s. Use Gast, Editor or Admin.', 16, 1, @Role);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @UserId AND RoleId = @RoleId)
BEGIN
    PRINT N'Nothing changed: ' + @Email + N' already has the role ' + @Role + N'.';
    RETURN;
END;

BEGIN TRANSACTION;

    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);

    -- New SecurityStamp: signs the user out everywhere. New ConcurrencyStamp: the row was changed
    -- outside Identity, so UserManager must not overwrite it with an older copy.
    UPDATE dbo.AspNetUsers
    SET SecurityStamp = CONVERT(nvarchar(36), NEWID()),
        ConcurrencyStamp = CONVERT(nvarchar(36), NEWID())
    WHERE Id = @UserId;

COMMIT TRANSACTION;

PRINT N'Done: ' + @Email + N' now has the role ' + @Role + N'. They have to sign in again.';

SELECT u.Email, r.Name AS Role
FROM dbo.AspNetUsers u
JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE u.Id = @UserId
ORDER BY r.Name;
