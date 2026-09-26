using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using Dapper;
using Npgsql;
using TestJob.Api.Models;

namespace TestJob.Api.Services;

public sealed class TestJobService
{
    private static readonly Regex EmailRegex = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _connectionString;

    public TestJobService(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("The connection string 'Default' is not configured.");
    }

    public async Task<TestJobResponse> ProcessAsync(TestJobRequest request, CancellationToken cancellationToken = default)
    {
        var response = new TestJobResponse();

        try
        {
            response.Url = DecodeBase64Utf8(request.UrlB64);
        }
        catch (Exception ex)
        {
            response.SetError("URL_BASE64_DECODE_ERROR", ex.Message);
            return response;
        }

        string page;
        try
        {
            page = DecodeBase64Utf8(request.PageB64);
        }
        catch (Exception ex)
        {
            response.SetError("PAGE_BASE64_DECODE_ERROR", ex.Message);
            return response;
        }

        var elements = await ExtractElementsAsync(page, request.Selector!, request.Attribute!, response, cancellationToken);
        if (elements is null)
        {
            return response;
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            const string insertSql =
                "INSERT INTO elements (attribute_value, element_html) VALUES (@AttributeValue, @ElementHtml);";

            foreach (var element in elements)
            {
                var command = new CommandDefinition(
                    insertSql,
                    new { element.AttributeValue, element.ElementHtml },
                    transaction: transaction,
                    cancellationToken: cancellationToken);
                await connection.ExecuteAsync(command);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            response.SetError("DB_ERROR", ex.Message);
            return response;
        }

        try
        {
            var matches = EmailRegex.Matches(page);
            response.EmailsCount = matches.Count;
            foreach (Match match in matches)
            {
                response.EmailsList.Add(match.Value);
            }
        }
        catch (Exception ex)
        {
            response.SetError("EMAIL_REGEX_ERROR", ex.Message);
            return response;
        }

        try
        {
            response.DecryptedPlainText = DecryptAes(request.EncryptedTextBytesB64!, request.KeyBytesB64!);
        }
        catch (Exception ex)
        {
            response.SetError("DECRYPTION_ERROR", ex.Message);
            return response;
        }

        return response;
    }

    private static async Task<List<ElementRecord>?> ExtractElementsAsync(
        string page,
        string selector,
        string attribute,
        TestJobResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await BrowsingContext
                .New(Configuration.Default)
                .OpenAsync(req => req.Content(page), cancellationToken);

            var found = document.QuerySelectorAll(selector).ToList();
            response.ElementsCount = found.Count;

            var records = new List<ElementRecord>(found.Count);
            foreach (var element in found)
            {
                var attributeValue = element.GetAttribute(attribute) ?? string.Empty;
                response.ElementsAttrList.Add(attributeValue);
                records.Add(new ElementRecord(attributeValue, element.OuterHtml));
            }

            return records;
        }
        catch (Exception ex)
        {
            response.SetError("PARSE_ERROR", ex.Message);
            return null;
        }
    }

    private static string DecodeBase64Utf8(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Base64 string is empty.");
        }

        Span<byte> buffer = new byte[value.Length * 3 / 4 + 1];
        if (!Convert.TryFromBase64String(value, buffer, out int bytesWritten))
        {
            throw new FormatException("Invalid base64 string.");
        }

        return Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
    }

    private static string DecryptAes(string encryptedB64, string keyB64)
    {
        var cipherBytes = DecodeBase64ToBytes(encryptedB64);
        var keyBytes = DecodeBase64ToBytes(keyB64);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = keyBytes;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DecodeBase64ToBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Base64 string is empty.");
        }

        return Convert.FromBase64String(value);
    }

    private sealed record ElementRecord(string AttributeValue, string ElementHtml);
}