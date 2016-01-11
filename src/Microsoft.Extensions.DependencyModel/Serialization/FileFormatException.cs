// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Newtonsoft.Json;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    public sealed class FileFormatException : Exception
    {
        private FileFormatException(string message) :
            base(message)
        {
        }

        private FileFormatException(string message, Exception innerException) :
            base(message, innerException)
        {
        }

        public string Path { get; private set; }
        public int Line { get; private set; }
        public int Column { get; private set; }
        
        public override string ToString()
        {            
            return $"{Path}({Line},{Column}): Error: {base.ToString()}";
        }

        public static FileFormatException Create(Exception exception, string filePath)
        {
            var readerException = exception as JsonReaderException;
            if (readerException != null)
            {
                return new FileFormatException(exception.Message, exception)
                   .WithFilePath(filePath)
                   .WithLineInfo(readerException);
            }
            else
            {
                return new FileFormatException(exception.Message, exception)
                    .WithFilePath(filePath);
            }
        }

        public static FileFormatException Create(Exception exception, IJsonLineInfo jsonValue, string filePath)
        {
            var result = Create(exception, jsonValue)
                .WithFilePath(filePath);

            return result;
        }

        public static FileFormatException Create(Exception exception, IJsonLineInfo jsonValue)
        {
            var result = new FileFormatException(exception.Message, exception)
                .WithLineInfo(jsonValue);

            return result;
        }

        public static FileFormatException Create(string message, IJsonLineInfo jsonValue, string filePath)
        {
            var result = Create(message, jsonValue)
                .WithFilePath(filePath);

            return result;
        }

        public static FileFormatException Create(string message, string filePath)
        {
            var result = new FileFormatException(message)
                .WithFilePath(filePath);

            return result;
        }

        public static FileFormatException Create(string message, IJsonLineInfo jsonValue)
        {
            var result = new FileFormatException(message)
                .WithLineInfo(jsonValue);

            return result;
        }

        public FileFormatException WithFilePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            Path = path;

            return this;
        }

        public FileFormatException WithLineInfo(IJsonLineInfo value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Line = value.LineNumber;
            Column = value.LinePosition;

            return this;
        }

        public FileFormatException WithLineInfo(JsonReaderException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            Line = exception.LineNumber;
            Column = exception.LinePosition;

            return this;
        }
    }
}
