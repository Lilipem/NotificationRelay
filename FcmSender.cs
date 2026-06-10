using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace NotificationRelay;

class FcmSender
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotificationRelay");

    private static readonly string KeyPath = Path.Combine(DataDir, "serviceAccountKey.json");
    private static readonly string EnvPath = Path.Combine(DataDir, ".env");

    private const string PlaceholderToken = "YOUR_TOKEN_HERE";
    private const string FcmScope         = "https://www.googleapis.com/auth/firebase.messaging";

    private static readonly HttpClient Http = new();

    private readonly string       _projectId;
    private readonly ITokenAccess _tokenAccess;
    private readonly string       _deviceToken;

    private FcmSender(string projectId, ITokenAccess tokenAccess, string deviceToken)
    {
        _projectId   = projectId;
        _tokenAccess = tokenAccess;
        _deviceToken = deviceToken;
    }

    // Returns null (and logs why) if FCM cannot be set up.
    public static FcmSender? TryCreate()
    {
        if (!File.Exists(KeyPath))
        {
            Log($"serviceAccountKey.json not found at {KeyPath} — FCM disabled");
            return null;
        }

        string projectId;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(KeyPath));
            projectId = doc.RootElement.GetProperty("project_id").GetString()
                        ?? throw new Exception("project_id field is null");
        }
        catch (Exception ex)
        {
            Log($"Could not read project_id from serviceAccountKey.json: {ex.Message} — FCM disabled");
            return null;
        }

        ITokenAccess tokenAccess;
        try
        {
#pragma warning disable CS0618 // FromFile is deprecated but equivalent; new CredentialFactory API is more verbose for no gain here
            var cred = GoogleCredential.FromFile(KeyPath).CreateScoped(FcmScope);
#pragma warning restore CS0618
            tokenAccess = (ITokenAccess)cred.UnderlyingCredential;
        }
        catch (Exception ex)
        {
            Log($"Could not load GoogleCredential: {ex.Message} — FCM disabled");
            return null;
        }

        var deviceToken = ReadEnvValue("FCM_DEVICE_TOKEN") ?? PlaceholderToken;

        if (deviceToken == PlaceholderToken)
            Log("FCM_DEVICE_TOKEN not set in .env — sends are skipped until token is added.");
        else
            Log($"FCM ready (project: {projectId}).");

        return new FcmSender(projectId, tokenAccess, deviceToken);
    }

    public async Task SendAsync(NotifInfo notif)
    {
        if (_deviceToken == PlaceholderToken)
        {
            Log($"[skip] no device token — would relay: [{notif.App}] {notif.Title}");
            return;
        }

        try
        {
            var accessToken = await _tokenAccess.GetAccessTokenForRequestAsync();

            var body = notif.Body.Length > 0
                ? $"{notif.Title}\n{notif.Body}"
                : notif.Title;

            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    token        = _deviceToken,
                    // notification = new { title = notif.App, body }, < cant have this if i want to save to room in app
                    data         = new Dictionary<string, string>
                    {
                        ["app"]   = notif.App,
                        ["title"] = notif.Title,
                        ["body"]  = notif.Body,
                        ["id"]    = notif.Id.ToString(),
                        ["time"]  = notif.Time.ToString("o"),
                    }
                }
            });

            var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var resp     = await Http.SendAsync(req);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
                Log($"[sent] [{notif.App}] {notif.Title}");
            else
                Log($"[error] HTTP {(int)resp.StatusCode}: {respBody}");
        }
        catch (Exception ex)
        {
            Log($"[exception] {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Reads KEY=VALUE lines; ignores blank lines and # comments.
    private static string? ReadEnvValue(string key)
    {
        if (!File.Exists(EnvPath)) return null;

        foreach (var line in File.ReadAllLines(EnvPath))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;

            var eq = t.IndexOf('=');
            if (eq < 0) continue;

            if (t[..eq].Trim() == key)
            {
                var value = t[(eq + 1)..].Trim();
                return value.Length > 0 ? value : null;
            }
        }
        return null;
    }

    private static void Log(string msg)
    {
        var logPath = Path.Combine(DataDir, "NotificationRelay.log");
        var line    = $"[{DateTime.Now:HH:mm:ss.fff}] [FCM] {msg}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }
}
