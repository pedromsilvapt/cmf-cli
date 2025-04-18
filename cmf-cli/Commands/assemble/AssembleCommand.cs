
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Abstractions;
using System.Linq;
using Cmf.CLI.Constants;
using Cmf.CLI.Core;
using Cmf.CLI.Core.Attributes;
using Cmf.CLI.Core.Enums;
using Cmf.CLI.Core.Objects;
using Cmf.CLI.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Cmf.CLI.Commands
{
    /// <summary>
    /// This command will be responsible for assembling a package based on a given cmfpackage and respective dependencies
    /// </summary>
    /// <seealso cref="BaseCommand" />
    [CmfCommand("assemble", Id = "assemble", Description = "Assemble all unreleased packages as release candidates. Also generate a dependency file with all needed released packages.")]
    public class AssembleCommand : BaseCommand
    {
        #region Private Properties

        /// <summary>
        /// Packages names and Uri to saved in a file in the end of the command execution
        /// </summary>
        private readonly Dictionary<string, string> packagesLocation = new();

        #endregion

        #region Constructors

        /// <summary>
        /// Assemble Command
        /// </summary>
        public AssembleCommand() : base() { }

        /// <summary>
        /// Assemble Command
        /// </summary>
        /// <param name="fileSystem"></param>
        public AssembleCommand(IFileSystem fileSystem) : base(fileSystem) { }

        #endregion

        #region Public Methods

        /// <summary>
        /// Configure command
        /// </summary>
        /// <param name="cmd"></param>
        public override void Configure(Command cmd)
        {
            cmd.AddArgument(new Argument<IDirectoryInfo>(
                name: "workingDir",
                parse: (argResult) => Parse<IDirectoryInfo>(argResult, "."),
                isDefault: true)
            {
                Description = "Working Directory"
            });

            cmd.AddOption(new Option<IDirectoryInfo>(
                aliases: new string[] { "-o", "--outputDir" },
                parseArgument: argResult => Parse<IDirectoryInfo>(argResult, "Assemble"),
                isDefault: true,
                description: "Output directory for assembled package"));

            cmd.AddOption(new Option<Uri>(
                aliases: new string[] { "--cirepo" },
                description: "Repository where Continuous Integration packages are located (url or folder)"));

            cmd.AddOption(new Option<Uri[]>(
                aliases: new string[] { "-r", "--repos", "--repo" },
                description: "Repository or repositories where published dependencies are located (url or folder)"));

            cmd.AddOption(new Option<bool>(
                aliases: new string[] { "--includeTestPackages" },
                description: "Include test packages on assemble"));

            // Add the handler
            cmd.Handler = CommandHandler.Create<IDirectoryInfo, IDirectoryInfo, Uri, Uri[], bool>(Execute);
        }

        /// <summary>
        /// Executes the specified working dir.
        /// </summary>
        /// <param name="workingDir">The working dir.</param>
        /// <param name="outputDir">The output dir.</param>
        /// <param name="ciRepo"></param>
        /// <param name="repos">The repo.</param>
        /// <param name="includeTestPackages">True to publish test packages</param>
        /// <returns></returns>
        public void Execute(IDirectoryInfo workingDir, IDirectoryInfo outputDir, Uri ciRepo, Uri[] repos, bool includeTestPackages)
        {
            using var activity = ExecutionContext.ServiceProvider?.GetService<ITelemetryService>()?.StartExtendedActivity(this.GetType().Name);
            if (ciRepo == null)
            {
                ciRepo = ExecutionContext.Instance.RepositoriesConfig.CIRepository;
                if (ciRepo == null)
                {
                    string errorMessage = string.Format(CliMessages.MissingMandatoryOption, "cirepo");
                    throw new CliException(errorMessage);
                }
            }

            if (repos != null)
            {
                ExecutionContext.Instance.RepositoriesConfig.Repositories.InsertRange(0, repos);
            }

            IFileInfo cmfpackageFile = fileSystem.FileInfo.New($"{workingDir}/{CliConstants.CmfPackageFileName}");

            CmfPackage cmfPackage = CmfPackage.Load(cmfpackageFile, setDefaultValues: false, fileSystem: fileSystem);

            if (cmfPackage.PackageType != PackageType.Root)
            {
                throw new CliException(CliMessages.NotARootPackage);
            }

            // The method LoadDependencies will return the dependency from the first repo in the list
            // We need to force the CI Repo to be the last to be checked, to make sure that we first check the "release repositories"
            ExecutionContext.Instance.RepositoriesConfig.Repositories.Add(ciRepo);

            cmfPackage.LoadDependencies(ExecutionContext.Instance.RepositoriesConfig.Repositories, null, true);

            #region Missing Dependencies Handling

            // If a dependency is not found in any repository an error should be throw
            List<string> missingPackages = new();
            foreach (Dependency dependency in cmfPackage.Dependencies.Where(x => x.IsMissing))
            {
                if (!dependency.IsIgnorable)
                {
                    missingPackages.Add($"{dependency.Id}@{dependency.Version}");
                }
            }

            if (missingPackages.HasAny())
            {
                string errorMessage = string.Format(CliMessages.SomePackagesNotFound, string.Join(", ", missingPackages));
                throw new CliException(errorMessage);
            }

            #endregion

            #region Output Directories Handling

            outputDir = FileSystemUtilities.GetOutputDir(cmfPackage, outputDir, force: true);

            if (includeTestPackages)
            {
                IDirectoryInfo outputTestDir = this.fileSystem.DirectoryInfo.New(outputDir + CliConstants.FolderTests);
                if (!outputTestDir.Exists)
                {
                    outputTestDir.Create();
                }
            }

            #endregion

            try
            {
                IDirectoryInfo[] repoDirectories = ExecutionContext.Instance.RepositoriesConfig.Repositories?.Select(r => r.GetDirectory()).ToArray();

                // Assemble current package
                AssemblePackage(outputDir, repoDirectories, cmfPackage, includeTestPackages);

                // Assemble Dependencies
                AssembleDependencies(outputDir, ciRepo, repoDirectories, cmfPackage);

                // Save Dependencies File
                // This file will be needed for CMF Internal Releases to know where the external dependencies are located
                string depedenciesFilePath = this.fileSystem.Path.Join(outputDir.FullName, CliConstants.FileDependencies);
                fileSystem.File.WriteAllText(depedenciesFilePath, JsonConvert.SerializeObject(packagesLocation));
            }
            catch (Exception e)
            {
                throw new CliException(e.Message, e);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Assemble Packages of Type Test
        /// </summary>
        /// <param name="outputDir"></param>
        /// <param name="repoDirectories"></param>
        /// <param name="cmfPackage"></param>
        private void AssembleTestPackages(IDirectoryInfo outputDir, IDirectoryInfo[] repoDirectories, CmfPackage cmfPackage)
        {
            if (!cmfPackage.TestPackages.HasAny())
            {
                // No test packages found for package
                Log.Information(string.Format(CliMessages.PackageHasNoTestPackages, cmfPackage.PackageId, cmfPackage.Version));
            }
            else
            {
                IDirectoryInfo testOutputDir = this.fileSystem.DirectoryInfo.New(outputDir + "/Tests");

                foreach (Dependency testPackage in cmfPackage.TestPackages)
                {
                    // Load test package from repo if is not loaded yet
                    if (testPackage.CmfPackage == null || (testPackage.CmfPackage != null && testPackage.CmfPackage.Uri == null))
                    {
                        string packageName = $"{testPackage.Id}.{testPackage.Version}";
                        testPackage.CmfPackage = CmfPackage.LoadFromRepo(repoDirectories, testPackage.Id, testPackage.Version, fromManifest: false);
                        if(testPackage.CmfPackage == null)
                        {
                            string errorMessage = string.Format(CliMessages.SomePackagesNotFound, $"{packageName}.zip");
                            throw new CliException(errorMessage);
                        }
                    }

                    AssemblePackage(testOutputDir, repoDirectories, testPackage.CmfPackage, false);
                }
            }
        }

        /// <summary>
        /// Publish Dependencies from one package. recursive operation
        /// </summary>
        /// <param name="outputDir">Destination for the dependencies package and also used for the current package</param>
        /// <param name="ciRepo"></param>
        /// <param name="repoDirectories">The repos.</param>
        /// <param name="cmfPackage">The CMF package.</param>
        /// <param name="assembledDependencies">The loaded dependencies.</param>
        /// <param name="includeTestPackages"></param>
        private void AssembleDependencies(IDirectoryInfo outputDir, Uri ciRepo, IDirectoryInfo[] repoDirectories, CmfPackage cmfPackage, DependencyCollection assembledDependencies = null, bool includeTestPackages = false)
        {
            if (cmfPackage.Dependencies.HasAny())
            {
                assembledDependencies ??= new();

                foreach (Dependency dependency in cmfPackage.Dependencies)
                {
                    if (!dependency.IsIgnorable)
                    {
                        // Validate dependency Uri
                        if (dependency.CmfPackage == null)
                        {
                            string errorMessage = string.Format(CoreMessages.MissingMandatoryDependency, dependency.Id, dependency.Version);
                            throw new CliException(errorMessage);
                        }

                        string dependencyPath = dependency.CmfPackage.Uri.GetFile().Directory.FullName;

                        // To avoid assembling the same dependency twice
                        // Only assemble dependencies from the CI Repository
                        if (!assembledDependencies.Contains(dependency) &&
                            string.Equals(ciRepo.GetDirectoryName(), dependencyPath))
                        {
                            assembledDependencies.Add(dependency);
                            AssemblePackage(outputDir, repoDirectories, dependency.CmfPackage, includeTestPackages);
                        }
                        // Save all external dependencies and locations in a dictionary
                        else
                        {
                            packagesLocation[$"{dependency.Id}@{dependency.Version}"] = ExecutionContext.Instance.RunningOnWindows ? dependency.CmfPackage.Uri.GetFileName() : dependency.CmfPackage.Uri.LocalPath;
                        }

                        AssembleDependencies(outputDir, ciRepo, repoDirectories, dependency.CmfPackage, assembledDependencies, includeTestPackages);
                    }
                }
            }
        }

        /// <summary>
        /// Publish a package to the output directory
        /// </summary>
        /// <param name="outputDir">Destiny for the package</param>
        /// <param name="repoDirectories">The repos.</param>
        /// <param name="cmfPackage">The CMF package.</param>
        /// <param name="includeTestPackages"></param>
        /// <exception cref="CliException"></exception>
        private void AssemblePackage(IDirectoryInfo outputDir, IDirectoryInfo[] repoDirectories, CmfPackage cmfPackage, bool includeTestPackages)
        {
            // Load package from repo if is not loaded yet
            if (cmfPackage == null || (cmfPackage != null && cmfPackage.Uri == null))
            {
                string packageName = cmfPackage.PackageName;
                cmfPackage = CmfPackage.LoadFromRepo(repoDirectories, cmfPackage.PackageId, cmfPackage.Version);
                if(cmfPackage == null)
                {
                    string errorMessage = string.Format(CoreMessages.NotFound, packageName);
                    throw new CliException(errorMessage);
                }
            }

            string destinationFile = $"{outputDir.FullName}/{cmfPackage.Uri.Segments.Last()}";
            if (fileSystem.File.Exists(destinationFile))
            {
                fileSystem.File.Delete(destinationFile);
            }

            Log.Information(string.Format(CliMessages.GetPackage, cmfPackage.PackageId, cmfPackage.Version));
            if(cmfPackage.SharedFolder == null)
            {
                cmfPackage.Uri.GetFile().CopyTo(destinationFile);
            }
            else
            {
                var file = cmfPackage.SharedFolder.GetFile(cmfPackage.ZipPackageName);
                using var fileStream = file.Item2;
                fileStream.Position = 0; // Reset stream position to the beginning
                using var fileStreamOutput = fileSystem.File.Create(destinationFile);
                fileStream.CopyTo(fileStreamOutput);
            }

            // Assemble Tests
            if (includeTestPackages)
            {
                AssembleTestPackages(outputDir, repoDirectories, cmfPackage);
            }
        }

        #endregion
    }
}
