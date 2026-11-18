using FilmSearch.Core.Catalog;
using FilmSearch.Core.Commerce;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Commerce;

// EF Core mapping of the Commerce module: tables in the "commerce" schema, enums as strings.
// Orders keep a snapshot of titles and prices, so they have no foreign keys to catalog tables: deleting a movie
// must not delete order history. Cart lines do reference movies and offers (a deleted movie leaves the cart).

internal static class CommerceSchema
{
    public const string Name = "commerce";
}

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts", CommerceSchema.Name);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasIndex(c => c.OwnerId).IsUnique();

        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items", CommerceSchema.Name);
        builder.HasKey(i => new { i.CartId, i.MovieId });
        builder.Property(i => i.OfferType).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.AddedBy).HasConversion<string>().HasMaxLength(16);

        builder.HasOne<Movie>().WithMany().HasForeignKey(i => i.MovieId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Offer>().WithMany().HasForeignKey(i => i.OfferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => i.OfferId);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", CommerceSchema.Name);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Currency).HasMaxLength(3);
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(CommerceLimits.IdempotencyKeyMaxLength);

        // Optimistic concurrency (spec 03 §2): a uint row version maps to the PostgreSQL system column xmin.
        builder.Property(o => o.Version).IsRowVersion();

        builder.HasIndex(o => new { o.OwnerId, o.CreatedAt });
        builder.HasIndex(o => new { o.OwnerId, o.IdempotencyKey }).IsUnique();
        // The expiry job looks for unpaid orders past their expiry time.
        builder.HasIndex(o => o.ExpiresAt)
            .HasFilter("status IN ('PendingPayment', 'Failed')")
            .HasDatabaseName("ix_orders_unpaid_expires_at");

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Entitlements)
            .WithOne()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", CommerceSchema.Name);
        builder.HasKey(i => new { i.OrderId, i.MovieId });
        builder.Property(i => i.OfferType).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.TitleSnapshot).HasMaxLength(500);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", CommerceSchema.Name);
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.ProviderSessionId).HasMaxLength(200);
        builder.Property(p => p.ProviderPaymentIntentId).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Currency).HasMaxLength(3);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(2000);

        builder.HasOne<Order>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.OrderId, p.Attempt }).IsUnique();
        builder.HasIndex(p => new { p.Provider, p.ProviderSessionId }).IsUnique();
    }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events", CommerceSchema.Name);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.ProviderEventId).HasMaxLength(200);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");
        builder.Property(e => e.Error).HasMaxLength(2000);

        // Webhook deduplication (FR-COM-5, spec 07 §5).
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId }).IsUnique();
    }
}

internal sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable("entitlements", CommerceSchema.Name);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(e => new { e.OwnerId, e.MovieId });
    }
}
