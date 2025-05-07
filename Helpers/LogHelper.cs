using Dalamud.Plugin.Services;
using Echorama.DataClasses;
using Echorama.Enums;
using Echorama.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echorama.Helpers
{
    public static class LogHelper
    {
        private static IPluginLog Log;
        private static Configuration Config;
        private static List<LogMessage> GeneralLogs = new List<LogMessage>();
        public static List<LogMessage> GeneralLogsFiltered = new List<LogMessage>();

        public static void Setup(Configuration config)
        {
            Log = Plugin.Log;
            Config = config;
        }

        public static void Start(string method, EREventId eventId)
        {
            var text = $"---------------------------Start----------------------------------";

            Info(method, text, eventId);
        }

        public static void End(string method, EREventId eventId)
        {
            var text = $"----------------------------End-----------------------------------";

            Info(method, text, eventId);
        }

        public static void Info(string method, string text, EREventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Info, eventId = eventId, method = method, message = $"{text}", color = Constants.INFOLOGCOLOR, timeStamp = DateTime.Now });

            Log.Info($"{method} - {text} - ID: {eventId.Id}");
        }

        public static void Debug(string method, string text, EREventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Debug, eventId = eventId, method = method, message = $"{text}", color = Constants.DEBUGLOGCOLOR, timeStamp = DateTime.Now });

            Log.Debug($"{method} - {text} - ID: {eventId.Id}");
        }

        public static void Error(string method, Exception e, EREventId eventId, bool internalLog = true)
        {
            var text = $"Error: {e.Message}\r\nStacktrace: {e.StackTrace}";
            SortLogEntry(new LogMessage() { type = LogType.Error, eventId = eventId, method = method, message = $"{text}", color = Constants.ERRORLOGCOLOR, timeStamp = DateTime.Now });

            Log.Error($"{method} - {text} - ID: {eventId.Id}");
        }

        private static void SortLogEntry(LogMessage logMessage)
        {
            GeneralLogs.Add(logMessage);
            if ((logMessage.type == LogType.Info)
                || (logMessage.type == LogType.Debug && Config.ShowGeneralDebugLog)
                || (logMessage.type == LogType.Error && Config.ShowGeneralErrorLog))
                GeneralLogsFiltered.Add(logMessage);
            MainWindow.UpdateLogGeneralFilter = true;
        }

        public static List<LogMessage> RecreateLogList()
        {
            var logListFiltered = new List<LogMessage>();
            var showDebug = false;
            var showError = false;
            var showId0 = false;
            GeneralLogsFiltered = new List<LogMessage>(GeneralLogs);
            logListFiltered = GeneralLogsFiltered;
            showDebug = Config.ShowGeneralDebugLog;
            showError = Config.ShowGeneralErrorLog;
            showId0 = true;
            if (!showDebug)
            {
                logListFiltered.RemoveAll(p => p.type == LogType.Debug);
            }
            if (!showError)
            {
                logListFiltered.RemoveAll(p => p.type == LogType.Error);
            }
            if (!showId0)
            {
                logListFiltered.RemoveAll(p => p.eventId.Id == 0);
            }

            logListFiltered.Sort((p, q) => p.timeStamp.CompareTo(q.timeStamp));

            return new List<LogMessage>(logListFiltered);
        }
    }
}
