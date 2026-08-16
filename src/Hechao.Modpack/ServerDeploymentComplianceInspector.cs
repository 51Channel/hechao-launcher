using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Hechao.Modpack;

internal static partial class ServerDeploymentComplianceInspector
{
    private const int MaximumTextFileBytes = 1024 * 1024;

    internal static ServerDeploymentInspection Inspect(
        IReadOnlyDictionary<string, ZipArchiveEntry> files,
        string? declaredCore)
    {
        var checks = new List<ServerDeploymentCheck>();
        var normalizedDeclaration = string.IsNullOrWhiteSpace(declaredCore)
            ? null
            : declaredCore.Trim();
        var declaredCoreKind = ParseCore(normalizedDeclaration);
        var detectedCores = DetectCores(files.Keys);
        var inferredCore = InferExpectedCore(detectedCores);
        var expectedCore = declaredCoreKind == ServerCoreKind.Unknown
            ? inferredCore
            : declaredCoreKind;

        AddDescriptorCheck(
            checks,
            normalizedDeclaration,
            declaredCoreKind,
            inferredCore);
        AddServerPropertiesChecks(checks, files);
        AddEulaCheck(checks, files);
        AddJvmArgumentsCheck(checks, files);

        files.TryGetValue("start.bat", out var startEntry);
        var startText = ReadText(startEntry);
        AddStartScriptChecks(checks, startEntry, startText);

        var launchCommands = FindJavaCommands(startText);
        var launchCommand = SelectJavaCommand(launchCommands);
        var launch = ResolveLaunch(launchCommand);
        AddLaunchChecks(
            checks,
            files,
            detectedCores,
            expectedCore,
            launchCommands.Count,
            launchCommand,
            launch);

        return new ServerDeploymentInspection(
            normalizedDeclaration,
            expectedCore,
            detectedCores,
            launch.Core,
            startEntry is null ? null : "start.bat",
            launchCommand,
            checks);
    }

    private static void AddDescriptorCheck(
        ICollection<ServerDeploymentCheck> checks,
        string? declaration,
        ServerCoreKind declaredCore,
        ServerCoreKind inferredCore)
    {
        if (declaration is null)
        {
            checks.Add(new ServerDeploymentCheck(
                "SERVER_CORE_UNDECLARED",
                DeploymentCheckStatus.Warning,
                "服务端核心未声明",
                inferredCore == ServerCoreKind.Unknown
                    ? "hechao-pack.json 没有 serverCore，且无法从文件可靠推断服务端核心。"
                    : $"hechao-pack.json 没有 serverCore；当前从文件推断为 {inferredCore}。",
                "hechao-pack.json",
                "在 hechao-pack.json 中增加 serverCore，并与 start.bat 的实际启动目标保持一致。"));
            return;
        }

        if (declaredCore == ServerCoreKind.Unknown)
        {
            checks.Add(new ServerDeploymentCheck(
                "SERVER_CORE_UNSUPPORTED",
                DeploymentCheckStatus.Blocking,
                "服务端核心声明无效",
                $"serverCore={declaration} 不属于当前支持的服务端核心。",
                "hechao-pack.json",
                "使用 Vanilla、Paper、Purpur、Fabric、Forge、NeoForge 或 Arclight。"));
            return;
        }

        checks.Add(new ServerDeploymentCheck(
            "SERVER_CORE_DECLARED",
            DeploymentCheckStatus.Passed,
            "服务端核心声明完整",
            $"serverCore 已声明为 {declaredCore}。",
            "hechao-pack.json"));
    }

    private static void AddServerPropertiesChecks(
        ICollection<ServerDeploymentCheck> checks,
        IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        if (!files.TryGetValue("server.properties", out var entry))
        {
            checks.Add(new ServerDeploymentCheck(
                "SERVER_PROPERTIES_MISSING",
                DeploymentCheckStatus.Blocking,
                "缺少服务端配置",
                "服务端归档根目录没有 server.properties。",
                "server.properties",
                "将服务端实际使用的 server.properties 放到 server 目录根部。"));
            return;
        }

        var text = ReadText(entry);
        if (text is null)
        {
            checks.Add(new ServerDeploymentCheck(
                "SERVER_PROPERTIES_INVALID",
                DeploymentCheckStatus.Blocking,
                "服务端配置不可读取",
                "server.properties 为空、过大或无法按文本读取。",
                "server.properties",
                "使用不超过 1 MiB 的标准文本 server.properties。"));
            return;
        }

        var values = ParseProperties(text);
        var loopback = values.TryGetValue("server-ip", out var serverIp) &&
                       string.Equals(serverIp, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
        checks.Add(new ServerDeploymentCheck(
            "SERVER_IP_LOOPBACK",
            loopback ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            loopback ? "后端仅监听回环地址" : "后端监听地址不安全",
            loopback
                ? "server-ip=127.0.0.1，玩家不能绕过 Velocity 直接连接后端。"
                : "server-ip 必须明确设置为 127.0.0.1。",
            "server.properties",
            loopback ? null : "设置 server-ip=127.0.0.1。"));

        var offlineBackend = values.TryGetValue("online-mode", out var onlineMode) &&
                             string.Equals(onlineMode, "false", StringComparison.OrdinalIgnoreCase);
        checks.Add(new ServerDeploymentCheck(
            "BACKEND_ONLINE_MODE",
            offlineBackend ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            offlineBackend ? "代理后端身份模式正确" : "代理后端身份模式错误",
            offlineBackend
                ? "online-mode=false，正版身份由 Velocity 和 forwarding 链路统一验证。"
                : "Velocity 后端必须设置 online-mode=false，否则代理转发身份无法按平台标准工作。",
            "server.properties",
            offlineBackend ? null : "设置 online-mode=false，并保留 Velocity forwarding 的服务端配置。"));
    }

    private static void AddEulaCheck(
        ICollection<ServerDeploymentCheck> checks,
        IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        files.TryGetValue("eula.txt", out var entry);
        var text = ReadText(entry);
        var accepted = text is not null &&
                       ParseProperties(text).TryGetValue("eula", out var value) &&
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        checks.Add(new ServerDeploymentCheck(
            "EULA_ACCEPTED",
            accepted ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            accepted ? "EULA 已确认" : "EULA 未确认",
            accepted
                ? "eula.txt 包含 eula=true。"
                : "服务端归档必须包含 eula.txt，并明确写入 eula=true。",
            "eula.txt",
            accepted ? null : "确认 Mojang EULA 后，在 server 目录根部添加 eula.txt。"));
    }

    private static void AddJvmArgumentsCheck(
        ICollection<ServerDeploymentCheck> checks,
        IReadOnlyDictionary<string, ZipArchiveEntry> files)
    {
        files.TryGetValue("user_jvm_args.txt", out var entry);
        var text = ReadText(entry);
        var valid = text is not null &&
                    InitialMemoryArgument().Matches(text).Count == 1 &&
                    MaximumMemoryArgument().Matches(text).Count == 1;
        checks.Add(new ServerDeploymentCheck(
            "MANAGED_JVM_MEMORY",
            valid ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            valid ? "内存参数可由服控管理" : "内存参数不符合服控要求",
            valid
                ? "user_jvm_args.txt 各包含一个 -Xms 和 -Xmx 参数。"
                : "user_jvm_args.txt 必须各包含且只包含一个 -Xms 和 -Xmx 参数。",
            "user_jvm_args.txt",
            valid ? null : "添加例如 -Xms1024M 和 -Xmx4096M；后台部署时会按目标槽设置改写。"));
    }

    private static void AddStartScriptChecks(
        ICollection<ServerDeploymentCheck> checks,
        ZipArchiveEntry? entry,
        string? text)
    {
        if (entry is null || text is null)
        {
            checks.Add(new ServerDeploymentCheck(
                "START_SCRIPT_MISSING",
                DeploymentCheckStatus.Blocking,
                "缺少受管启动脚本",
                "服务端归档根目录没有可读取的 start.bat。",
                "start.bat",
                "将实际启动服务端的 start.bat 放到 server 目录根部。"));
            return;
        }

        var managed = ManagedStartGuard().IsMatch(text);
        checks.Add(new ServerDeploymentCheck(
            "MANAGED_START_GUARD",
            managed ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            managed ? "受管启动标记正确" : "缺少受管启动标记",
            managed
                ? "start.bat 可在后台计划任务中无交互启动。"
                : "start.bat 缺少 HECHAO_MANAGED_START 守卫，部署代理会拒绝该归档。",
            "start.bat",
            managed ? null : "将独立 pause 改为 if not defined HECHAO_MANAGED_START pause。"));
    }

    private static void AddLaunchChecks(
        ICollection<ServerDeploymentCheck> checks,
        IReadOnlyDictionary<string, ZipArchiveEntry> files,
        IReadOnlyList<ServerCoreKind> detectedCores,
        ServerCoreKind expectedCore,
        int launchCommandCount,
        string? launchCommand,
        LaunchResolution launch)
    {
        if (launchCommand is null)
        {
            checks.Add(new ServerDeploymentCheck(
                "JAVA_LAUNCH_COMMAND_MISSING",
                DeploymentCheckStatus.Blocking,
                "没有识别到 Java 启动命令",
                "start.bat 中没有可识别的 Java 服务端启动命令。",
                "start.bat",
                "使用 java @user_jvm_args.txt -jar <核心.jar> nogui，或加载器官方的 win_args.txt。"));
            return;
        }

        checks.Add(new ServerDeploymentCheck(
            "JAVA_LAUNCH_COMMAND_FOUND",
            DeploymentCheckStatus.Passed,
            "Java 启动命令已识别",
            launchCommand,
            "start.bat"));

        var portableJava = IsPortableJavaCommand(launchCommand);
        checks.Add(new ServerDeploymentCheck(
            "PORTABLE_JAVA_COMMAND",
            portableJava ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            portableJava ? "Java 入口可由服控注入" : "Java 入口绑定了本机路径",
            portableJava
                ? "启动脚本使用 java 或 java.exe，由受管 runner 提供目标 Java。"
                : "启动脚本必须调用 java 或 java.exe，不能写制作者电脑上的绝对 Java 路径。",
            "start.bat",
            portableJava ? null : "把绝对 java.exe 路径改为 java，并保留 user_jvm_args.txt。"));

        checks.Add(new ServerDeploymentCheck(
            "SINGLE_JAVA_LAUNCH_COMMAND",
            launchCommandCount == 1
                ? DeploymentCheckStatus.Passed
                : DeploymentCheckStatus.Blocking,
            launchCommandCount == 1 ? "启动入口唯一" : "存在多个 Java 启动入口",
            launchCommandCount == 1
                ? "start.bat 只有一个可执行的 Java 服务端启动命令。"
                : $"start.bat 中识别到 {launchCommandCount} 个 Java 启动命令，无法可靠确定实际核心。",
            "start.bat",
            launchCommandCount == 1
                ? null
                : "只保留一个实际服务端 Java 命令；不同核心不要写在同一启动脚本中。"));

        if (launch.ReferencePath is null || !files.ContainsKey(launch.ReferencePath))
        {
            checks.Add(new ServerDeploymentCheck(
                "LAUNCH_TARGET_MISSING",
                DeploymentCheckStatus.Blocking,
                "启动目标不存在",
                launch.ReferencePath is null
                    ? "无法从 Java 命令解析服务端核心或 win_args.txt。"
                    : $"start.bat 引用的 {launch.ReferencePath} 不在服务端归档中。",
                "start.bat",
                "修正启动命令，或把实际启动目标完整放入 server 目录。"));
        }
        else
        {
            checks.Add(new ServerDeploymentCheck(
                "LAUNCH_TARGET_PRESENT",
                DeploymentCheckStatus.Passed,
                "启动目标完整",
                $"启动目标 {launch.ReferencePath} 已包含在服务端归档中。",
                launch.ReferencePath));
        }

        if (launch.Core == ServerCoreKind.Unknown)
        {
            checks.Add(new ServerDeploymentCheck(
                "LAUNCH_CORE_UNKNOWN",
                DeploymentCheckStatus.Blocking,
                "无法识别实际启动核心",
                "Java 命令存在，但无法判断它启动的是哪一种服务端核心。",
                "start.bat",
                "使用受支持核心的明确 JAR 名称或加载器官方 win_args.txt 路径。"));
            return;
        }

        checks.Add(new ServerDeploymentCheck(
            "LAUNCH_CORE_IDENTIFIED",
            DeploymentCheckStatus.Passed,
            "实际启动核心已识别",
            $"start.bat 实际启动 {launch.Core}。",
            "start.bat"));

        if (expectedCore == ServerCoreKind.Unknown)
        {
            checks.Add(new ServerDeploymentCheck(
                "SERVER_CORE_FILES_UNKNOWN",
                DeploymentCheckStatus.Warning,
                "归档核心文件无法独立确认",
                $"启动命令指向 {launch.Core}，但仅凭归档文件无法形成第二份核心证据。",
                launch.ReferencePath,
                "在 hechao-pack.json 声明 serverCore。"));
            return;
        }

        var matchesExpected = launch.Core == expectedCore;
        checks.Add(new ServerDeploymentCheck(
            "SERVER_CORE_LAUNCH_MATCH",
            matchesExpected ? DeploymentCheckStatus.Passed : DeploymentCheckStatus.Blocking,
            matchesExpected ? "服务端核心与启动入口一致" : "服务端核心与启动入口冲突",
            matchesExpected
                ? $"归档预期核心和 start.bat 均为 {expectedCore}。"
                : $"归档预期使用 {expectedCore}，但 start.bat 实际启动 {launch.Core}。",
            "start.bat",
            matchesExpected
                ? null
                : $"将 start.bat 改为启动 {expectedCore}，不要仅把核心 JAR 放入目录。"));

        if (detectedCores.Contains(ServerCoreKind.Arclight) &&
            launch.Core != ServerCoreKind.Arclight)
        {
            checks.Add(new ServerDeploymentCheck(
                "ARCLIGHT_BYPASSED",
                DeploymentCheckStatus.Blocking,
                "Arclight 被启动脚本绕过",
                $"归档包含 Arclight JAR，但 start.bat 实际启动 {launch.Core}；Bukkit 插件和 Velocity forwarding mixin 不会加载。",
                "start.bat",
                "改为 java @user_jvm_args.txt -jar <arclight-*.jar> nogui。"));
        }
    }

    private static IReadOnlyList<ServerCoreKind> DetectCores(IEnumerable<string> paths)
    {
        var cores = new HashSet<ServerCoreKind>();
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            if (fileName.StartsWith("arclight", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Arclight);
            }
            else if (fileName.Contains("purpur", StringComparison.OrdinalIgnoreCase) &&
                     fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Purpur);
            }
            else if (fileName.Contains("paper", StringComparison.OrdinalIgnoreCase) &&
                     fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Paper);
            }
            else if (fileName.Equals("fabric-server-launch.jar", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Equals("fabric-loader.jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Fabric);
            }
            else if (normalized.Contains("libraries/net/neoforged/neoforge/", StringComparison.OrdinalIgnoreCase) &&
                     normalized.EndsWith("/win_args.txt", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Equals("neoforge.jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.NeoForge);
            }
            else if (normalized.Contains("libraries/net/minecraftforge/forge/", StringComparison.OrdinalIgnoreCase) &&
                     normalized.EndsWith("/win_args.txt", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Equals("forge.jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Forge);
            }
            else if (fileName.Equals("server.jar", StringComparison.OrdinalIgnoreCase) ||
                     fileName.StartsWith("minecraft_server", StringComparison.OrdinalIgnoreCase) &&
                     fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                cores.Add(ServerCoreKind.Vanilla);
            }
        }

        return cores
            .OrderBy(CorePrecedence)
            .ToArray();
    }

    private static ServerCoreKind InferExpectedCore(
        IReadOnlyCollection<ServerCoreKind> detectedCores) =>
        detectedCores.OrderBy(CorePrecedence).FirstOrDefault();

    private static int CorePrecedence(ServerCoreKind core) => core switch
    {
        ServerCoreKind.Arclight => 0,
        ServerCoreKind.Purpur => 1,
        ServerCoreKind.Paper => 2,
        ServerCoreKind.Fabric => 3,
        ServerCoreKind.NeoForge => 4,
        ServerCoreKind.Forge => 5,
        ServerCoreKind.Vanilla => 6,
        _ => 99
    };

    private static ServerCoreKind ParseCore(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "vanilla" => ServerCoreKind.Vanilla,
            "paper" => ServerCoreKind.Paper,
            "purpur" => ServerCoreKind.Purpur,
            "fabric" => ServerCoreKind.Fabric,
            "forge" => ServerCoreKind.Forge,
            "neoforge" => ServerCoreKind.NeoForge,
            "arclight" => ServerCoreKind.Arclight,
            _ => ServerCoreKind.Unknown
        };

    private static LaunchResolution ResolveLaunch(string? command)
    {
        if (command is null)
        {
            return new LaunchResolution(ServerCoreKind.Unknown, null);
        }

        var argumentsFile = ArgumentsFileReference().Match(command);
        if (argumentsFile.Success)
        {
            var path = NormalizeReference(
                argumentsFile.Groups["quoted"].Success
                    ? argumentsFile.Groups["quoted"].Value
                    : argumentsFile.Groups["bare"].Value);
            if (path is not null)
            {
                var core = path.Contains(
                    "libraries/net/neoforged/neoforge/",
                    StringComparison.OrdinalIgnoreCase)
                    ? ServerCoreKind.NeoForge
                    : path.Contains(
                        "libraries/net/minecraftforge/forge/",
                        StringComparison.OrdinalIgnoreCase)
                        ? ServerCoreKind.Forge
                        : ServerCoreKind.Unknown;
                return new LaunchResolution(core, path);
            }
        }

        var jar = JarReference().Match(command);
        if (!jar.Success)
        {
            return new LaunchResolution(ServerCoreKind.Unknown, null);
        }

        var jarPath = NormalizeReference(
            jar.Groups["quoted"].Success
                ? jar.Groups["quoted"].Value
                : jar.Groups["bare"].Value);
        return new LaunchResolution(ResolveJarCore(jarPath), jarPath);
    }

    private static ServerCoreKind ResolveJarCore(string? path)
    {
        if (path is null)
        {
            return ServerCoreKind.Unknown;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.StartsWith("arclight", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.Arclight;
        }

        if (fileName.Contains("purpur", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.Purpur;
        }

        if (fileName.Contains("paper", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.Paper;
        }

        if (fileName.Contains("fabric", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.Fabric;
        }

        if (fileName.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.NeoForge;
        }

        if (fileName.Equals("forge.jar", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("forge-", StringComparison.OrdinalIgnoreCase))
        {
            return ServerCoreKind.Forge;
        }

        return fileName.Equals("server.jar", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("minecraft_server", StringComparison.OrdinalIgnoreCase)
            ? ServerCoreKind.Vanilla
            : ServerCoreKind.Unknown;
    }

    private static IReadOnlyList<string> FindJavaCommands(string? text)
    {
        if (text is null)
        {
            return [];
        }

        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(IsJavaCommand)
            .ToArray();
    }

    private static string? SelectJavaCommand(IReadOnlyList<string> candidates) =>
        candidates.FirstOrDefault(line =>
                   line.Contains("win_args.txt", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("-jar", StringComparison.OrdinalIgnoreCase)) ??
        candidates.FirstOrDefault();

    private static bool IsJavaCommand(string line)
    {
        var value = line.TrimStart('@').TrimStart();
        if (value.StartsWith("call ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["call ".Length..].TrimStart();
        }

        return JavaExecutable().IsMatch(value);
    }

    private static bool IsPortableJavaCommand(string line)
    {
        var value = line.TrimStart('@').TrimStart();
        if (value.StartsWith("call ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["call ".Length..].TrimStart();
        }

        return PortableJavaExecutable().IsMatch(value);
    }

    private static string? NormalizeReference(string value)
    {
        var path = value.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        try
        {
            return SafeArchivePath.Normalize(path);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string? ReadText(ZipArchiveEntry? entry)
    {
        if (entry is null || entry.Length is <= 0 or > MaximumTextFileBytes)
        {
            return null;
        }

        using var stream = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        stream.CopyTo(memory);
        return Encoding.Latin1.GetString(memory.ToArray());
    }

    private static Dictionary<string, string> ParseProperties(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private sealed record LaunchResolution(
        ServerCoreKind Core,
        string? ReferencePath);

    [GeneratedRegex(
        @"(?im)^[ \t]*if not defined HECHAO_MANAGED_START pause[ \t]*(?:\r)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ManagedStartGuard();

    [GeneratedRegex(
        @"(?<!\S)-Xms[1-9][0-9]*[KMG](?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InitialMemoryArgument();

    [GeneratedRegex(
        @"(?<!\S)-Xmx[1-9][0-9]*[KMG](?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaximumMemoryArgument();

    [GeneratedRegex(
        """^(?:"[^"]*java(?:\.exe)?"|[^\s"]*java(?:\.exe)?)(?:\s|$)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaExecutable();

    [GeneratedRegex(
        """^(?:"java(?:\.exe)?"|java(?:\.exe)?)(?:\s|$)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortableJavaExecutable();

    [GeneratedRegex(
        """@(?:"(?<quoted>[^"\r\n]*win_args\.txt)"|(?<bare>[^\s\r\n]*win_args\.txt))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArgumentsFileReference();

    [GeneratedRegex(
        """(?:^|\s)-jar\s+(?:"(?<quoted>[^"\r\n]+)"|(?<bare>[^\s\r\n]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JarReference();
}
