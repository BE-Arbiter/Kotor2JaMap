namespace Kotor.Core.Export;

/// <summary>Which of the Settings paths are usable (the Jedi Knight folder is optional: empty counts as valid). Computed off the render thread (it touches the disk).</summary>
public readonly record struct PathStatus(bool Kotor, bool Kotor2, bool Ja, bool Work, bool JediKnight = true)
{
    /// <summary>Everything still unknown is shown as valid until the first check finishes.</summary>
    public static PathStatus Unknown => new(true, true, true, true);
}

/// <summary>Disk checks and merge rules of the Settings panel, kept out of the UI so they can be tested.</summary>
public static class SettingsProbe
{
    /// <summary>Checks the paths. Slow on a dead network share: never call from the render thread.</summary>
    public static PathStatus Check(string kotor, string kotor2, string ja, string work, string jediKnight = "") =>
        new(FolderOk(kotor, needChitin: true),
            (kotor2 ?? "").Trim().Length == 0 || FolderOk(kotor2, needChitin: true),   // KOTOR 2 is optional
            FolderOk(ja, needChitin: false),
            WorkOk(work),
            (jediKnight ?? "").Trim().Length == 0 || JediKnightOk(jediKnight));   // Jedi Knight is optional

    public static bool FolderOk(string? path, bool needChitin)
    {
        try
        {
            string p = (path ?? "").Trim();
            if (p.Length == 0 || !Directory.Exists(p)) return false;
            return !needChitin || File.Exists(Path.Combine(p, "chitin.key"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Dossier de Jedi Knight : il contient <c>Episode\JK1.GOB</c>.</summary>
    public static bool JediKnightOk(string? path)
    {
        try
        {
            string p = (path ?? "").Trim();
            return p.Length > 0 && File.Exists(Path.Combine(p, "Episode", "JK1.GOB"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public static bool WorkOk(string? path)
    {
        try
        {
            string p = (path ?? "").Trim();
            return p.Length > 0 && !File.Exists(p);   // the folder is created on demand
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Detect: a field is replaced only by a folder that was really found. A field already valid in <paramref name="status"/>
    /// is kept (the user's choice), as is any field whose detection came back empty.
    /// </summary>
    public static ExportOptions ApplyDetected(ExportOptions current, GamePaths found, PathStatus status)
    {
        string Pick(string cur, bool ok, string? det) => !ok && !string.IsNullOrWhiteSpace(det) ? det : cur;
        return current with
        {
            KotorDir = Pick(current.KotorDir ?? "", status.Kotor && !string.IsNullOrWhiteSpace(current.KotorDir), found.KotorDir),
            Kotor2Dir = Pick(current.Kotor2Dir ?? "", status.Kotor2 && !string.IsNullOrWhiteSpace(current.Kotor2Dir), found.Kotor2Dir),
            JaBaseDir = Pick(current.JaBaseDir ?? "", status.Ja, found.JaBaseDir),
            WorkDir = Pick(current.WorkDir ?? "", status.Work, found.WorkDir),
            JediKnightDir = Pick(current.JediKnightDir ?? "", status.JediKnight && !string.IsNullOrWhiteSpace(current.JediKnightDir), found.JediKnightDir),
        };
    }
}

/// <summary>
/// One background computation at a time. <see cref="TryStart"/> does nothing while a run is in progress; the render thread
/// collects the finished result with <see cref="TryTake"/> on a later frame.
/// </summary>
public sealed class BackgroundJob<T>
{
    private int _running;
    private T? _result;
    private int _hasResult;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Starts <paramref name="work"/> on the thread pool unless one is already running. Exceptions end the run without a result.</summary>
    public bool TryStart(Func<T> work)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        Task.Run(() =>
        {
            try
            {
                T r = work();
                _result = r;
                Volatile.Write(ref _hasResult, 1);
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
            finally { Volatile.Write(ref _running, 0); }
        });
        return true;
    }

    /// <summary>The finished result, once (null/false until a run completes).</summary>
    public bool TryTake(out T? result)
    {
        if (Interlocked.CompareExchange(ref _hasResult, 0, 1) == 1) { result = _result; return true; }
        result = default;
        return false;
    }
}
