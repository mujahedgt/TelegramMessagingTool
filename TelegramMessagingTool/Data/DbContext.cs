using Microsoft.EntityFrameworkCore;
using TelegramMessagingTool.Models;

namespace TelegramMessagingTool.Data;

public class TelegramDbContext : DbContext
{
    public DbSet<ConnectedUser> Users => Set<ConnectedUser>();

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    public DbSet<Memory> Memories => Set<Memory>();

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<PendingAction> PendingActions => Set<PendingAction>();

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    public DbSet<AgentTaskStep> AgentTaskSteps => Set<AgentTaskStep>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string connectionString = Environment.GetEnvironmentVariable("TELEGRAM_DB_CONNECTION")
                ?? @"Server=(localdb)\MSSQLLocalDB;Database=TelegramMessagingTool;Trusted_Connection=True;TrustServerCertificate=True";

            optionsBuilder.UseSqlServer(connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConnectedUser>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ChatId)
                .IsUnique();

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.FirstName)
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .HasMaxLength(100);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(x => x.LastSeenAt)
                .HasDefaultValueSql("GETUTCDATE()");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Content)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.Role)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(x => x.Timestamp)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ChatId);

            entity.HasIndex(x => x.Timestamp);

            entity.HasOne(x => x.User)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Memory>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Content)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(x => x.UpdatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ConnectedUserId);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UploadedFile>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.OriginalFileName)
                .HasMaxLength(255);

            entity.Property(x => x.StoredFileName)
                .HasMaxLength(300);

            entity.Property(x => x.RelativePath)
                .HasMaxLength(500);

            entity.Property(x => x.AbsolutePath)
                .HasMaxLength(1000);

            entity.Property(x => x.ContentType)
                .HasMaxLength(100);

            entity.Property(x => x.Source)
                .HasMaxLength(50);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ConnectedUserId);
            entity.HasIndex(x => x.ChatId);
            entity.HasIndex(x => x.CreatedAt);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.OriginalFileName)
                .HasMaxLength(255);

            entity.Property(x => x.Text)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.CharacterCount);

            entity.Property(x => x.EmbeddingJson)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.EmbeddingModel)
                .HasMaxLength(100);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ConnectedUserId);
            entity.HasIndex(x => x.ChatId);
            entity.HasIndex(x => x.UploadedFileId);
            entity.HasIndex(x => new { x.ConnectedUserId, x.UploadedFileId, x.ChunkNumber })
                .IsUnique();

            entity.HasOne(x => x.UploadedFile)
                .WithMany()
                .HasForeignKey(x => x.UploadedFileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PendingAction>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ToolName)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.PayloadJson)
                .HasColumnType("nvarchar(max)");

            entity.Property(x => x.RiskLevel)
                .HasMaxLength(50);

            entity.Property(x => x.Status)
                .HasMaxLength(30);

            entity.Property(x => x.DecisionNote)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ConnectedUserId);
            entity.HasIndex(x => x.ChatId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.ExpiresAt);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTask>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Goal)
                .HasMaxLength(500);

            entity.Property(x => x.Status)
                .HasMaxLength(30);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(x => x.UpdatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.ConnectedUserId);
            entity.HasIndex(x => x.ChatId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.UpdatedAt);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.ConnectedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTaskStep>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(x => x.AgentTaskId);
            entity.HasIndex(x => new { x.AgentTaskId, x.StepNumber })
                .IsUnique();

            entity.HasOne(x => x.AgentTask)
                .WithMany(x => x.Steps)
                .HasForeignKey(x => x.AgentTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}