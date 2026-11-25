using System.Numerics;

namespace Soundstage.SelfCheck;

/// <summary>
/// One area of the self-check, usually one per plan part. Program.cs finds every non-abstract subclass with a
/// parameterless constructor by reflection and runs them in Order, so a part adds its checks with one new file in
/// Checks/ and edits nothing shared.
/// </summary>
public abstract class CheckArea
{
    public abstract string Name { get; }

    /// <summary>Presets 10, Poses 20, Migration 30, Checklist 40; use the part number times 10 after that.</summary>
    public abstract int Order { get; }

    public abstract void Run();

    internal CheckRun Results { get; set; } = null!;

    protected const float Rad = MathF.PI / 180f;

    /// <summary>The plugin's source folder (src/Soundstage).</summary>
    protected static string root { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Soundstage"));

    /// <summary>This test project's folder (tests/Soundstage.SelfCheck), where TestData lives.</summary>
    protected static string TestRoot { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    protected void Check(string name, bool ok, string detail = "") => Results.Check(name, ok, detail);

    protected static bool Near(float a, float b, float tol = 1e-3f) => MathF.Abs(a - b) <= tol;

    protected static bool NearV(Vector3 a, Vector3 b, float tol = 1e-3f) => Near(a.X, b.X, tol) && Near(a.Y, b.Y, tol) && Near(a.Z, b.Z, tol);
}

/// <summary>The running totals for one self-check run.</summary>
public sealed class CheckRun
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }

    public void Check(string name, bool ok, string detail = "")
    {
        if (ok) Passed++; else Failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  [" + detail + "]" : "")}");
    }
}
