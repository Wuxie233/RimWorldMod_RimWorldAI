using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimWorldAgent
{
    /// <summary>基于 System.Net.WebSockets 的 BridgeBus WS 客户端，和 WebUI(index.html) 完全一致。</summary>
    public class BridgeClient : IDisposable
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private readonly string _url;
        private bool _isConnected;

        public bool IsConnected => _isConnected && _ws?.State == WebSocketState.Open;
        public event Action<string>? OnMessage;
        public event Action? OnConnectedChanged;

        public BridgeClient(string url = "ws://127.0.0.1:19999")
        {
            _url = url;
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected) return;
            try
            {
                _ws = new ClientWebSocket();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await _ws.ConnectAsync(new System.Uri(_url), _cts.Token);
                _isConnected = true;
                OnConnectedChanged?.Invoke();
                _ = ReceiveLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Verse.Log.Warning($"[BridgeClient] 连接失败: {ex.Message}");
            }
        }

        public async Task SendChat(string text)
        {
            await SendJson(new { type = "chat", text });
        }

        public async Task SendAbort()
        {
            await SendJson(new { type = "abort" });
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[8192];
            try
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    OnMessage?.Invoke(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception ex) { Verse.Log.Warning($"[BridgeClient] 接收异常: {ex.Message}"); }
            finally
            {
                _isConnected = false;
                OnConnectedChanged?.Invoke();
            }
        }

        private async Task SendJson(object obj)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var json = System.Text.Json.JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { _isConnected = false; }
            catch (Exception ex) { Verse.Log.Warning($"[BridgeClient] 发送失败: {ex.Message}"); }
        }

        public void Dispose()
        {
            _isConnected = false;
            _cts?.Cancel();
            try { _ws?.Dispose(); } catch { }
        }
    }
}
