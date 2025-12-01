using CareerConnect.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Server.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<EmailVerificationCode> EmailVerificationCodes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EmailVerificationCode>()
                .HasIndex(e => new { e.Email, e.VerificationType, e.IsUsed });

            modelBuilder.Entity<EmailVerificationCode>()
                .HasIndex(e => e.CreatedAt);

            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "admin" },
                new Role { Id = 2, Name = "employee" },
                new Role { Id = 3, Name = "employer" }
            );
        }
    }
}