using System.Text.Json.Serialization;

namespace BankingApi.EventReceiver;

public class TransactionModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("bankAccountId")]
    public Guid BankAccountId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}