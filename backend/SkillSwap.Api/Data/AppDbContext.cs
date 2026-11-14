using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace SkillSwap.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();
    public DbSet<Swap> Swaps => Set<Swap>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<LessonSession> Sessions => Set<LessonSession>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<UserEvent> UserEvents => Set<UserEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<Follow> Follows => Set<Follow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>().Property(u => u.Name).HasMaxLength(80);

        b.Entity<Skill>(e =>
        {
            e.HasIndex(s => s.Name).IsUnique();
            e.Property(s => s.Name).HasMaxLength(60);
            e.Property(s => s.Category).HasMaxLength(40);
        });

        b.Entity<UserSkill>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Level).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.SkillId, x.Type }).IsUnique();
            e.HasIndex(x => new { x.SkillId, x.Type });
            e.HasOne(x => x.User).WithMany(u => u.Skills).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Swap>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.ProposerId);
            e.HasIndex(x => x.ReceiverId);
            e.HasOne(x => x.Proposer).WithMany().HasForeignKey(x => x.ProposerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Receiver).WithMany().HasForeignKey(x => x.ReceiverId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ProposerTeaches).WithMany().HasForeignKey(x => x.ProposerTeachesId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReceiverTeaches).WithMany().HasForeignKey(x => x.ReceiverTeachesId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Message>(e =>
        {
            e.HasIndex(x => new { x.SwapId, x.SentAt });
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasOne(x => x.Swap).WithMany(s => s.Messages).HasForeignKey(x => x.SwapId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LessonSession>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Swap).WithMany(s => s.Sessions).HasForeignKey(x => x.SwapId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Review>(e =>
        {
            e.HasIndex(x => new { x.SwapId, x.ReviewerId }).IsUnique();   // one review per person per swap
            e.HasIndex(x => x.RevieweeId);
            e.HasOne(x => x.Swap).WithMany().HasForeignKey(x => x.SwapId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Reviewee).WithMany().HasForeignKey(x => x.RevieweeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<UserEvent>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.SkillId, x.CreatedAt });   // for "trending" later
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Post>(e =>
        {
            e.HasIndex(x => new { x.Category, x.CreatedAt });
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostLike>(e =>
        {
            e.HasKey(x => new { x.PostId, x.UserId });
            e.HasOne(x => x.Post).WithMany(p => p.Likes).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PostComment>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.CreatedAt });
            e.Property(x => x.Content).HasMaxLength(2000);
            e.HasOne(x => x.Post).WithMany(p => p.Comments).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FollowingId });
            e.HasIndex(x => x.FollowingId);
            e.HasOne(x => x.Follower).WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Following).WithMany().HasForeignKey(x => x.FollowingId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
