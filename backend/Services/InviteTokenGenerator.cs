using System.Security.Cryptography;

namespace QuizParty.Api.Services;

public static class InviteTokenGenerator
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
