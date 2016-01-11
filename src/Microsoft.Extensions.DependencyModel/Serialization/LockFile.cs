// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    public class LockFile
    {
        public static readonly int CurrentVersion = 2;
        public static readonly string LockFileName = "project.lock.json";
        public static readonly string RuntimeConfigFileName = "runtime.config.json";

        public int Version { get; set; }

        // The lock file doesn't care what's inside these sections :)
        public JObject CompilationOptions { get; set; }
        public JObject RuntimeOptions { get; set; }

        public IList<ProjectFileDependencyGroup> ProjectFileDependencyGroups { get; set; } = new List<ProjectFileDependencyGroup>();
        public IList<LockFilePackageLibrary> PackageLibraries { get; set; } = new List<LockFilePackageLibrary>();
        public IList<LockFileProjectLibrary> ProjectLibraries { get; set; } = new List<LockFileProjectLibrary>();
        public IList<LockFileTarget> Targets { get; set; } = new List<LockFileTarget>();
    }
}