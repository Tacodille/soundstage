namespace Soundstage.GameAccess;

/// <summary>Angle helpers for the probe's "orbit about the subject" write.</summary>
public static class OrbitMath
{
    /// <summary>Adds degrees to a yaw and wraps the result into -pi to pi, the range the game keeps.</summary>
    public static float OrbitedYaw(float yawRadians, float degrees) => WrapPi(yawRadians + degrees * MathF.PI / 180f);

    public static float WrapPi(float a)
    {
        const float TwoPi = MathF.PI * 2f;
        a %= TwoPi;
        if (a > MathF.PI) a -= TwoPi;
        else if (a < -MathF.PI) a += TwoPi;
        return a;
    }

    /// <summary>The GPose FOV slider runs -0.5 to 0.5; anything outside is a zoom hack the game does not expect.</summary>
    public static float ClampAddedFov(float v) => Math.Clamp(v, -0.5f, 0.5f);
}
