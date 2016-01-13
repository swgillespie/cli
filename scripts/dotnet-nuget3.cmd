@Echo OFF

REM Copyright (c) .NET Foundation and contributors. All rights reserved.
REM Licensed under the MIT license. See LICENSE file in the project root for full license information.

SETLOCAL
SET ERRORLEVEL=

"%~dp0corehost.exe" "%~dp0NuGet.CommandLine.XPlat.dll" %*

exit /b %ERRORLEVEL%
ENDLOCAL
