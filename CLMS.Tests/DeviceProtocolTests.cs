using System.Security.Cryptography;
using System.Text;
using CLMS.Application.UnitRegistry;
using CLMS.Infrastructure.Auth;
using CLMS.Shared.Security;
using Xunit;

namespace CLMS.Tests;

public sealed class DeviceProtocolTests
{
    [Fact]
    public void EncodingIsDeterministicAndEveryContextFieldIsSigned()
    {
        var device = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var nonce = DeviceProtocol.Encode(new byte[32]);
        var payload = DeviceProtocol.Payload(DeviceProtocol.Authenticate, "CLMS.Agent", device, Guid.Empty, Guid.Empty, Guid.Empty, nonce);
        Assert.Equal($"CLMS.Device.v1\nauthenticate\nCLMS.Agent\n{device:D}\n{Guid.Empty:D}\n{Guid.Empty:D}\n{Guid.Empty:D}\n{nonce}\n\n", Encoding.UTF8.GetString(payload));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new DeviceProofVerifier();
        var publicKey = DeviceProtocol.Encode(key.ExportSubjectPublicKeyInfo());
        Assert.Equal(publicKey, verifier.ValidatePublicKey(publicKey).PublicKey);
        var signature = DeviceProtocol.Encode(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.True(verifier.Verify(publicKey, payload, signature));
        Assert.False(verifier.Verify(publicKey, DeviceProtocol.Payload(DeviceProtocol.Complete, "CLMS.Agent", device, Guid.Empty, Guid.Empty, Guid.Empty, nonce), signature));
        Assert.False(verifier.Verify(publicKey, DeviceProtocol.Payload(DeviceProtocol.Authenticate, "other", device, Guid.Empty, Guid.Empty, Guid.Empty, nonce), signature));
        Assert.False(verifier.Verify(publicKey, payload, signature[..^1]));
        Assert.False(verifier.Verify(publicKey, payload, DeviceProtocol.Encode(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("AA==")]
    [InlineData("")]
    public void MalformedKeysAreRejected(string input) => Assert.Throws<RegistryException>(() => new DeviceProofVerifier().ValidatePublicKey(input));

    [Fact]
    public void WrongCurvesPrivateKeysAndTrailingBytesAreRejected()
    {
        var verifier = new DeviceProofVerifier();
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var valid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<RegistryException>(() => verifier.ValidatePublicKey(DeviceProtocol.Encode(wrongCurve.ExportSubjectPublicKeyInfo())));
        Assert.Throws<RegistryException>(() => verifier.ValidatePublicKey(DeviceProtocol.Encode(valid.ExportPkcs8PrivateKey())));
        Assert.Throws<RegistryException>(() => verifier.ValidatePublicKey(DeviceProtocol.Encode(valid.ExportSubjectPublicKeyInfo().Concat(new byte[] { 0 }).ToArray())));
        using var rsa = RSA.Create(2048);
        Assert.Throws<RegistryException>(() => verifier.ValidatePublicKey(DeviceProtocol.Encode(rsa.ExportSubjectPublicKeyInfo())));
    }
}
