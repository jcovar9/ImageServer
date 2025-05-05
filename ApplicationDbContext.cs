using JonahsImageServer.Models;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<DBUser> Users { get; set; }
    public DbSet<DBFolder> Folders { get; set; }
    public DbSet<DBImage> Images { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DBUser>().HasKey(u => u.Username); // Username is the primary key
            
        modelBuilder.Entity<DBFolder>().HasKey(f => f.ID); // ID is the primary key
        
        modelBuilder.Entity<DBImage>().HasKey(i => i.ID); // ID is the primary key

        base.OnModelCreating(modelBuilder);
    }
}
