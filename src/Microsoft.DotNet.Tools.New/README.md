% DOTNET-NEW(1)
% Zlatko Knezevic zlakne@microsoft.com
% January 2016

# NAME
dotnet-new -- Create a new sample .NET Core project

# SYNOPSIS
dotnet new

# DESCRIPTION
The new command provides a convenient way to initalize a valid .NET Core project and sample source code to try out the CLI toolset. 

This command is invoked in the context of a directory. When invoked, the command will result in two main artifacts being dropped to the directory: 

1. A sample "Hello World" program that exists in `Program.cs` file.
2. A valid `project.json` file

After this, the project is ready to be compiled and/or edited further. 

# EXAMPLES

`dotnet new`
    
    Drops a sample in the current directory.

# SEE ALSO
dotnet-run(1)
