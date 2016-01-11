// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    public class LockFileTarget
    {
        public NuGetFramework TargetFramework { get; set; }

        public string RuntimeIdentifier { get; set; }

        public string Name { get { return $"{TargetFramework}/{RuntimeIdentifier}"; } }

        public IList<LockFileTargetLibrary> Libraries { get; set; } = new List<LockFileTargetLibrary>();
    }
}
