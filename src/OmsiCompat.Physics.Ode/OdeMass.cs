using System.Runtime.InteropServices;

namespace OmsiCompat.Physics.Ode;

// ODE single-precision dMass layout:
// dReal mass + dVector3 c[4] + dMatrix3 I[12] = 17 floats.
[StructLayout(
    LayoutKind.Sequential)]
internal struct OdeMass
{
    public float Mass;

    public float CenterX;
    public float CenterY;
    public float CenterZ;
    public float CenterPadding;

    public float I00;
    public float I01;
    public float I02;
    public float I03;

    public float I10;
    public float I11;
    public float I12;
    public float I13;

    public float I20;
    public float I21;
    public float I22;
    public float I23;

    public static OdeMass FromPrincipalInertia(
        float massKilograms,
        float centerX,
        float centerY,
        float centerZ,
        float inertiaX,
        float inertiaY,
        float inertiaZ)
    {
        return new OdeMass
        {
            Mass =
                massKilograms,
            CenterX =
                centerX,
            CenterY =
                centerY,
            CenterZ =
                centerZ,
            I00 =
                inertiaX,
            I11 =
                inertiaY,
            I22 =
                inertiaZ
        };
    }
}
