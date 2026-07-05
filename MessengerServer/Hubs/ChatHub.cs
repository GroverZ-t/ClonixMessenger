using Microsoft.AspNetCore.SignalR;
using MessengerServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MessengerServer.Hubs
{
    public class ChatHub : Hub
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ChatHub> _logger;

        private static readonly Dictionary<int, string> OnlineUsers = new();
        private static readonly object Lock = new();

        public ChatHub(AppDbContext context, ILogger<ChatHub> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> Register(string username, string password)
        {
            if (await _context.Users.AnyAsync(u => u.Username == username)) return false;
            _context.Users.Add(new User { Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> Login(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return false;

            if (BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                Context.Items["UserId"] = user.Id;
                Context.Items["Username"] = username;

                // Добавляем в онлайн
                lock (Lock)
                {
                    OnlineUsers[user.Id] = Context.ConnectionId;
                }

                _logger.LogInformation($"Пользователь {username} (ID={user.Id}) вошёл и теперь онлайн");

                // Уведомляем контакты
                await NotifyContactsAboutStatus(user.Id, true);

                return true;
            }
            return false;
        }

        public async Task<Dictionary<string, bool>> GetOnlineStatuses()
        {
            var result = new Dictionary<string, bool>();

            if (!Context.Items.TryGetValue("UserId", out var userIdObj))
                return result;

            int currentUserId = Convert.ToInt32(userIdObj);

            // Получаем ID контактов
            var chatIds = await _context.ChatParticipants
                .Where(cp => cp.UserId == currentUserId)
                .Select(cp => cp.ChatId)
                .ToListAsync();

            var contactIds = await _context.ChatParticipants
                .Where(cp => chatIds.Contains(cp.ChatId) && cp.UserId != currentUserId)
                .Select(cp => cp.UserId)
                .Distinct()
                .ToListAsync();

            // Проверяем, кто из них онлайн
            foreach (var contactId in contactIds)
            {
                var contact = await _context.Users.FindAsync(contactId);
                if (contact != null)
                {
                    lock (Lock)
                    {
                        result[contact.Username] = OnlineUsers.ContainsKey(contactId);
                    }
                }
            }

            return result;
        }

        public override async Task OnConnectedAsync()
        {
            // Ничего не делаем здесь, статус установится после Login
            await base.OnConnectedAsync();
        }

        // Получить список всех чатов текущего пользователя
        public async Task<List<ChatInfo>> GetMyChats()
        {
            if (!Context.Items.TryGetValue("UserId", out var userIdObj))
            {
                _logger.LogError("UserId не найден в Context.Items");
                return new List<ChatInfo>();
            }

            int userId = Convert.ToInt32(userIdObj);
            _logger.LogInformation($"Загрузка чатов для пользователя {userId}");

            try
            {
                // Сначала получаем ID чатов, в которых состоит пользователь
                var chatIds = await _context.ChatParticipants
                    .Where(cp => cp.UserId == userId)
                    .Select(cp => cp.ChatId)
                    .ToListAsync();

                if (chatIds.Count == 0)
                {
                    _logger.LogInformation("Чатов не найдено");
                    return new List<ChatInfo>();
                }

                // Теперь загружаем чаты с сообщениями
                var chats = await _context.Chats
                    .Where(c => chatIds.Contains(c.Id))
                    .Include(c => c.Messages)
                    .ToListAsync();

                _logger.LogInformation($"Найдено {chats.Count} чатов");

                var result = new List<ChatInfo>();
                foreach (var chat in chats)
                {
                    var lastMsg = chat.Messages.OrderByDescending(m => m.Timestamp).FirstOrDefault();
                    result.Add(new ChatInfo
                    {
                        Id = chat.Id,
                        Name = chat.Name,
                        LastMessage = lastMsg?.Text ?? "Нет сообщений",
                        LastMessageTime = lastMsg?.Timestamp ?? chat.CreatedAt
                    });
                }

                return result.OrderByDescending(c => c.LastMessageTime).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка загрузки чатов: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        // Создать новый чат с другим пользователем
        // Создать новый чат с другим пользователем
        public async Task<int> CreateChat(string otherUsername)
        {
            if (!Context.Items.TryGetValue("UserId", out var userIdObj))
            {
                _logger.LogError("UserId не найден в Context.Items");
                return -1;
            }

            int currentUserId = Convert.ToInt32(userIdObj);
            _logger.LogInformation($"Пользователь {currentUserId} пытается создать чат с {otherUsername}");

            try
            {
                // Ищем другого пользователя
                var otherUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == otherUsername);
                if (otherUser == null)
                {
                    _logger.LogWarning($"Пользователь {otherUsername} не найден");
                    return -1;
                }

                // Проверяем, нет ли уже такого чата
                var existingParticipants = await _context.ChatParticipants
                    .Where(cp => cp.UserId == currentUserId || cp.UserId == otherUser.Id)
                    .Select(cp => new { cp.ChatId, cp.UserId })
                    .ToListAsync();

                var myChatIds = existingParticipants.Where(p => p.UserId == currentUserId).Select(p => p.ChatId).ToList();
                var otherUserChatIds = existingParticipants.Where(p => p.UserId == otherUser.Id).Select(p => p.ChatId).ToList();

                var commonChatId = myChatIds.Intersect(otherUserChatIds).FirstOrDefault();
                if (commonChatId != 0)
                {
                    _logger.LogInformation($"Чат уже существует: {commonChatId}");
                    return commonChatId;
                }

                // Создаем новый чат
                var currentUser = await _context.Users.FindAsync(currentUserId);
                var chat = new Chat
                {
                    Name = $"{currentUser.Username} и {otherUser.Username}"
                };

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Создан новый чат ID={chat.Id}");

                // Добавляем участников
                _context.ChatParticipants.Add(new ChatParticipant { ChatId = chat.Id, UserId = currentUserId });
                _context.ChatParticipants.Add(new ChatParticipant { ChatId = chat.Id, UserId = otherUser.Id });
                await _context.SaveChangesAsync();

                return chat.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка создания чата: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        // Вход в чат (подписка на группу SignalR)
        public async Task JoinChat(int chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        }

        // Загрузка истории чата
        // Загрузка истории чата с пагинацией
        public async Task<List<MessageDto>> LoadChatHistory(int chatId, int skip = 0, int take = 50)
        {
            var messages = await _context.Messages
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            messages.Reverse(); // Чтобы старые сообщения были сверху

            return messages.Select(m => new MessageDto
            {
                User = m.User,
                Text = m.Text,
                Timestamp = m.Timestamp
            }).ToList();
        }

        // Отправка сообщения в чат
        public async Task SendMessage(int chatId, string message)
        {
            if (!Context.Items.TryGetValue("Username", out var usernameObj)) return;
            string user = usernameObj.ToString();

            var newMessage = new Message
            {
                User = user,
                Text = message,
                ChatId = chatId,
                Timestamp = DateTime.UtcNow
            };
            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();

            // Отправляем всем участникам чата
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", user, message, newMessage.Timestamp);
        }

        public async Task UserTyping(int chatId)
        {
            if (!Context.Items.TryGetValue("Username", out var usernameObj)) return;
            string user = usernameObj.ToString();

            // Отправляем всем в чате, кроме отправителя, что он печатает
            await Clients.OthersInGroup($"chat_{chatId}").SendAsync("UserTyping", user);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // При отключении ищем пользователя по ConnectionId и убираем из онлайн
            int? userId = null;
            string username = null;

            lock (Lock)
            {
                var entry = OnlineUsers.FirstOrDefault(x => x.Value == Context.ConnectionId);
                if (entry.Key != 0)
                {
                    userId = entry.Key;
                    OnlineUsers.Remove(entry.Key);
                }
            }

            if (userId.HasValue)
            {
                var user = await _context.Users.FindAsync(userId.Value);
                if (user != null)
                {
                    username = user.Username;
                    await NotifyContactsAboutStatus(userId.Value, false);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Метод для уведомления контактов о смене статуса
        private async Task NotifyContactsAboutStatus(int userId, bool isOnline)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return;

            // Получаем все чаты, в которых состоит пользователь
            var chatIds = await _context.ChatParticipants
                .Where(cp => cp.UserId == userId)
                .Select(cp => cp.ChatId)
                .ToListAsync();

            // Получаем всех участников этих чатов (кроме самого пользователя)
            var contactIds = await _context.ChatParticipants
                .Where(cp => chatIds.Contains(cp.ChatId) && cp.UserId != userId)
                .Select(cp => cp.UserId)
                .Distinct()
                .ToListAsync();

            // Отправляем уведомления
            foreach (var contactId in contactIds)
            {
                if (OnlineUsers.TryGetValue(contactId, out var connectionId))
                {
                    await Clients.Client(connectionId).SendAsync("UserStatusChanged", user.Username, isOnline);
                }
            }
        }

        // Метод для проверки статуса пользователя
        public async Task<bool> IsUserOnline(string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return false;

            lock (Lock)
            {
                return OnlineUsers.ContainsKey(user.Id);
            }
        }
    }

    // DTO для передачи информации о чате
    public class ChatInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string LastMessage { get; set; }
        public DateTime LastMessageTime { get; set; }
    }

    public class MessageDto
    {
        public string User { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
    }
}