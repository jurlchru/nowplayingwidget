using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WebSocketMediaServer
{

    class Program
    {
        static void Main(string[] args)
        {
            MediaWebSocketService.Run(args);
        }
    }

    public class MediaWebSocketService
    {
        private readonly HttpListener webSocketListener = new();
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly List<WebSocket> connectedClients = [];
        private const string wsServerUri = "http://localhost:3001/ws/";

        public static void Run(string[] args)
        {
            var service = new MediaWebSocketService();
            service.StartServer();
            Console.WriteLine("Service running... Press Enter to stop.");
            Console.ReadLine();
            service.StopServer();
        }

        private void StartServer()
        {
            try
            {
                webSocketListener.Prefixes.Add("http://localhost:3001/ws/");
                webSocketListener.Start();
                Task.Run(() => AcceptWebSocketClients(), cancellationTokenSource.Token);
                Console.WriteLine($"WebSocket server started on {wsServerUri}.");
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine("Failed to start WebSocket listener: " + ex.Message);
            }

            _ = BroadcastMediaProperties();
        }

        private void StopServer()
        {
            cancellationTokenSource.Cancel();
            if (webSocketListener.IsListening) webSocketListener.Stop();

            lock (connectedClients)
            {
                foreach (var ws in connectedClients)
                {
                    if (ws.State == WebSocketState.Open)
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", CancellationToken.None).Wait();
                }
                connectedClients.Clear();
            }
        }

        private async Task AcceptWebSocketClients()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await webSocketListener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        lock (connectedClients)
                        {
                            connectedClients.Add(wsContext.WebSocket);
                            Console.WriteLine($"New listener connected, total listeners: {connectedClients.Count}");
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("WebSocket accept error: " + ex.Message);
                }
            }
        }

        private static async Task<string> GetThumbnailBase64(IRandomAccessStreamReference thumbnailRef)
        {
            try
            {
                using var stream = await thumbnailRef.OpenReadAsync();
                using var reader = new DataReader(stream);
                var bytes = new byte[stream.Size];
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                return "";
            }
        }

        private async Task BroadcastMediaProperties()
        {
            Console.WriteLine("Starting broadcast...");

            var smtcManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                var session = smtcManager.GetCurrentSession();
                string json;
                if (session != null)
                {
                    string appId = session.SourceAppUserModelId.ToLower();

                    try
                    {
                        var mediaProperties = await session.TryGetMediaPropertiesAsync();
                        var timelineProperties = session.GetTimelineProperties();
                        var playbackInfo = session.GetPlaybackInfo();
                        string thumbnailBase64 = "";
                        if (mediaProperties.Thumbnail != null)
                        {
                            thumbnailBase64 = await GetThumbnailBase64(mediaProperties.Thumbnail);
                        }

                        var mediaInfo = new
                        {
                            AppId = appId,
                            mediaProperties.Title,
                            mediaProperties.Artist,
                            mediaProperties.AlbumTitle,
                            mediaProperties.AlbumArtist,
                            mediaProperties.Genres,
                            mediaProperties.Subtitle,
                            mediaProperties.TrackNumber,
                            Duration = timelineProperties.EndTime - timelineProperties.StartTime,
                            timelineProperties.Position,
                            playbackInfo.PlaybackStatus,
                            playbackInfo.PlaybackType,
                            playbackInfo.IsShuffleActive,
                            playbackInfo.AutoRepeatMode,
                            ThumbnailBase64 = thumbnailBase64
                        };

                        json = JsonSerializer.Serialize(mediaInfo);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Broadcast error: {ex}");
                        json = JsonSerializer.Serialize(new { Error = $"Broadcast error: {ex}" });
                    }

                }
                else
                {
                    json = JsonSerializer.Serialize(new { Message = "No media session detected." });
                }

                lock (connectedClients)
                {
                    Console.WriteLine($"Sending data to {connectedClients.Count} connected clients.");
                    for (int i = 0; connectedClients.Count > i; i++)
                    {
                        var clientNr = i + 1;
                        Console.WriteLine($"- {clientNr}: Sending...");
                        var ws = connectedClients[i];
                        if (ws.State == WebSocketState.Open)
                        {
                            try
                            {
                                var buffer = Encoding.UTF8.GetBytes(json);
                                ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                                Console.WriteLine($"- {clientNr}: Sent.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Client send failed: {ex.Message}. Removing client.");
                                connectedClients.RemoveAt(i);
                                try
                                {
                                    ws.Abort(); 
                                    ws.Dispose();
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            connectedClients.RemoveAt(i);
                            Console.WriteLine($"- {clientNr}: Disconnected.");
                        }
                    }
                }

                Console.WriteLine($"Data packet sent at {DateTime.Now}");
                await Task.Delay(1000);
            }

            Console.WriteLine("Ending broadcast...");
        }
    }
}
