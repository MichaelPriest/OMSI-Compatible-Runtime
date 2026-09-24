namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleLoadProgress(
    int Percent,
    string Detail);
