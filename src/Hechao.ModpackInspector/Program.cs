using System.Text.Json;
using Hechao.Modpack;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Hechao.ModpackInspector <archive.zip> <output-directory>");
    return 64;
}

var result = await ModpackArchiveAnalyzer.AnalyzeAndSplitAsync(args[0], args[1]);
Console.WriteLine(JsonSerializer.Serialize(new
{
    layout = result.Layout.ToString(),
    hasBlockingIssues = result.HasBlockingIssues,
    metadata = result.Metadata,
    client = result.Client,
    server = result.Server,
    issues = result.Issues
}, new JsonSerializerOptions
{
    WriteIndented = true
}));
return result.HasBlockingIssues ? 2 : 0;
