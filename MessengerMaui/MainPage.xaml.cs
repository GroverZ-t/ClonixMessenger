using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MessengerMaui;

public partial class MainPage : ContentPage
{
    private HubConnection _connection;
    private string _myUsername = string.Empty;
    private int _currentChatId;

    public ObservableCollection<ChatModel> Chats { get; } = new();
    public ObservableCollection<MessageModel> Messages { get; } = new();
    private Dictionary<string, bool> UserStatuses { get; } = new();

    private int _loadedMessagesCount = 0;
    private bool _isLoadingMore = false;
    private const int MessagesPerPage = 50;

    public MainPage()
    {
        InitializeComponent();
        ChatsList.ItemsSource = Chats;
        MessagesList.ItemsSource = Messages;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ConnectToServer();
    }

    private async Task ConnectToServer()
    {
        try
        {
            var url = "https://clonixserver.cloudpub.ru/chatHub";

            _connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string, string, DateTime>("ReceiveMessage", (user, message, timestamp) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Add(new MessageModel
                    {
                        User = user,
                        Text = message,
                        Timestamp = timestamp,
                        IsMine = user == _myUsername
                    });
                });
            });

            // Обработка индикатора "печатает..."
            Dictionary<string, CancellationTokenSource> typingTimers = new();

            _connection.On<string>("UserTyping", (user) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Если печатает не я — игнорируем
                    if (user == _myUsername) return;

                    // Показываем индикатор
                    TypingStatusLabel.Text = $"{user} печатает...";
                    TypingStatusLabel.IsVisible = true;

                    // Если уже есть таймер для этого пользователя — отменяем его
                    if (typingTimers.TryGetValue(user, out var existingTimer))
                    {
                        existingTimer.Cancel();
                        typingTimers.Remove(user);
                    }

                    // Создаем новый таймер на 2 секунды
                    var cts = new CancellationTokenSource();
                    typingTimers[user] = cts;

                    Task.Delay(2000, cts.Token).ContinueWith(_ =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            // Скрываем индикатор только если это всё ещё тот же пользователь
                            if (TypingStatusLabel.Text == $"{user} печатает...")
                            {
                                TypingStatusLabel.IsVisible = false;
                            }
                            typingTimers.Remove(user);
                        });
                    });
                });
            });

            _connection.On<string, bool>("UserStatusChanged", (username, isOnline) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UserStatuses[username] = isOnline;

                    // Обновляем список чатов
                    foreach (var chat in Chats)
                    {
                        var otherUser = chat.Name.Replace($"{_myUsername} и ", "").Replace($" и {_myUsername}", "");
                        if (otherUser == username)
                        {
                            chat.IsOnline = isOnline;
                            break;
                        }
                    }

                    // Если мы в чате с этим пользователем, обновляем статус в шапке
                    if (ChatNameLabel.Text?.Contains(username) == true)
                    {
                        OnlineStatusLabel.Text = isOnline ? "онлайн" : "оффлайн";
                        OnlineStatusLabel.TextColor = isOnline ? Colors.Green : Colors.Gray;
                    }
                });
            });

            await _connection.StartAsync();
            Debug.WriteLine("Подключено к серверу!");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось подключиться к серверу.\n{ex.Message}", "OK");
        }
    }

    private DateTime _lastTypingNotification = DateTime.MinValue;

    private async void OnMessageTextChanged(object sender, TextChangedEventArgs e)
    {
        // Отправляем уведомление о наборе текста не чаще раза в секунду
        if (!string.IsNullOrWhiteSpace(e.NewTextValue) && _currentChatId > 0)
        {
            var now = DateTime.Now;
            if ((now - _lastTypingNotification).TotalSeconds >= 1)
            {
                try
                {
                    await _connection.InvokeAsync("UserTyping", _currentChatId);
                    _lastTypingNotification = now;
                }
                catch
                {
                    // Игнорируем ошибки
                }
            }
        }
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var username = AuthUsernameEntry.Text?.Trim();
        var password = AuthPasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AuthStatusLabel.Text = "Заполните все поля";
            return;
        }

        try
        {
            bool success = await _connection.InvokeAsync<bool>("Login", username, password);
            if (success)
            {
                _myUsername = username;
                ShowChatsList();
                await LoadChats();
                await LoadOnlineStatuses();
            }
            else
            {
                AuthStatusLabel.Text = "Неверное имя пользователя или пароль";
            }
        }
        catch (Exception ex)
        {
            AuthStatusLabel.Text = $"Ошибка: {ex.Message}";
        }
    }

    private async Task LoadOnlineStatuses()
    {
        try
        {
            var statuses = await _connection.InvokeAsync<Dictionary<string, bool>>("GetOnlineStatuses");
            UserStatuses.Clear();
            foreach (var kvp in statuses)
            {
                UserStatuses[kvp.Key] = kvp.Value;
            }

            // Обновляем список чатов
            foreach (var chat in Chats)
            {
                // Извлекаем имя собеседника из названия чата
                var otherUser = chat.Name.Replace($"{_myUsername} и ", "").Replace($" и {_myUsername}", "");
                chat.IsOnline = UserStatuses.ContainsKey(otherUser) && UserStatuses[otherUser];
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка загрузки статусов: {ex.Message}");
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var username = AuthUsernameEntry.Text?.Trim();
        var password = AuthPasswordEntry.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            AuthStatusLabel.Text = "Заполните все поля";
            return;
        }

        if (password.Length < 4)
        {
            AuthStatusLabel.Text = "Пароль должен быть минимум 4 символа";
            return;
        }

        try
        {
            bool success = await _connection.InvokeAsync<bool>("Register", username, password);
            if (success)
            {
                AuthStatusLabel.TextColor = Colors.Green;
                AuthStatusLabel.Text = "Регистрация успешна! Теперь войдите.";
            }
            else
            {
                AuthStatusLabel.TextColor = Colors.Red;
                AuthStatusLabel.Text = "Пользователь с таким именем уже существует";
            }
        }
        catch (Exception ex)
        {
            AuthStatusLabel.Text = $"Ошибка: {ex.Message}";
        }
    }

    private void ShowChatsList()
    {
        AuthLayout.IsVisible = false;
        ChatsListLayout.IsVisible = true;
        ChatViewLayout.IsVisible = false;
    }

    private async Task LoadChats()
    {
        try
        {
            var chats = await _connection.InvokeAsync<List<ChatInfoDto>>("GetMyChats");
            Chats.Clear();
            foreach (var chat in chats)
            {
                Chats.Add(new ChatModel
                {
                    Id = chat.Id,
                    Name = chat.Name,
                    LastMessage = chat.LastMessage,
                    LastMessageTime = chat.LastMessageTime
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось загрузить чаты: {ex.Message}", "OK");
        }
    }

    private async void OnCreateChatClicked(object sender, EventArgs e)
    {
        var otherUsername = await DisplayPromptAsync("Новый чат", "Введите имя пользователя:");
        if (!string.IsNullOrWhiteSpace(otherUsername))
        {
            try
            {
                int chatId = await _connection.InvokeAsync<int>("CreateChat", otherUsername);
                if (chatId > 0)
                {
                    await LoadChatsAndStatuses();
                    await DisplayAlert("Успех", $"Чат с {otherUsername} создан!", "OK");
                }
                else
                {
                    await DisplayAlert("Ошибка", "Пользователь не найден", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", ex.Message, "OK");
            }
        }
    }

    private async void OnChatSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ChatModel selectedChat)
        {
            _currentChatId = selectedChat.Id;
            ChatNameLabel.Text = selectedChat.Name;

            await _connection.InvokeAsync("JoinChat", _currentChatId);

            Messages.Clear();
            _loadedMessagesCount = 0;
            await LoadMoreMessages();

            AuthLayout.IsVisible = false;
            ChatsListLayout.IsVisible = false;
            ChatViewLayout.IsVisible = true;

            // Сбрасываем выбор
            ((CollectionView)sender).SelectedItem = null;
        }
    }

    private async Task LoadMoreMessages()
    {
        if (_isLoadingMore) return;

        _isLoadingMore = true;

        try
        {
            var messages = await _connection.InvokeAsync<List<MessageDto>>("LoadChatHistory", _currentChatId, _loadedMessagesCount, MessagesPerPage);

            if (messages.Count > 0)
            {
                // Вставляем старые сообщения в начало списка
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    var msg = messages[i];
                    Messages.Insert(0, new MessageModel
                    {
                        User = msg.User,
                        Text = msg.Text,
                        Timestamp = msg.Timestamp,
                        IsMine = msg.User == _myUsername
                    });
                }

                _loadedMessagesCount += messages.Count;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка загрузки истории: {ex.Message}");
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    private void OnBackClicked(object sender, EventArgs e)
    {
        ShowChatsList();
        _ = LoadChatsAndStatuses(); // Обновляем список чатов и статусы
    }

    private void OnMessagesScrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        // Если прокрутили до верха (FirstVisibleItemIndex == 0) и есть ещё сообщения
        if (e.FirstVisibleItemIndex == 0 && !_isLoadingMore && _loadedMessagesCount > 0)
        {
            _ = LoadMoreMessages();
        }
    }

    private async Task LoadChatsAndStatuses()
    {
        await LoadChats();
        await LoadOnlineStatuses();
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        var text = MessageEntry.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                await _connection.InvokeAsync("SendMessage", _currentChatId, text);
                MessageEntry.Text = string.Empty;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", ex.Message, "OK");
            }
        }
    }

    private void MessagesList_Scrolled(Object sender, ItemsViewScrolledEventArgs e)
    {

    }
}

public class ChatModel : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isOnline;

    public int Id { get; set; }
    public string Name { get; set; }
    public string LastMessage { get; set; }
    public DateTime LastMessageTime { get; set; }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOnline)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
}

public class MessageModel
{
    public string User { get; set; }
    public string Text { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsMine { get; set; }

    public string Initial => string.IsNullOrEmpty(User) ? "?" : User[0].ToString().ToUpper();

    public Color BubbleColor => IsMine ? Color.FromArgb("#DCF8C6") : Colors.White;
    public Color NameColor => IsMine ? Colors.Gray : Color.FromArgb("#007AFF");
    public LayoutOptions Align => IsMine ? LayoutOptions.End : LayoutOptions.Start;

    // Генерируем цвет аватарки на основе имени
    public Color AvatarColor
    {
        get
        {
            if (string.IsNullOrEmpty(User)) return Colors.Gray;

            // Простой хеш для генерации цвета
            int hash = User.GetHashCode();
            Random rand = new Random(Math.Abs(hash));
            return Color.FromRgb(rand.Next(256), rand.Next(256), rand.Next(256));
        }
    }
}

// DTO для получения списка чатов с сервера
public class ChatInfoDto
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