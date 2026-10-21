using EEMC.WM.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EEMC.WM.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();
    public DbSet<AppNotification> Notifications => Set<AppNotification>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<CodeCounter> CodeCounters => Set<CodeCounter>();
    public DbSet<SyncTombstone> SyncTombstones => Set<SyncTombstone>();
    public DbSet<SyncSourceState> SyncSourceStates => Set<SyncSourceState>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AttachmentLink> AttachmentLinks => Set<AttachmentLink>();
    public DbSet<AttachmentVersion> AttachmentVersions => Set<AttachmentVersion>();
    public DbSet<DocFolder> DocFolders => Set<DocFolder>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<LegalSection> LegalSections => Set<LegalSection>();
    public DbSet<LegalPoint> LegalPoints => Set<LegalPoint>();
    public DbSet<LegalRelation> LegalRelations => Set<LegalRelation>();
    public DbSet<LegalTaskLink> LegalTaskLinks => Set<LegalTaskLink>();

    /// <summary>Mã máy hiện tại (đặt khi khởi động), ghi vào công việc mới tạo.</summary>
    public static string? CurrentDeviceId { get; set; }

    /// <summary>Khi nhập dữ liệu từ máy khác: không đánh dấu thay đổi cần gửi đi (tránh gửi vòng lại).</summary>
    public bool ImportMode { get; set; }

    private static long _lastStamp;

    /// <summary>Dấu thời gian UTC tăng dần trong tiến trình.</summary>
    public static long NextStamp()
    {
        while (true)
        {
            var last = Interlocked.Read(ref _lastStamp);
            var next = Math.Max(DateTime.UtcNow.Ticks, last + 1);
            if (Interlocked.CompareExchange(ref _lastStamp, next, last) == last) return next;
        }
    }

    /// <summary>Các trường chỉ có ý nghĩa trên máy này; thay đổi riêng chúng không cần đồng bộ.</summary>
    private static readonly HashSet<string> LocalOnlyProps = new()
    {
        nameof(WorkTask.SnoozeUntil), nameof(ISyncEntity.SyncDirty), nameof(ISyncEntity.SyncStamp)
    };

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampChanges()
    {
        if (ImportMode) return;
        foreach (var entry in ChangeTracker.Entries<ISyncEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (string.IsNullOrEmpty(entry.Entity.SyncId)) entry.Entity.SyncId = SyncIds.New();
                entry.Entity.SyncStamp = NextStamp();
                entry.Entity.SyncDirty = true;
                if (entry.Entity is WorkTask t && string.IsNullOrEmpty(t.OriginDevice)) t.OriginDevice = CurrentDeviceId;
            }
            else if (entry.State == EntityState.Modified)
            {
                bool meaningful = entry.Properties.Any(p => p.IsModified && !LocalOnlyProps.Contains(p.Metadata.Name));
                if (!meaningful) continue;
                entry.Entity.SyncStamp = NextStamp();
                entry.Entity.SyncDirty = true;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WorkTask>(e =>
        {
            e.ToTable("WorkTasks");
            e.Property(x => x.Code).IsRequired().HasMaxLength(32);
            // Không đặt UNIQUE: hai máy làm việc offline có thể tạm sinh trùng mã, được xử lý khi đồng bộ
            e.HasIndex(x => x.Code);
            e.Property(x => x.SyncId).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.SyncId).IsUnique();
            e.HasIndex(x => x.SyncDirty);
            e.Property(x => x.OriginDevice).HasMaxLength(32);
            e.Property(x => x.Title).IsRequired().HasMaxLength(2000);
            e.Property(x => x.ProjectName).HasMaxLength(500);
            e.Property(x => x.AssignedBy).HasMaxLength(200);
            e.Property(x => x.Assignee).HasMaxLength(200);
            e.Property(x => x.Coordinator).HasMaxLength(500);
            e.Property(x => x.DocumentNumber).HasMaxLength(200);
            e.Property(x => x.FolderPath).HasMaxLength(1000);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.IsDeleted, x.Status, x.DueAt });
            e.HasIndex(x => x.DueAt);
            e.HasIndex(x => x.Priority);
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.PlannedStart);
            e.HasIndex(x => x.ReceivedAt);
            e.HasIndex(x => x.ClosedAt);
            e.HasIndex(x => x.ParentId);
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("Categories");
            e.Property(x => x.Name).IsRequired().HasMaxLength(300);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.DisplayName);
            e.Property(x => x.SyncId).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.SyncId).IsUnique();
        });

        b.Entity<TaskHistory>(e =>
        {
            e.ToTable("TaskHistories");
            e.HasIndex(x => new { x.TaskId, x.At });
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.SyncId).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.SyncId).IsUnique();
            e.HasIndex(x => x.SyncDirty);
        });

        b.Entity<SyncTombstone>(e =>
        {
            e.ToTable("SyncTombstones");
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.SyncId).HasMaxLength(32);
            e.HasIndex(x => new { x.Kind, x.SyncId }).IsUnique();
        });

        b.Entity<SyncSourceState>(e =>
        {
            e.ToTable("SyncSourceStates");
            e.HasKey(x => x.DeviceId);
            e.Property(x => x.DeviceId).HasMaxLength(32);
        });

        // ---------------- Giai đoạn 2
        b.Entity<WorkTask>(e =>
        {
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.ProjectId);
            e.Property(x => x.Weight).HasDefaultValue(1.0);
            e.Ignore(x => x.DependsOnIds);
            e.Ignore(x => x.BlockedBy);
            e.Ignore(x => x.IsBlocked);
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Code).IsRequired().HasMaxLength(50);
            e.Property(x => x.Name).IsRequired().HasMaxLength(1000);
            e.HasMany(x => x.Milestones).WithOne().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Code);
        });

        b.Entity<Milestone>(e =>
        {
            e.ToTable("Milestones");
            e.Property(x => x.Name).IsRequired().HasMaxLength(500);
        });

        b.Entity<Attachment>(e =>
        {
            e.ToTable("Attachments");
            e.Property(x => x.Name).IsRequired().HasMaxLength(500);
            e.Property(x => x.Link).HasMaxLength(2000);
            e.Property(x => x.StoredFile).HasMaxLength(300);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.Property(x => x.CurrentVersion).HasDefaultValue(1);
            e.HasIndex(x => x.Sha256);
            e.HasIndex(x => x.IsDeleted);
        });

        b.Entity<AttachmentLink>(e =>
        {
            e.ToTable("AttachmentLinks");
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<DocFolder>().WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.AttachmentId);
            e.HasIndex(x => x.FolderId);
            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.LegalDocumentId);
        });

        b.Entity<AttachmentVersion>(e =>
        {
            e.ToTable("AttachmentVersions");
            e.Property(x => x.StoredFile).IsRequired().HasMaxLength(300);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(500);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AttachmentId, x.VersionNo }).IsUnique();
        });

        b.Entity<DocFolder>(e =>
        {
            e.ToTable("DocFolders");
            e.Property(x => x.Name).IsRequired().HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasOne<DocFolder>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.TaskId);
            e.HasIndex(x => x.LegalDocumentId);
        });

        b.Entity<TaskDependency>(e =>
        {
            e.ToTable("TaskDependencies");
            e.HasIndex(x => new { x.TaskId, x.DependsOnTaskId }).IsUnique();
            e.HasIndex(x => x.DependsOnTaskId);
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.UserName).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.UserName).IsUnique();
            e.Ignore(x => x.IsAdmin);
        });

        // ---------------- Văn bản pháp luật
        b.Entity<LegalDocument>(e =>
        {
            e.ToTable("LegalDocuments");
            e.Property(x => x.Number).HasMaxLength(100);
            e.Property(x => x.Title).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Issuer).HasMaxLength(300);
            e.Property(x => x.Tags).HasMaxLength(500);
            e.Property(x => x.SourceUrl).HasMaxLength(2000);
            e.Property(x => x.StoredFile).HasMaxLength(300);
            e.Property(x => x.FileName).HasMaxLength(300);
            e.Property(x => x.SummaryAuthor).HasMaxLength(200);
            e.Property(x => x.SummaryApprovedBy).HasMaxLength(200);
            e.Ignore(x => x.ShortName);
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Points).WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Effect);
            e.HasIndex(x => x.Number);
        });

        b.Entity<LegalSection>(e =>
        {
            e.ToTable("LegalSections");
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Heading).HasMaxLength(1000);
            e.HasIndex(x => new { x.DocumentId, x.SortOrder });
        });

        b.Entity<LegalPoint>(e =>
        {
            e.ToTable("LegalPoints");
            e.Property(x => x.Text).IsRequired().HasMaxLength(4000);
            e.Property(x => x.Citation).HasMaxLength(300);
            e.Property(x => x.Due).HasMaxLength(300);
            e.HasIndex(x => new { x.DocumentId, x.Kind, x.SortOrder });
        });

        b.Entity<LegalRelation>(e =>
        {
            e.ToTable("LegalRelations");
            e.Property(x => x.TargetText).HasMaxLength(1000);
            e.HasOne<LegalDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<LegalDocument>().WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.TargetId);
        });

        b.Entity<LegalTaskLink>(e =>
        {
            e.ToTable("LegalTaskLinks");
            e.HasOne<LegalDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DocumentId, x.TaskId }).IsUnique();
            e.HasIndex(x => x.TaskId);
        });

        // ---------------- Trợ lý AI
        b.Entity<ChatConversation>(e =>
        {
            e.ToTable("ChatConversations");
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });

        b.Entity<ChatMessage>(e =>
        {
            e.ToTable("ChatMessages");
            e.Property(x => x.Role).IsRequired().HasMaxLength(16);
            e.Property(x => x.Model).HasMaxLength(64);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasOne<ChatConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ConversationId, x.Id });
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<AppNotification>(e =>
        {
            e.ToTable("Notifications");
            e.HasIndex(x => new { x.IsRead, x.CreatedAt });
        });

        b.Entity<ReminderLog>(e =>
        {
            e.ToTable("ReminderLogs");
            e.HasIndex(x => new { x.TaskId, x.ReminderKey }).IsUnique();
            e.Property(x => x.ReminderKey).HasMaxLength(64);
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });

        b.Entity<CodeCounter>(e =>
        {
            e.ToTable("CodeCounters");
            e.HasKey(x => x.Year);
            e.Property(x => x.Year).ValueGeneratedNever();
        });
    }
}

/// <summary>Dùng cho công cụ dotnet-ef khi tạo migration.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design.db")
            .Options;
        return new AppDbContext(options);
    }
}
