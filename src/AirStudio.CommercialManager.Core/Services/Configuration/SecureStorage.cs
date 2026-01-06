using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AirStudio.CommercialManager.Core.Services.Configuration
{
    /// <summary>
    /// Provides secure storage utilities for passwords and sensitive data
    /// - DPAPI (machine scope) for at-rest storage on local machine
    /// - AES encryption for export/import with fixed passphrase
    /// </summary>
    public static class SecureStorage
    {
        // Fixed passphrase for export/import as specified in frozen spec
        private const string EXPORT_PASSPHRASE = "air";

        // Salt for key derivation (fixed for compatibility)
        private static readonly byte[] AES_SALT = new byte[]
        {
            0x41, 0x49, 0x52, 0x53, 0x54, 0x55, 0x44, 0x49,
            0x4F, 0x5F, 0x43, 0x4F, 0x4D, 0x4D, 0x45, 0x52
        }; // "AIRSTUDIO_COMMER" in bytes

        #region DPAPI (Machine-scope at-rest encryption)

        /// <summary>
        /// Encrypt a string using DPAPI with machine scope
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64-encoded encrypted data</returns>
        public static string ProtectDpapi(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.LocalMachine);

                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to encrypt data using DPAPI", ex);
            }
        }

        /// <summary>
        /// Decrypt a DPAPI-protected string
        /// </summary>
        /// <param name="encryptedBase64">Base64-encoded encrypted data</param>
        /// <returns>Decrypted plain text</returns>
        public static string UnprotectDpapi(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.LocalMachine);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decrypt data using DPAPI", ex);
            }
        }

        #endregion

        #region AES (Export/Import encryption with fixed passphrase)

        /// <summary>
        /// Encrypt a string using AES with the fixed export passphrase
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64-encoded encrypted data with IV prepended</returns>
        public static string EncryptForExport(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = DeriveKeyFromPassphrase(EXPORT_PASSPHRASE, 32); // 256-bit key
                    aes.GenerateIV();
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var encryptor = aes.CreateEncryptor())
                    using (var ms = new MemoryStream())
                    {
                        // Prepend IV to the encrypted data
                        ms.Write(aes.IV, 0, aes.IV.Length);

                        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        {
                            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                            cs.Write(plainBytes, 0, plainBytes.Length);
                            cs.FlushFinalBlock();
                        }

                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to encrypt data for export", ex);
            }
        }

        /// <summary>
        /// Decrypt an AES-encrypted string using the fixed export passphrase
        /// </summary>
        /// <param name="encryptedBase64">Base64-encoded encrypted data with IV prepended</param>
        /// <returns>Decrypted plain text</returns>
        public static string DecryptFromExport(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

                using (var aes = Aes.Create())
                {
                    aes.Key = DeriveKeyFromPassphrase(EXPORT_PASSPHRASE, 32);
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    // Extract IV from the beginning of the encrypted data
                    byte[] iv = new byte[16];
                    Array.Copy(encryptedBytes, 0, iv, 0, 16);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var ms = new MemoryStream(encryptedBytes, 16, encryptedBytes.Length - 16))
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (var sr = new StreamReader(cs, Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decrypt data from export", ex);
            }
        }

        /// <summary>
        /// Derive a key from a passphrase using PBKDF2
        /// </summary>
        private static byte[] DeriveKeyFromPassphrase(string passphrase, int keyLength)
        {
            using (var deriveBytes = new Rfc2898DeriveBytes(
                passphrase,
                AES_SALT,
                100000, // iterations
                HashAlgorithmName.SHA256))
            {
                return deriveBytes.GetBytes(keyLength);
            }
        }

        #endregion

        #region Password Conversion (DPAPI <-> AES)

        /// <summary>
        /// Convert a DPAPI-protected password to AES-encrypted for export
        /// </summary>
        public static string ConvertDpapiToExport(string dpapiProtected)
        {
            if (string.IsNullOrEmpty(dpapiProtected))
                return string.Empty;

            string plainPassword = UnprotectDpapi(dpapiProtected);
            return EncryptForExport(plainPassword);
        }

        /// <summary>
        /// Convert an AES-encrypted password from import to DPAPI-protected
        /// </summary>
        public static string ConvertExportToDpapi(string aesEncrypted)
        {
            if (string.IsNullOrEmpty(aesEncrypted))
                return string.Empty;

            string plainPassword = DecryptFromExport(aesEncrypted);
            return ProtectDpapi(plainPassword);
        }

        #endregion
    }
}
