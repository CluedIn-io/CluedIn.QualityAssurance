using System.Diagnostics.CodeAnalysis;

using CluedIn.QualityAssurance.Cli.Models.Operations;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal static class TestFileHelper
{
    public static Stream GetTestFileStream(string path)
    {
        if (IsExternalTestFile(path))
        {
            return File.OpenRead(path);
        }

        var type = typeof(TestFileHelper);
        var resourceName = GetEmbeddedFilePath(path, type);
        var assembly = type.Assembly;
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Failed to read manifest resource stream for '{resourceName}'");
        }

        return stream;
    }

    private static string GetEmbeddedFilePath(string path, Type currentType)
    {
        return $"{currentType.Namespace}.Data.TestData.{path}";
    }

    public static bool IsExternalTestFile(string path)
    {
        return File.Exists(path);
    }

    private static string GetCustomizationFilePath(string path)
    {
        return path + ".customization.json";
    }

    public static bool TryGetCustomizationFileStream(string path, [NotNullWhen(true)] out Stream? stream)
    {
        if (TestFileHelper.IsExternalTestFile(path))
        {
            var customizationFilePath = GetCustomizationFilePath(path);
            if (File.Exists(customizationFilePath))
            {
                stream = File.OpenRead(customizationFilePath);
                return true;
            }
        }
        else
        {
            var currentType = typeof(Organization);
            var resourceName = GetCustomizationFilePath(path);
            var assembly = currentType.Assembly;
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream != null)
            {
                stream = resourceStream;
                return true;
            }
        }

        stream = null;
        return false;
    }
}
