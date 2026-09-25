namespace OmsiCompat.Physics.Ode;

public readonly record struct OdeSuspensionTuning(
    float ErrorReductionParameter,
    float ConstraintForceMixing)
{
    public static OdeSuspensionTuning FromSpringDamper(
        float springNewtonsPerMeter,
        float damperNewtonSecondsPerMeter,
        float timeStepSeconds)
    {
        var spring =
            Math.Max(
                springNewtonsPerMeter,
                0.0f);

        var damper =
            Math.Max(
                damperNewtonSecondsPerMeter,
                0.0f);

        var step =
            Math.Clamp(
                timeStepSeconds,
                1.0f / 1_000.0f,
                1.0f / 20.0f);

        // ODE's soft-constraint spring/damper equivalence:
        // ERP = h*k / (h*k + c)
        // CFM = 1 / (h*k + c)
        var denominator =
            step *
                spring +
            damper;

        if (denominator <=
            0.000001f)
        {
            return new OdeSuspensionTuning(
                0.0f,
                1.0f);
        }

        return new OdeSuspensionTuning(
            Math.Clamp(
                step *
                    spring /
                    denominator,
                0.0f,
                1.0f),
            1.0f /
            denominator);
    }
}
