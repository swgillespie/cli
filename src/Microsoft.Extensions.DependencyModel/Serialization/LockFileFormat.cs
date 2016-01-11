// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    public static class LockFileFormat
    {
        internal static class Properties
        {
            public static readonly string Version = "version";
            public static readonly string CompilationOptions = "compilationOptions";
            public static readonly string RuntimeOptions = "runtimeOptions";
            public static readonly string ProjectFileDependencyGroups = "projectFileDependencyGroups";
            public static readonly string Path = "path";
            public static readonly string Files = "files";
            public static readonly string Compile = "compile";
            public static readonly string Runtime = "runtime";
            public static readonly string Libraries = "libraries";
            public static readonly string Targets = "targets";
            public static readonly string Dependencies = "dependencies";
            public static readonly string Sha512 = "sha512";
            public static readonly string Type = "type";
            public static readonly string Serviceable = "serviceable";
            public static readonly string Framework = "framework";
            public static readonly string FrameworkAssemblies = "frameworkAssemblies";
            public static readonly string Resource = "resource";
            public static readonly string Native = "native";
            public static readonly string TargetFramework = "targetFramework";
        }

        internal static class Types
        {
            public static readonly string Project = "project";
            public static readonly string Package = "package";
        }

        public static LockFile Read(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    return Read(stream);
                }
                catch (FileFormatException ex)
                {
                    throw ex.WithFilePath(filePath);
                }
                catch (Exception ex)
                {
                    throw FileFormatException.Create(ex, filePath);
                }
            }
        }

        internal static LockFile Read(Stream stream)
        {
            JObject jobject;
            using (var jsonReader = new JsonTextReader(new StreamReader(stream)) { CloseInput = false })
            {
                jobject = JToken.ReadFrom(jsonReader) as JObject;
            }

            if (jobject != null)
            {
                return Read(jobject);
            }
            else
            {
                throw new InvalidDataException();
            }
        }

        public static void Write(string filePath, LockFile lockFile)
        {
            // Make sure that if the lock file exists, it is not readonly
            if (File.Exists(filePath))
            {
                FileOperationUtils.MakeWritable(filePath);
            }

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Write(stream, lockFile);
            }
        }

        public static void Write(Stream stream, LockFile lockFile)
        {
            using (var textWriter = new StreamWriter(stream))
            {
                using (var jsonWriter = new JsonTextWriter(textWriter))
                {
                    jsonWriter.Formatting = Formatting.Indented;

                    var json = WriteLockFile(lockFile);
                    json.WriteTo(jsonWriter);
                }
            }
        }

        public static LockFile Read(JObject cursor)
        {
            var lockFile = new LockFile();
            lockFile.Version = ReadInt(cursor, Properties.Version, defaultValue: int.MinValue);
            lockFile.Targets = ReadObject(cursor[Properties.Targets] as JObject, ReadTarget);
            lockFile.ProjectFileDependencyGroups = ReadObject(cursor[Properties.ProjectFileDependencyGroups] as JObject, ReadProjectFileDependencyGroup);
            lockFile.CompilationOptions = cursor[Properties.CompilationOptions] as JObject;
            lockFile.RuntimeOptions = cursor[Properties.RuntimeOptions] as JObject;
            ReadLibraries(cursor[Properties.Libraries] as JObject, lockFile);
            return lockFile;
        }

        private static JObject WriteLockFile(LockFile lockFile)
        {
            var json = new JObject();
            json[Properties.Version] = new JValue(LockFile.CurrentVersion);

            if (lockFile.RuntimeOptions != null && lockFile.RuntimeOptions.Count > 0)
            {
                json[Properties.RuntimeOptions] = lockFile.RuntimeOptions;
            }

            if (lockFile.CompilationOptions != null && lockFile.CompilationOptions.Count > 0)
            {
                json[Properties.CompilationOptions] = lockFile.CompilationOptions;
            }

            if (lockFile.Targets.Count > 0)
            {
                json[Properties.Targets] = WriteObject(lockFile.Targets, WriteTarget);
            }

            if (lockFile.PackageLibraries.Count > 0 || lockFile.ProjectLibraries.Count > 0)
            {
                json[Properties.Libraries] = WriteLibraries(lockFile);
            }

            if (lockFile.ProjectFileDependencyGroups.Count > 0)
            {
                json[Properties.ProjectFileDependencyGroups] = WriteObject(lockFile.ProjectFileDependencyGroups, WriteProjectFileDependencyGroup);
            }
            return json;
        }

        private static void ReadLibraries(JObject json, LockFile lockFile)
        {
            if (json == null)
            {
                return;
            }

            foreach (var property in json)
            {
                var value = property.Value as JObject;
                if (value == null)
                {
                    continue;
                }

                var parts = property.Key.Split(new[] { '/' }, 2);
                var name = parts[0];
                var version = parts.Length == 2 ? NuGetVersion.Parse(parts[1]) : null;

                var type = value[Properties.Type]?.Value<string>();

                if (type == null || type == Types.Package)
                {
                    lockFile.PackageLibraries.Add(new LockFilePackageLibrary
                    {
                        Name = name,
                        Version = version,
                        IsServiceable = ReadBool(value, Properties.Serviceable, defaultValue: false),
                        Sha512 = ReadString(value[Properties.Sha512]),
                        Files = ReadPathArray(value[Properties.Files] as JArray, ReadString)
                    });
                }
                else if (type == Types.Project)
                {
                    lockFile.ProjectLibraries.Add(new LockFileProjectLibrary
                    {
                        Name = name,
                        Version = version,
                        Path = ReadString(value[Properties.Path])
                    });
                }
            }
        }

        private static JObject WriteLibraries(LockFile lockFile)
        {
            var result = new JObject();

            foreach (var library in lockFile.ProjectLibraries)
            {
                var value = new JObject();
                value[Properties.Type] = WriteString(Types.Project);
                value[Properties.Path] = WriteString(library.Path);

                result[$"{library.Name}/{library.Version.ToString()}"] = value;
            }

            foreach (var library in lockFile.PackageLibraries)
            {
                var value = new JObject();
                value[Properties.Type] = WriteString(Types.Package);

                if (library.IsServiceable)
                {
                    WriteBool(value, Properties.Serviceable, library.IsServiceable);
                }

                value[Properties.Sha512] = WriteString(library.Sha512);
                WritePathArray(value, Properties.Files, library.Files.OrderBy(f => f), WriteString);

                result[$"{library.Name}/{library.Version.ToString()}"] = value;
            }

            return result;
        }

        private static JProperty WriteTarget(LockFileTarget target)
        {
            var json = WriteObject(target.Libraries, WriteTargetLibrary);

            var key = target.TargetFramework + (target.RuntimeIdentifier == null ? "" : "/" + target.RuntimeIdentifier);

            return new JProperty(key, json);
        }

        private static LockFileTarget ReadTarget(string property, JToken json)
        {
            var target = new LockFileTarget();
            var parts = property.Split(new[] { '/' }, 2);
            target.TargetFramework = NuGetFramework.Parse(parts[0]);
            if (parts.Length == 2)
            {
                target.RuntimeIdentifier = parts[1];
            }

            target.Libraries = ReadObject(json as JObject, ReadTargetLibrary);

            return target;
        }

        private static LockFileTargetLibrary ReadTargetLibrary(string property, JToken json)
        {
            var library = new LockFileTargetLibrary();

            var parts = property.Split(new[] { '/' }, 2);
            library.Name = parts[0];
            if (parts.Length == 2)
            {
                library.Version = NuGetVersion.Parse(parts[1]);
            }

            var type = json[Properties.Type];
            if (type != null)
            {
                library.Type = ReadString(type);
            }

            var framework = json[Properties.Framework];
            if (framework != null)
            {
                library.TargetFramework = NuGetFramework.Parse(ReadString(framework));
            }

            library.Dependencies = ReadObject(json[Properties.Dependencies] as JObject, ReadPackageDependency);
            library.FrameworkAssemblies = new HashSet<string>(ReadArray(json[Properties.FrameworkAssemblies] as JArray, ReadFrameworkAssemblyReference), StringComparer.OrdinalIgnoreCase);
            library.RuntimeAssemblies = ReadObject(json[Properties.Runtime] as JObject, ReadFileItem);
            library.CompileTimeAssemblies = ReadObject(json[Properties.Compile] as JObject, ReadFileItem);
            library.ResourceAssemblies = ReadObject(json[Properties.Resource] as JObject, ReadFileItem);
            library.NativeLibraries = ReadObject(json[Properties.Native] as JObject, ReadFileItem);

            return library;
        }

        private static JProperty WriteTargetLibrary(LockFileTargetLibrary library)
        {
            var json = new JObject();

            json[Properties.Type] = WriteString(library.Type);

            if (library.TargetFramework != null)
            {
                json[Properties.Framework] = WriteString(library.TargetFramework.ToString());
            }

            if (library.Dependencies.Count > 0)
            {
                json[Properties.Dependencies] = WriteObject(library.Dependencies.OrderBy(p => p.Id), WritePackageDependency);
            }

            if (library.FrameworkAssemblies.Count > 0)
            {
                json[Properties.FrameworkAssemblies] = WriteArray(library.FrameworkAssemblies.OrderBy(f => f), WriteFrameworkAssemblyReference);
            }

            if (library.CompileTimeAssemblies.Count > 0)
            {
                json[Properties.Compile] = WriteObject(library.CompileTimeAssemblies, WriteFileItem);
            }

            if (library.RuntimeAssemblies.Count > 0)
            {
                json[Properties.Runtime] = WriteObject(library.RuntimeAssemblies, WriteFileItem);
            }

            if (library.ResourceAssemblies.Count > 0)
            {
                json[Properties.Resource] = WriteObject(library.ResourceAssemblies, WriteFileItem);
            }

            if (library.NativeLibraries.Count > 0)
            {
                json[Properties.Native] = WriteObject(library.NativeLibraries, WriteFileItem);
            }

            return new JProperty(library.Name + "/" + library.Version, json);
        }

        private static ProjectFileDependencyGroup ReadProjectFileDependencyGroup(string property, JToken json)
        {
            return new ProjectFileDependencyGroup(
                NuGetFramework.Parse(property),
                ReadArray(json as JArray, ReadString));
        }

        private static JProperty WriteProjectFileDependencyGroup(ProjectFileDependencyGroup frameworkInfo)
        {
            return new JProperty(
                frameworkInfo.FrameworkName.ToString(),
                WriteArray(frameworkInfo.Dependencies, WriteString));
        }

        private static PackageDependencyGroup ReadPackageDependencySet(string property, JToken json)
        {
            var targetFramework = string.Equals(property, "*") ? null : NuGetFramework.Parse(property);
            return new PackageDependencyGroup(
                targetFramework,
                ReadObject(json as JObject, ReadPackageDependency));
        }

        private static JProperty WritePackageDependencySet(PackageDependencyGroup item)
        {
            return new JProperty(
                item.TargetFramework?.ToString() ?? "*",
                WriteObject(item.Packages, WritePackageDependency));
        }


        private static PackageDependency ReadPackageDependency(string property, JToken json)
        {
            var versionStr = json.Value<string>();
            return new PackageDependency(
                property,
                versionStr == null ? null : VersionRange.Parse(versionStr));
        }

        private static JProperty WritePackageDependency(PackageDependency item)
        {
            return new JProperty(
                item.Id,
                WriteString(GetLegacyShortString(item.VersionRange)));
        }

        private static LockFileItem ReadFileItem(string property, JToken json)
        {
            var item = new LockFileItem { Path = GetPathWithDirectorySeparator(property) };
            foreach (var subProperty in json.OfType<JProperty>())
            {
                item.Properties[subProperty.Name] = subProperty.Value.Value<string>();
            }
            return item;
        }

        private static JProperty WriteFileItem(LockFileItem item)
        {
            return new JProperty(
                item.Path,
                new JObject(item.Properties.Select(x => new JProperty(x.Key, x.Value))));
        }

        private static string ReadFrameworkAssemblyReference(JToken json)
        {
            return json.Value<string>();
        }

        private static JToken WriteFrameworkAssemblyReference(string item)
        {
            return new JValue(item);
        }

        private static IList<TItem> ReadArray<TItem>(JArray json, Func<JToken, TItem> readItem)
        {
            if (json == null)
            {
                return new List<TItem>();
            }
            var items = new List<TItem>();
            foreach (var child in json)
            {
                items.Add(readItem(child));
            }
            return items;
        }

        private static IList<string> ReadPathArray(JArray json, Func<JToken, string> readItem)
        {
            return ReadArray(json, readItem).Select(f => GetPathWithDirectorySeparator(f)).ToList();
        }

        private static void WriteArray<TItem>(JToken json, string property, IEnumerable<TItem> items, Func<TItem, JToken> writeItem)
        {
            if (items.Any())
            {
                json[property] = WriteArray(items, writeItem);
            }
        }

        private static void WritePathArray(JToken json, string property, IEnumerable<string> items, Func<string, JToken> writeItem)
        {
            WriteArray(json, property, items.Select(f => GetPathWithForwardSlashes(f)), writeItem);
        }

        private static JArray WriteArray<TItem>(IEnumerable<TItem> items, Func<TItem, JToken> writeItem)
        {
            var array = new JArray();
            foreach (var item in items)
            {
                array.Add(writeItem(item));
            }
            return array;
        }

        private static JArray WritePathArray(IEnumerable<string> items, Func<string, JToken> writeItem)
        {
            return WriteArray(items.Select(f => GetPathWithForwardSlashes(f)), writeItem);
        }

        private static IList<TItem> ReadObject<TItem>(JObject json, Func<string, JToken, TItem> readItem)
        {
            if (json == null)
            {
                return new List<TItem>();
            }
            var items = new List<TItem>();
            foreach (var child in json)
            {
                items.Add(readItem(child.Key, child.Value));
            }
            return items;
        }

        private static void WriteObject<TItem>(JToken json, string property, IEnumerable<TItem> items, Func<TItem, JProperty> writeItem)
        {
            if (items.Any())
            {
                json[property] = WriteObject(items, writeItem);
            }
        }

        private static JObject WriteObject<TItem>(IEnumerable<TItem> items, Func<TItem, JProperty> writeItem)
        {
            var array = new JObject();
            foreach (var item in items)
            {
                array.Add(writeItem(item));
            }
            return array;
        }

        private static bool ReadBool(JToken cursor, string property, bool defaultValue)
        {
            var valueToken = cursor[property];
            if (valueToken == null)
            {
                return defaultValue;
            }
            return valueToken.Value<bool>();
        }

        private static int ReadInt(JToken cursor, string property, int defaultValue)
        {
            var valueToken = cursor[property];
            if (valueToken == null)
            {
                return defaultValue;
            }
            return valueToken.Value<int>();
        }

        private static string ReadString(JToken json)
        {
            return json?.Value<string>();
        }

        private static SemanticVersion ReadSemanticVersion(JToken json, string property)
        {
            var valueToken = json[property];
            if (valueToken == null)
            {
                throw new ArgumentException($"lock file missing required property '{property}'", nameof(property));
            }
            return SemanticVersion.Parse(valueToken.Value<string>());
        }

        private static void WriteBool(JToken token, string property, bool value)
        {
            token[property] = new JValue(value);
        }

        private static JToken WriteString(string item)
        {
            return item != null ? new JValue(item) : JValue.CreateNull();
        }

        private static FrameworkName ReadFrameworkName(JToken json)
        {
            return json == null ? null : new FrameworkName(json.Value<string>());
        }
        private static JToken WriteFrameworkName(FrameworkName item)
        {
            return item != null ? new JValue(item.ToString()) : JValue.CreateNull();
        }

        // LockFile paths always use '/'
        private static string GetPathWithForwardSlashes(string path)
        {
            if (Path.DirectorySeparatorChar == '/')
            {
                return path;
            }
            else {
                return path.Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        // LockFile paths always use '/'
        private static string GetPathWithDirectorySeparator(string path)
        {
            if (Path.DirectorySeparatorChar == '/')
            {
                return path;
            }
            else
            {
                return path.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        // Based on https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Versioning/VersionRangeFormatter.cs
        // Remove this when that code reaches the feed.
        private static string GetLegacyShortString(VersionRange range)
        {
            if(range == null)
            {
                return string.Empty;
            }

            string s = null;

            if (range.HasLowerBound
                && range.IsMinInclusive
                && !range.HasUpperBound)
            {
                s = range.MinVersion.ToString();
            }
            else if (range.HasLowerAndUpperBounds
                     && range.IsMinInclusive
                     && range.IsMaxInclusive
                     &&
                     range.MinVersion.Equals(range.MaxVersion))
            {
                s = $"[{range.MinVersion.ToNormalizedString()}]";
            }
            else
            {
                s = range.ToLegacyString();
            }

            return s;
        }
    }
}