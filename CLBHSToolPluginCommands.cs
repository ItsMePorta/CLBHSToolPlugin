using System;
using Serilog;
using Qmmands;
using System.Threading.Tasks;
using JetBrains.Annotations;
using System.Linq;
using System.Reflection;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using AssettoServer.Server;
using AssettoServer.Server.Weather;
using AssettoServer.Commands.Attributes;
using AssettoServer.Network.Tcp;
using AssettoServer.Commands.Modules;
using AssettoServer.Server.Plugin;
using AssettoServer.Commands;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;

namespace CLBHSToolPlugin
{
    public class CLBHSToolPlugin : IAssettoServerAutostart
    {
        private LogTcpServer _logServer;

        public CLBHSToolPlugin(CLBHSToolPluginConfiguration config)
        {

            Log.Information("------------------------------------");
            Log.Information("CLBHSToolPlugin v.0.15");
            Log.Information("CLBHSToolPlugin is a plugin for den Clubhouse Admin Tool.");
            Log.Information("Check Patreon or Discord for updates or support.");
            Log.Information("------------------------------------");

            _logServer = new LogTcpServer(config.LogTcpPort, "logs");
            _logServer.Start();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logServer?.Stop();
            return Task.CompletedTask;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.PostConfigure<CommandService>(commandService =>
            {
                commandService.AddModules(Assembly.GetExecutingAssembly());
            });
        }
    }

    namespace AssettoServer.Commands.Modules
    {
        [RequireAdmin]
        [JetBrains.Annotations.UsedImplicitly(JetBrains.Annotations.ImplicitUseKindFlags.Access, JetBrains.Annotations.ImplicitUseTargetFlags.WithMembers)]
        public class CLBHSAdminCommands : ACModuleBase
        {
            private readonly SessionManager _sessionManager;
            private readonly EntryCarManager _entryCarManager;
            private readonly WeatherManager _weatherManager;

            public CLBHSAdminCommands(SessionManager sessionManager, EntryCarManager entryCarManager, WeatherManager weatherManager)
            {
                _sessionManager = sessionManager;
                _entryCarManager = entryCarManager;
                _weatherManager = weatherManager;
            }
            //Just saying hello for debug stuff. Leaving it because might become handy later.                                                                
            [Command("hello")]
            public void Hello()
            {
                Reply("Hello, Admin!");
            }

            [Command("listplayers")]
            public void ListPlayers()
            {
                var players = _entryCarManager.EntryCars.Where(c => c.Client != null).ToList();

                if (players.Count == 0)
                {
                    Reply("No players connected.");
                    return;
                }

                Reply("Connected Players:");
                foreach (var car in players)
                {
                    var player = car.Client;
                    if (player == null) continue;
                    Reply($"Name: {player.Name}, ID: {player.SessionId}, GUID: {player.Guid}, Car: {car.Model}");
                }
            }

            [Command("changetime")]
            public void ChangeTime(string time)
            {
                if (DateTime.TryParseExact(time, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                {
                    var method = _weatherManager.GetType().GetMethod("SetTime");
                    if (method != null)
                    {
                        method.Invoke(_weatherManager, new object[] { (int)dateTime.TimeOfDay.TotalSeconds });
                    }
                }
            }


            [Command("showlog")]
            public void ShowLog(int start = 0, int count = 20)
            {
                string logDir = "logs";
                if (!System.IO.Directory.Exists(logDir))
                {
                    Reply("Log directory not found.");
                    return;
                }

                var dirInfo = new System.IO.DirectoryInfo(logDir);
                var newestLog = dirInfo.GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (newestLog == null)
                {
                    Log.Error("LogTcpServer: No log files found in " + logDir);
                    return;
                }

                try
                {
                    string[] allLines;
                    using (var fs = new System.IO.FileStream(newestLog.FullName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var sr = new System.IO.StreamReader(fs))
                    {
                        var linesList = new System.Collections.Generic.List<string>();
                        string line;
                        while ((line = sr.ReadLine()) != null)
                            linesList.Add(line);
                        allLines = linesList.ToArray();
                    }

                    var chunk = allLines.Skip(start).Take(count)
                        .Select(l => new string(l.Select(c => c <= 127 ? c : '?').ToArray()));

                    int maxReplyLen = 96;
                    foreach (var safeLine in chunk)
                    {
                        int pos = 0;
                        while (pos < safeLine.Length)
                        {
                            int take = Math.Min(maxReplyLen, safeLine.Length - pos);
                            string part = safeLine.Substring(pos, take);
                            if (!string.IsNullOrWhiteSpace(part))
                                Reply(part);
                            pos += take;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Reply($"Error reading log file: {ex.Message}");
                }
            }

            [Command("gametime")]
            public void GameTime()
            {
                var prop = _weatherManager.GetType().GetProperty("CurrentDateTime");
                if (prop != null)
                {
                    var zonedDateTimeObj = prop.GetValue(_weatherManager);
                    if (zonedDateTimeObj != null)
                    {
                        var localDateTimeProp = zonedDateTimeObj.GetType().GetProperty("LocalDateTime");
                        if (localDateTimeProp != null)
                        {
                            var localDateTimeObj = localDateTimeProp.GetValue(zonedDateTimeObj);
                            if (localDateTimeObj != null)
                            {
                                var timeOfDayProp = localDateTimeObj.GetType().GetProperty("TimeOfDay");
                                if (timeOfDayProp != null)
                                {
                                    var timeOfDayObj = timeOfDayProp.GetValue(localDateTimeObj);
                                    if (timeOfDayObj != null)
                                    {
                                        var toStringMethod = timeOfDayObj.GetType().GetMethod("ToString", new[] { typeof(string), typeof(IFormatProvider) });
                                        if (toStringMethod != null)
                                        {
                                            var timeStr = (string)toStringMethod.Invoke(timeOfDayObj, new object[] { "HH:mm", System.Globalization.CultureInfo.InvariantCulture });
                                            Reply($"Aktuelle Spielzeit: {timeStr}");
                                            return;
                                        }
                                        Reply($"Aktuelle Spielzeit: {timeOfDayObj}");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    Reply("Konnte die Spielzeit nicht abrufen.");
                }
                else
                {
                    Reply("Property 'CurrentDateTime' nicht gefunden.");
                }
            }
        }
    }

    public class LogTcpServer
    {
        private readonly int _port;
        private readonly string _logDir;
        private Thread _serverThread;
        private bool _running = false;

        public LogTcpServer(int port, string logDir)
        {
            _port = port;
            _logDir = logDir;
        }

        public void Start()
        {
            _running = true;
            _serverThread = new Thread(ServerLoop);
            _serverThread.IsBackground = true;
            _serverThread.Start();
            Log.Information("LogTcpServer started on port " + _port);
        }

        public void Stop()
        {
            _running = false;
        }

        private void ServerLoop()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();
            Log.Information("LogTcpServer: Waiting for connection...");
            while (_running)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (client)
                        using (var stream = client.GetStream())
                        {
                            var dirInfo = new System.IO.DirectoryInfo(_logDir);
                            if (!dirInfo.Exists)
                            {
                                Log.Error("LogTcpServer: Log directory not found: " + _logDir);
                            }
                            else
                            {
                                var files = dirInfo.GetFiles("*.txt");
                                Log.Information($"LogTcpServer: Found {files.Length} log files in {_logDir}");
                                var newestLog = files
                                    .OrderByDescending(f => f.LastWriteTime)
                                    .FirstOrDefault();
                                if (newestLog != null)
                                {
                                    Log.Information("LogTcpServer: Sending file " + newestLog.FullName + " (" + newestLog.Length + " bytes)");
                                    using (var fs = new System.IO.FileStream(newestLog.FullName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                                    {
                                        fs.CopyTo(stream);
                                        stream.Flush();
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("LogTcpServer error: " + ex);
                    }
                });
            }
            listener.Stop();
        }
    }
}
