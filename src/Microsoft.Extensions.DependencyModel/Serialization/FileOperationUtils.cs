// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    internal static class FileOperationUtils
    {
        public static void MakeWritable(string filePath)
        {
            var attributes = File.GetAttributes(filePath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                attributes &= ~FileAttributes.ReadOnly;
                File.SetAttributes(filePath, attributes);
            }
        }
    }
}