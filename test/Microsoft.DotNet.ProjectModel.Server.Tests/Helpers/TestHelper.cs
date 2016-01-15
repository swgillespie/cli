﻿using System;
using System.IO;
using Microsoft.DotNet.ProjectModel.Graph;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ProjectModel.Server.Tests.Helpers
{
    public class TestHelper
    {
        private readonly string _tempPath;

        public TestHelper()
        {
            LoggerFactory = new LoggerFactory();

            var testVerbose = Environment.GetEnvironmentVariable("DOTNET_TEST_VERBOSE");
            if (testVerbose == "2")
            {
                LoggerFactory.AddConsole(LogLevel.Trace);
            }
            else if (testVerbose == "1")
            {
                LoggerFactory.AddConsole(LogLevel.Information);
            }
            else
            {
                LoggerFactory.AddConsole(LogLevel.Warning);
            }

            _tempPath = CreateTempFolder();
            var dthTestProjectsFolder = Path.Combine(FindRoot(), "testapp", "DthTestProjects");
            CopyFiles(dthTestProjectsFolder, _tempPath);

            var logger = LoggerFactory.CreateLogger<TestHelper>();
            logger.LogInformation($"Test projects are copied to {_tempPath}");
        }

        public ILoggerFactory LoggerFactory { get; }

        public string FindSampleProject(string name)
        {
            var result = Path.Combine(_tempPath, "src", name);
            if (Directory.Exists(result))
            {
                return result;
            }
            else
            {
                return null;
            }
        }

        public string CreateSampleProject(string name)
        {
            var source = Path.Combine(FindRoot(), "test", name);
            if (!Directory.Exists(source))
            {
                return null;
            }

            var target = Path.Combine(CreateTempFolder(), name);
            CopyFiles(source, target);

            return target;
        }

        public string BuildProjectCopy(string projectName)
        {
            var projectPath = FindSampleProject(projectName);
            var movedProjectPath = Path.Combine(CreateTempFolder(), projectName);
            CopyFiles(projectPath, movedProjectPath);

            return movedProjectPath;
        }

        public void DeleteLockFile(string folder)
        {
            var lockFilePath = Path.Combine(folder, LockFile.FileName);
            if (File.Exists(lockFilePath))
            {
                File.Delete(lockFilePath);
            }
        }

        private static string FindRoot()
        {
            var solutionName = "Microsoft.DotNet.Cli.sln";
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (root != null && root.GetFiles(solutionName).Length == 0)
            {
                root = Directory.GetParent(root.FullName);
            }

            if (root != null)
            {
                return root.FullName;
            }
            else
            {
                return null;
            }
        }

        private static string CreateTempFolder()
        {
            var result = Path.GetTempFileName();
            File.Delete(result);
            Directory.CreateDirectory(result);

            return result;
        }

        private static void CopyFiles(string sourceFolder, string targetFolder)
        {
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            foreach (var filePath in Directory.EnumerateFiles(sourceFolder))
            {
                var filename = Path.GetFileName(filePath);
                File.Copy(filePath, Path.Combine(targetFolder, filename));
            }

            foreach (var folderPath in Directory.EnumerateDirectories(sourceFolder))
            {
                var folderName = new DirectoryInfo(folderPath).Name;
                CopyFiles(folderPath, Path.Combine(targetFolder, folderName));
            }
        }
    }
}
