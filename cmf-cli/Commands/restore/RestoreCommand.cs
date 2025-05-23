using Cmf.CLI.Constants;
using Cmf.CLI.Core.Attributes;
using Cmf.CLI.Core.Interfaces;
using Cmf.CLI.Core.Objects;
using Cmf.CLI.Factories;
using Cmf.CLI.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.IO.Abstractions;

namespace Cmf.CLI.Commands.restore
{
    /// <summary>
    /// Restore package dependencies (declared cmfpackage.json) from repository packages
    /// </summary>
    [CmfCommand("restore", Id = "restore")]
    public class RestoreCommand : BaseCommand
    {
        #region Constructors

        /// <summary>
        /// Restore Command
        /// </summary>
        public RestoreCommand() : base() { }

        /// <summary>
        /// Restore Command
        /// </summary>
        /// <param name="fileSystem"></param>
        public RestoreCommand(IFileSystem fileSystem) : base(fileSystem) { }

        #endregion Constructors

        /// <summary>
        /// Configure the Restore command options and arguments
        /// </summary>
        /// <param name="cmd"></param>
        public override void Configure(Command cmd)
        {
            cmd.AddOption(new Option<Uri[]>(
                aliases: new string[] { "-r", "--repos", "--repo" },
                description: "Repositories where dependencies are located (folder)"));

            var packageRoot = FileSystemUtilities.GetPackageRoot(this.fileSystem);
            var packagePath = ".";
            if (packageRoot != null)
            {
                packagePath = this.fileSystem.Path.GetRelativePath(this.fileSystem.Directory.GetCurrentDirectory(), packageRoot.FullName);
            }
            var arg = new Argument<IDirectoryInfo>(
                name: "packagePath",
                parse: (argResult) => Parse<IDirectoryInfo>(argResult, packagePath),
                isDefault: true,
                description: "Package path");
            cmd.AddArgument(arg);
            cmd.Handler = CommandHandler.Create<IDirectoryInfo, Uri[]>(Execute);
        }

        /// <summary>
        /// Execute the restore command
        /// </summary>
        /// <param name="packagePath">The path of the current package folder</param>
        /// <param name="repos">The package repositories URI/path</param>
        public void Execute(IDirectoryInfo packagePath, Uri[] repos)
        {
            using var activity = ExecutionContext.ServiceProvider?.GetService<ITelemetryService>()?.StartExtendedActivity(this.GetType().Name);
            IFileInfo cmfpackageFile = this.fileSystem.FileInfo.New($"{packagePath}/{CliConstants.CmfPackageFileName}");
            IPackageTypeHandler packageTypeHandler = PackageTypeFactory.GetPackageTypeHandler(cmfpackageFile, setDefaultValues: false);
            if (repos != null)
            {
                ExecutionContext.Instance.RepositoriesConfig.Repositories.InsertRange(0, repos);
            }
           
            packageTypeHandler.RestoreDependencies(ExecutionContext.Instance.RepositoriesConfig.Repositories.ToArray());
        }
    }
}