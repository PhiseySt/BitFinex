using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Messages;
using Bitfinex.Client.Websocket.Requests;
using Bitfinex.Client.Websocket.Requests.Subscriptions;
using Bitfinex.Client.Websocket.Responses;
using Bitfinex.Client.Websocket.Responses.Books;
using Bitfinex.Client.Websocket.Responses.Candles;
using Bitfinex.Client.Websocket.Responses.Configurations;
using Bitfinex.Client.Websocket.Responses.Fundings;
using Bitfinex.Client.Websocket.Responses.Tickers;
using Bitfinex.Client.Websocket.Responses.Trades;
using Bitfinex.Client.Websocket.Responses.Wallets;
using Bitfinex.Client.Websocket.Utils;
using Bitfinex.Client.Websocket.Websockets;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace BitFinex
{
    class Program
    {
        private static readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);
        static void Main(string[] args)
        {
            InitLogging();

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
            AssemblyLoadContext.Default.Unloading += DefaultOnUnloading;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            Console.WriteLine("|===================|");
            Console.WriteLine("|  BITFINEX CLIENT  |");
            Console.WriteLine("|===================|");
            Console.WriteLine();

            var apiWebsocketUrl = BitfinexValues.ApiWebsocketUrl;

            using var communicator = new BitfinexWebsocketCommunicator(apiWebsocketUrl)
            {
                Name = "Bitfinex",
                ReconnectTimeout = TimeSpan.FromSeconds(90)
            };

            communicator.ReconnectionHappened.Subscribe(info => Log.Information($"Случился реконнект, тип реконнекта: {info.Type}"));

            using var client = new BitfinexWebsocketClient(communicator);

            client.Streams.InfoStream.Subscribe(info =>
            {
                Log.Information($"Полученные данные: {info.Version}, случился реконнект, переподписка на потоки");
                SendSubscriptionRequests(client);
            });

            SubscribeToStreams(client);

            communicator.Start();

            ExitEvent.WaitOne();
        }

        private static void InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
            if (executingDir != null)
            {
                var logPath = Path.Combine(executingDir, "logs", "verbose.log");
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                    .WriteTo.Console(LogEventLevel.Debug,
                        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();
            }
        }

        private static void SendSubscriptionRequests(BitfinexWebsocketClient client)
        {
            client.Send(new PingRequest() { Cid = 123456 });

            client.Send(new TickerSubscribeRequest("BTC/USD"));
            client.Send(new TickerSubscribeRequest("ETH/USD"));

            client.Send(new TradesSubscribeRequest("BTC/USD"));
            client.Send(new TradesSubscribeRequest("NEC/ETH"));
            client.Send(new FundingsSubscribeRequest("BTC"));
            client.Send(new FundingsSubscribeRequest("USD"));

            client.Send(new CandlesSubscribeRequest("BTC/USD", BitfinexTimeFrame.OneMinute));
            client.Send(new CandlesSubscribeRequest("ETH/USD", BitfinexTimeFrame.OneMinute));

            client.Send(new BookSubscribeRequest("BTC/USD"));
            client.Send(new BookSubscribeRequest("BTC/USD", BitfinexPrecision.P3));
            client.Send(new BookSubscribeRequest("ETH/USD"));

            client.Send(new BookSubscribeRequest("fUSD"));

            client.Send(new RawBookSubscribeRequest("BTCUSD", "100"));
            client.Send(new RawBookSubscribeRequest("fUSD", "25"));
            client.Send(new RawBookSubscribeRequest("fBTC", "25"));

            client.Send(new StatusSubscribeRequest("liq:global"));
            client.Send(new StatusSubscribeRequest("deriv:tBTCF0:USTF0"));
        }

        private static void SubscribeToStreams(BitfinexWebsocketClient client)
        {

            client.Streams.ConfigurationStream.Subscribe(OnNext);

            client.Streams.PongStream.Subscribe(OnNext);

            client.Streams.TickerStream.Subscribe(OnNext);

            client.Streams.TradesSnapshotStream.Subscribe(OnNext);

            client.Streams.FundingStream.Subscribe(OnNext);

            client.Streams.RawBookStream.Subscribe(OnNext);

            client.Streams.CandlesStream.Subscribe(OnNext);

            client.Streams.BookChecksumStream.Subscribe(OnNext);

            client.Streams.WalletStream.Subscribe(OnNext);
        }

        #region OnNext

        private static void OnNext(Wallet obj)
        {
            var jsonInfo = JsonConvert.SerializeObject(obj);
            Log.Information($"WalletResponse получен {jsonInfo}");
        }

        private static void OnNext(Trade[] obj)
        {
            var jsonInfo = JsonConvert.SerializeObject(obj);
            Log.Information($"TradeResponse получен {jsonInfo}");
        }

        private static void OnNext(MessageBase obj)
        {
            if (obj is ConfigurationResponse configurationResponse)
            {
                var jsonInfo = JsonConvert.SerializeObject(configurationResponse);
                Log.Information($"ConfigurationResponse получен {jsonInfo}");
            }
            else if (obj is PongResponse pongResponse)
            {
                var jsonInfo = JsonConvert.SerializeObject(pongResponse);
                Log.Information($"PongResponse получен {jsonInfo}");
            }
        }

        private static void OnNext(ResponseBase obj)
        {
            if (obj is Ticker ticker)
            {
                var jsonInfo = JsonConvert.SerializeObject(ticker);
                Log.Information($"TickerResponse получен {jsonInfo}");
            }
            else if (obj is Funding funding)
            {
                var jsonInfo = JsonConvert.SerializeObject(funding);
                Log.Information($"FundingResponse получен {jsonInfo}");
            }
            else if (obj is RawBook rawBook)
            {
                var jsonInfo = JsonConvert.SerializeObject(rawBook);
                Log.Information($"RawBookResponse получен {jsonInfo}");
            }
            else if (obj is Candles candles)
            {
                var jsonInfo = JsonConvert.SerializeObject(candles);
                Log.Information($"CandlesResponse получен {jsonInfo}");
            }
            else if (obj is ChecksumResponse checksumResponse)
            {
                var jsonInfo = JsonConvert.SerializeObject(checksumResponse);
                Log.Information($"ChecksumResponse получен {jsonInfo}");
            }
        }

        #endregion

        private static void CurrentDomainOnProcessExit(object sender, EventArgs eventArgs)
        {
            Log.Warning("Exiting process");
            ExitEvent.Set();
        }

        private static void DefaultOnUnloading(AssemblyLoadContext assemblyLoadContext)
        {
            Log.Warning("Unloading process");
            ExitEvent.Set();
        }

        private static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Log.Warning("Canceling process");
            e.Cancel = true;
            ExitEvent.Set();
        }
    }
}
