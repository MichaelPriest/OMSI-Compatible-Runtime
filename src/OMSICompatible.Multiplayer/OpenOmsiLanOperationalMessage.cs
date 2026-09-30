using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OMSICompatible.Multiplayer;

public sealed record OpenOmsiLanOperationalMessage(
    uint SenderId,
    string SenderName,
    string CompanyName,
    string EmployeeNumber,
    string Module,
    string Kind,
    string Text,
    string Target,
    long TimestampUnixMilliseconds);

public static class OpenOmsiLanOperationalCodec
{
    public const string Prefix =
        "RTOPS1";

    private const int MaximumCompanyCharacters = 80;
    private const int MaximumEmployeeCharacters = 40;
    private const int MaximumModuleCharacters = 32;
    private const int MaximumKindCharacters = 32;
    private const int MaximumTextCharacters = 1500;
    private const int MaximumTargetCharacters = 80;

    public static string Encode(
        OpenOmsiLanOperationalMessage message)
    {
        var clean =
            Sanitize(
                message);

        var json =
            JsonSerializer.Serialize(
                clean);

        var payload =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    json));

        var text =
            string.Join(
                "|",
                Prefix,
                clean.SenderId.ToString(
                    CultureInfo.InvariantCulture),
                payload);

        if (Encoding.UTF8.GetByteCount(
                text) >
            OpenOmsiLanProtocol.MaximumDatagramBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                "Operational message exceeds the LAN datagram limit.");
        }

        return text;
    }

    public static bool TryDecode(
        string text,
        out OpenOmsiLanOperationalMessage message)
    {
        message =
            new OpenOmsiLanOperationalMessage(
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0);

        if (string.IsNullOrWhiteSpace(
                text) ||
            Encoding.UTF8.GetByteCount(
                text) >
            OpenOmsiLanProtocol.MaximumDatagramBytes)
        {
            return false;
        }

        var fields =
            text.Split(
                '|',
                3);

        if (fields.Length !=
                3 ||
            !fields[0].Equals(
                Prefix,
                StringComparison.Ordinal) ||
            !uint.TryParse(
                fields[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var senderId) ||
            senderId ==
                0)
        {
            return false;
        }

        try
        {
            var json =
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        fields[2]));

            var decoded =
                JsonSerializer.Deserialize<
                    OpenOmsiLanOperationalMessage>(
                    json);

            if (decoded is null ||
                decoded.SenderId !=
                    senderId)
            {
                return false;
            }

            message =
                Sanitize(
                    decoded);

            return
                !string.IsNullOrWhiteSpace(
                    message.Module) &&
                !string.IsNullOrWhiteSpace(
                    message.Kind);
        }
        catch
        {
            return false;
        }
    }

    private static OpenOmsiLanOperationalMessage Sanitize(
        OpenOmsiLanOperationalMessage message) =>
        message with
        {
            SenderName =
                OpenOmsiLanProtocol.CleanText(
                    message.SenderName,
                    OpenOmsiLanProtocol.MaximumNameCharacters),
            CompanyName =
                OpenOmsiLanProtocol.CleanText(
                    message.CompanyName,
                    MaximumCompanyCharacters),
            EmployeeNumber =
                OpenOmsiLanProtocol.CleanText(
                    message.EmployeeNumber,
                    MaximumEmployeeCharacters),
            Module =
                OpenOmsiLanProtocol.CleanText(
                    message.Module,
                    MaximumModuleCharacters)
                    .ToUpperInvariant(),
            Kind =
                OpenOmsiLanProtocol.CleanText(
                    message.Kind,
                    MaximumKindCharacters)
                    .ToUpperInvariant(),
            Text =
                OpenOmsiLanProtocol.CleanText(
                    message.Text,
                    MaximumTextCharacters),
            Target =
                OpenOmsiLanProtocol.CleanText(
                    message.Target,
                    MaximumTargetCharacters)
        };
}
