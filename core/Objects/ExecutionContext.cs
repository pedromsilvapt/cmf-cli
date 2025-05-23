using Cmf.CLI.Core.Interfaces;
using Cmf.CLI.Core.Repository.Credentials;
using Cmf.CLI.Utilities;
using Core.Objects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Utils;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cmf.CLI.Core.Objects
{
    /// <summary>
    /// The current execution context
    /// </summary>
    public class ExecutionContext
    {
        private static ExecutionContext instance;
        private IFileSystem fileSystem;

        /// <summary>
        /// The current ExecutionContext object
        /// </summary>
        public static ExecutionContext Instance => instance;

        /// <summary>
        /// The current FileSystem object
        /// </summary>
        public IFileSystem FileSystem => fileSystem;

        /// <summary>
        /// The current execution RepositoriesConfig object
        /// </summary>
        public RepositoriesConfig RepositoriesConfig { get; set; }

        /// <summary>
        /// the current repository's project config
        /// </summary>
        public ProjectConfig ProjectConfig { get; private set; }

        /// <summary>
        /// Get the current (executing) version of the CLI
        /// </summary>
        public static string CurrentVersion => (ServiceProvider.GetService<IVersionService>()!.CurrentVersion) ?? "dev";

        /// <summary>
        /// Get or set the latest vetsion of the CLI. Use this if the CLI checks for new versions
        /// </summary>
        public static string LatestVersion { get; set; }

        /// <summary>
        /// Get the package id of the current running application
        /// </summary>
        public static string PackageId => (ServiceProvider.GetService<IVersionService>()!.PackageId) ?? "unknown";

        /// <summary>
        /// true if we're running a development/unstable version 
        /// </summary>
        public static bool IsDevVersion => CurrentVersion.Contains("-");

        /// <summary>
        /// IoC container for services
        /// NOTE: As we already have this ExecutionContext object, we're not enabling Hosting, but instead we are hosting the container in the execution context
        /// </summary>
        public static ServiceProvider ServiceProvider { get; set; }

        /// <summary>
        /// Is the current CLI outdated.
        /// True if LatestVersion is not null (meaning a check was performed) and if the LatestVersion does not match the CurrentVersion
        /// </summary>
        public static bool IsOutdated => ExecutionContext.LatestVersion != null && ExecutionContext.CurrentVersion != ExecutionContext.LatestVersion;

        /// <summary>
        /// Prefix that should be used in environment variables defition in the whole aplication
        /// example: telemetry service envars use this prefix
        /// </summary>
        public static string EnvVarPrefix { get; set; }

        /// <summary>
        /// Cache of the Related Packages
        /// </summary>
        public static RelatedPackageCollection RelatedPackagesCache { get; set; }

        public bool RunningOnWindows { get; set; }

        public List<ICIFSClient> CIFSClients { get; set; }

        private ExecutionContext(IFileSystem fileSystem)
        {
            RunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            // private constructor, can only obtain instance via the Instance property
            this.fileSystem = fileSystem;
            this.RepositoriesConfig = FileSystemUtilities.ReadRepositoriesConfig(fileSystem);
            if (ServiceProvider != null)
            {
                IProjectConfigService pcs = ServiceProvider.GetService<IProjectConfigService>();
                if (pcs != null)
                {
                    this.ProjectConfig = pcs.Load(fileSystem);
                }
            }

            // connect and load shares for all UNC repositories
            if (!RunningOnWindows && RepositoriesConfig.Repositories.HasAny())
            {
                CIFSClients = RepositoriesConfig?.Repositories
                    .Where(uri => uri.IsUnc)
                    .Select(uri => new CIFSClient(uri) as ICIFSClient)
                    .ToList() ?? [];
            }

            RelatedPackagesCache = new();
        }

        /// <summary>
        /// Initialize the current ExecutionContext instance
        /// </summary>
        public static ExecutionContext Initialize(IFileSystem fileSystem) => instance = new ExecutionContext(fileSystem);
    }
}
