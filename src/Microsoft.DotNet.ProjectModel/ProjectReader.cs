// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.DotNet.ProjectModel.Files;
using Microsoft.DotNet.ProjectModel.Graph;
using Microsoft.DotNet.ProjectModel.Utilities;
using Microsoft.Extensions.DependencyModel.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Microsoft.DotNet.ProjectModel
{
    public class ProjectReader
    {
        public static bool TryGetProject(string path, out Project project, ICollection<DiagnosticMessage> diagnostics = null, ProjectReaderSettings settings = null)
        {
            project = null;

            string projectPath = null;

            if (string.Equals(Path.GetFileName(path), Project.FileName, StringComparison.OrdinalIgnoreCase))
            {
                projectPath = path;
                path = Path.GetDirectoryName(path);
            }
            else if (!HasProjectFile(path))
            {
                return false;
            }
            else
            {
                projectPath = Path.Combine(path, Project.FileName);
            }

            // Assume the directory name is the project name if none was specified
            var projectName = PathUtility.GetDirectoryName(Path.GetFullPath(path));
            projectPath = Path.GetFullPath(projectPath);

            if (!File.Exists(projectPath))
            {
                return false;
            }

            try
            {
                using (var stream = File.OpenRead(projectPath))
                {
                    var reader = new ProjectReader();
                    project = reader.ReadProject(stream, projectName, projectPath, diagnostics, settings);
                }
            }
            catch (Exception ex)
            {
                throw FileFormatException.Create(ex, projectPath);
            }

            return true;
        }

        public static Project GetProject(string projectFile, ProjectReaderSettings settings = null)
        {
            return GetProject(projectFile, new List<DiagnosticMessage>(), settings);
        }

        public static Project GetProject(string projectFile, ICollection<DiagnosticMessage> diagnostics, ProjectReaderSettings settings = null)
        {
            var name = Path.GetFileName(Path.GetDirectoryName(projectFile));

            using (var stream = new FileStream(projectFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return new ProjectReader().ReadProject(stream, name, projectFile, diagnostics, settings);
            }
        }

        public Project ReadProject(Stream stream, string projectName, string projectPath, ICollection<DiagnosticMessage> diagnostics, ProjectReaderSettings settings = null)
        {
            settings = settings ?? new ProjectReaderSettings();
            var project = new Project();

            JObject rawProject = null;
            using (var reader = new JsonTextReader(new StreamReader(stream)) { CloseInput = false })
            {
                rawProject = JToken.ReadFrom(reader) as JObject;
            }
            
            if (rawProject == null)
            {
                throw FileFormatException.Create(
                    "The JSON file can't be deserialized to a JSON object.",
                    projectPath);
            }

            // Meta-data properties
            project.Name = rawProject.Value<string>("name") ?? projectName;
            project.ProjectFilePath = Path.GetFullPath(projectPath);

            var version = rawProject["version"];
            if (version == null)
            {
                project.Version = new NuGetVersion("1.0.0");
            }
            else
            {
                try
                {
                    var buildVersion = settings.VersionSuffix;
                    project.Version = SpecifySnapshot(version.Value<string>(), buildVersion);
                }
                catch (Exception ex)
                {
                    throw FileFormatException.Create(ex, version, project.ProjectFilePath);
                }
            }

            var fileVersion = settings.AssemblyFileVersion;
            if (string.IsNullOrWhiteSpace(fileVersion))
            {
                project.AssemblyFileVersion = project.Version.Version;
            }
            else
            {
                try
                {
                    var simpleVersion = project.Version.Version;
                    project.AssemblyFileVersion = new Version(simpleVersion.Major,
                        simpleVersion.Minor,
                        simpleVersion.Build,
                        int.Parse(fileVersion));
                }
                catch (FormatException ex)
                {
                    throw new FormatException("The assembly file version is invalid: " + fileVersion, ex);
                }
            }

            project.Description = rawProject.Value<string>("description");
            project.Summary = rawProject.Value<string>("summary");
            project.Copyright = rawProject.Value<string>("copyright");
            project.Title = rawProject.Value<string>("title");
            project.EntryPoint = rawProject.Value<string>("entryPoint");
            project.ProjectUrl = rawProject.Value<string>("projectUrl");
            project.LicenseUrl = rawProject.Value<string>("licenseUrl");
            project.IconUrl = rawProject.Value<string>("iconUrl");
            project.CompilerName = rawProject.Value<string>("compilerName");
            project.TestRunner = rawProject.Value<string>("testRunner");

            project.Authors = rawProject.Value<JArray>("authors")?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>();
            project.Owners = rawProject.Value<JArray>("owners")?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>();
            project.Tags = rawProject.Value<JArray>("tags")?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>();

            project.Language = rawProject.Value<string>("language");
            project.ReleaseNotes = rawProject.Value<string>("releaseNotes");

            project.RequireLicenseAcceptance = rawProject.Value<bool?>("requireLicenseAcceptance") ?? false;

            // REVIEW: Move this to the dependencies node?
            project.EmbedInteropTypes = rawProject.Value<bool?>("embedInteropTypes") ?? false;

            project.Dependencies = new List<LibraryRange>();
            project.Tools = new List<LibraryRange>();

            // Project files
            project.Files = new ProjectFilesCollection(rawProject, project.ProjectDirectory, project.ProjectFilePath);

            var commands = rawProject.Value<JObject>("commands");
            if (commands != null)
            {
                foreach (var command in commands)
                {
                    var value = command.Value.Value<string>();
                    if (value != null)
                    {
                        project.Commands[command.Key] = value;
                    }
                }
            }

            var scripts = rawProject.Value<JObject>("scripts");
            if (scripts != null)
            {
                foreach (var script in scripts)
                {
                    if (script.Value.Type == JTokenType.String)
                    {
                        project.Scripts[script.Key] = new string[] { script.Value.Value<string>() };
                        continue;
                    }

                    if (script.Value.Type == JTokenType.Array)
                    {
                        project.Scripts[script.Key] = script.Value.Value<JArray>()?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>();
                        continue;
                    }

                    throw FileFormatException.Create(
                        string.Format("The value of a script in {0} can only be a string or an array of strings", Project.FileName),
                        script.Value,
                        project.ProjectFilePath);
                }
            }

            BuildTargetFrameworksAndConfigurations(project, rawProject, diagnostics);

            PopulateDependencies(
                project.ProjectFilePath,
                project.Dependencies,
                rawProject,
                "dependencies",
                isGacOrFrameworkReference: false);

            PopulateDependencies(
                project.ProjectFilePath,
                project.Tools,
                rawProject,
                "tools",
                isGacOrFrameworkReference: false);

            return project;
        }

        private static NuGetVersion SpecifySnapshot(string version, string snapshotValue)
        {
            if (version.EndsWith("-*"))
            {
                if (string.IsNullOrEmpty(snapshotValue))
                {
                    version = version.Substring(0, version.Length - 2);
                }
                else
                {
                    version = version.Substring(0, version.Length - 1) + snapshotValue;
                }
            }

            return new NuGetVersion(version);
        }

        private static void PopulateDependencies(
            string projectPath,
            IList<LibraryRange> results,
            JObject settings,
            string propertyName,
            bool isGacOrFrameworkReference)
        {
            var dependencies = settings.Value<JObject>(propertyName);
            if (dependencies != null)
            {
                foreach (var dependency in dependencies)
                {
                    if (string.IsNullOrEmpty(dependency.Key))
                    {
                        throw FileFormatException.Create(
                            "Unable to resolve dependency ''.",
                            dependency.Value,
                            projectPath);
                    }

                    var dependencyTypeValue = LibraryDependencyType.Default;
                    string dependencyVersionAsString = null;
                    LibraryType target = isGacOrFrameworkReference ? LibraryType.ReferenceAssembly : LibraryType.Unspecified;

                    if (dependency.Value.Type == JTokenType.Object)
                    {
                        // "dependencies" : { "Name" : { "version": "1.0", "type": "build", "target": "project" } }
                        var dependencyValueAsObject = dependency.Value.Value<JObject>();
                        dependencyVersionAsString = dependencyValueAsObject.Value<string>("version");

                        var type = dependencyValueAsObject.Value<string>("type");
                        if (type != null)
                        {
                            dependencyTypeValue = LibraryDependencyType.Parse(type);
                        }

                        // Read the target if specified
                        if (!isGacOrFrameworkReference)
                        {
                            LibraryType parsedTarget;
                            var targetStr = dependencyValueAsObject.Value<string>("target");
                            if (!string.IsNullOrEmpty(targetStr) && LibraryType.TryParse(targetStr, out parsedTarget))
                            {
                                target = parsedTarget;
                            }
                        }
                    }
                    else if (dependency.Value.Type == JTokenType.String)
                    {
                        // "dependencies" : { "Name" : "1.0" }
                        dependencyVersionAsString = dependency.Value.Value<string>();
                    }
                    else
                    {
                        throw FileFormatException.Create(
                            string.Format("Invalid dependency version: {0}. The format is not recognizable.", dependency.Key),
                            dependency.Value,
                            projectPath);
                    }

                    VersionRange dependencyVersionRange = null;
                    if (!string.IsNullOrEmpty(dependencyVersionAsString))
                    {
                        try
                        {
                            dependencyVersionRange = VersionRange.Parse(dependencyVersionAsString);
                        }
                        catch (Exception ex)
                        {
                            throw FileFormatException.Create(
                                ex,
                                dependency.Value,
                                projectPath);
                        }
                    }

                    results.Add(new LibraryRange(
                        dependency.Key,
                        dependencyVersionRange,
                        target,
                        dependencyTypeValue,
                        projectPath,
                        ((IJsonLineInfo)dependencies[dependency.Key]).LineNumber,
                        ((IJsonLineInfo)dependencies[dependency.Key]).LinePosition));
                }
            }
        }

        private static bool TryGetStringEnumerable(JObject parent, string property, out IEnumerable<string> result)
        {
            var collection = new List<string>();
            if (parent[property].Type == JTokenType.String)
            {
                collection.Add(parent.Value<string>(property));
            }
            else
            {
                var valueInArray = parent.Value<JArray>(property)?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>();
                if (valueInArray != null)
                {
                    collection.AddRange(valueInArray);
                }
                else
                {
                    result = null;
                    return false;
                }
            }

            result = collection.SelectMany(value => value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
            return true;
        }

        private void BuildTargetFrameworksAndConfigurations(Project project, JObject projectJsonObject, ICollection<DiagnosticMessage> diagnostics)
        {
            // Get the shared compilationOptions
            project._defaultCompilerOptions = GetCompilationOptions(projectJsonObject) ?? new CommonCompilerOptions();

            project._defaultTargetFrameworkConfiguration = new TargetFrameworkInformation
            {
                Dependencies = new List<LibraryRange>()
            };

            // Add default configurations
            project._compilerOptionsByConfiguration["Debug"] = new CommonCompilerOptions
            {
                Defines = new[] { "DEBUG", "TRACE" },
                Optimize = false
            };

            project._compilerOptionsByConfiguration["Release"] = new CommonCompilerOptions
            {
                Defines = new[] { "RELEASE", "TRACE" },
                Optimize = true
            };

            // The configuration node has things like debug/release compiler settings
            /*
                {
                    "configurations": {
                        "Debug": {
                        },
                        "Release": {
                        }
                    }
                }
            */

            var configurationsSection = projectJsonObject.Value<JObject>("configurations");
            if (configurationsSection != null)
            {
                foreach (var config in configurationsSection)
                {
                    var compilerOptions = GetCompilationOptions(config.Value.Value<JObject>());

                    // Only use this as a configuration if it's not a target framework
                    project._compilerOptionsByConfiguration[config.Key] = compilerOptions;
                }
            }

            // The frameworks node is where target frameworks go
            /*
                {
                    "frameworks": {
                        "net45": {
                        },
                        "dnxcore50": {
                        }
                    }
                }
            */

            var frameworks = projectJsonObject.Value<JObject>("frameworks");
            if (frameworks != null)
            {
                foreach (var framework in frameworks)
                {
                    try
                    {
                        var success = BuildTargetFrameworkNode(project, framework.Key, framework.Value.Value<JObject>());
                        if (!success)
                        {
                            diagnostics?.Add(
                                new DiagnosticMessage(
                                    ErrorCodes.NU1008,
                                    $"\"{framework.Key}\" is an unsupported framework.",
                                    project.ProjectFilePath,
                                    DiagnosticMessageSeverity.Error,
                                    ((IJsonLineInfo)framework.Value).LineNumber,
                                    ((IJsonLineInfo)framework.Value).LinePosition));
                        }
                    }
                    catch (Exception ex)
                    {
                        throw FileFormatException.Create(ex, framework.Value, project.ProjectFilePath);
                    }
                }
            }
        }

        /// <summary>
        /// Parse a Json object which represents project configuration for a specified framework
        /// </summary>
        /// <param name="frameworkKey">The name of the framework</param>
        /// <param name="frameworkValue">The Json object represent the settings</param>
        /// <returns>Returns true if it successes.</returns>
        private bool BuildTargetFrameworkNode(Project project, string frameworkKey, JObject frameworkValue)
        {
            // If no compilation options are provided then figure them out from the node
            var compilerOptions = GetCompilationOptions(frameworkValue) ??
                                  new CommonCompilerOptions();

            var frameworkName = NuGetFramework.Parse(frameworkKey);

            // If it's not unsupported then keep it
            if (frameworkName.IsUnsupported)
            {
                // REVIEW: Should we skip unsupported target frameworks
                return false;
            }

            // Add the target framework specific define
            var defines = new HashSet<string>(compilerOptions.Defines ?? Enumerable.Empty<string>());
            var frameworkDefine = MakeDefaultTargetFrameworkDefine(frameworkName);

            if (!string.IsNullOrEmpty(frameworkDefine))
            {
                defines.Add(frameworkDefine);
            }

            compilerOptions.Defines = defines;

            var targetFrameworkInformation = new TargetFrameworkInformation
            {
                FrameworkName = frameworkName,
                Dependencies = new List<LibraryRange>(),
                CompilerOptions = compilerOptions,
                Line = ((IJsonLineInfo)frameworkValue).LineNumber,
                Column = ((IJsonLineInfo)frameworkValue).LinePosition
            };

            var frameworkDependencies = new List<LibraryRange>();

            PopulateDependencies(
                project.ProjectFilePath,
                frameworkDependencies,
                frameworkValue,
                "dependencies",
                isGacOrFrameworkReference: false);

            var frameworkAssemblies = new List<LibraryRange>();
            PopulateDependencies(
                project.ProjectFilePath,
                frameworkAssemblies,
                frameworkValue,
                "frameworkAssemblies",
                isGacOrFrameworkReference: true);

            frameworkDependencies.AddRange(frameworkAssemblies);
            targetFrameworkInformation.Dependencies = frameworkDependencies;

            targetFrameworkInformation.WrappedProject = frameworkValue.Value<string>("wrappedProject");

            var binNode = frameworkValue.Value<JObject>("bin");
            if (binNode != null)
            {
                targetFrameworkInformation.AssemblyPath = binNode.Value<string>("assembly");
                targetFrameworkInformation.PdbPath = binNode.Value<string>("pdb");
            }

            project._targetFrameworks[frameworkName] = targetFrameworkInformation;

            return true;
        }

        private static CommonCompilerOptions GetCompilationOptions(JObject rawObject)
        {
            var rawOptions = rawObject.Value<JObject>("compilationOptions");
            if (rawOptions == null)
            {
                return null;
            }

            return new CommonCompilerOptions
            {
                Defines = rawOptions.Value<JArray>("define")?.Select(a => a.Value<string>())?.ToArray() ?? Array.Empty<string>(),
                LanguageVersion = rawOptions.Value<string>("languageVersion"),
                AllowUnsafe = rawOptions.Value<bool?>("allowUnsafe"),
                Platform = rawOptions.Value<string>("platform"),
                WarningsAsErrors = rawOptions.Value<bool?>("warningsAsErrors"),
                Optimize = rawOptions.Value<bool?>("optimize"),
                KeyFile = rawOptions.Value<string>("keyFile"),
                DelaySign = rawOptions.Value<bool?>("delaySign"),
                PublicSign = rawOptions.Value<bool?>("publicSign"),
                EmitEntryPoint = rawOptions.Value<bool?>("emitEntryPoint"),
                GenerateXmlDocumentation = rawOptions.Value<bool?>("xmlDoc"),
                PreserveCompilationContext = rawOptions.Value<bool?>("preserveCompilationContext")
            };
        }

        private static string MakeDefaultTargetFrameworkDefine(NuGetFramework targetFramework)
        {
            var shortName = targetFramework.GetTwoDigitShortFolderName();

            if (targetFramework.IsPCL)
            {
                return null;
            }

            var candidateName = shortName.ToUpperInvariant();

            // Replace '-', '.', and '+' in the candidate name with '_' because TFMs with profiles use those (like "net40-client")
            // and we want them representable as defines (i.e. "NET40_CLIENT")
            candidateName = candidateName.Replace('-', '_').Replace('+', '_').Replace('.', '_');

            // We require the following from our Target Framework Define names
            // Starts with A-Z or _
            // Contains only A-Z, 0-9 and _
            if (!string.IsNullOrEmpty(candidateName) &&
                (char.IsLetter(candidateName[0]) || candidateName[0] == '_') &&
                candidateName.All(c => Char.IsLetterOrDigit(c) || c == '_'))
            {
                return candidateName;
            }

            return null;
        }

        private static bool HasProjectFile(string path)
        {
            string projectPath = Path.Combine(path, Project.FileName);

            return File.Exists(projectPath);
        }
    }
}
