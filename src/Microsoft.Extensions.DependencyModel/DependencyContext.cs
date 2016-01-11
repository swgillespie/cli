// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyModel.Serialization;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;

namespace Microsoft.Extensions.DependencyModel
{
    public class DependencyContext
    {
        private static Lazy<DependencyContext> _defaultContext = new Lazy<DependencyContext>(LoadDefault);

        public DependencyContext(NuGetFramework targetFramework, string runtimeIdentifier, CompilationOptions compilationOptions, JObject runtimeOptions, CompilationLibrary[] compileLibraries, RuntimeLibrary[] runtimeLibraries)
        {
            RuntimeOptions = runtimeOptions;
            TargetFramework = targetFramework;
            CompileLibraries = compileLibraries;
            RuntimeLibraries = runtimeLibraries;
            RuntimeIdentifier = runtimeIdentifier;
            CompilationOptions = compilationOptions;
        }

        public static DependencyContext Default => _defaultContext.Value;

        public NuGetFramework TargetFramework { get; }

        public string RuntimeIdentifier { get; }

        public CompilationOptions CompilationOptions { get; }

        public JObject RuntimeOptions { get; }

        public IReadOnlyList<CompilationLibrary> CompileLibraries { get; }

        public IReadOnlyList<RuntimeLibrary> RuntimeLibraries { get; }

        private static DependencyContext LoadDefault()
        {
            var entryAssembly = (Assembly)typeof(Assembly).GetTypeInfo().GetDeclaredMethod("GetEntryAssembly").Invoke(null, null);
            var location = entryAssembly.Location;
            var runtimeConfig = Path.Combine(
                Path.GetDirectoryName(location),
                LockFile.RuntimeConfigFileName);

            if (!File.Exists(runtimeConfig))
            {
                // Try reading the old embedded file
                var stream = entryAssembly.GetManifestResourceStream(entryAssembly.GetName().Name + ".deps.json");
                if (stream == null)
                {
                    throw new InvalidOperationException("Entry assembly was compiled without `preserveCompilationContext` enabled");
                }
                return Load(stream);
            }
            else
            {
                using (var stream = new FileStream(runtimeConfig, FileMode.Open, FileAccess.Read))
                {
                    return Load(stream);
                }
            }
        }

        public static DependencyContext Load(Stream stream)
        {
            var lockFile = LockFileFormat.Read(stream);
            return DependencyContextConverter.CreateFromLockFile(lockFile);
        }

        public void Write(Stream stream)
        {
            Write(stream, preserveCompilationContext: false);
        }

        public void Write(string outputPath)
        {
            Write(outputPath, preserveCompilationContext: false);
        }

        public void Write(Stream stream, bool preserveCompilationContext)
        {
            var lockFile = DependencyContextConverter.CreateLockFile(this, preserveCompilationContext);
            LockFileFormat.Write(stream, lockFile);
        }

        public void Write(string outputPath, bool preserveCompilationContext)
        {
            using(var fs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite))
            {
                Write(fs, preserveCompilationContext);
            }
        }
    }
}
