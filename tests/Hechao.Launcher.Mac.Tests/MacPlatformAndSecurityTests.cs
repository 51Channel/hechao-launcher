using System.Runtime.InteropServices;
using Hechao.Contracts;
using Hechao.Distribution;
using Hechao.Launcher.Mac.Services;
using Hechao.Launcher.Services;

namespace Hechao.Launcher.Mac.Tests;

public sealed class MacPlatformAndSecurityTests
{
    [Fact]
    public void PlatformGate_AcceptsOnlyMacOsArm64()
    {
        Program.EnsureSupportedPlatform(
            isMacOS: true,
            Architecture.Arm64,
            "Apple M4 Max");

        Assert.Throws<PlatformNotSupportedException>(
            () => Program.EnsureSupportedPlatform(
                isMacOS: true,
                Architecture.X64,
                "Intel(R) Core(TM) i9"));
        Assert.Throws<PlatformNotSupportedException>(
            () => Program.EnsureSupportedPlatform(
                isMacOS: false,
                Architecture.Arm64,
                "Apple M4"));
        Assert.Throws<PlatformNotSupportedException>(
            () => Program.EnsureSupportedPlatform(
                isMacOS: true,
                Architecture.Arm64,
                "Apple M3 Pro"));
    }

    [Fact]
    public void KeychainSave_KeepsCredentialOutOfProcessArguments()
    {
        const string refreshToken = "refresh-token-that-must-not-be-an-argument";
        var invocation = MacKeychainSessionStore.CreateSaveInvocation(
            new StoredLauncherSession(refreshToken, TestLauncherFixture.CreateAccount()));

        Assert.Equal(["-i"], invocation.Arguments);
        Assert.DoesNotContain(refreshToken, string.Join(' ', invocation.Arguments));
        Assert.DoesNotContain(refreshToken, invocation.StandardInput);
        Assert.Contains("add-generic-password", invocation.StandardInput);
    }

    [Fact]
    public void MacDefaults_UseApplicationSupportAndUnixJava()
    {
        var dataRoot = ClientStorageLayout.GetDefaultDataRoot(
            isMacOS: true,
            "/Users/tester",
            "ignored");

        Assert.Equal(
            Path.Combine(
                "/Users/tester",
                "Library",
                "Application Support",
                "Hechao",
                "GameData"),
            dataRoot);
        Assert.Equal("java", ProfileJavaRuntimeService.GetJavaExecutableName(isWindows: false));
        Assert.Equal("java.exe", ProfileJavaRuntimeService.GetJavaExecutableName(isWindows: true));
        Assert.True(JavaRuntimeValidator.IsSupportedExecutableName("java", isWindows: false));
        Assert.False(JavaRuntimeValidator.IsSupportedExecutableName("java.exe", isWindows: false));
    }
}
