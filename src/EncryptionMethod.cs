namespace Tapo;

/// <summary>
/// Hashing algorithm the camera uses to derive its credential digest. Older
/// firmware sticks with MD5 while newer firmware advertises SHA-256 during the
/// secure-login handshake.
/// </summary>
public enum EncryptionMethod
{
    /// <summary>MD5 (legacy firmware).</summary>
    Md5,

    /// <summary>SHA-256 (modern firmware, encrypted control channel).</summary>
    Sha256,
}
