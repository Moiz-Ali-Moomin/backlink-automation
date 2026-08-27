using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public static class Idempotency
{
    public static string RequireKey(string? key)
    {
        var value = key?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            throw new ValidationException("An idempotency key of 1-200 characters is required.");
        }

        return value;
    }

    public static string Scope(ActorContext actor, string operation) => $"{actor.ActorType}:{actor.ActorId}:{operation}";

    public static string HashRequest<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    public static T ReadExisting<T>(IdempotencyRecord record, string requestHash)
    {
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(record.RequestHash), Encoding.ASCII.GetBytes(requestHash)))
        {
            throw new IdempotencyConflictException();
        }

        return JsonSerializer.Deserialize<T>(record.ResponseJson) ?? throw new ConflictException("Stored idempotency response is invalid.");
    }
}
