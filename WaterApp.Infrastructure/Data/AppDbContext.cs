using Microsoft.EntityFrameworkCore;
using WaterApp.Domain.Entities;

namespace WaterApp.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<ServiceArea> ServiceAreas => Set<ServiceArea>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushToken> PushTokens => Set<PushToken>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Phone).IsUnique();
            e.Property(u => u.Role).HasConversion<string>();
        });

        modelBuilder.Entity<Seller>(e =>
        {
            e.HasOne(s => s.User)
                .WithOne(u => u.SellerProfile)
                .HasForeignKey<Seller>(s => s.UserId);
            e.Property(s => s.Status).HasConversion<string>();
            e.Property(s => s.Category).HasConversion<string>();
        });

        modelBuilder.Entity<ServiceArea>(e =>
        {
            e.HasOne(sa => sa.Seller)
                .WithMany(s => s.ServiceAreas)
                .HasForeignKey(sa => sa.SellerId);
            e.HasIndex(sa => sa.Pincode);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasOne(p => p.Seller)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.SellerId);
            e.Property(p => p.Price).HasColumnType("decimal(10,2)");
            e.Property(p => p.Category).HasConversion<string>();
        });

        modelBuilder.Entity<ProductImage>(e =>
        {
            e.HasKey(pi => pi.ProductId);
            e.HasOne(pi => pi.Product)
                .WithOne()
                .HasForeignKey<ProductImage>(pi => pi.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Address>(e =>
        {
            e.HasOne(a => a.User)
                .WithMany(u => u.Addresses)
                .HasForeignKey(a => a.UserId);
        });

        modelBuilder.Entity<Cart>(e =>
        {
            e.HasOne(c => c.Buyer)
                .WithMany()
                .HasForeignKey(c => c.BuyerId);
        });

        modelBuilder.Entity<CartItem>(e =>
        {
            e.HasOne(ci => ci.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CartId);
            e.HasOne(ci => ci.Product)
                .WithMany()
                .HasForeignKey(ci => ci.ProductId);
            e.HasIndex(ci => new { ci.CartId, ci.ProductId }).IsUnique();
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasOne(o => o.Buyer).WithMany().HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Seller).WithMany().HasForeignKey(o => o.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Address).WithMany().HasForeignKey(o => o.AddressId).OnDelete(DeleteBehavior.Restrict);
            e.Property(o => o.Status).HasConversion<string>();
            e.Property(o => o.PaymentMode).HasConversion<string>();
            e.Property(o => o.PaymentStatus).HasConversion<string>();
            e.Property(o => o.TotalAmount).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasOne(oi => oi.Order).WithMany(o => o.Items).HasForeignKey(oi => oi.OrderId);
            e.HasOne(oi => oi.Product).WithMany().HasForeignKey(oi => oi.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Property(oi => oi.PriceAtPurchase).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasOne(p => p.Order).WithOne(o => o.Payment).HasForeignKey<Payment>(p => p.OrderId);
            e.Property(p => p.Status).HasConversion<string>();
            e.Property(p => p.Amount).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<Review>(e =>
        {
            e.HasOne(r => r.Seller).WithMany().HasForeignKey(r => r.SellerId);
            e.HasOne(r => r.Buyer).WithMany().HasForeignKey(r => r.BuyerId);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId);
            e.HasIndex(n => new { n.UserId, n.CreatedAt });
        });

        modelBuilder.Entity<PushToken>(e =>
        {
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId);
            // A token identifies one device; re-registering it (new login,
            // reinstall) should move it to the new owner, not duplicate it.
            e.HasIndex(t => t.Token).IsUnique();
        });

        modelBuilder.Entity<PasswordResetOtp>(e =>
        {
            e.HasOne(o => o.User).WithMany().HasForeignKey(o => o.UserId);
            e.HasIndex(o => new { o.UserId, o.CreatedAt });
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.TokenHash).IsUnique();
        });
    }
}
