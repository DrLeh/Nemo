﻿using Microsoft.EntityFrameworkCore;
using Nemo.Configuration;

namespace Nemo.Benchmark.Entities;

public class EFContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Order> Orders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var settings = new ConfigurationFactory().DefaultConfiguration.SystemConfiguration.ConnectionString("DbConnectionSqlServer");
        optionsBuilder.UseSqlServer(settings.ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Property(x => x.Id).HasColumnName("CustomerID");
        modelBuilder.Entity<Customer>().HasKey(x => x.Id);
        modelBuilder.Entity<Order>().Property(x => x.CustomerId).HasColumnName("CustomerID");
        modelBuilder.Entity<Order>().HasKey(x => x.OrderId);
    }
}
