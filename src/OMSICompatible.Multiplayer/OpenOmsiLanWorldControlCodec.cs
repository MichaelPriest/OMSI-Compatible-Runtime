using System.Globalization;

namespace OMSICompatible.Multiplayer;

public readonly record struct OpenOmsiLanWorldEntityRef(
    bool Person,
    uint Id);

public sealed record OpenOmsiLanWorldWantRequest(
    uint PlayerId,
    IReadOnlyList<OpenOmsiLanWorldEntityRef> Entities);

public sealed record OpenOmsiLanWorldClaimRequest(
    uint PlayerId,
    IReadOnlyList<uint> People);

public sealed record OpenOmsiLanWorldClaimResult(
    bool Granted,
    IReadOnlyList<uint> People);

public static class OpenOmsiLanWorldControlCodec
{
    private const uint MaximumEntityId =
        (1u << 24) -
        1u;
    private const int MaximumItems =
        64;

    public static string EncodeWant(
        uint playerId,
        IReadOnlyList<OpenOmsiLanWorldEntityRef> entities) =>
        string.Join(
            "|",
            "WANT",
            playerId.ToString(
                CultureInfo.InvariantCulture),
            string.Join(
                ",",
                entities
                    .Take(
                        MaximumItems)
                    .Where(
                        static entity =>
                            entity.Id <=
                            MaximumEntityId)
                    .Select(
                        static entity =>
                            string.Concat(
                                entity.Person
                                    ? "p"
                                    : "c",
                                entity.Id.ToString(
                                    CultureInfo.InvariantCulture)))));

    public static bool TryDecodeWant(
        string text,
        out OpenOmsiLanWorldWantRequest request)
    {
        request =
            new OpenOmsiLanWorldWantRequest(
                0,
                Array.Empty<
                    OpenOmsiLanWorldEntityRef>());

        var fields =
            text.Split(
                '|');

        if (fields.Length <
                3 ||
            !fields[0].Equals(
                "WANT",
                StringComparison.Ordinal) ||
            !TryPlayerId(
                fields[1],
                out var playerId))
        {
            return false;
        }

        var entities =
            new List<
                OpenOmsiLanWorldEntityRef>();

        foreach (var token in
                 fields[2]
                     .Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries)
                     .Take(
                         MaximumItems))
        {
            if (token.Length <
                2)
            {
                continue;
            }

            var person =
                token[0] switch
                {
                    'p' or 'P' =>
                        true,
                    'c' or 'C' =>
                        false,
                    _ =>
                        (bool?)null
                };

            if (!person.HasValue ||
                !TryEntityId(
                    token[
                        1..],
                    out var id))
            {
                continue;
            }

            entities.Add(
                new OpenOmsiLanWorldEntityRef(
                    person.Value,
                    id));
        }

        request =
            new OpenOmsiLanWorldWantRequest(
                playerId,
                entities);

        return true;
    }

    public static string EncodeClaim(
        uint playerId,
        IReadOnlyList<uint> people) =>
        string.Join(
            "|",
            "CLAIM",
            playerId.ToString(
                CultureInfo.InvariantCulture),
            EncodeIds(
                people));

    public static bool TryDecodeClaim(
        string text,
        out OpenOmsiLanWorldClaimRequest request)
    {
        request =
            new OpenOmsiLanWorldClaimRequest(
                0,
                Array.Empty<uint>());

        var fields =
            text.Split(
                '|');

        if (fields.Length <
                3 ||
            !fields[0].Equals(
                "CLAIM",
                StringComparison.Ordinal) ||
            !TryPlayerId(
                fields[1],
                out var playerId))
        {
            return false;
        }

        request =
            new OpenOmsiLanWorldClaimRequest(
                playerId,
                DecodeIds(
                    fields[2]));

        return true;
    }

    public static string EncodeClaimResult(
        bool granted,
        IReadOnlyList<uint> people) =>
        string.Join(
            "|",
            granted
                ? "GRANT"
                : "DENY",
            EncodeIds(
                people));

    public static bool TryDecodeClaimResult(
        string text,
        out OpenOmsiLanWorldClaimResult result)
    {
        result =
            new OpenOmsiLanWorldClaimResult(
                false,
                Array.Empty<uint>());

        var fields =
            text.Split(
                '|');

        if (fields.Length <
            2)
        {
            return false;
        }

        var granted =
            fields[0] switch
            {
                "GRANT" =>
                    true,
                "DENY" =>
                    false,
                _ =>
                    (bool?)null
            };

        if (!granted.HasValue)
        {
            return false;
        }

        result =
            new OpenOmsiLanWorldClaimResult(
                granted.Value,
                DecodeIds(
                    fields[1]));

        return true;
    }

    private static string EncodeIds(
        IReadOnlyList<uint> ids) =>
        string.Join(
            ",",
            ids
                .Where(
                    static id =>
                        id <=
                        MaximumEntityId)
                .Take(
                    MaximumItems)
                .Select(
                    static id =>
                        id.ToString(
                            CultureInfo.InvariantCulture)));

    private static IReadOnlyList<uint> DecodeIds(
        string text)
    {
        var result =
            new List<uint>();

        foreach (var token in
                 text.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries)
                     .Take(
                         MaximumItems))
        {
            if (TryEntityId(
                    token,
                    out var id))
            {
                result.Add(
                    id);
            }
        }

        return result;
    }

    private static bool TryPlayerId(
        string text,
        out uint id) =>
        uint.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out id) &&
        id >
            0;

    private static bool TryEntityId(
        string text,
        out uint id) =>
        uint.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out id) &&
        id <=
            MaximumEntityId;
}
