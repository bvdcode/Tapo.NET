using System;
using Tapo.Authentication;
using Xunit;

namespace Tapo.Tests.Unit;

public sealed class DigestChallengeTests
{
    [Fact]
    public void Parses_camera_style_challenge()
    {
        var header = "Digest realm=\"Login\",nonce=\"abc123\",qop=\"auth\",opaque=\"deadbeef\"";
        var challenge = DigestChallenge.Parse(header);

        Assert.Equal("Login", challenge.Realm);
        Assert.Equal("abc123", challenge.Nonce);
        Assert.Equal("auth", challenge.Qop);
        Assert.Equal("deadbeef", challenge.Opaque);
        Assert.Equal("MD5", challenge.Algorithm);
    }

    [Fact]
    public void Defaults_qop_to_auth_and_algorithm_to_md5()
    {
        var challenge = DigestChallenge.Parse("Digest realm=\"r\",nonce=\"n\"");
        Assert.Equal("auth", challenge.Qop);
        Assert.Equal("MD5", challenge.Algorithm);
        Assert.Null(challenge.Opaque);
    }

    [Fact]
    public void Authorization_header_has_all_required_fields()
    {
        var challenge = DigestChallenge.Parse("Digest realm=\"Login\",nonce=\"server-nonce\",qop=\"auth\"");
        var header = DigestAuthorization.Build(
            challenge,
            username: "admin",
            hashedPasswordHex: "ABCDEF0123",
            method: "POST",
            uri: "/stream",
            cnonce: "client-nonce");

        Assert.StartsWith("Digest ", header);
        Assert.Contains("username=\"admin\"", header);
        Assert.Contains("realm=\"Login\"", header);
        Assert.Contains("uri=\"/stream\"", header);
        Assert.Contains("nonce=\"server-nonce\"", header);
        Assert.Contains("nc=00000001", header);
        Assert.Contains("cnonce=\"client-nonce\"", header);
        Assert.Contains("qop=auth", header);
        Assert.Contains("algorithm=MD5", header);
        Assert.Contains("response=\"", header);
    }

    [Fact]
    public void Empty_challenge_throws()
    {
        Assert.Throws<ArgumentException>(() => DigestChallenge.Parse(""));
    }
}
