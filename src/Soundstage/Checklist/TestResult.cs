// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>One saved Pass / Fail click from the test checklist.</summary>
[Serializable]
public sealed class TestResult
{
    public string Status { get; set; } = "";   // "PASS" or "FAIL"
    public string Note { get; set; } = "";
    public string When { get; set; } = "";
}
