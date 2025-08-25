using Microsoft.EntityFrameworkCore;
using BankingApi.EventReceiver;

namespace BankingApi.EventReceiver.Test.TestHelpers;

public class TestDbContext : BankingApiDbContext
{
    private readonly DbContextOptions<BankingApiDbContext> _options;

    public TestDbContext(DbContextOptions<BankingApiDbContext> options) : base()
    {
        _options = options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString());
    }
}

public static class InMemoryDbContext
{
    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<BankingApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static TestDbContext CreateWithData(params BankAccount[] accounts)
    {
        var context = Create();
        context.BankAccounts.AddRange(accounts);
        context.SaveChanges();
        return context;
    }
}