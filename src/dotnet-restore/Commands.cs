using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.ProjectModel.Graph;

namespace Microsoft.DotNet.Tools.Restore
{
    public static class Commands
    {
        public static int RunRestore(IEnumerable<string> args, bool quiet)
        {
            // Build up args by pushing items into the front of the list (so we need to do it in reverse order :))
            var allArgs = args.ToList();
            allArgs.Insert(0, "restore");
            if (quiet)
            {
                allArgs.Insert(0, "Error");
                allArgs.Insert(0, "--verbosity");
            }

            var result = RunNuGet(allArgs)
                .ForwardStdErr()
                .ForwardStdOut()
                .Execute();

            return result.ExitCode;
        }

        private static Command RunNuGet(IEnumerable<string> nugetArgs)
        {
            return Command.Create("dotnet-nuget3", nugetArgs);
        }
    }
}