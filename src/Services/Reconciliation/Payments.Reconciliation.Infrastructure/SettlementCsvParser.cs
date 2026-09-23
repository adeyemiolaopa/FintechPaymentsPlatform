using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public static class SettlementCsvParser
{
    private static readonly string[] RequiredHeaders = ["provider_reference", "client_reference", "amount", "currency", "status", "settlement_date"];

    public static IEnumerable<SettlementRecord> Parse(Stream stream, Guid fileId)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 81920, leaveOpen: true);
        using var parser = new TextFieldParser(reader);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        var header = parser.ReadFields() ?? throw new InvalidDataException("Settlement file is empty.");
        if (header.Length != RequiredHeaders.Length || !header.SequenceEqual(RequiredHeaders, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Settlement file headers are invalid.");
        long line = 1;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? throw new InvalidDataException($"Missing settlement row at line {line + 1}.");
            line++;
            if (fields.Length != 6) throw new InvalidDataException($"Invalid field count at line {line}.");
            if (!decimal.TryParse(fields[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || decimal.Round(amount, 4) != amount)
                throw new InvalidDataException($"Invalid amount at line {line}.");
            if (!DateOnly.TryParseExact(fields[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException($"Invalid settlement date at line {line}.");
            if (fields[0].Length is < 1 or > 128 || fields[1].Length is < 1 or > 128 || fields[3].Length != 3 || !fields[3].All(char.IsAsciiLetter) || fields[4].Length is < 1 or > 40)
                throw new InvalidDataException($"Invalid reference, currency, or status at line {line}.");
            var fingerprint = string.Join('\u001f', fields);
            yield return new SettlementRecord
            {
                SettlementFileId = fileId, ProviderReference = fields[0], ClientReference = fields[1], Amount = amount,
                Currency = fields[3].ToUpperInvariant(), ProviderStatus = fields[4], SettlementDate = date,
                SourceLineNumber = line, RawRecordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            };
        }
    }
}
