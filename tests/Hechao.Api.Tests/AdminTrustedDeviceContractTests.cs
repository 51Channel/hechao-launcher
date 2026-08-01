namespace Hechao.Api.Tests;

public sealed class AdminTrustedDeviceContractTests
{
    [Fact]
    public void Migration_StoresHashedRevocableExpiringCredentials()
    {
        var sql = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Database",
            "Migrations",
            "021_admin_trusted_devices.sql");

        Assert.Contains("token_hash bytea NOT NULL UNIQUE", sql, StringComparison.Ordinal);
        Assert.Contains("octet_length(token_hash) = 32", sql, StringComparison.Ordinal);
        Assert.Contains("expires_at timestamp with time zone NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("revoked_at timestamp with time zone", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (expires_at > created_at)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("token text", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token varchar", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_RequiresMfaAdministratorAndActiveCredential()
    {
        var source = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Admin",
            "AdminTrustedDeviceRepository.cs");

        Assert.Contains("if (!state.MfaVerified)", source, StringComparison.Ordinal);
        Assert.Contains("session.mfa_verified_at IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("user_account.access_tier = 'Administrator'", source, StringComparison.Ordinal);
        Assert.Contains("FROM launcher.admin_mfa_credentials", source, StringComparison.Ordinal);
        Assert.Contains("AdminWebTokenGenerator.Hash(token)", source, StringComparison.Ordinal);
        Assert.Contains("admin.trusted_device.created", source, StringComparison.Ordinal);
        Assert.Contains("admin.trusted_device.used", source, StringComparison.Ordinal);
        Assert.Contains("admin.trusted_device.revoked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountSecurityRevocation_AlsoRevokesTrustedDevices()
    {
        var adminSource = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Admin",
            "AdminAccountSecurityRepository.cs");
        var accountSource = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Authentication",
            "AuthenticationRepository.cs");

        Assert.Contains("UPDATE launcher.admin_trusted_devices", adminSource, StringComparison.Ordinal);
        Assert.Contains("UPDATE launcher.admin_trusted_devices", accountSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Issuance_RequiresMfaPolicyCsrfAndHostOnlySecureCookie()
    {
        var source = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Admin",
            "AdminWebEndpoints.cs");

        Assert.Contains("session.MapPost(\"/trusted-device\"", source, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(AdminAuthorization.PolicyName)", source, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AdminAntiforgeryFilter>()", source, StringComparison.Ordinal);
        Assert.Contains("__Host-HechaoAdminTrusted", source, StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", source, StringComparison.Ordinal);
        Assert.Contains("Secure = true", source, StringComparison.Ordinal);
        Assert.Contains("SameSite = SameSiteMode.Strict", source, StringComparison.Ordinal);
        Assert.Contains("DeleteTrustedDeviceCookie(context)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Frontend_RequiresExplicitTrustAndProvidesAuthenticatedConsoleAction()
    {
        var mfaView = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "AdminWeb",
            "src",
            "components",
            "MfaView.vue");
        var appShell = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "AdminWeb",
            "src",
            "components",
            "AppShell.vue");
        var css = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "AdminWeb",
            "src",
            "styles",
            "admin.css");

        Assert.Contains("v-model=\"trustDevice\" type=\"checkbox\"", mfaView, StringComparison.Ordinal);
        Assert.Contains("tryTrustSelectedDevice", mfaView, StringComparison.Ordinal);
        Assert.Contains("if (!trustDevice.value) return", mfaView, StringComparison.Ordinal);
        Assert.Contains("/v1/admin-auth/trusted-device", mfaView, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"信任这台电脑\"", appShell, StringComparison.Ordinal);
        Assert.Contains("/v1/admin-auth/trusted-device", appShell, StringComparison.Ordinal);
        Assert.Contains(".trusted-device-option input", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_DefaultsToThirtyDaysAndCapsAtNinety()
    {
        var options = ReadRepositoryFile(
            "src",
            "Hechao.Api",
            "Admin",
            "AdminWebOptions.cs");
        var program = ReadRepositoryFile("src", "Hechao.Api", "Program.cs");

        Assert.Contains("TrustedDeviceDays { get; init; } = 30", options, StringComparison.Ordinal);
        Assert.Contains("options.TrustedDeviceDays is >= 1 and <= 90", program, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(segments)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Hechao.Launcher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
