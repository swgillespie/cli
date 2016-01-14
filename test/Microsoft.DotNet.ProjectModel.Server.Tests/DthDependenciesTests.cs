using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.ProjectModel.Server.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.DotNet.ProjectModel.Server.Tests
{
    public class DthDependenciesTests 
    {
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        public DthDependenciesTests()
        {
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddConsole();
            _logger = _loggerFactory.CreateLogger<DthDependenciesTests>();
        }

        [Fact]
        public void AddFramework()
        {
            var projectPath = @"C:\code\aspnet5-beta8\src\RC1SignOff";
            _logger.LogInformation($"Project {projectPath}");

            var projectJson = Path.Combine(projectPath, "project.json");
            var backup = Path.Combine(projectPath, "project.json.backup");

            // remove one framework
            var project = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(Path.Combine(projectPath, "project.json")));
            project["frameworks"].First().Remove();

            File.Copy(projectJson, backup, overwrite:true);
            File.WriteAllText(projectJson, JsonConvert.SerializeObject(project, Formatting.Indented));

            // restore
            Restore(projectPath);

            using (var server = new DthTestServer(_loggerFactory))
            using (var client = new DthTestClient(server))
            {
                client.Initialize(projectPath);
                client.DrainAllMessages();

                _logger.LogWarning(">>> update project.json");
                File.Copy(backup, projectJson, overwrite: true);
                File.SetLastWriteTime(projectJson, DateTime.Now);

                client.SendPayLoad(projectPath, MessageTypes.FilesChanged);
                client.DrainAllMessages();
            }
        }

        private void Restore(string projectPath)
        {
            var processInfo = new ProcessStartInfo("dotnet", "restore")
            {
                WorkingDirectory = projectPath,
                UseShellExecute = false
            };

            var process = Process.Start(processInfo);
            process.WaitForExit();
        }
    }
}
