namespace Generator;

public static class ProjectPaths
{
    public static string FindGeneratorRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "Generator.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Generator project folder was not found.");
    }
}
