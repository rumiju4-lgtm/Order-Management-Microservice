using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. Force EF Core to use the singular table name "Order" instead of "Orders"
        modelBuilder.Entity<Order>().ToTable("Order");

        // 2. Map the exact decimal precision configuration your manual table uses 
        //    (This will also remove that warning we saw during startup!)
        modelBuilder.Entity<Order>()
            .Property(o => o.Price)
            .HasPrecision(18, 4); 

        base.OnModelCreating(modelBuilder);
    }
}