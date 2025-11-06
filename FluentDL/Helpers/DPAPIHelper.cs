using System.Security.Cryptography;
using System.Text;

namespace FluentDL.Helpers
{
    internal class DPAPIHelper
    {
        public static string Encrypt(string plainText)
        {
            try
            {
                byte[] encryptedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plainText),
                    null,
                    DataProtectionScope.CurrentUser);

                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string Decrypt(string encryptedBase64)
        {
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

                byte[] decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
