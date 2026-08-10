using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Hechao.Api.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Hechao.Api.Tests;

public sealed class AdminWebStaticHostingTests
{
    [Fact]
    public async Task StaticHost_ServesDeepVueRoutesAndBuiltAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiRoot = Path.Combine(repositoryRoot, "src", "Hechao.Api");
        var port = FindAvailablePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = apiRoot,
            WebRootPath = "wwwroot",
            EnvironmentName = "Development"
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        await using var app = builder.Build();
        app.UseMiddleware<AdminWebCanonicalPathMiddleware>();
        app.UseStaticFiles();
        app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");
        await app.StartAsync();

        try
        {
            using var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };

            using var deepRoute = await client.GetAsync("/admin/control");
            Assert.Equal(HttpStatusCode.OK, deepRoute.StatusCode);
            Assert.Contains(
                "<div id=\"app\"></div>",
                await deepRoute.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            using var script = await client.GetAsync("/admin/assets/admin.js");
            Assert.Equal(HttpStatusCode.OK, script.StatusCode);
            Assert.Equal(
                "text/javascript",
                script.Content.Headers.ContentType?.MediaType);

            using var stylesheet = await client.GetAsync("/admin/assets/admin.css");
            Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
            Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);

            using var canonical = await client.GetAsync("/admin");
            Assert.Equal(HttpStatusCode.Redirect, canonical.StatusCode);
            Assert.Equal("/admin/", canonical.Headers.Location?.OriginalString);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void ProductionPipeline_PreservesAdminHostAndSecurityHeaders()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Program.cs"));

        Assert.Contains("adminWebOptions.IsExpectedHost", program, StringComparison.Ordinal);
        Assert.Contains("Cache-Control", program, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", program, StringComparison.Ordinal);
        Assert.Contains("X-Frame-Options", program, StringComparison.Ordinal);
        Assert.Contains(
            "app.MapFallbackToFile(\"/admin/{*path:nonfile}\", \"admin/index.html\")",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityCalendar_CspAuthorizesBuiltFullCalendarStyleAnchor()
    {
        const string anchor = "/* fullcalendar-csp-anchor */";
        const string styleElement = "<style data-fullcalendar>/* fullcalendar-csp-anchor */</style>";
        const string expectedHash = "ipzKv5H4ieKlTTlJ/yUoqe2zh7iU5Iy8a9PrIETK5us=";
        var repositoryRoot = FindRepositoryRoot();
        var sourceIndex = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "AdminWeb",
            "index.html"));
        var builtIndex = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "wwwroot",
            "admin",
            "index.html"));
        var program = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Hechao.Api",
            "Program.cs"));
        var actualHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(anchor)));

        Assert.Equal(expectedHash, actualHash);
        Assert.Contains(styleElement, sourceIndex, StringComparison.Ordinal);
        Assert.Contains(styleElement, builtIndex, StringComparison.Ordinal);
        Assert.Contains($"'sha256-{expectedHash}'", program, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectBuild_CompilesAdminWebButExcludesFrontendSourcesFromPublish()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hechao.Api",
            "Hechao.Api.csproj"));

        Assert.Contains("<AdminWebSource Include=", project, StringComparison.Ordinal);
        Assert.Contains("Command=\"npm run build\"", project, StringComparison.Ordinal);
        Assert.Contains("<Content Remove=\"AdminWeb/**/*\" />", project, StringComparison.Ordinal);
        Assert.Contains("<None Remove=\"AdminWeb/**/*\" />", project, StringComparison.Ordinal);
    }

    private static int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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
