using System.IO;
using System.Xml.Linq;

namespace AutoHouseLottery.Tests;

// A fresh checkout builds with nothing but the repository, its submodule and Dalamud: no project, file or library may be
// referenced by an absolute path or from outside the repository.
public sealed class BuildReferenceTests
{
    private const string MSBuildProperty = "$(";
    private const char ItemSeparator = ';';

    private static readonly string[] projectFiles = ["AutoHouseLottery/AutoHouseLottery.csproj", "AutoHouseLottery.Tests/AutoHouseLottery.Tests.csproj", "Directory.Build.props"];
    private static readonly string[] pathAttributes = ["Include", "Link"];

    public static TheoryData<string> ProjectFiles()
    {
        var data = new TheoryData<string>();
        for (var index = 0; index < projectFiles.Length; index++)
        {
            data.Add(projectFiles[index]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProjectFiles))]
    public void EveryReferencedPathStaysInsideTheRepository(string projectFile)
    {
        var root = Path.GetFullPath(RepositoryFiles.Root);
        var path = Path.Combine(root, projectFile);
        var projectDirectory = Path.GetDirectoryName(path)!;
        foreach (var element in XDocument.Load(path).Descendants())
        {
            for (var index = 0; index < pathAttributes.Length; index++)
            {
                var references = element.Attribute(pathAttributes[index])?.Value.Split(ItemSeparator) ?? [];
                for (var referenceIndex = 0; referenceIndex < references.Length; referenceIndex++)
                {
                    AssertInside(root, projectDirectory, projectFile, references[referenceIndex]);
                }
            }

            if (element.Name.LocalName == "HintPath")
            {
                AssertInside(root, projectDirectory, projectFile, element.Value);
            }
        }
    }

    private static void AssertInside(string root, string projectDirectory, string projectFile, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith(MSBuildProperty, StringComparison.Ordinal) || !LooksLikePath(reference))
        {
            return;
        }

        Assert.False(Path.IsPathRooted(reference), $"{projectFile} references the absolute path {reference}.");
        var resolved = Path.GetFullPath(Path.Combine(projectDirectory, reference.Replace('\\', Path.DirectorySeparatorChar)));
        Assert.True(resolved.StartsWith(root, StringComparison.Ordinal), $"{projectFile} references {reference}, outside the repository.");
    }

    private static bool LooksLikePath(string reference) => reference.Contains('/') || reference.Contains('\\') || reference.Contains('.');
}
