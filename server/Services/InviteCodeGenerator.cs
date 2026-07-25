using System.Security.Cryptography;

namespace Server.Services;

public static class InviteCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(int length = 8)
    {
        Span<char> code = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(code);
    }
}
