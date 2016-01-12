// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Versioning;
using Microsoft.DotNet.ProjectModel.Resolution;
using NuGet.Frameworks;

namespace Microsoft.DotNet.ProjectModel.Server.Models
{
    public static class NuGetFrameworkExtensions
    {
        public static FrameworkData ToPayload(this NuGetFramework framework)
        {
            var frameworkName = new FrameworkName(framework.DotNetFrameworkName);

            return new FrameworkData
            {
                ShortName = framework.GetShortFolderName(),
                FrameworkName = framework.DotNetFrameworkName,
                FriendlyName = $"{frameworkName.Identifier} {frameworkName.Version}",
                RedistListPath = FrameworkReferenceResolver.Default.GetFrameworkRedistListPath(framework)
            };
        }
    }
}
