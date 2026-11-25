using System.Globalization;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using SceneObject = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Soundstage;

/// <summary>
/// Reads the game's own lights (placed lamps, map lights, and any Stagehand lights) straight from the scene graph and
/// logs their values, so Soundstage's intensities can be calibrated against lights the game itself ships.
/// Read-only: it never writes to a game object. Run it on the framework thread.
/// </summary>
public static unsafe class LightSurvey
{
    public const string LogPrefix = "[Soundstage/SURVEY]";
    private const int NodeCap = 200_000;

    public sealed record Found(float Distance, string Line);

    /// <summary>Walks the whole scene tree from World and returns every light, nearest first.</summary>
    public static List<Found> Take(Vector3 player, out int nodesVisited)
    {
        var found = new List<Found>();
        nodesVisited = 0;
        var world = World.Instance();
        if (world == null) return found;

        var stack = new Stack<nint>();
        stack.Push((nint)world);
        while (stack.Count > 0 && nodesVisited < NodeCap)
        {
            var obj = (SceneObject*)stack.Pop();
            var first = obj->ChildObject;
            if (first == null) continue;
            var cur = first;
            do
            {
                nodesVisited++;
                if (cur->GetObjectType() == ObjectType.Light) found.Add(Describe((Light*)cur, player));
                if (cur->ChildObject != null) stack.Push((nint)cur);
                cur = cur->NextSiblingObject;
            }
            while (cur != null && cur != first && nodesVisited < NodeCap);
        }
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return found;
    }

    private static Found Describe(Light* light, Vector3 player)
    {
        var pos = light->Position;
        var dist = Vector3.Distance(pos, player);
        var r = light->RenderLight;
        if (r == null)
            return new Found(dist, string.Create(CultureInfo.InvariantCulture, $"dist={dist:F2} pos={F(pos)} (no render light)"));
        var c = r->Color;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"dist={dist:F2} shape={r->LightShape} col=({c.X:F3},{c.Y:F3},{c.Z:F3}) I={r->Intensity:F3} R={r->Range:F2} " +
            $"falloff={r->FalloffType} ff={r->FalloffFactor:F2} cone={r->SpotLightAngleDegrees:F1} soft={r->AngularFalloffDegrees:F1} " +
            $"flags={r->LightFlags} visible={light->IsVisible} pos={F(pos)} heightAbovePlayer={pos.Y - player.Y:F2}");
        return new Found(dist, line);
    }

    private static string F(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F2},{v.Y:F2},{v.Z:F2})");
}
