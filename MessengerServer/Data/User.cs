using System.ComponentModel.DataAnnotations;

namespace MessengerServer.Data
{
    public class User
    {
        [Key]
        public Int32 Id { get; set; }
        public String Username { get; set; } = String.Empty;
        public String PasswordHash { get; set; } = String.Empty;
    }
}