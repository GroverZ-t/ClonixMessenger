using MessengerServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using MessengerServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer
{
    public class Program
    {
        public static void Main(String[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                // Если у тебя LocalDB (стандарт для VS)
                options.UseSqlServer("Server=DESKTOP-3E4PLS4\\SQLEXPRESS;Database=ClonixMessengerDBUpdate-Database;Trusted_Connection=True;TrustServerCertificate=True;");
            });

            // 1. Добавляем SignalR
            builder.Services.AddSignalR();

            // 2. Настраиваем CORS (разрешаем подключения отовсюду, включая MAUI и CloudPub)
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowAnyOrigin() // В реальном продакшене лучше указывать конкретные домены
                          .WithOrigins("*");
                });
            });

            var app = builder.Build();

            // 3. Применяем CORS
            app.UseCors();

            // 4. Добавляем маршрутизацию для SignalR хаба
            app.MapHub<ChatHub>("/chatHub");

            // Простой эндпоинт для проверки, что сервер жив
            app.MapGet("/", () => "Сервер мессенджера работает!");

            app.Run();
        }
    }
}