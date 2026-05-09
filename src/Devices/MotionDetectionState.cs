namespace Tapo.Devices;

/// <summary>Motion detection toggle and digital sensitivity (0 – 100, higher = more sensitive).</summary>
public sealed class MotionDetectionState
{
    /// <summary>Creates a motion detection snapshot.</summary>
    public MotionDetectionState(bool enabled, int sensitivity)
    {
        Enabled = enabled;
        Sensitivity = sensitivity;
    }

    /// <summary>True when motion detection is active.</summary>
    public bool Enabled { get; }

    /// <summary>Digital sensitivity, 0 (least) – 100 (most).</summary>
    public int Sensitivity { get; }
}
