namespace Generator;

internal sealed class PendingFeedStore(
    string projectRoot,
    string gitDirectory,
    IReadOnlyList<string> paths)
{
    public void Snapshot(string path)
    {
        var source = Path.Combine(projectRoot, path);
        var content = GetPath("files", path);
        var deletion = GetPath("deletions", path);

        if (File.Exists(source))
        {
            CopyAtomic(source, content);

            if (File.Exists(deletion))
                File.Delete(deletion);

            return;
        }

        WriteMarkerAtomic(deletion);

        if (File.Exists(content))
            File.Delete(content);
    }

    public void Restore()
    {
        for (var index = 0; index < paths.Count; index++)
        {
            var path = paths[index];
            var destination = Path.Combine(projectRoot, path);
            var content = GetPath("files", path);
            var deletion = GetPath("deletions", path);

            if (File.Exists(content))
            {
                CopyAtomic(content, destination);
                continue;
            }

            if (File.Exists(deletion) && File.Exists(destination))
                File.Delete(destination);
        }
    }

    public void Clear()
    {
        for (var index = 0; index < paths.Count; index++)
            Clear(paths[index]);
    }

    public void Clear(string path)
    {
        var content = GetPath("files", path);
        var deletion = GetPath("deletions", path);

        if (File.Exists(content))
            File.Delete(content);

        if (File.Exists(deletion))
            File.Delete(deletion);
    }

    private string GetPath(string category, string path)
    {
        var root = Path.GetFullPath(Path.Combine(
            gitDirectory,
            "generator-pending"));
        var candidate = Path.GetFullPath(Path.Combine(root, category, path));
        var prefix = Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (candidate.StartsWith(prefix, comparison) == false)
            throw new InvalidOperationException(
                "Pending feed path escaped Git storage.");

        return candidate;
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("Pending path has no directory."));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            File.Copy(source, temporary, true);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void WriteMarkerAtomic(string destination)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("Pending path has no directory."));
        var temporary = destination + "." + Guid.NewGuid().ToString("N") +
            ".tmp";

        try
        {
            File.Create(temporary).Dispose();
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
