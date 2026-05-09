namespace Tapo.Control;

/// <summary>
/// One method invocation inside a <c>multipleRequest</c> batch.
/// </summary>
public sealed class TapoMethodCall
{
    /// <summary>Creates a method call.</summary>
    /// <param name="method">Tapo API method name (e.g. <c>getDeviceInfo</c>).</param>
    /// <param name="parameters">Method parameters object, or <see langword="null"/> for a parameter-less call.</param>
    public TapoMethodCall(string method, object? parameters = null)
    {
        Method = method;
        Parameters = parameters;
    }

    /// <summary>Tapo API method name.</summary>
    public string Method { get; }

    /// <summary>Method parameters. <see langword="null"/> for parameter-less methods.</summary>
    public object? Parameters { get; }
}
