using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.DotNetSonarScanner;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.DotNetSonarScanner.DotNetSonarScannerTasks;

namespace Ubiety.Build.Tool
{
    internal class Program : NukeBuild
    {
        [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
        private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

        [Parameter] private readonly bool? Cover = true;
        [Parameter] private readonly string NuGetKey;
        [Parameter] private readonly string SonarKey;
        [Parameter] private readonly string SonarProjectKey;

        [GitRepository] private readonly GitRepository GitRepository;
        [GitVersion(DisableOnUnix = true)] private readonly GitVersion GitVersion;

        private const string NuGetSource = "https://api.nuget.org/v3/index.json";

        [Solution] private readonly Solution Solution;
        
        private AbsolutePath SourceDirectory => RootDirectory / "src";
        private AbsolutePath TestsDirectory => RootDirectory / "tests";
        private AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

        private Target Clean => _ => _
            .Before(Restore)
            .Executes(() =>
            {
                SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
                TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
                EnsureCleanDirectory(ArtifactsDirectory);
            });

        private Target Restore => _ => _
            .Executes(() =>
            {
                DotNetRestore(s => s
                    .SetProjectFile(Solution));
            });

        private Target Compile => _ => _
            .DependsOn(Restore)
            .Executes(() =>
            {
                var settings = GitVersion is null
                    ? new DotNetBuildSettings().SetProjectFile(Solution)
                        .SetConfiguration(Configuration)
                        .EnableNoRestore()
                    : new DotNetBuildSettings().SetProjectFile(Solution)
                        .SetConfiguration(Configuration)
                        .SetAssemblyVersion(GitVersion.AssemblySemVer)
                        .SetFileVersion(GitVersion.AssemblySemFileVer)
                        .SetInformationalVersion(GitVersion.InformationalVersion)
                        .EnableNoRestore();

                DotNetBuild(settings);
            });

        private Target SonarBegin => _ => _
            .Before(Compile)
            .Requires(() => SonarKey)
            .Unlisted()
            .Executes(() =>
            {
                DotNetSonarScannerBegin(s => s
                    .SetLogin(SonarKey)
                    .SetProjectKey(SonarProjectKey)
                    .SetOrganization("ubiety")
                    .SetServer("https://sonarcloud.io")
                    .SetVersion(GitVersion.NuGetVersionV2)
                    .SetOpenCoverPaths(ArtifactsDirectory / "coverage.opencover.xml"));
            });

        private Target SonarEnd => _ => _
            .After(Test)
            .DependsOn(SonarBegin)
            .Requires(() => SonarKey)
            .Unlisted()
            .Executes(() =>
            {
                DotNetSonarScannerEnd(s => s
                    .SetLogin(SonarKey));
            });

        private Target Test => _ => _
            .DependsOn(Compile)
            .Executes(() =>
            {
                var project = Solution.GetProject("*.Test");

                DotNetTest(s => s
                    .SetProjectFile(project)
                    .EnableNoBuild()
                    .SetConfiguration(Configuration)
                    .SetArgumentConfigurator(args => args.Add("/p:CollectCoverage={0}", Cover)
                        .Add("/p:CoverletOutput={0}", ArtifactsDirectory / "coverage")
                        .Add("/p:CoverletOutputFormat={0}", "opencover")
                        .Add("/p:Exclude={0}", "[xunit.*]*")));
            });

        private Target Pack => _ => _
            .After(Test)
            .OnlyWhenStatic(() => GitRepository.IsOnMasterBranch())
            .Executes(() =>
            {
                DotNetPack(s => s
                    .EnableNoBuild()
                    .SetProject(Solution)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetVersion(GitVersion.NuGetVersionV2));
            });

        private Target Publish => _ => _
            .DependsOn(Pack)
            .Requires(() => NuGetKey)
            .Requires(() => Configuration.Equals(Configuration.Release))
            .OnlyWhenStatic(() => GitRepository.IsOnMasterBranch())
            .Executes(() =>
            {
                DotNetNuGetPush(s => s
                        .SetApiKey(NuGetKey)
                        .SetSource(NuGetSource)
                        .CombineWith(
                            ArtifactsDirectory.GlobFiles("*.nupkg").NotEmpty(), (cs, v) =>
                                cs.SetTargetPath(v)),
                    5,
                    true);
            });

        private Target CI => _ => _
            .DependsOn(Clean, Test, SonarEnd, Publish);

        public static int Main()
        {
            return Execute<Program>(x => x.Test);
        }
    }
}