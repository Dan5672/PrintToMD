using System.Security.Cryptography;
using System.Text;

namespace Print2Md.Core;

internal static class Hashing
{
    public static string Sha256(byte[] bytes)
    {
        using (var algorithm = SHA256.Create())
        {
            return ToHex(algorithm.ComputeHash(bytes));
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}
