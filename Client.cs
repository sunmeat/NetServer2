using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static readonly HttpClient Http = new();
    static string ServerUrl = "https://p45-bvmh.onrender.com";

    static string ClientId = "";
    static int NextFrom = 0;

    static async Task Main()
    {

        await Connect();

        Console.WriteLine("Введіть повідомлення та натисніть Enter. Ctrl+C для виходу.\n");

        // Запускаємо polling у фоні
        using var cts = new CancellationTokenSource();
        var pollTask = Task.Run(() => PollLoop(cts.Token));

        // Головний потік: читаємо введення та надсилаємо повідомлення
        Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            await Disconnect();
            Environment.Exit(0);
        };

        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            await SendMessage(line);
        }

        cts.Cancel();
        await Disconnect();
    }

    // POST /connect
    static async Task Connect()
    {
        while (true)
        {
            try
            {
                var body = new { clientId = Guid.NewGuid().ToString("N")[..8] };
                var response = await PostJson("/connect", body);

                ClientId = response.GetProperty("clientId").GetString()!;
                NextFrom = response.GetProperty("fromIndex").GetInt32();

                Console.WriteLine($"Підключено! Ваш ID: {ClientId}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не вдалося підключитися: {ex.Message}");
                Console.Write("Спробувати ще раз? (Enter = так, будь-що = ні): ");
                var ans = Console.ReadLine();
                if (ans != "") Environment.Exit(1);
            }
        }
    }

    // POST /disconnect
    static async Task Disconnect()
    {
        try
        {
            await PostJson("/disconnect", new { clientId = ClientId });
            Console.WriteLine("Відключено від сервера.");
        }
        catch { /* ігноруємо помилки при виході */ }
    }

    // POST /send
    static async Task SendMessage(string text)
    {
        try
        {
            await PostJson("/send", new { clientId = ClientId, text });
        }
        catch (Exception ex)
        {
            PrintSystem($"Помилка відправки: {ex.Message}");
        }
    }

    // GET /poll  — виконується кожні 1 секунду
    static async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{ServerUrl}/poll?clientId={ClientId}&from={NextFrom}";
                var json = await Http.GetStringAsync(url, ct);
                var doc = JsonDocument.Parse(json).RootElement;

                NextFrom = doc.GetProperty("nextFrom").GetInt32();

                foreach (var msg in doc.GetProperty("messages").EnumerateArray())
                {
                    string from = msg.GetProperty("from").GetString()!;
                    string text = msg.GetProperty("text").GetString()!;
                    bool isSystem = msg.GetProperty("isSystem").GetBoolean();

                    if (isSystem)
                        PrintSystem(text);
                    else
                        PrintMessage(from, text);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                PrintSystem($"Polling помилка: {ex.Message}");
            }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // --- Вивід у консоль ---

    static void PrintMessage(string from, string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{from}]: ");
        Console.ForegroundColor = prev;
        Console.WriteLine(text);
    }

    static void PrintSystem(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"*** {text}");
        Console.ForegroundColor = prev;
    }

    // --- HTTP хелпери ---

    static async Task<JsonElement> PostJson(string path, object body)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(ServerUrl + path, content);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }
}