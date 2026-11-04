using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PcKvm
{
    internal sealed class DirectTlsCertificate : IDisposable
    {
        const int SlotCount = 4;
        static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
        readonly FileStream _lease;
        readonly CngKey _key;
        bool _disposed;

        internal X509Certificate2 Certificate { get; private set; }
        internal string Sha256 { get; private set; }

        internal DirectTlsCertificate()
        {
            string keyName;
            _lease = AcquireSlot(out keyName);
            try
            {
                var parameters = new CngKeyCreationParameters { Provider = Provider };
                parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
                _key = CngKey.Create(CngAlgorithm.Rsa, keyName, parameters);
                using (var rsa = new RSACng(_key))
                {
                    var request = new CertificateRequest("CN=PcKvm session", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                    request.CertificateExtensions.Add(new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                    var usages = new OidCollection();
                    usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
                    Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
                }
                using (SHA256 hash = SHA256.Create())
                    Sha256 = BitConverter.ToString(hash.ComputeHash(Certificate.RawData)).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static FileStream AcquireSlot(out string keyName)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcKvm", "TlsKeys");
            Directory.CreateDirectory(root);
            keyName = null;
            FileStream selected = null;
            try
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    string name = "PcKvm.Direct.Tls.v1.Slot" + slot;
                    FileStream lease;
                    try { lease = new FileStream(Path.Combine(root, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException error)
                    {
                        int win32 = error.HResult & 0xffff;
                        if (win32 == 32 || win32 == 33) continue;
                        throw;
                    }
                    try
                    {
                        // Only a process holding this slot's exclusive handle may remove its named key.
                        if (CngKey.Exists(name, Provider))
                            using (CngKey stale = CngKey.Open(name, Provider)) stale.Delete();
                        if (selected != null) continue;
                        selected = lease;
                        keyName = name;
                        lease = null;
                    }
                    finally { if (lease != null) lease.Dispose(); }
                }
                if (selected == null) throw new IOException("No available direct TLS key slot");
                return selected;
            }
            catch
            {
                if (selected != null) selected.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (Certificate != null) Certificate.Dispose();
                if (_key != null) _key.Delete();
            }
            finally
            {
                if (_key != null) _key.Dispose();
                _lease.Dispose();
            }
        }
    }
}
