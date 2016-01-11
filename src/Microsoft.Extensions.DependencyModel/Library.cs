// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Microsoft.Extensions.DependencyModel
{
    public class Library
    {
        public Library(string libraryType, string packageName, NuGetVersion version, NuGetFramework targetFramework, string hash, IEnumerable<PackageDependency> dependencies, IEnumerable<string> frameworkAssemblies, bool serviceable)
        {
            LibraryType = libraryType;
            PackageName = packageName;
            Version = version;
            TargetFramework = targetFramework;
            Hash = hash;
            Dependencies = dependencies.ToList().AsReadOnly();
            FrameworkAssemblies = frameworkAssemblies.ToList().AsReadOnly();
            Serviceable = serviceable;
        }

        public string LibraryType { get; }

        public string PackageName { get; }

        public NuGetVersion Version { get; }

        public NuGetFramework TargetFramework { get; }

        public string Hash { get; }

        public IReadOnlyList<PackageDependency> Dependencies { get; }

        public IReadOnlyList<string> FrameworkAssemblies { get; }

        public bool Serviceable { get; }
    }
}