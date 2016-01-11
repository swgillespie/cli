using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.ProjectModel;
using Microsoft.DotNet.ProjectModel.Graph;
using Microsoft.DotNet.ProjectModel.Utilities;

namespace Microsoft.Extensions.DependencyModel.Serialization
{
    public static class LockFileExtensions
    {
        public static bool IsValidForProject(this LockFile self, Project project)
        {
            string message;
            return IsValidForProject(self, project, out message);
        }

        public static bool IsValidForProject(this LockFile self, Project project, out string message)
        {
            if (self.Version != LockFile.CurrentVersion)
            {
                message = $"The expected lock file version does not match the actual version";
                return false;
            }

            message = $"Dependencies in {Project.FileName} were modified";

            var actualTargetFrameworks = project.GetTargetFrameworks();

            // The lock file should contain dependencies for each framework plus dependencies shared by all frameworks
            if (self.ProjectFileDependencyGroups.Count != actualTargetFrameworks.Count() + 1)
            {
                return false;
            }

            foreach (var group in self.ProjectFileDependencyGroups)
            {
                IOrderedEnumerable<string> actualDependencies;
                var expectedDependencies = group.Dependencies.OrderBy(x => x);

                // If the framework name is empty, the associated dependencies are shared by all frameworks
                if (group.FrameworkName == null)
                {
                    actualDependencies = project.Dependencies.Select(RenderDependency).OrderBy(x => x);
                }
                else
                {
                    var framework = actualTargetFrameworks
                        .FirstOrDefault(f => Equals(f.FrameworkName, group.FrameworkName));
                    if (framework == null)
                    {
                        return false;
                    }

                    actualDependencies = framework.Dependencies.Select(RenderDependency).OrderBy(x => x);
                }

                if (!actualDependencies.SequenceEqual(expectedDependencies))
                {
                    return false;
                }
            }

            message = null;
            return true;
        }

        private static string RenderDependency(LibraryRange arg)
        {
            var name = arg.Name;

            if (arg.Target == LibraryType.ReferenceAssembly)
            {
                name = $"fx/{name}";
            }

            return $"{name} {VersionUtility.RenderVersion(arg.VersionRange)}";
        }
    }
}
