using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MessengerServer.Data
{
    public class Chat
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ChatParticipant> Participants { get; set; } = new List<ChatParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }

    public class ChatParticipant
    {
        [Key]
        public int Id { get; set; }

        public int ChatId { get; set; }
        [ForeignKey("ChatId")]
        public Chat Chat { get; set; } = null!;

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;
    }
}