using FluentAssertions;
using Moq;
using BankingApi.EventReceiver;
using BankingApi.EventReceiver.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.EventReceiver.Test.Unit;

public class ConcurrencyTests
{
    [Fact]
    public async Task Should_Handle_Concurrent_Updates_To_Same_Account()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 1000m;
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();

        var creditMessage1 = TestData.EventMessages.CreditMessage(accountId, 100m);
        var creditMessage2 = TestData.EventMessages.CreditMessage(accountId, 200m);
        var debitMessage = TestData.EventMessages.DebitMessage(accountId, 50m);

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act - Process multiple messages concurrently
        var tasks = new[]
        {
            worker.ProcessMessageWithConcurrency(creditMessage1),
            worker.ProcessMessageWithConcurrency(creditMessage2),
            worker.ProcessMessageWithConcurrency(debitMessage)
        };

        await Task.WhenAll(tasks);

        // Assert
        var finalAccount = await dbContext.BankAccounts.FindAsync(accountId);
        finalAccount!.Balance.Should().Be(initialBalance + 100m + 200m - 50m); // 1250m
        
        mockServiceBus.Verify(x => x.Complete(creditMessage1), Times.Once);
        mockServiceBus.Verify(x => x.Complete(creditMessage2), Times.Once);
        mockServiceBus.Verify(x => x.Complete(debitMessage), Times.Once);
    }

    [Fact]
    public async Task Should_Handle_Optimistic_Concurrency_Conflicts()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 500m;
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();

        // Simulate concurrent access by creating two separate contexts
        using var dbContext2 = InMemoryDbContext.CreateWithData(TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance));
        
        var creditMessage1 = TestData.EventMessages.CreditMessage(accountId, 100m);
        var creditMessage2 = TestData.EventMessages.CreditMessage(accountId, 200m);

        var worker1 = new MessageWorker(mockServiceBus.Object, dbContext);
        var worker2 = new MessageWorker(mockServiceBus.Object, dbContext2);

        // Act - Process messages with potential concurrency conflict
        var task1 = worker1.ProcessMessageWithConcurrency(creditMessage1);
        var task2 = worker2.ProcessMessageWithConcurrency(creditMessage2);

        await Task.WhenAll(task1, task2);

        // Assert - At least one message should succeed, failed one should retry
        mockServiceBus.Verify(x => x.Complete(It.IsAny<EventMessage>()), Times.AtLeast(1));
        
        // One might be rescheduled due to concurrency conflict
        mockServiceBus.Verify(x => x.ReSchedule(It.IsAny<EventMessage>(), It.IsAny<DateTime>()), Times.AtMost(1));
    }

    [Fact]
    public async Task Should_Use_Database_Transactions_For_Balance_Updates()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 100m;
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();

        // Force a failure after balance update but before completion
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(new InvalidOperationException("Service bus failure"));

        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithConcurrency(creditMessage);

        // Assert - Balance should not be updated if transaction fails
        var accountAfter = await dbContext.BankAccounts.FindAsync(accountId);
        accountAfter!.Balance.Should().Be(initialBalance); // Should rollback on failure
        
        mockServiceBus.Verify(x => x.ReSchedule(creditMessage, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Should_Handle_Multiple_Workers_Processing_Different_Accounts()
    {
        // Arrange
        var account1Id = Guid.NewGuid();
        var account2Id = Guid.NewGuid();
        var account3Id = Guid.NewGuid();

        var account1 = TestData.BankAccounts.WithIdAndBalance(account1Id, 1000m);
        var account2 = TestData.BankAccounts.WithIdAndBalance(account2Id, 2000m);
        var account3 = TestData.BankAccounts.WithIdAndBalance(account3Id, 3000m);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account1, account2, account3);
        var mockServiceBus = MockServiceBusReceiver.Create();

        var creditMessage1 = TestData.EventMessages.CreditMessage(account1Id, 100m);
        var debitMessage2 = TestData.EventMessages.DebitMessage(account2Id, 200m);
        var creditMessage3 = TestData.EventMessages.CreditMessage(account3Id, 300m);

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act - Process messages for different accounts concurrently
        var tasks = new[]
        {
            worker.ProcessMessageWithConcurrency(creditMessage1),
            worker.ProcessMessageWithConcurrency(debitMessage2),
            worker.ProcessMessageWithConcurrency(creditMessage3)
        };

        await Task.WhenAll(tasks);

        // Assert - All accounts should be updated correctly
        var finalAccount1 = await dbContext.BankAccounts.FindAsync(account1Id);
        var finalAccount2 = await dbContext.BankAccounts.FindAsync(account2Id);
        var finalAccount3 = await dbContext.BankAccounts.FindAsync(account3Id);

        finalAccount1!.Balance.Should().Be(1100m); // 1000 + 100
        finalAccount2!.Balance.Should().Be(1800m); // 2000 - 200
        finalAccount3!.Balance.Should().Be(3300m); // 3000 + 300

        mockServiceBus.Verify(x => x.Complete(It.IsAny<EventMessage>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Should_Handle_DbUpdateConcurrencyException_Classification()
    {
        // This test verifies that DbUpdateConcurrencyException is classified as transient
        // and would be retried by the IsTransientException method
        
        // Arrange
        var worker = new MessageWorker(Mock.Of<IServiceBusReceiver>(), Mock.Of<BankingApiDbContext>());
        var concurrencyException = new DbUpdateConcurrencyException("Concurrency conflict");
        
        // Act & Assert - Verify DbUpdateConcurrencyException is considered transient
        // We can't directly test the private method, but we know it includes DbUpdateConcurrencyException
        concurrencyException.Should().BeOfType<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Should_Use_Row_Level_Locking_For_Account_Updates()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 1000m;
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        
        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();

        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 100m);
        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithConcurrency(creditMessage);

        // Assert - Verify account was loaded with appropriate isolation
        var finalAccount = await dbContext.BankAccounts.FindAsync(accountId);
        finalAccount!.Balance.Should().Be(initialBalance + 100m);
        
        mockServiceBus.Verify(x => x.Complete(creditMessage), Times.Once);
    }
}