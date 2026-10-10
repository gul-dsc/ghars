using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GharsPlatform.Tests;

/// <summary>
/// The production deployment can run only from main, and the deployment script cannot be pointed at
/// production resources when it is asked to deploy staging. Reads azure-pipelines.yml and
/// deploy/Deploy-Ghars.ps1 directly, evaluates the Deploy stage condition, and runs the script's
/// -ValidateOnly check through Windows PowerShell. Nothing is deployed.
/// </summary>
public class DeploymentSafetyTests
{
    private const string ProductionPipeline = "azure-pipelines.yml";
    private const string Script = "deploy/Deploy-Ghars.ps1";

    private static readonly string[] ProductionMarkers =
        { @"C:\inetpub\ghars", @"D:\GharsReleases", @"D:\GharsBackups", "ghars.dubaisc.ae", "Ghars-Production" };

    // ---- the pipeline ---------------------------------------------------------------------------

    [Fact]
    public void Pipeline_is_triggered_only_by_main_and_never_by_pull_requests_or_schedules()
    {
        var yml = Read(ProductionPipeline);
        var trigger = Regex.Match(yml, @"^trigger:\s*\n\s+branches:\s*\n\s+include:\s*\n((?:\s+- .+\n)+)", RegexOptions.Multiline);
        Assert.True(trigger.Success, "trigger.branches.include not found");
        Assert.Equal(new[] { "main" }, trigger.Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().TrimStart('-').Trim()));
        Assert.Matches(new Regex(@"^pr:\s*none\s*$", RegexOptions.Multiline), yml);
        Assert.DoesNotMatch(new Regex(@"^(schedules|resources):", RegexOptions.Multiline), yml);
    }

    [Fact]
    public void Deploy_stage_targets_production_with_the_explicit_target_argument()
    {
        var deploy = DeployStage();
        Assert.Contains("environment: Ghars-Production", deploy);
        Assert.Contains("-DeploymentTarget Production", deploy);
        Assert.Contains("dependsOn: Build", deploy);
    }

    public static TheoryData<string, string, bool, bool> ConditionCases => new()
    {
        // source branch, build reason, build succeeded, expected to deploy
        { "refs/heads/main", "IndividualCI", true, true },
        { "refs/heads/main", "BatchedCI", true, true },
        { "refs/heads/main", "Manual", true, true },                  // manual run of main: gated by the environment approval
        { "refs/heads/main", "IndividualCI", false, false },          // failed build
        { "refs/heads/main", "PullRequest", true, false },
        { "refs/heads/feature/ghars-content-localization-security", "Manual", true, false },
        { "refs/heads/feature/ghars-content-localization-security", "IndividualCI", true, false },
        { "refs/heads/main-hotfix", "Manual", true, false },
        { "refs/heads/feature/main", "Manual", true, false },
        { "refs/heads/MAIN-x", "Manual", true, false },
        { "refs/tags/main", "Manual", true, false },
        { "refs/pull/12/merge", "PullRequest", true, false },
        { "refs/pull/12/merge", "Manual", true, false },
    };

    [Theory]
    [MemberData(nameof(ConditionCases))]
    public void Deploy_condition_allows_only_main_outside_pull_requests(string branch, string reason, bool succeeded, bool expected)
    {
        var condition = Regex.Match(DeployStage(), @"^\s+condition:\s*(.+)$", RegexOptions.Multiline);
        Assert.True(condition.Success, "Deploy stage has no condition");
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Build.SourceBranch"] = branch,
            ["Build.Reason"] = reason,
        };
        Assert.Equal(expected, ConditionEvaluator.Evaluate(condition.Groups[1].Value.Trim(), vars, succeeded));
    }

    [Fact]
    public void Every_pipeline_job_that_uses_the_production_environment_is_branch_guarded()
    {
        foreach (var file in PipelineFiles())
        {
            var yml = File.ReadAllText(file);
            if (!yml.Contains("Ghars-Production")) continue;
            foreach (var stage in Regex.Split(yml, @"^  - stage:", RegexOptions.Multiline).Skip(1).Where(s => s.Contains("Ghars-Production")))
                Assert.Contains("eq(variables['Build.SourceBranch'], 'refs/heads/main')", stage);
        }
    }

    [Fact]
    public void No_other_pipeline_or_staging_file_names_a_production_resource()
    {
        var root = RepoRoot();
        var candidates = PipelineFiles()
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "deploy"), "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root, "appsettings*.json"))
            .Where(f => !Path.GetFileName(f).Equals(ProductionPipeline, StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).Equals("Deploy-Ghars.ps1", StringComparison.OrdinalIgnoreCase));

        foreach (var file in candidates)
        {
            var text = File.ReadAllText(file);
            foreach (var marker in ProductionMarkers)
                Assert.False(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} names production resource '{marker}'");
        }
    }

    // ---- the script ----------------------------------------------------------------------------

    [Fact]
    public void Script_requires_an_explicit_target_and_checks_it_before_changing_anything()
    {
        var ps = Read(Script);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\[ValidateSet\('Production', 'Staging'\)\]\[string\] \$DeploymentTarget"), ps);
        Assert.DoesNotMatch(new Regex(@"\$DeploymentTarget\s*=", RegexOptions.IgnoreCase), ps);
        Assert.DoesNotMatch(new Regex(@"\[string\]\s*\$SitePath\s*=", RegexOptions.IgnoreCase), ps);

        var body = ps[ps.IndexOf("$ErrorActionPreference", StringComparison.Ordinal)..];
        var check = body.IndexOf("Step \"Validate deployment target", StringComparison.Ordinal);
        Assert.True(check > 0, "target check not found");
        foreach (var effect in new[] { "New-Item", "Set-Content", "Stop-WebAppPool", "Start-WebAppPool", "Invoke-Robocopy -From", "Invoke-SqlcmdCli -ExtraArgs", "Remove-Item", "icacls" })
        {
            var at = body.IndexOf(effect, StringComparison.Ordinal);
            Assert.True(at < 0 || at > check, $"'{effect}' runs before the target check");
        }
    }

    public static TheoryData<string, string, string, bool> TargetCases => new()
    {
        // target, extra arguments, label, expected to be accepted
        { "Production", @"-SitePath C:\inetpub\ghars -AppPool GharsPlatform -HealthCheckUrl https://ghars.dubaisc.ae/", "production as the pipeline calls it", true },
        { "Production", @"-SitePath C:\inetpub\ghars\ -AppPool GharsPlatform -HealthCheckUrl https://ghars.dubaisc.ae/", "trailing slash", true },
        { "Production", @"-SitePath C:\inetpub\ghars-staging -AppPool GharsPlatform -HealthCheckUrl https://ghars.dubaisc.ae/", "production to another folder", false },
        { "Production", @"-SitePath C:\inetpub\ghars -AppPool GharsPlatform-Staging -HealthCheckUrl https://ghars.dubaisc.ae/", "production with another pool", false },
        { "Production", @"-SitePath C:\inetpub\ghars -AppPool GharsPlatform -HealthCheckUrl https://staging.example.test/", "production smoke-tested elsewhere", false },
        { "Production", @"-SitePath C:\inetpub\ghars -AppPool GharsPlatform", "production without a smoke test", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool GharsPlatform-Staging -HealthCheckUrl https://staging.example.test/ -ReleaseHistoryRoot D:\GharsStagingReleases -Database GharsStagingDb", "separate staging", true },
        { "Staging", @"-SitePath C:\inetpub\ghars -AppPool GharsPlatform-Staging", "staging into the production folder", false },
        { "Staging", @"-SitePath C:\INETPUB\GHARS\ -AppPool GharsPlatform-Staging", "production folder, other case", false },
        { "Staging", @"-SitePath C:\inetpub\ghars\staging -AppPool GharsPlatform-Staging", "inside the production folder", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging\..\ghars -AppPool GharsPlatform-Staging", "traversal into production", false },
        { "Staging", @"-SitePath C:\inetpub -AppPool GharsPlatform-Staging", "a folder containing production", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool GharsPlatform", "production pool", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool S -AppPoolIdentity ""IIS AppPool\GharsPlatform""", "production identity", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool S -ReleaseHistoryRoot D:\GharsReleases", "production release history", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool S -BackupRoot D:\GharsBackups\predeploy", "production backup folder", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool S -Database GharsPlatformDb", "production database name", false },
        { "Staging", @"-SitePath C:\inetpub\ghars-staging -AppPool S -HealthCheckUrl https://ghars.dubaisc.ae/", "production host", false },
        { "Bogus", @"-SitePath C:\inetpub\ghars-staging -AppPool S", "unknown target", false },
    };

    [Theory]
    [MemberData(nameof(TargetCases))]
    public async Task Script_accepts_only_matching_targets(string target, string arguments, string label, bool expected)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(powershell)) return; // the script is Windows-only

        var script = Path.Combine(RepoRoot(), Script.Replace('/', Path.DirectorySeparatorChar));
        var psi = new ProcessStartInfo(powershell,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -ValidateOnly -DeploymentTarget {target} -Source C:\\drop\\site {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await p.WaitForExitAsync(timeout.Token);
        var (stdout, stderr) = (await output, await error);
        var accepted = p.ExitCode == 0 && stdout.Contains("-ValidateOnly: target accepted");
        Assert.True(accepted == expected, $"{label}: exit {p.ExitCode}\n{stdout}\n{stderr}");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static string DeployStage()
    {
        var yml = Read(ProductionPipeline);
        var start = yml.IndexOf("  - stage: Deploy", StringComparison.Ordinal);
        Assert.True(start >= 0, "Deploy stage not found");
        var next = yml.IndexOf("\n  - stage:", start + 1, StringComparison.Ordinal);
        return next < 0 ? yml[start..] : yml[start..next];
    }

    private static IEnumerable<string> PipelineFiles()
    {
        var root = RepoRoot();
        return Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}"));
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GharsPlatform.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>
    /// The subset of Azure Pipelines condition expressions the Deploy stage may use: and, or, not,
    /// eq, ne, startsWith, endsWith, contains, succeeded(), string literals and variables['name'].
    /// Comparisons ignore case, as Azure Pipelines does. Anything else throws, so a new function in
    /// the condition fails the test rather than being guessed at.
    /// </summary>
    private sealed class ConditionEvaluator
    {
        private readonly string _s;
        private readonly IReadOnlyDictionary<string, string> _vars;
        private readonly bool _succeeded;
        private int _i;

        private ConditionEvaluator(string s, IReadOnlyDictionary<string, string> vars, bool succeeded) { _s = s; _vars = vars; _succeeded = succeeded; }

        public static bool Evaluate(string expression, IReadOnlyDictionary<string, string> vars, bool succeeded)
        {
            var e = new ConditionEvaluator(expression, vars, succeeded);
            var value = e.Value();
            e.Skip();
            if (e._i != e._s.Length) throw new FormatException($"Unexpected text at {e._i}: {expression[e._i..]}");
            return value is bool b ? b : throw new FormatException("Condition is not a boolean.");
        }

        private object Value()
        {
            Skip();
            if (_s[_i] == '\'')
            {
                var end = _s.IndexOf('\'', _i + 1);
                var literal = _s[(_i + 1)..end];
                _i = end + 1;
                return literal;
            }
            var name = Regex.Match(_s[_i..], @"^[A-Za-z]+").Value;
            if (name.Length == 0) throw new FormatException($"Unexpected text at {_i}: {_s[_i..]}");
            _i += name.Length;
            if (name == "variables")
            {
                Expect('['); var key = (string)Value(); Expect(']');
                return _vars.TryGetValue(key, out var v) ? v : "";
            }
            Expect('(');
            var args = new List<object>();
            Skip();
            if (_s[_i] != ')')
            {
                args.Add(Value());
                while (Peek(',')) { _i++; args.Add(Value()); }
            }
            Expect(')');
            string S(int n) => Convert.ToString(args[n])!;
            return name switch
            {
                "succeeded" when args.Count == 0 => _succeeded,
                "and" => args.All(a => (bool)a),
                "or" => args.Any(a => (bool)a),
                "not" => !(bool)args.Single(),
                "eq" => string.Equals(S(0), S(1), StringComparison.OrdinalIgnoreCase),
                "ne" => !string.Equals(S(0), S(1), StringComparison.OrdinalIgnoreCase),
                "startsWith" => S(0).StartsWith(S(1), StringComparison.OrdinalIgnoreCase),
                "endsWith" => S(0).EndsWith(S(1), StringComparison.OrdinalIgnoreCase),
                "contains" => S(0).Contains(S(1), StringComparison.OrdinalIgnoreCase),
                _ => throw new NotSupportedException($"Condition function '{name}' is not understood by this test.")
            };
        }

        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Peek(char c) { Skip(); return _i < _s.Length && _s[_i] == c; }
        private void Expect(char c) { if (!Peek(c)) throw new FormatException($"Expected '{c}' at {_i}"); _i++; }
    }
}
