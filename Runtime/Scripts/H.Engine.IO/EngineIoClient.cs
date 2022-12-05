﻿using System.Net;
using System.Timers;
using H.WebSockets;
using H.WebSockets.Args;
using H.WebSockets.Utilities;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace H.Engine.IO
{
    /// <summary>
    /// Engine.IO Client
    /// </summary>
    public sealed partial class EngineIoClient : IDisposable
#if NETSTANDARD2_1
        , IAsyncDisposable
#endif
    {
        #region Constants

        private const string PingMessage = "ping";

        #endregion

        #region Properties

        /// <summary>
        /// Internal WebSocket Client
        /// </summary>
        public WebSocketClient WebSocketClient { get; private set; }

        /// <summary>
        /// Proxy
        /// </summary>
        public IWebProxy? Proxy
        {
            get => WebSocketClient.Proxy;
            set
            {
                WebSocketClient = WebSocketClient ?? throw new ObjectDisposedException(nameof(WebSocketClient));
                WebSocketClient.Proxy = value;
            }
        }

        /// <summary>
        /// This property contains OpenMessage after successful <seealso cref="OpenAsync(Uri, CancellationToken)"/>
        /// </summary>
        public EngineIoOpenMessage? OpenMessage { get; private set; }

        /// <summary>
        /// An unique identifier for the socket session. <br/>
        /// Set after the <seealso cref="Opened"/> event is triggered.
        /// </summary>
        public string? Id => OpenMessage?.Sid;

        /// <summary>
        /// This property is true after successful <seealso cref="OpenAsync(Uri, CancellationToken)"/>
        /// </summary>
        public bool IsOpened { get; set; }

        /// <summary>
        /// Opened uri.
        /// </summary>
        public Uri? Uri { get; set; }

        private string Framework { get; }
        private System.Timers.Timer Timer { get; set; }

        #endregion

        #region Events

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<EngineIoOpenMessage?>>? Opened;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<WebSocketCloseEventArgs>? Closed;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? PingSent;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? PingReceived;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? PongReceived;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? MessageReceived;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? Upgraded;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<string>>? NoopReceived;

        /// <summary>
        /// 
        /// </summary>
        public event EventHandler<DataEventArgs<Exception>>? ExceptionOccurred;

        private void OnOpened(DataEventArgs<EngineIoOpenMessage>? value)
        {
            Opened?.Invoke(this, value);
        }

        private void OnClosed(WebSocketCloseEventArgs? evt)
        {
            IsOpened = false;
            Closed?.Invoke(this, evt);
        }

        private void OnPingSent(DataEventArgs<string> value)
        {
            PingSent?.Invoke(this, value);
        }

        private void OnPingReceived(DataEventArgs<string> value)
        {
            PingReceived?.Invoke(this, value);
        }

        private void OnPongReceived(DataEventArgs<string> value)
        {
            PongReceived?.Invoke(this, value);
        }

        private void OnMessageReceived(DataEventArgs<string> value)
        {
            MessageReceived?.Invoke(this, value);
        }

        private void OnUpgraded(DataEventArgs<string> value)
        {
            Upgraded?.Invoke(this, value);
        }

        private void OnNoopReceived(DataEventArgs<string> value)
        {
            NoopReceived?.Invoke(this, value);
        }

        private void OnExceptionOccurred(DataEventArgs<Exception> value)
        {
            ExceptionOccurred?.Invoke(this, value);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// 
        /// </summary>
        /// <param name="framework"></param>
        public EngineIoClient(string framework = "engine.io")
        {
            Framework = framework ?? throw new ArgumentNullException(nameof(framework));

            Timer = new System.Timers.Timer(25000);
            Timer.Elapsed += Timer_Elapsed;

            WebSocketClient = new WebSocketClient();
            WebSocketClient.TextReceived += WebSocketClient_OnTextReceived;
            WebSocketClient.ExceptionOccurred += (_, args) => OnExceptionOccurred(new DataEventArgs<Exception>(args.Value));
            WebSocketClient.Disconnected += (_, args) =>
            {
                IsOpened = false;
                OnClosed(args);
            };
        }

        #endregion

        #region Event Handlers

        private async void Timer_Elapsed(object sender, ElapsedEventArgs args)
        {
            WebSocketClient = WebSocketClient ?? throw new ObjectDisposedException(nameof(WebSocketClient));

            try
            {
                // Reconnect if required
                await WebSocketClient.ConnectAsync(WebSocketClient.LastConnectUri).ConfigureAwait(false);

                await WebSocketClient.SendTextAsync(new EngineIoPacket(EngineIoPacket.PingPrefix, PingMessage).Encode()).ConfigureAwait(false);

                OnPingSent(new DataEventArgs<string>(PingMessage));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                OnExceptionOccurred(new DataEventArgs<Exception>(exception));
            }
        }

        private void WebSocketClient_OnTextReceived(object? sender, DataEventArgs<string>? args)
        {
            try
            {
                var text = args?.Value ?? throw new InvalidOperationException("Null Engine.IO string");
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("Empty Engine.IO string");
                }

                var packet = EngineIoPacket.Decode(text);
                switch (packet.Prefix)
                {
                    case EngineIoPacket.OpenPrefix:
                        OpenMessage = JsonConvert.DeserializeObject<EngineIoOpenMessage>(packet.Value);
                        IsOpened = true;

                        Timer.Interval = OpenMessage?.PingInterval ?? 25000;
                        Timer.Start();

                        OnOpened(new DataEventArgs<EngineIoOpenMessage>(OpenMessage ?? new EngineIoOpenMessage()));
                        break;

                    case EngineIoPacket.ClosePrefix:
                        IsOpened = false;
                        OnClosed(new WebSocketCloseEventArgs(
                            reason: "Received close message from server",
                            status: null));
                        break;

                    case EngineIoPacket.PingPrefix:
                        OnPingReceived(new DataEventArgs<string>(packet.Value));
                        break;

                    case EngineIoPacket.PongPrefix:
                        OnPongReceived(new DataEventArgs<string>(packet.Value));
                        break;

                    case EngineIoPacket.MessagePrefix:
                        OnMessageReceived(new DataEventArgs<string>(packet.Value));
                        break;

                    case EngineIoPacket.UpgradePrefix:
                        OnUpgraded(new DataEventArgs<string>(packet.Value));
                        break;

                    case EngineIoPacket.NoopPrefix:
                        OnNoopReceived(new DataEventArgs<string>(packet.Value));
                        break;
                }
            }
            catch (Exception exception)
            {
                OnExceptionOccurred(new DataEventArgs<Exception>(exception));
            }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public async Task<EngineIoOpenMessage?> OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            WebSocketClient = WebSocketClient ?? throw new ObjectDisposedException(nameof(WebSocketClient));

            if (WebSocketClient.IsConnected)
            {
                return OpenMessage;
            }

            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            var socketIoUri = ToWebSocketUri(uri, Framework);

            var args = await this.WaitEventAsync<DataEventArgs<EngineIoOpenMessage?>>(async () =>
            {
                await WebSocketClient.ConnectAsync(socketIoUri, cancellationToken).ConfigureAwait(false);
            }, nameof(Opened), cancellationToken).ConfigureAwait(false);

            return args.Value;
        }

        internal static Uri ToWebSocketUri(Uri uri, string framework)
        {
            uri = uri ?? throw new ArgumentNullException(nameof(uri));
            framework = framework ?? throw new ArgumentNullException(nameof(framework));

            var scheme = uri.Scheme switch
            {
                "http" => "ws",
                "https" => "wss",
                "ws" => "ws",
                "wss" => "wss",
                _ => throw new ArgumentException($"Scheme is not supported: {uri.Scheme}"),
            };

            return new Uri($"{scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}/{framework}/?EIO=3&transport=websocket&{uri.Query.TrimStart('?')}");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<EngineIoOpenMessage?> OpenAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationSource.CancelAfter(timeout);

            return await OpenAsync(uri, cancellationSource.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            WebSocketClient = WebSocketClient ?? throw new ObjectDisposedException(nameof(WebSocketClient));

            Timer.Stop();

            await WebSocketClient.SendTextAsync(new EngineIoPacket(EngineIoPacket.ClosePrefix).Encode(), cancellationToken).ConfigureAwait(false);

            await WebSocketClient.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends new data with message prefix
        /// </summary>
        /// <param name="message"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async System.Threading.Tasks.Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            WebSocketClient = WebSocketClient ?? throw new ObjectDisposedException(nameof(WebSocketClient));

            await WebSocketClient.SendTextAsync(new EngineIoPacket(EngineIoPacket.MessagePrefix, message).Encode(), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose internal resources
        /// Prefer DisposeAsync if possible
        /// </summary>
        /// <returns></returns>
        public void Dispose()
        {
            WebSocketClient.Dispose();

            Timer.Dispose();
        }

#if NETSTANDARD2_1
    /// <summary>
    /// Dispose internal resources
    /// </summary>
    /// <returns></returns>
    public async ValueTask DisposeAsync()
    {
        await WebSocketClient.DisposeAsync().ConfigureAwait(false);

        Timer.Dispose();
    }
#endif

        #endregion
    }

}
