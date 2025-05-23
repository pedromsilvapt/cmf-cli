using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cmf.CLI.Core;
using Cmf.CLI.Core.Interfaces;
using Cmf.CLI.Core.Objects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using tests.Mocks;
using Xunit;
using ExecutionContext = Cmf.CLI.Core.Objects.ExecutionContext;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
namespace tests.Specs
{
    public class Startup
    {
        #region mock services
        class MockNPMClient999 : INPMClient
        {
            public Task<string> GetLatestVersion(bool preRelease = false)
            {
                return Task.FromResult("999.99.99");
            }

            public IPackage[] FindPlugins(Uri[] registries)
            {
                throw new NotImplementedException();
            }
        }
        
        class MockNPMClientCurrent : INPMClient
        {
            public Task<string> GetLatestVersion(bool preRelease = false)
            {
                return Task.FromResult(ExecutionContext.CurrentVersion);
            }

            public IPackage[] FindPlugins(Uri[] registries)
            {
                throw new NotImplementedException();
            }
        }

        class MockVersionServiceDev : IVersionService
        {
            public string PackageId => "test";
            public string CurrentVersion => "1.0.0-0";
        }


        #endregion

        [Fact]
        public async Task NotAtLatestVersion()
        {
            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<INPMClient, MockNPMClient999>()
                .AddSingleton<IVersionService, MockVersionService>()
                .AddSingleton<ITelemetryService, MockTelemetryService>()
                .BuildServiceProvider();

            var logWriter = (new Logging()).GetLogStringWriter();
            
            await StartupModule.VersionChecks();
            
            logWriter.ToString().Should().Contain("Please update");
        }
        
        [Fact]
        public async Task AtLatestVersion()
        {
            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<INPMClient, MockNPMClientCurrent>()
                .AddSingleton<IVersionService, MockVersionService>()
                .AddSingleton<ITelemetryService, MockTelemetryService>()
                .BuildServiceProvider();

            var logWriter = (new Logging()).GetLogStringWriter();
            
            await StartupModule.VersionChecks();
            
            logWriter.ToString().Should().NotContain("Please update");
        }

        [Fact]
        public async Task InDevVersion()
        {
            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<INPMClient, MockNPMClientCurrent>()
                .AddSingleton<IVersionService, MockVersionServiceDev>()
                .AddSingleton<ITelemetryService, MockTelemetryService>()
                .BuildServiceProvider();
            
            var logWriter = (new Logging()).GetLogStringWriter();
            
            await StartupModule.VersionChecks();
            
            logWriter.ToString().Should().Contain("You are using test development version");
        }
        
        [Fact]
        public async Task InStableVersion()
        {
            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<INPMClient, MockNPMClientCurrent>()
                .AddSingleton<IVersionService, MockVersionService>()
                .AddSingleton<ITelemetryService, MockTelemetryService>()
                .BuildServiceProvider();
            
            var logWriter = (new Logging()).GetLogStringWriter();
            
            await StartupModule.VersionChecks();
            
            logWriter.ToString().Should().NotContain("You are using test development version");
        }
        
        [Fact]
        public async Task VersionCheckFailed()
        {
            var repositoryAuthStoreMock = new Mock<IRepositoryAuthStore>();
            repositoryAuthStoreMock.Setup(x => x.GetOrLoad()).ReturnsAsync(new CmfAuthFile());

            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<IVersionService, MockVersionService>()
                .AddSingleton(repositoryAuthStoreMock.Object)
                .BuildServiceProvider();
            
            var logWriter = (new Logging()).GetLogStringWriter();
            
            // mock HttpClient
            var httpMessageHandler = new Mock<HttpMessageHandler>();
            httpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    HttpResponseMessage response = new HttpResponseMessage();
                    
                    response.StatusCode = HttpStatusCode.InternalServerError;

                    return response;
                });
            var httpClient = new HttpClient(httpMessageHandler.Object);

            var npmClient = new NPMClient(httpClient);

            await npmClient.GetLatestVersion();
            
            logWriter.ToString().Should().Contain("Could not retrieve test latest version information");
        }
        
        [Theory]
        [InlineData(true, "3.1.3-1")]
        [InlineData(false, "3.1.3")]
        public async Task CheckVersionJSONParse(bool prerelease, string expectedVersion)
        {
            var repositoryAuthStoreMock = new Mock<IRepositoryAuthStore>();
            repositoryAuthStoreMock.Setup(x => x.GetOrLoad()).ReturnsAsync(new CmfAuthFile());

            ExecutionContext.ServiceProvider = (new ServiceCollection())
                .AddSingleton<IVersionService, MockVersionServiceDev>()
                .AddSingleton(repositoryAuthStoreMock.Object)
                .BuildServiceProvider();
            
            var content = @"{""name"": ""@criticalmanufacturing/cli"",""dist-tags"": {""latest"": ""3.1.3"",""next"": ""3.1.3-1""}}";

            // mock HttpClient
            var httpMessageHandler = new Mock<HttpMessageHandler>();
            httpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync((HttpRequestMessage request, CancellationToken token) =>
                {
                    HttpResponseMessage response = new HttpResponseMessage();

                    response.Content = new StringContent(content);
                    response.StatusCode = HttpStatusCode.OK;

                    return response;
                });
            var httpClient = new HttpClient(httpMessageHandler.Object);

            var npmClient = new NPMClient(httpClient);

            var version = await npmClient.GetLatestVersion(prerelease);

            version.Should().Be(expectedVersion);
        }
        
    }
}