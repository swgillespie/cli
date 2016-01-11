// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyModel.Serialization;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Microsoft.Extensions.DependencyModel
{
    public class RuntimeLibrary : Library
    {
        public RuntimeLibrary(string libraryType, string packageName, NuGetVersion version, NuGetFramework targetFramework, string hash, IEnumerable<string> assemblies, IEnumerable<string> nativeLibraries, IEnumerable<string> frameworkAssemblies, IEnumerable<PackageDependency> dependencies, bool serviceable)
            : base(libraryType, packageName, version, targetFramework, hash, dependencies, frameworkAssemblies, serviceable)
        {
            NativeLibraries = nativeLibraries.ToList().AsReadOnly();
            Assemblies = assemblies.Select(path => new RuntimeAssembly(path)).ToList().AsReadOnly();
        }

        public IReadOnlyList<RuntimeAssembly> Assemblies { get; }
        public IReadOnlyList<string> NativeLibraries { get; }
    }
}