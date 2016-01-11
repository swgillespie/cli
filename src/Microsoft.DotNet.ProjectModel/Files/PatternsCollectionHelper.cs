// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyModel.Serialization;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.ProjectModel.Files
{
    internal static class PatternsCollectionHelper
    {
        private static readonly char[] PatternSeparator = new[] { ';' };

        public static IEnumerable<string> GetPatternsCollection(JObject rawProject,
                                                                string projectDirectory,
                                                                string projectFilePath,
                                                                string propertyName,
                                                                IEnumerable<string> defaultPatterns = null,
                                                                bool literalPath = false)
        {
            defaultPatterns = defaultPatterns ?? Enumerable.Empty<string>();

            var value = rawProject[propertyName];

            try
            {
                if (value == null)
                {
                    return CreateCollection(projectDirectory, propertyName, defaultPatterns, literalPath);
                }

                if (value.Type == JTokenType.String)
                {
                    return CreateCollection(projectDirectory, propertyName, new string[] { value.Value<string>() }, literalPath);
                }
                else if (value.Type == JTokenType.Array)
                {
                    return CreateCollection(projectDirectory, propertyName, value.Value<JArray>().Select(s => s.Value<string>()), literalPath);
                }
            }
            catch (Exception ex)
            {
                throw FileFormatException.Create(ex, value, projectFilePath);
            }

            throw FileFormatException.Create("Value must be either string or array.", value, projectFilePath);
        }

        private static IEnumerable<string> CreateCollection(string projectDirectory, string propertyName, IEnumerable<string> patternsStrings, bool literalPath)
        {
            var patterns = patternsStrings.SelectMany(patternsString => GetSourcesSplit(patternsString))
                                          .Select(patternString => patternString.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

            foreach (var pattern in patterns)
            {
                if (Path.IsPathRooted(pattern))
                {
                    throw new InvalidOperationException($"The '{propertyName}' property cannot be a rooted path.");
                }

                if (literalPath && pattern.Contains('*'))
                {
                    throw new InvalidOperationException($"The '{propertyName}' property cannot contain wildcard characters.");
                }
            }

            return new List<string>(patterns.Select(pattern => FolderToPattern(pattern, projectDirectory)));
        }

        private static IEnumerable<string> GetSourcesSplit(string sourceDescription)
        {
            if (string.IsNullOrEmpty(sourceDescription))
            {
                return Enumerable.Empty<string>();
            }

            return sourceDescription.Split(PatternSeparator, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string FolderToPattern(string candidate, string projectDir)
        {
            // This conversion is needed to support current template

            // If it's already a pattern, no change is needed
            if (candidate.Contains('*'))
            {
                return candidate;
            }

            // If the given string ends with a path separator, or it is an existing directory
            // we convert this folder name to a pattern matching all files in the folder
            if (candidate.EndsWith(@"\") ||
                candidate.EndsWith("/") ||
                Directory.Exists(Path.Combine(projectDir, candidate)))
            {
                return Path.Combine(candidate, "**", "*");
            }

            // Otherwise, it represents a single file
            return candidate;
        }
    }
}
