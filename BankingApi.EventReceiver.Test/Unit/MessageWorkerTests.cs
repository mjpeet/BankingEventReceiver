using FluentAssertions;
using Moq;
using BankingApi.EventReceiver;
using BankingApi.EventReceiver.Test.TestHelpers;

namespace BankingApi.EventReceiver.Test.Unit;

public class MessageWorkerTests
{
    [Fact]
    public async Task Should_Process_Credit_Message_And_Update_Balance()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 100m;
        var creditAmount = 50m;
        
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, creditAmount);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.WithMessages(creditMessage);
        
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessage(creditMessage);

        // Assert
        var updatedAccount = dbContext.BankAccounts.Find(accountId);
        updatedAccount!.Balance.Should().Be(initialBalance + creditAmount);
        mockServiceBus.Verify(x => x.Complete(creditMessage), Times.Once);
    }

    [Fact]
    public async Task Should_Process_Debit_Message_And_Update_Balance()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 100m;
        var debitAmount = 30m;
        
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        var debitMessage = TestData.EventMessages.DebitMessage(accountId, debitAmount);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.WithMessages(debitMessage);
        
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessage(debitMessage);

        // Assert
        var updatedAccount = dbContext.BankAccounts.Find(accountId);
        updatedAccount!.Balance.Should().Be(initialBalance - debitAmount);
        mockServiceBus.Verify(x => x.Complete(debitMessage), Times.Once);
    }

    [Fact]
    public async Task Should_Move_Invalid_Message_To_DeadLetter()
    {
        // Arrange
        var invalidMessage = TestData.EventMessages.InvalidMessage();
        
        using var dbContext = InMemoryDbContext.Create();
        var mockServiceBus = MockServiceBusReceiver.WithMessages(invalidMessage);
        
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessage(invalidMessage);

        // Assert
        mockServiceBus.Verify(x => x.MoveToDeadLetter(invalidMessage), Times.Once);
        mockServiceBus.Verify(x => x.Complete(It.IsAny<EventMessage>()), Times.Never);
    }

    [Fact]
    public async Task Should_Move_Unknown_MessageType_To_DeadLetter()
    {
        // Arrange
        var unknownMessage = new EventMessage
        {
            Id = Guid.NewGuid(),
            MessageBody = """
            {
              "id": "123e4567-e89b-12d3-a456-426614174000",
              "messageType": "Unknown",
              "bankAccountId": "123e4567-e89b-12d3-a456-426614174000",
              "amount": 100.00
            }
            """,
            ProcessingCount = 0
        };
        
        using var dbContext = InMemoryDbContext.Create();
        var mockServiceBus = MockServiceBusReceiver.WithMessages(unknownMessage);
        
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessage(unknownMessage);

        // Assert
        mockServiceBus.Verify(x => x.MoveToDeadLetter(unknownMessage), Times.Once);
    }

    [Fact]
    public async Task Should_Handle_NonExistent_BankAccount_Gracefully()
    {
        // Arrange
        var nonExistentAccountId = Guid.NewGuid();
        var creditMessage = TestData.EventMessages.CreditMessage(nonExistentAccountId, 100m);
        
        using var dbContext = InMemoryDbContext.Create();
        var mockServiceBus = MockServiceBusReceiver.WithMessages(creditMessage);
        
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessage(creditMessage);

        // Assert
        mockServiceBus.Verify(x => x.MoveToDeadLetter(creditMessage), Times.Once);
    }
}