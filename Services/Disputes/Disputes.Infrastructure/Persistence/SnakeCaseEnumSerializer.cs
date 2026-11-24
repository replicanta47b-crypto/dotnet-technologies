using Disputes.Domain.Common;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Disputes.Infrastructure.Persistence;

/// <summary>Зберігає enum як snake_case-рядок (UnderReview ↔ "under_review") — формат документів з Лаб. 01.</summary>
public sealed class SnakeCaseEnumSerializer<TEnum> : SerializerBase<TEnum> where TEnum : struct, Enum
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value)
        => context.Writer.WriteString(EnumText.ToText(value));

    public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        if (reader.GetCurrentBsonType() != BsonType.String)
            throw new BsonSerializationException(
                $"Cannot deserialize {typeof(TEnum).Name} from BSON type {reader.GetCurrentBsonType()}.");

        var text = reader.ReadString();
        if (EnumText.TryParse<TEnum>(text, out var value))
            return value;

        throw new BsonSerializationException($"Unknown {typeof(TEnum).Name} value '{text}'.");
    }
}
