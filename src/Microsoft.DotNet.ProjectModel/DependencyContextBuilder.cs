// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.ProjectModel.Compilation;
using Microsoft.DotNet.ProjectModel.Graph;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Microsoft.Extensions.DependencyModel
{
    public static class DependencyContextBuilder
    {
        public static DependencyContext Build(
            ProjectContext context,
            string configuration)
        {
            return Build(
                context.ProjectFile.RuntimeOptions,
                context.ProjectFile.GetCompilerOptions(context.TargetFramework, configuration),
                context.CreateExporter(configuration),
                configuration,
                context.TargetFramework,
                context.RuntimeIdentifier);
        }

        public static DependencyContext Build(
            JObject runtimeOptions,
            CommonCompilerOptions compilerOptions,
            LibraryExporter libraryExporter,
            string configuration,
            NuGetFramework target,
            string runtime)
        {
            var dependencies = libraryExporter.GetAllExports().Where(export => export.Library.Framework.Equals(target)).ToList();

            // Sometimes we have package and reference assembly with the same name (System.Runtime for example) thats why we
            // deduplicating them prefering reference assembly
            var dependencyLookup = dependencies
                .OrderBy(export => export.Library.Identity.Type == LibraryType.ReferenceAssembly)
                .GroupBy(export => export.Library.Identity.Name)
                .Select(exports => exports.First())
                .Select(export => new PackageDependency(export.Library.Identity.Name, new VersionRange(export.Library.Identity.Version)))
                .ToDictionary(dependency => dependency.Id);

            return new DependencyContext(
                target,
                runtime,
                GetCompilationOptions(compilerOptions),
                runtimeOptions,
                GetLibraries(dependencies, dependencyLookup, target, configuration, runtime: false).Cast<CompilationLibrary>().ToArray(),
                GetLibraries(dependencies, dependencyLookup, target, configuration, runtime: true).Cast<RuntimeLibrary>().ToArray());
        }

        private static CompilationOptions GetCompilationOptions(CommonCompilerOptions compilerOptions)
        {
            return new CompilationOptions(compilerOptions.Defines,
                compilerOptions.LanguageVersion,
                compilerOptions.Platform,
                compilerOptions.AllowUnsafe,
                compilerOptions.WarningsAsErrors,
                compilerOptions.Optimize,
                compilerOptions.KeyFile,
                compilerOptions.DelaySign,
                compilerOptions.PublicSign,
                compilerOptions.EmitEntryPoint,
                compilerOptions.GenerateXmlDocumentation);
        }

        private static IEnumerable<Library> GetLibraries(IEnumerable<LibraryExport> dependencies,
            IDictionary<string, PackageDependency> dependencyLookup,
            NuGetFramework target,
            string configuration,
            bool runtime)
        {
            return dependencies.Select(export => GetLibrary(export, target, configuration, runtime, dependencyLookup));
        }

        private static Library GetLibrary(LibraryExport export,
            NuGetFramework target,
            string configuration,
            bool runtime,
            IDictionary<string, PackageDependency> dependencyLookup)
        {
            var type = export.Library.Identity.Type.Value.ToLowerInvariant();

            var serviceable = (export.Library as PackageDescription)?.Library.IsServiceable ?? false;
            var libraryDependencies = new List<PackageDependency>();

            var libraryAssets = runtime ? export.RuntimeAssemblies : export.CompilationAssemblies;

            // Skip framework assembly references, they will be processed separately
            foreach (var libraryDependency in export.Library.Dependencies.Where(r => !r.Target.Equals(LibraryType.ReferenceAssembly)))
            {
                PackageDependency dependency;
                if (dependencyLookup.TryGetValue(libraryDependency.Name, out dependency))
                {
                    libraryDependencies.Add(dependency);
                }
            }

            var frameworkAssemblies = export.Library.Dependencies
                .Where(r => r.Target.Equals(LibraryType.ReferenceAssembly))
                .Select(r => r.Name);

            string[] assemblies;
            if (type == "project")
            {
                var isExe = ((ProjectDescription)export.Library)
                    .Project
                    .GetCompilerOptions(target, configuration)
                    .EmitEntryPoint
                    .GetValueOrDefault(false);

                isExe &= target.IsDesktop();

                assemblies = new[] { export.Library.Identity.Name + (isExe ? ".exe" : ".dll") };
            }
            else
            {
                assemblies = libraryAssets.Select(libraryAsset => libraryAsset.RelativePath).ToArray();
            }

            if (runtime)
            {
                return new RuntimeLibrary(
                    type,
                    export.Library.Identity.Name,
                    export.Library.Identity.Version,
                    export.Library.Framework,
                    export.Library.Hash,
                    assemblies,
                    export.NativeLibraries.Select(l => l.RelativePath),
                    frameworkAssemblies,
                    libraryDependencies.ToArray(),
                    serviceable);
            }
            else
            {
                return new CompilationLibrary(
                    type,
                    export.Library.Identity.Name,
                    export.Library.Identity.Version,
                    export.Library.Framework,
                    export.Library.Hash,
                    assemblies,
                    frameworkAssemblies,
                    libraryDependencies.ToArray(),
                    serviceable);
            }
        }
    }
}
