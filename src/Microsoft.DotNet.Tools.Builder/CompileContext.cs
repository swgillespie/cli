﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.Tools.Compiler;
using Microsoft.DotNet.ProjectModel.Utilities;
using Microsoft.DotNet.Cli.Compiler.Common;

namespace Microsoft.DotNet.Tools.Build
{
    // todo: Convert CompileContext into a DAG of dependencies: if a node needs recompilation, the entire path up to root needs compilation
    // Knows how to orchestrate compilation for a ProjectContext
    // Collects icnremental safety checks and transitively compiles a project context
    internal class CompileContext
    {

        public static readonly string[] KnownCompilers = { "csc", "vbc", "fsc" };

        private readonly ProjectContext _rootProject;
        private readonly BuilderCommandApp _args;
        private readonly IncrementalPreconditions _preconditions;
        private readonly ProjectDependenciesFacade _dependencies;

        public bool IsSafeForIncrementalCompilation => !_preconditions.PreconditionsDetected();

        public CompileContext(ProjectContext rootProject, BuilderCommandApp args)
        {
            _rootProject = rootProject;

            // Cleaner to clone the args and mutate the clone than have separate CompileContext fields for mutated args 
            // and then reasoning which ones to get from args and which ones from fields.
            _args = (BuilderCommandApp) args.ShallowCopy();

            // Set up Output Paths. They are unique per each CompileContext
            _args.OutputValue = _rootProject.GetOutputPath(_args.ConfigValue, _args.OutputValue);
            _args.IntermediateValue = _rootProject.GetIntermediateOutputPath(_args.ConfigValue, _args.IntermediateValue, _args.OutputValue);

            // Set up dependencies
            _dependencies = new ProjectDependenciesFacade(_rootProject, _args.ConfigValue);

            // gather preconditions
            _preconditions = GatherIncrementalPreconditions();
        }

        public bool Compile(bool incremental)
        {
            CreateOutputDirectories();

            // compile dependencies
            foreach (var dependency in Sort(_dependencies.ProjectDependenciesWithSources))
            {
                if (incremental)
                {
                    var dependencyContext = ProjectContext.Create(dependency.Path, dependency.Framework);

                    if (!NeedsRebuilding(dependencyContext, new ProjectDependenciesFacade(dependencyContext, _args.ConfigValue)))
                    {
                        continue;
                    }
                }

                if (!InvokeCompileOnDependency(dependency))
                {
                    return false;
                }
            }

            if (incremental && !NeedsRebuilding(_rootProject, _dependencies))
            {
                // todo: what if the previous build had errors / warnings and nothing changed? Need to propagate them in case of incremental
                return true;
            }

            // compile project
            var success = InvokeCompileOnRootProject();

            PrintSummary(success);

            return success;
        }

        private bool NeedsRebuilding(ProjectContext project, ProjectDependenciesFacade dependencies)
        {
            var compilerIO = GetCompileIO(project, _args.ConfigValue, _args.OutputValue, _args.IntermediateValue, dependencies);

            // rebuild if empty inputs / outputs
            if (!(compilerIO.Outputs.Any() && compilerIO.Inputs.Any()))
            {
                Reporter.Verbose.WriteLine($"\nProject {project.ProjectName()} will be compiled because it either has empty inputs or outputs");
                return true;
            }

            //rebuild if missing inputs / outputs
            if (AnyMissingIO(project, compilerIO.Outputs, "outputs") || AnyMissingIO(project, compilerIO.Inputs, "inputs"))
            {
                return true;
            }

            // find the output with the earliest write time
            var minOutputPath = compilerIO.Outputs.First();
            var minDate = File.GetLastWriteTime(minOutputPath);

            foreach (var outputPath in compilerIO.Outputs)
            {
                if (File.GetLastWriteTime(outputPath) >= minDate)
                {
                    continue;
                }

                minDate = File.GetLastWriteTime(outputPath);
                minOutputPath = outputPath;
            }

            // find inputs that are older than the earliest output
            var newInputs = compilerIO.Inputs.FindAll(p => File.GetLastWriteTime(p) > minDate);

            if (!newInputs.Any())
            {
                Reporter.Verbose.WriteLine($"\nSkipped compilation for project {project.ProjectName()}. All the input files were older than the output files.");
                return false;
            }

            Reporter.Verbose.WriteLine($"\nProject {project.ProjectName()} was compiled because some of its inputs were newer than its oldest output:");
            Reporter.Verbose.WriteLine($"Oldest output item was written at {minDate} : {minOutputPath}");
            Reporter.Verbose.WriteLine($"Inputs newer than the oldest output item:");

            foreach (var newInput in newInputs)
            {
                Reporter.Verbose.WriteLine($"\t{File.GetLastWriteTime(newInput)}\t:\t{newInput}");
            }

            return true;
        }

        private static bool AnyMissingIO(ProjectContext project, IEnumerable<string> items, string itemsType)
        {
            var missingItems = items.Where(i => !File.Exists(i)).ToList();

            if (!missingItems.Any())
            {
                return false;
            }

            Reporter.Verbose.WriteLine($"\nProject {project.ProjectName()} will be compiled because expected {itemsType} are missing: ");

            foreach (var missing in missingItems)
            {
                Reporter.Verbose.WriteLine($"\t {missing}");
            }

            Reporter.Verbose.WriteLine();

            return true;
        }

        private void PrintSummary(bool success)
        {
            // todo: Ideally it's the builder's responsibility for adding the time elapsed. That way we avoid cross cutting display concerns between compile and build for printing time elapsed
            if (success)
            {
                Reporter.Output.Write(" " + _preconditions.LogMessage());
                Reporter.Output.WriteLine();
            }

            Reporter.Output.WriteLine();
        }

        private void CreateOutputDirectories()
        {
            Directory.CreateDirectory(_args.OutputValue);
            Directory.CreateDirectory(_args.IntermediateValue);
        }

        private IncrementalPreconditions GatherIncrementalPreconditions()
        {
            var preconditions = new IncrementalPreconditions(_args.BuildProfileValue);

            if (_args.ForceUnsafeValue)
            {
                preconditions.AddForceUnsafePrecondition();
            }

            var projectsToCheck = GetProjectsToCheck();

            foreach (var project in projectsToCheck)
            {
                CollectScriptPreconditions(project, preconditions);
                CollectCompilerNamePreconditions(project, preconditions);
                CollectCheckPathProbingPreconditions(project, preconditions);
            }

            return preconditions;
        }

        // check the entire project tree that needs to be compiled, duplicated for each framework
        private List<ProjectContext> GetProjectsToCheck()
        {
            // include initial root project
            var contextsToCheck = new List<ProjectContext>(1 + _dependencies.ProjectDependenciesWithSources.Count) {_rootProject};

            // convert ProjectDescription to ProjectContext
            var dependencyContexts = _dependencies.ProjectDependenciesWithSources.Select
                (keyValuePair => ProjectContext.Create(keyValuePair.Value.Path, keyValuePair.Value.Framework));

            contextsToCheck.AddRange(dependencyContexts);


            return contextsToCheck;
        }

        private void CollectCheckPathProbingPreconditions(ProjectContext project, IncrementalPreconditions preconditions)
        {
            var pathCommands = CompilerUtil.GetCommandsInvokedByCompile(project)
                .Select(commandName => Command.Create(commandName, "", project.TargetFramework))
                .Where(c => c.ResolutionStrategy.Equals(CommandResolutionStrategy.Path));

            foreach (var pathCommand in pathCommands)
            {
                preconditions.AddPathProbingPrecondition(project.ProjectName(), pathCommand.CommandName);
            }
        }

        private void CollectCompilerNamePreconditions(ProjectContext project, IncrementalPreconditions preconditions)
        {
            var projectCompiler = CompilerUtil.ResolveCompilerName(project);

            if (!KnownCompilers.Any(knownCompiler => knownCompiler.Equals(projectCompiler, StringComparison.Ordinal)))
            {
                preconditions.AddUnknownCompilerPrecondition(project.ProjectName(), projectCompiler);
            }
        }

        private void CollectScriptPreconditions(ProjectContext project, IncrementalPreconditions preconditions)
        {
            var preCompileScripts = project.ProjectFile.Scripts.GetOrEmpty(ScriptNames.PreCompile);
            var postCompileScripts = project.ProjectFile.Scripts.GetOrEmpty(ScriptNames.PostCompile);

            if (preCompileScripts.Any())
            {
                preconditions.AddPrePostScriptPrecondition(project.ProjectName(), ScriptNames.PreCompile);
            }

            if (postCompileScripts.Any())
            {
                preconditions.AddPrePostScriptPrecondition(project.ProjectName(), ScriptNames.PostCompile);
            }
        }

        private bool InvokeCompileOnDependency(ProjectDescription projectDependency)
        {
            var compileResult = Command.Create("dotnet-compile",
                $"--framework {projectDependency.Framework} " +
                $"--configuration {_args.ConfigValue} " +
                $"--output \"{_args.OutputValue}\" " +
                $"--temp-output \"{_args.IntermediateValue}\" " +
                (_args.NoHostValue ? "--no-host " : string.Empty) +
                $"\"{projectDependency.Project.ProjectDirectory}\"")
                .ForwardStdOut()
                .ForwardStdErr()
                .Execute();

            return compileResult.ExitCode == 0;
        }

        private bool InvokeCompileOnRootProject()
        {
            // todo: add methods to CompilerCommandApp to generate the arg string?
            var compileResult = Command.Create("dotnet-compile",
                $"--framework {_rootProject.TargetFramework} " +
                $"--configuration {_args.ConfigValue} " +
                $"--output \"{_args.OutputValue}\" " +
                $"--temp-output \"{_args.IntermediateValue}\" " +
                (_args.NoHostValue ? "--no-host " : string.Empty) +
                //nativeArgs
                (_args.IsNativeValue ? "--native " : string.Empty) +
                (_args.IsCppModeValue ? "--cpp " : string.Empty) +
                (!string.IsNullOrWhiteSpace(_args.ArchValue) ? $"--arch {_args.ArchValue} " : string.Empty) +
                (!string.IsNullOrWhiteSpace(_args.IlcArgsValue) ? $"--ilcargs \"{_args.IlcArgsValue}\" " : string.Empty) +
                (!string.IsNullOrWhiteSpace(_args.IlcPathValue) ? $"--ilcpath \"{_args.IlcPathValue}\" " : string.Empty) +
                (!string.IsNullOrWhiteSpace(_args.IlcSdkPathValue) ? $"--ilcsdkpath \"{_args.IlcSdkPathValue}\" " : string.Empty) +
                $"\"{_rootProject.ProjectDirectory}\"")
                .ForwardStdOut()
                .ForwardStdErr()
                .Execute();

            return compileResult.ExitCode == 0;
        }

        private static ISet<ProjectDescription> Sort(Dictionary<string, ProjectDescription> projects)
        {
            var outputs = new HashSet<ProjectDescription>();

            foreach (var pair in projects)
            {
                Sort(pair.Value, projects, outputs);
            }

            return outputs;
        }

        private static void Sort(ProjectDescription project, Dictionary<string, ProjectDescription> projects, ISet<ProjectDescription> outputs)
        {
            // Sorts projects in dependency order so that we only build them once per chain
            foreach (var dependency in project.Dependencies)
            {
                ProjectDescription projectDependency;
                if (projects.TryGetValue(dependency.Name, out projectDependency))
                {
                    Sort(projectDependency, projects, outputs);
                }
            }

            outputs.Add(project);
        }

        public struct CompilerIO
        {
            public readonly List<string> Inputs;
            public readonly List<string> Outputs;

            public CompilerIO(List<string> inputs, List<string> outputs)
            {
                Inputs = inputs;
                Outputs = outputs;
            }
        }

        // computes all the inputs and outputs that would be used in the compilation of a project
        // ensures that all paths are files
        // ensures no missing inputs
        public static CompilerIO GetCompileIO(ProjectContext project, string config, string outputPath, string intermediaryOutputPath, ProjectDependenciesFacade dependencies)
        {
            var compilerIO = new CompilerIO(new List<string>(), new List<string>());

            // input: project.json
            compilerIO.Inputs.Add(project.ProjectFile.ProjectFilePath);

            // input: source files
            compilerIO.Inputs.AddRange(CompilerUtil.GetCompilationSources(project));

            // todo: Factor out dependency resolution between Build and Compile. Ideally Build injects the dependencies into Compile
            // todo: use lock file insteaf of dependencies. One file vs many
            // input: dependencies
            AddDependencies(dependencies, compilerIO);

            // input: key file
            AddKeyFile(project, config, compilerIO);

            // output: compiler output
            compilerIO.Outputs.Add(CompilerUtil.GetCompilationOutput(project.ProjectFile, project.TargetFramework, config, outputPath));

            // input / output: resources without culture
            AddCultureResources(project, intermediaryOutputPath, compilerIO);

            // input / output: resources with culture
            AddNonCultureResources(project, outputPath, compilerIO);

            return compilerIO;
        }

        private static void AddDependencies(ProjectDependenciesFacade dependencies, CompilerIO compilerIO)
        {
            // add dependency sources that need compilation
            compilerIO.Inputs.AddRange(dependencies.ProjectDependenciesWithSources.Values.SelectMany(p => p.Project.Files.SourceFiles));

            // add compilation binaries
            compilerIO.Inputs.AddRange(dependencies.Dependencies.SelectMany(d => d.CompilationAssemblies.Select(ca => ca.ResolvedPath)));
        }

        private static void AddKeyFile(ProjectContext project, string config, CompilerIO compilerIO)
        {
            var keyFile = CompilerUtil.ResolveCompilationOptions(project, config).KeyFile;

            if (keyFile != null)
            {
                compilerIO.Inputs.Add(keyFile);
            }
        }

        private static void AddNonCultureResources(ProjectContext project, string intermediaryOutputPath, CompilerIO compilerIO)
        {
            foreach (var resourceIO in CompilerUtil.GetNonCultureResources(project.ProjectFile, intermediaryOutputPath))
            {
                compilerIO.Inputs.Add(resourceIO.InputFile);

                if (resourceIO.OutputFile != null)
                {
                    compilerIO.Outputs.Add(resourceIO.OutputFile);
                }
            }
        }

        private static void AddCultureResources(ProjectContext project, string outputPath, CompilerIO compilerIO)
        {
            foreach (var cultureResourceIO in CompilerUtil.GetCultureResources(project.ProjectFile, outputPath))
            {
                compilerIO.Inputs.AddRange(cultureResourceIO.InputFileToMetadata.Keys);

                if (cultureResourceIO.OutputFile != null)
                {
                    compilerIO.Outputs.Add(cultureResourceIO.OutputFile);
                }
            }
        }
    }

}
