using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Service.Template.Domain.Examples;

namespace Payments.Service.Template.Infrastructure.Persistence;

public sealed class TemplateDbContext : DbContext, IUnitOfWork
{
    public TemplateDbContext(DbContextOptions<TemplateDbContext> options)
        : base(options)
    {
    }

    public DbSet<Example> Examples => Set<Example>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("template");
        modelBuilder.ApplyConfiguration(new ExampleConfiguration());
    }
}

internal sealed class ExampleConfiguration : IEntityTypeConfiguration<Example>
{
    public void Configure(EntityTypeBuilder<Example> builder)
    {
        builder.ToTable("examples");
        builder.HasKey(example => example.Id);
        builder.Property(example => example.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ExampleId.From(value))
            .ValueGeneratedNever();
        builder.Property(example => example.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(example => example.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(example => example.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property<uint>("xmin")
            .IsRowVersion();
        builder.HasIndex(example => example.Email)
            .HasDatabaseName("ix_examples_email")
            .IsUnique();
    }
}
