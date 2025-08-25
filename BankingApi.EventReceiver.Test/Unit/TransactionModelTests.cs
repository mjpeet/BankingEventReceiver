using FluentAssertions;
using System.Text.Json;
using BankingApi.EventReceiver.Test.TestHelpers;

namespace BankingApi.EventReceiver.Test.Unit;

public class TransactionModelTests
{
    [Fact]
    public void Should_Deserialize_Credit_Transaction_From_Json()
    {
        // Arrange
        var bankAccountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var amount = 100.50m;
        
        var json = $$"""
        {
          "id": "{{transactionId}}",
          "messageType": "Credit",
          "bankAccountId": "{{bankAccountId}}",
          "amount": {{amount}}
        }
        """;

        // Act
        var transaction = JsonSerializer.Deserialize<TransactionModel>(json);

        // Assert
        transaction.Should().NotBeNull();
        transaction!.Id.Should().Be(transactionId);
        transaction.MessageType.Should().Be("Credit");
        transaction.BankAccountId.Should().Be(bankAccountId);
        transaction.Amount.Should().Be(amount);
    }

    [Fact]
    public void Should_Deserialize_Debit_Transaction_From_Json()
    {
        // Arrange
        var bankAccountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var amount = 50.25m;
        
        var json = $$"""
        {
          "id": "{{transactionId}}",
          "messageType": "Debit",
          "bankAccountId": "{{bankAccountId}}",
          "amount": {{amount}}
        }
        """;

        // Act
        var transaction = JsonSerializer.Deserialize<TransactionModel>(json);

        // Assert
        transaction.Should().NotBeNull();
        transaction!.Id.Should().Be(transactionId);
        transaction.MessageType.Should().Be("Debit");
        transaction.BankAccountId.Should().Be(bankAccountId);
        transaction.Amount.Should().Be(amount);
    }

    [Theory]
    [InlineData("Credit")]
    [InlineData("Debit")]
    public void Should_Handle_Valid_Message_Types(string messageType)
    {
        // Arrange
        var json = $$"""
        {
          "id": "{{Guid.NewGuid()}}",
          "messageType": "{{messageType}}",
          "bankAccountId": "{{Guid.NewGuid()}}",
          "amount": 100.00
        }
        """;

        // Act
        var transaction = JsonSerializer.Deserialize<TransactionModel>(json);

        // Assert
        transaction.Should().NotBeNull();
        transaction!.MessageType.Should().Be(messageType);
    }
}