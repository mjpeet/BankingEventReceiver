using BankingApi.EventReceiver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Create host builder
var builder = Host.CreateApplicationBuilder(args);

// Configure services - Use in-memory database for demo
builder.Services.AddDbContext<BankingApiDbContext>(options =>
    options.UseInMemoryDatabase("BankingApiDemo"));
builder.Services.AddScoped<IServiceBusReceiver, ServiceBusReceiver>(); // Mock implementation
builder.Services.AddScoped<MessageWorker>();
builder.Services.AddHostedService<BankingEventReceiverService>();
builder.Services.AddLogging(configure => configure.AddConsole());

var host = builder.Build();

// Ensure database is created (in-memory database doesn't need migrations)
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BankingApiDbContext>();
    await context.Database.EnsureCreatedAsync();
    
    // Seed with some demo data
    if (!context.BankAccounts.Any())
    {
        context.BankAccounts.AddRange(
            new BankAccount { Id = Guid.Parse("7d445724-24ec-4d52-aa7a-ff2bac9f191d"), Balance = 1000m },
            new BankAccount { Id = Guid.Parse("3bbaf4ca-5bfa-4922-a395-d755beac475f"), Balance = 500m }
        );
        await context.SaveChangesAsync();
    }
}

// Run the application
await host.RunAsync();

public class BankingEventReceiverService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BankingEventReceiverService> _logger;

    public BankingEventReceiverService(IServiceProvider serviceProvider, ILogger<BankingEventReceiverService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Banking Event Receiver Service starting...");

        using var scope = _serviceProvider.CreateScope();
        var messageWorker = scope.ServiceProvider.GetRequiredService<MessageWorker>();

        try
        {
            await messageWorker.Start(stoppingToken);
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Banking Event Receiver Service stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Banking Event Receiver Service");
            throw;
        }
    }
}

// Mock implementation for demonstration - replace with actual Azure Service Bus implementation
public class ServiceBusReceiver : IServiceBusReceiver
{
    private readonly ILogger<ServiceBusReceiver> _logger;

    public ServiceBusReceiver(ILogger<ServiceBusReceiver> logger)
    {
        _logger = logger;
    }

    public Task<EventMessage?> Peek()
    {
        // In production, this would connect to Azure Service Bus
        _logger.LogInformation("Peeking for messages from Service Bus...");
        return Task.FromResult<EventMessage?>(null); // No messages for demo
    }

    public Task Abandon(EventMessage message)
    {
        _logger.LogInformation($"Abandoning message {message.Id}");
        return Task.CompletedTask;
    }

    public Task Complete(EventMessage message)
    {
        _logger.LogInformation($"Completing message {message.Id}");
        return Task.CompletedTask;
    }

    public Task ReSchedule(EventMessage message, DateTime nextAvailableTime)
    {
        _logger.LogInformation($"Rescheduling message {message.Id} for {nextAvailableTime}");
        return Task.CompletedTask;
    }

    public Task MoveToDeadLetter(EventMessage message)
    {
        _logger.LogWarning($"Moving message {message.Id} to dead letter queue");
        return Task.CompletedTask;
    }
}