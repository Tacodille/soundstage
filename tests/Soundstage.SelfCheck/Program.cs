// Offline self-check for Soundstage. Run: dotnet run -c Release --project tests/Soundstage.SelfCheck
// Needs no game. Exits 0 if every check passes, 1 otherwise.
// The checks live in Checks/<Area>Checks.cs, one CheckArea subclass per area, found here by reflection.
using Soundstage.SelfCheck;

var run = new CheckRun();
var areas = typeof(CheckArea).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && typeof(CheckArea).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
    .Select(t => (CheckArea)Activator.CreateInstance(t)!)
    .OrderBy(a => a.Order).ThenBy(a => a.Name, StringComparer.Ordinal)
    .ToList();

foreach (var area in areas)
{
    Console.WriteLine($"=== {area.Name} ===");
    area.Results = run;
    try { area.Run(); }
    catch (Exception ex) { run.Check($"{area.Name}: ran to the end", false, ex.GetType().Name + ": " + ex.Message); }
}

Console.WriteLine();
Console.WriteLine($"{areas.Count} areas, {run.Passed} passed, {run.Failed} failed.");
return run.Failed == 0 ? 0 : 1;
