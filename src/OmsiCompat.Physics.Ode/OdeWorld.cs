namespace OmsiCompat.Physics.Ode;

public sealed class OdeWorld :
    IDisposable
{
    private nint _world;

    public OdeWorld()
    {
        OdeRuntime.EnsureInitialized();

        _world =
            OdeNative.dWorldCreate();

        if (_world ==
            nint.Zero)
        {
            throw new InvalidOperationException(
                "dWorldCreate returned null.");
        }

        // Keep native physics in OMSI vehicle coordinates:
        // X = right, Y = forward, Z = up.
        OdeNative.dWorldSetGravity(
            _world,
            0.0f,
            0.0f,
            -9.80665f);

        OdeNative.dWorldSetERP(
            _world,
            0.20f);
        OdeNative.dWorldSetCFM(
            _world,
            0.00001f);
        OdeNative.dWorldSetQuickStepNumIterations(
            _world,
            32);
    }

    public bool Step(
        float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(
            _world ==
                nint.Zero,
            this);

        var step =
            Math.Clamp(
                deltaSeconds,
                1.0f / 1_000.0f,
                1.0f / 20.0f);

        return OdeNative.dWorldQuickStep(
                   _world,
                   step) !=
               0;
    }

    public void Dispose()
    {
        var world =
            Interlocked.Exchange(
                ref _world,
                nint.Zero);

        if (world !=
            nint.Zero)
        {
            OdeNative.dWorldDestroy(
                world);
        }

        GC.SuppressFinalize(
            this);
    }
}
