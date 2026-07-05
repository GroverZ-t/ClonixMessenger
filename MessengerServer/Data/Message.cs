using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MessengerServer.Data
{
    public class Message
    {
        [Key]
        public int Id { get; set; }
        public string User { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;

        public int ChatId { get; set; }
        [ForeignKey("ChatId")]
        public Chat Chat { get; set; } = null!;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}