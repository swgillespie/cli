// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyModel.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.ProjectModel
{
    public class GlobalSettings
    {
        public const string FileName = "global.json";

        public IList<string> ProjectSearchPaths { get; private set; }
        public string PackagesPath { get; private set; }
        public string FilePath { get; private set; }
        public string DirectoryPath
        {
            get
            {
                return Path.GetFullPath(Path.GetDirectoryName(FilePath));
            }
        }

        public static bool TryGetGlobalSettings(string path, out GlobalSettings globalSettings)
        {
            globalSettings = null;
            string globalJsonPath = null;

            if (Path.GetFileName(path) == FileName)
            {
                globalJsonPath = path;
                path = Path.GetDirectoryName(path);
            }
            else if (!HasGlobalFile(path))
            {
                return false;
            }
            else
            {
                globalJsonPath = Path.Combine(path, FileName);
            }

            globalSettings = new GlobalSettings();

            try
            {
                using (var fs = File.OpenRead(globalJsonPath))
                using (var reader = new JsonTextReader(new StreamReader(fs)))
                {
                    var jobject = JToken.ReadFrom(reader) as JObject;

                    if (jobject == null)
                    {
                        throw new InvalidOperationException("The JSON file can't be deserialized to a JSON object.");
                    }

                    var projectSearchPaths = jobject.Value<JArray>("projects")?.Select(a => a.Value<string>())?.ToArray() ??
                                             jobject.Value<JArray>("sources")?.Select(a => a.Value<string>())?.ToArray() ??
                                             Array.Empty<string>();

                    globalSettings.ProjectSearchPaths = new List<string>(projectSearchPaths);
                    globalSettings.PackagesPath = jobject.Value<string>("packages");
                    globalSettings.FilePath = globalJsonPath;
                }
            }
            catch (Exception ex)
            {
                throw FileFormatException.Create(ex, globalJsonPath);
            }

            return true;
        }

        public static bool HasGlobalFile(string path)
        {
            string projectPath = Path.Combine(path, FileName);

            return File.Exists(projectPath);
        }

    }
}
