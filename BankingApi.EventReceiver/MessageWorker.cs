using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.EventReceiver
{
    public class MessageWorker
    {
        private readonly IServiceBusReceiver _serviceBusReceiver;
        private readonly BankingApiDbContext _dbContext;

        public MessageWorker(IServiceBusReceiver serviceBusReceiver)
        {
            _serviceBusReceiver = serviceBusReceiver;
        }

        public MessageWorker(IServiceBusReceiver serviceBusReceiver, BankingApiDbContext dbContext)
        {
            _serviceBusReceiver = serviceBusReceiver;
            _dbContext = dbContext;
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var message = await _serviceBusReceiver.Peek();
                    
                    if (message == null)
                    {
                        // No messages available, wait 10 seconds as per requirements
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        continue;
                    }

                    // Process message with proper retry and concurrency handling
                    await ProcessMessageWithRetryAndAbandon(message);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    // Log error and continue processing (in production, would log to ILogger)
                    Console.WriteLine($"Error in message processing loop: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Brief pause before continuing
                }
            }
        }

        private async Task ProcessMessageWithRetryAndAbandon(EventMessage message)
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<TransactionModel>(message.MessageBody);
                
                if (transaction == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var account = await _dbContext.BankAccounts.FindAsync(transaction.BankAccountId);
                if (account == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                switch (transaction.MessageType)
                {
                    case "Credit":
                        account.Balance += transaction.Amount;
                        break;
                    case "Debit":
                        account.Balance -= transaction.Amount;
                        break;
                    default:
                        await _serviceBusReceiver.MoveToDeadLetter(message);
                        return;
                }

                await _dbContext.SaveChangesAsync();
                await _serviceBusReceiver.Complete(message);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                // For transient exceptions, abandon the message to trigger retry
                // Service Bus will automatically move to dead letter after 3 abandons
                await _serviceBusReceiver.Abandon(message);
            }
            catch
            {
                // For non-transient exceptions, move directly to dead letter
                await _serviceBusReceiver.MoveToDeadLetter(message);
            }
        }

        public async Task ProcessMessage(EventMessage message)
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<TransactionModel>(message.MessageBody);
                
                if (transaction == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var account = _dbContext.BankAccounts.Find(transaction.BankAccountId);
                if (account == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                switch (transaction.MessageType)
                {
                    case "Credit":
                        account.Balance += transaction.Amount;
                        break;
                    case "Debit":
                        account.Balance -= transaction.Amount;
                        break;
                    default:
                        await _serviceBusReceiver.MoveToDeadLetter(message);
                        return;
                }

                await _dbContext.SaveChangesAsync();
                await _serviceBusReceiver.Complete(message);
            }
            catch
            {
                await _serviceBusReceiver.MoveToDeadLetter(message);
            }
        }

        public async Task ProcessMessageWithRetry(EventMessage message)
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<TransactionModel>(message.MessageBody);
                
                if (transaction == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var account = _dbContext.BankAccounts.Find(transaction.BankAccountId);
                if (account == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                switch (transaction.MessageType)
                {
                    case "Credit":
                        account.Balance += transaction.Amount;
                        break;
                    case "Debit":
                        account.Balance -= transaction.Amount;
                        break;
                    default:
                        await _serviceBusReceiver.MoveToDeadLetter(message);
                        return;
                }

                await _dbContext.SaveChangesAsync();
                await _serviceBusReceiver.Complete(message);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                if (message.ProcessingCount > 3)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var delay = GetRetryDelay(message.ProcessingCount);
                var nextAvailableTime = DateTime.UtcNow.Add(delay);
                await _serviceBusReceiver.ReSchedule(message, nextAvailableTime);
            }
            catch
            {
                await _serviceBusReceiver.MoveToDeadLetter(message);
            }
        }

        private bool IsTransientException(Exception ex)
        {
            return ex is TimeoutException ||
                   ex is Microsoft.EntityFrameworkCore.DbUpdateException ||
                   ex is InvalidOperationException;
        }

        private TimeSpan GetRetryDelay(int processingCount)
        {
            return processingCount switch
            {
                1 => TimeSpan.FromSeconds(5),
                2 => TimeSpan.FromSeconds(25),
                3 => TimeSpan.FromSeconds(125),
                _ => TimeSpan.FromSeconds(5)
            };
        }

        public virtual async Task ProcessMessageWithConcurrency(EventMessage message)
        {
            var originalBalance = (decimal?)null;
            
            try
            {
                var transactionModel = JsonSerializer.Deserialize<TransactionModel>(message.MessageBody);
                
                if (transactionModel == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var account = await _dbContext.BankAccounts.FindAsync(transactionModel.BankAccountId);
                if (account == null)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                originalBalance = account.Balance;

                switch (transactionModel.MessageType)
                {
                    case "Credit":
                        account.Balance += transactionModel.Amount;
                        break;
                    case "Debit":
                        account.Balance -= transactionModel.Amount;
                        break;
                    default:
                        await _serviceBusReceiver.MoveToDeadLetter(message);
                        return;
                }

                await _dbContext.SaveChangesAsync();
                await _serviceBusReceiver.Complete(message);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (originalBalance.HasValue)
                {
                    var account = await _dbContext.BankAccounts.FindAsync(JsonSerializer.Deserialize<TransactionModel>(message.MessageBody)!.BankAccountId);
                    if (account != null)
                    {
                        account.Balance = originalBalance.Value;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                
                if (message.ProcessingCount > 3)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var delay = GetRetryDelay(message.ProcessingCount);
                var nextAvailableTime = DateTime.UtcNow.Add(delay);
                await _serviceBusReceiver.ReSchedule(message, nextAvailableTime);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                if (originalBalance.HasValue)
                {
                    var account = await _dbContext.BankAccounts.FindAsync(JsonSerializer.Deserialize<TransactionModel>(message.MessageBody)!.BankAccountId);
                    if (account != null)
                    {
                        account.Balance = originalBalance.Value;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                
                if (message.ProcessingCount > 3)
                {
                    await _serviceBusReceiver.MoveToDeadLetter(message);
                    return;
                }

                var delay = GetRetryDelay(message.ProcessingCount);
                var nextAvailableTime = DateTime.UtcNow.Add(delay);
                await _serviceBusReceiver.ReSchedule(message, nextAvailableTime);
            }
            catch
            {
                if (originalBalance.HasValue)
                {
                    var account = await _dbContext.BankAccounts.FindAsync(JsonSerializer.Deserialize<TransactionModel>(message.MessageBody)!.BankAccountId);
                    if (account != null)
                    {
                        account.Balance = originalBalance.Value;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                await _serviceBusReceiver.MoveToDeadLetter(message);
            }
        }
    }
}
