using FluentAssertions;
using Moq;
using BankingApi.EventReceiver;
using BankingApi.EventReceiver.Test.TestHelpers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BankingApi.EventReceiver.Test.Unit;

public class RetryLogicTests
{
    [Fact]
    public async Task Should_Retry_Transient_Failure_With_5_Second_Delay_On_First_Attempt()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, 100m);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        creditMessage.ProcessingCount = 1;

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();
        
        // Simulate transient failure (database timeout)
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(new TimeoutException("Database timeout"));

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        var expectedDelay = TimeSpan.FromSeconds(5);
        mockServiceBus.Verify(x => x.ReSchedule(creditMessage, 
            It.Is<DateTime>(dt => dt >= DateTime.UtcNow.Add(expectedDelay.Subtract(TimeSpan.FromSeconds(1))) &&
                                 dt <= DateTime.UtcNow.Add(expectedDelay.Add(TimeSpan.FromSeconds(1))))), Times.Once);
    }

    [Fact]
    public async Task Should_Retry_Transient_Failure_With_25_Second_Delay_On_Second_Attempt()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, 100m);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        creditMessage.ProcessingCount = 2;

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();
        
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(new DbUpdateException("Database connection failed"));

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        var expectedDelay = TimeSpan.FromSeconds(25);
        mockServiceBus.Verify(x => x.ReSchedule(creditMessage, 
            It.Is<DateTime>(dt => dt >= DateTime.UtcNow.Add(expectedDelay.Subtract(TimeSpan.FromSeconds(1))) &&
                                 dt <= DateTime.UtcNow.Add(expectedDelay.Add(TimeSpan.FromSeconds(1))))), Times.Once);
    }

    [Fact]
    public async Task Should_Retry_Transient_Failure_With_125_Second_Delay_On_Third_Attempt()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, 100m);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        creditMessage.ProcessingCount = 3;

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();
        
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(new InvalidOperationException("Temporary service unavailable"));

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        var expectedDelay = TimeSpan.FromSeconds(125);
        mockServiceBus.Verify(x => x.ReSchedule(creditMessage, 
            It.Is<DateTime>(dt => dt >= DateTime.UtcNow.Add(expectedDelay.Subtract(TimeSpan.FromSeconds(1))) &&
                                 dt <= DateTime.UtcNow.Add(expectedDelay.Add(TimeSpan.FromSeconds(1))))), Times.Once);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(DbUpdateException), true)]
    [InlineData(typeof(InvalidOperationException), true)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(FormatException), false)]
    [InlineData(typeof(JsonException), false)]
    public async Task Should_Classify_Exceptions_As_Transient_Or_NonTransient(Type exceptionType, bool isTransient)
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, 100m);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        creditMessage.ProcessingCount = 1;

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();
        
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test exception")!;
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(exception);

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        if (isTransient)
        {
            mockServiceBus.Verify(x => x.ReSchedule(It.IsAny<EventMessage>(), It.IsAny<DateTime>()), Times.Once);
            mockServiceBus.Verify(x => x.MoveToDeadLetter(It.IsAny<EventMessage>()), Times.Never);
        }
        else
        {
            mockServiceBus.Verify(x => x.MoveToDeadLetter(creditMessage), Times.Once);
            mockServiceBus.Verify(x => x.ReSchedule(It.IsAny<EventMessage>(), It.IsAny<DateTime>()), Times.Never);
        }
    }

    [Fact]
    public async Task Should_Move_To_DeadLetter_After_Max_Retry_Attempts()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, 100m);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, 50m);
        creditMessage.ProcessingCount = 4; // Exceeded 3 retries

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();
        
        mockServiceBus.Setup(x => x.Complete(It.IsAny<EventMessage>()))
                     .ThrowsAsync(new TimeoutException("Database timeout"));

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        mockServiceBus.Verify(x => x.MoveToDeadLetter(creditMessage), Times.Once);
        mockServiceBus.Verify(x => x.ReSchedule(It.IsAny<EventMessage>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Should_Process_Successfully_After_Transient_Failure_Resolution()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var initialBalance = 100m;
        var creditAmount = 50m;
        var account = TestData.BankAccounts.WithIdAndBalance(accountId, initialBalance);
        var creditMessage = TestData.EventMessages.CreditMessage(accountId, creditAmount);

        using var dbContext = InMemoryDbContext.CreateWithData(account);
        var mockServiceBus = MockServiceBusReceiver.Create();

        var worker = new MessageWorker(mockServiceBus.Object, dbContext);

        // Act - Process message successfully (no transient failure)
        await worker.ProcessMessageWithRetry(creditMessage);

        // Assert
        var updatedAccount = dbContext.BankAccounts.Find(accountId);
        updatedAccount!.Balance.Should().Be(initialBalance + creditAmount);
        mockServiceBus.Verify(x => x.Complete(creditMessage), Times.Once);
        mockServiceBus.Verify(x => x.ReSchedule(It.IsAny<EventMessage>(), It.IsAny<DateTime>()), Times.Never);
        mockServiceBus.Verify(x => x.MoveToDeadLetter(It.IsAny<EventMessage>()), Times.Never);
    }
}