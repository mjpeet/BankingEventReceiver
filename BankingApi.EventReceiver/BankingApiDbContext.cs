using Microsoft.EntityFrameworkCore;

namespace BankingApi.EventReceiver
{
    public class BankingApiDbContext : DbContext
    {
        public BankingApiDbContext() { }
        
        public BankingApiDbContext(DbContextOptions<BankingApiDbContext> options) : base(options) { }

        public DbSet<BankAccount> BankAccounts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Only configure if not already configured (allows DI to override)
            if (!options.IsConfigured)
            {
                options.UseSqlServer("Data Source=.\\SQLEXPRESS;Initial Catalog=BankingApiTest;Integrated Security=True;TrustServerCertificate=True;");
            }
        }
    }
}
