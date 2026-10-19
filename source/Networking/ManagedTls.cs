using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lantoss.Core;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using BcCertificate = Org.BouncyCastle.Tls.Certificate;
using BcCertificateRequest = Org.BouncyCastle.Tls.CertificateRequest;
using BcHash = Org.BouncyCastle.Tls.HashAlgorithm;

// Native transport uses the Bouncy Castle TLS implementation. Remote identity is a saved public-key fingerprint, so self-signed certificates are checked by pin rather than public CA trust.
namespace Lantoss.Networking;

// TLS is implemented by the maintained Bouncy Castle library. RSA handshake
// signatures delegate to .NET, allowing the installed identity to remain
// non-exportable in Windows' key store. No certificate downloads or PKI network
// lookups occur: saved SPKI pins and explicit pairing comparisons establish trust.
public static class ManagedTls
{
    public sealed record Connection(Stream Stream, string Fingerprint);
    private static readonly SignatureAndHashAlgorithm Signature = new(BcHash.sha256, SignatureAlgorithm.rsa);
    private static readonly int[] Suites = [CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384, CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256];
    public static async Task<Connection> ConnectAsync(TcpClient socket, Identity identity, string? expected, CancellationToken token)
    {
        socket.ReceiveTimeout = 130000;
        socket.SendTimeout = 30000;
        ClientProfile profile = new(identity, expected);
        TlsClientProtocol protocol = new();
        AsyncTlsStream stream = new(protocol, socket);
        await stream.HandshakeAsync(() => protocol.Connect(profile), () => profile.Complete, token);
        if (!profile.Complete || profile.RemoteKey == null)
        {
            protocol.Close();
            throw new AuthenticationException("The peer did not complete mutual TLS authentication.");
        }
        return new(stream, profile.RemoteKey);
    }
    public static async Task<Connection> AcceptAsync(TcpClient socket, Identity identity, CancellationToken token)
    {
        socket.ReceiveTimeout = 130000;
        socket.SendTimeout = 30000;
        ServerProfile profile = new(identity);
        TlsServerProtocol protocol = new();
        AsyncTlsStream stream = new(protocol, socket);
        await stream.HandshakeAsync(() => protocol.Accept(profile), () => profile.Complete, token);
        if (!profile.Complete || profile.RemoteKey == null)
        {
            protocol.Close();
            throw new AuthenticationException("A client certificate and completed TLS handshake are required.");
        }
        return new(stream, profile.RemoteKey);
    }
    private static string Verify(BcCertificate chain, string? expected = null)
    {
        if (chain == null || chain.IsEmpty || chain.Length > 4)
            throw new TlsFatalAlert(AlertDescription.bad_certificate);
        byte[] encoded = chain.GetCertificateAt(0).GetEncoded();
        if (encoded.Length > 8192)
            throw new TlsFatalAlert(AlertDescription.bad_certificate);
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(encoded);
        if (!Identity.Acceptable(certificate))
            throw new TlsFatalAlert(AlertDescription.bad_certificate);
        string actual = Identity.KeyFingerprint(certificate);
        if (expected != null && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw new TlsFatalAlert(AlertDescription.bad_certificate);
        return actual;
    }
    private static TlsCredentialedSigner Credentials(TlsContext context, Identity identity) => new DefaultTlsCredentialedSigner(
        new TlsCryptoParameters(context), new WindowsSigner(identity),
        new BcCertificate([context.Crypto.CreateCertificate(identity.Certificate.RawData)]), Signature);
    private sealed class WindowsSigner(Identity identity) : TlsSigner
    {
        public byte[] GenerateRawSignature(SignatureAndHashAlgorithm algorithm, byte[] hash)
        {
            if (algorithm.Hash != BcHash.sha256 || algorithm.Signature != SignatureAlgorithm.rsa)
                throw new TlsFatalAlert(AlertDescription.handshake_failure);
            using RSA rsa = identity.Certificate.GetRSAPrivateKey() ?? throw new AuthenticationException("This device's private key is unavailable.");
            return rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        public TlsStreamSigner GetStreamSigner(SignatureAndHashAlgorithm algorithm) => null!; // TLS 1.2 uses GenerateRawSignature.
    }
    private sealed class ServerProfile(Identity identity) : DefaultTlsServer(new BcTlsCrypto())
    {
        public string? RemoteKey
        {
            get; private set;
        }
        public bool Complete
        {
            get; private set;
        }
        public override ProtocolVersion[] GetProtocolVersions() => [ProtocolVersion.TLSv12];
        public override int[] GetCipherSuites() => Suites;
        protected override TlsCredentialedSigner GetRsaSignerCredentials() => Credentials(m_context, identity);
        public override BcCertificateRequest GetCertificateRequest() => new([ClientCertificateType.rsa_sign], new List<SignatureAndHashAlgorithm> { Signature }, new List<X509Name>());
        public override void NotifyClientCertificate(BcCertificate certificate) => RemoteKey = Verify(certificate);
        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();
            Complete = true;
        }
        public override bool RequiresExtendedMasterSecret() => true;
    }
    private sealed class ClientProfile(Identity identity, string? expected) : DefaultTlsClient(new BcTlsCrypto())
    {
        public string? RemoteKey
        {
            get; set;
        }
        public bool Complete
        {
            get; private set;
        }
        public override ProtocolVersion[] GetProtocolVersions() => [ProtocolVersion.TLSv12];
        public override int[] GetCipherSuites() => Suites;
        public override TlsAuthentication GetAuthentication() => new Authentication(this, m_context, identity, expected);
        public override void NotifyHandshakeComplete()
        {
            base.NotifyHandshakeComplete();
            Complete = true;
        }
        public override bool RequiresExtendedMasterSecret() => true;
    }
    private sealed class Authentication(ClientProfile client, TlsContext context, Identity identity, string? expected) : TlsAuthentication
    {
        public void NotifyServerCertificate(TlsServerCertificate serverCertificate) => client.RemoteKey = Verify(serverCertificate.Certificate, expected);
        public TlsCredentials GetClientCredentials(BcCertificateRequest request)
        {
            if (request.CertificateTypes is null || request.SupportedSignatureAlgorithms is null || !request.CertificateTypes.Contains(ClientCertificateType.rsa_sign) || !request.SupportedSignatureAlgorithms.Contains(Signature))
                throw new TlsFatalAlert(AlertDescription.handshake_failure);
            return Credentials(context, identity);
        }
    }
    // Bouncy Castle's nonblocking record API is driven by real NetworkStream
    // async I/O. No thread-pool worker blocks on a socket. This application's
    // protocol is sequential per connection, so one gate serializes record use.
    private sealed class AsyncTlsStream(TlsProtocol protocol, TcpClient socket) : Stream
    {
        private readonly NetworkStream network = socket.GetStream();
        private readonly SemaphoreSlim io = new(1, 1);
        private readonly byte[] input = new byte[65536], output = new byte[65536], clear = new byte[65536];
        private int closed;
        public override bool CanRead => closed == 0; public override bool CanWrite => closed == 0; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }
        public async Task HandshakeAsync(Action begin, Func<bool> complete, CancellationToken token)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using CancellationTokenRegistration abort = timeout.Token.Register(socket.Dispose);
            try
            {
                begin();
                await DrainAsync(timeout.Token);
                while (!complete())
                    await PumpAsync(timeout.Token);
            }
            catch (Exception error) when (error is IOException or CryptographicException or ObjectDisposedException)
            {
                throw new AuthenticationException("The encrypted identity handshake failed.", error);
            }
        }
        private async Task DrainAsync(CancellationToken token)
        {
            while (protocol.GetAvailableOutputBytes() > 0)
            {
                int count = protocol.ReadOutput(output, 0, Math.Min(output.Length, protocol.GetAvailableOutputBytes()));
                await network.WriteAsync(output.AsMemory(0, count), token);
            }
        }
        private async Task PumpAsync(CancellationToken token)
        {
            int count = await network.ReadAsync(input, token);
            if (count == 0)
                throw new EndOfStreamException("The peer closed the encrypted connection.");
            protocol.OfferInput(input, 0, count);
            await DrainAsync(token);
        }
        public override void Flush()
        {
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (buffer.Length == 0)
                return 0;
            await io.WaitAsync(token);
            try
            {
                while (protocol.GetAvailableInputBytes() == 0)
                    await PumpAsync(token);
                int count = protocol.ReadInput(clear, 0, Math.Min(clear.Length, Math.Min(buffer.Length, protocol.GetAvailableInputBytes())));
                clear.AsMemory(0, count).CopyTo(buffer);
                return count;
            }
            finally { io.Release(); }
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            await io.WaitAsync(token);
            try
            {
                while (!buffer.IsEmpty)
                {
                    int count = Math.Min(clear.Length, buffer.Length);
                    buffer[..count].CopyTo(clear);
                    protocol.WriteApplicationData(clear, 0, count);
                    await DrainAsync(token);
                    buffer = buffer[count..];
                }
            }
            finally { io.Release(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref closed, 1) == 0)
            {
                socket.Dispose();
                try
                {
                    protocol.Close();
                }
                catch (IOException) { }
            }
            base.Dispose(disposing);
        }
    }
}
