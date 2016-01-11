// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyModel.Serialization;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;

namespace Microsoft.Extensions.DependencyModel
{
    // Converts a DependencyContext to/from a LockFile format.
    internal static class DependencyContextConverter
    {
        public static DependencyContext CreateFromLockFile(LockFile lockFile)
        {
            var lookup = new LockFileLookup(lockFile);
            var runtimeTarget = lockFile.Targets.First(target => !string.IsNullOrEmpty(target.RuntimeIdentifier));
            var compileTarget = lockFile.Targets.First(target => string.IsNullOrEmpty(target.RuntimeIdentifier));

            return new DependencyContext(
                compileTarget.TargetFramework,
                runtimeTarget.RuntimeIdentifier,
                CreateCompilationOptions(lockFile.CompilationOptions),
                lockFile.RuntimeOptions,
                ReadLibraries(compileTarget.Libraries, lookup, runtime: false).Cast<CompilationLibrary>().ToArray(),
                ReadLibraries(runtimeTarget.Libraries, lookup, runtime: true).Cast<RuntimeLibrary>().ToArray());
        }

        private static CompilationOptions CreateCompilationOptions(JObject compilationOptionsObject)
        {
            return new CompilationOptions(
                compilationOptionsObject[DependencyContextStrings.DefinesPropertyName]?.Values<string>(),
                compilationOptionsObject[DependencyContextStrings.LanguageVersionPropertyName]?.Value<string>(),
                compilationOptionsObject[DependencyContextStrings.PlatformPropertyName]?.Value<string>(),
                compilationOptionsObject[DependencyContextStrings.AllowUnsafePropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.WarningsAsErrorsPropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.OptimizePropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.KeyFilePropertyName]?.Value<string>(),
                compilationOptionsObject[DependencyContextStrings.DelaySignPropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.PublicSignPropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.EmitEntryPointPropertyName]?.Value<bool>(),
                compilationOptionsObject[DependencyContextStrings.GenerateXmlDocumentationPropertyName]?.Value<bool>());
        }

        private static IEnumerable<Library> ReadLibraries(IEnumerable<LockFileTargetLibrary> libraries, LockFileLookup lookup, bool runtime)
        {
            return libraries.Select(lib => ReadLibrary(lib, runtime, lookup));
        }

        private static Library ReadLibrary(LockFileTargetLibrary library, bool runtime, LockFileLookup lookup)
        {
            var dependencies = library.Dependencies;
            var assemblies = runtime ? library.RuntimeAssemblies : library.CompileTimeAssemblies;

            var package = lookup.GetPackage(library.Name, library.Version);

            if (runtime)
            {
                return new RuntimeLibrary(
                    library.Type,
                    library.Name,
                    library.Version,
                    library.TargetFramework,
                    package?.Sha512,
                    assemblies.Select(i => i.Path),
                    library.NativeLibraries.Select(i => i.Path),
                    library.FrameworkAssemblies,
                    dependencies,
                    package?.IsServiceable ?? false);
            }
            else
            {
                return new CompilationLibrary(
                    library.Type,
                    library.Name,
                    library.Version,
                    library.TargetFramework,
                    package?.Sha512,
                    assemblies.Select(i => i.Path),
                    library.FrameworkAssemblies,
                    dependencies,
                    package?.IsServiceable ?? false);
            }
        }

        public static LockFile CreateLockFile(DependencyContext context, bool preserveCompilationContext)
        {
            var lockFile = new LockFile();
            lockFile.Version = LockFile.CurrentVersion;
            lockFile.RuntimeOptions = (context.RuntimeOptions?.DeepClone() as JObject) ?? new JObject();

            var runtimeTarget = CreateTarget(context.RuntimeLibraries, context.TargetFramework, context.RuntimeIdentifier);
            lockFile.Targets.Add(runtimeTarget);
            lockFile.RuntimeOptions["target"] = runtimeTarget.Name;

            if (preserveCompilationContext)
            {
                lockFile.CompilationOptions = context.CompilationOptions.ToJson();
                lockFile.Targets.Add(CreateTarget(context.CompileLibraries, context.TargetFramework, runtimeIdentifier: null));
            }

            return lockFile;
        }

        private static LockFileTarget CreateTarget(IEnumerable<Library> libraries, NuGetFramework framework, string runtimeIdentifier)
        {
            var target = new LockFileTarget()
            {
                TargetFramework = framework,
                RuntimeIdentifier = runtimeIdentifier
            };
            foreach(var lib in libraries)
            {
                target.Libraries.Add(CreateTargetLibrary(lib));
            }
            return target;
        }

        private static LockFileTargetLibrary CreateTargetLibrary(Library library)
        {
            string[] assemblies;

            var runtimeLibrary = library as RuntimeLibrary;
            if (runtimeLibrary != null)
            {
                assemblies = runtimeLibrary.Assemblies.Select(assembly => assembly.Path).ToArray();
            }
            else
            {
                var compilationLibrary = library as CompilationLibrary;
                if (compilationLibrary != null)
                {
                    assemblies = compilationLibrary.Assemblies.ToArray();
                }
                else
                {
                    throw new NotSupportedException();
                }
            }

            return new LockFileTargetLibrary()
            {
                Name = library.PackageName,
                Type = library.LibraryType,
                TargetFramework = library.TargetFramework,
                Version = library.Version,
                Dependencies = library.Dependencies.ToList(),
                FrameworkAssemblies = new HashSet<string>(runtimeLibrary != null ? runtimeLibrary.FrameworkAssemblies : Enumerable.Empty<string>()),
                RuntimeAssemblies = GenerateAssemblyItems(runtimeLibrary != null ? assemblies : null),
                CompileTimeAssemblies = GenerateAssemblyItems(runtimeLibrary == null ? assemblies : null),
                NativeLibraries = GenerateAssemblyItems(runtimeLibrary != null ? runtimeLibrary.NativeLibraries : null)
            };
        }

        private static IList<LockFileItem> GenerateAssemblyItems(object p)
        {
            throw new NotImplementedException();
        }

        private static IList<LockFileItem> GenerateAssemblyItems(IEnumerable<string> assemblies)
        {
            return (assemblies ?? Enumerable.Empty<string>()).Select(a => new LockFileItem() { Path = a.Replace(Path.DirectorySeparatorChar, '/') }).ToList();
        }

        private static void AddLibraries(DependencyContext context, LockFile lockFile)
        {
            var allLibraries =
                context.RuntimeLibraries.Cast<Library>().Concat(context.CompileLibraries)
                    .GroupBy(library => new { library.PackageName, library.Version });
            foreach(var libs in allLibraries)
            {
                // We only care about the first copy of a particular Name/Version pair
                var lib = libs.First();

                if(lib.LibraryType.Equals(LockFileFormat.Types.Package))
                {
                    lockFile.PackageLibraries.Add(new LockFilePackageLibrary()
                    {
                        Name = lib.PackageName,
                        Version = lib.Version,
                        IsServiceable = lib.Serviceable,
                        Sha512 = lib.Hash
                    });
                } else
                {
                    lockFile.ProjectLibraries.Add(new LockFileProjectLibrary()
                    {
                        Name = lib.PackageName,
                        Version = lib.Version
                    });
                }
            }
        }
    }
}