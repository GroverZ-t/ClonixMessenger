using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Message> Messages { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Chat> Chats { get; set; }
        public DbSet<ChatParticipant> ChatParticipants { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Настраиваем связь Chat-ChatParticipant (один ко многим)
            modelBuilder.Entity<ChatParticipant>()
                .HasOne(cp => cp.Chat)
                .WithMany(c => c.Participants)
                .HasForeignKey(cp => cp.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatParticipant>()
                .HasOne(cp => cp.User)
                .WithMany()
                .HasForeignKey(cp => cp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Настраиваем связь Chat-Message (один ко многим)
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
}