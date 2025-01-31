using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDL.Contracts.Services;
using Windows.Storage;

namespace FluentDL.Services
{
    class LocalCommands
    {
        private static ILocalSettingsService _localSettings;
        private static readonly int _maxCommands = 20;
        private static readonly string _commandMapKey = "CommandMap", _latestCommandKey = "LatestCommand", _pathMapKey = "PathMap", _latestPathKey = "LatestPath";
        public static SortedDictionary<string, string> CommandMap = new(), PathMap = new(); // {timestamp, command}, sorted by key ascending

        public static async Task Init()
        {
            _localSettings = App.GetService<ILocalSettingsService>(); // Get the local settings service

            // Load the command map
            var commandMap = await _localSettings.ReadSettingAsync<ApplicationDataCompositeValue>(_commandMapKey);
            if (commandMap == null)
            {
                commandMap = new ApplicationDataCompositeValue();
                await _localSettings.SaveSettingAsync(_commandMapKey, commandMap);
            }

            foreach (var pair in commandMap)
            {
                CommandMap.Add(pair.Key, (string)pair.Value);
            }

            // Load the path map
            var pathMap = await _localSettings.ReadSettingAsync<ApplicationDataCompositeValue>(_pathMapKey);
            if (pathMap == null)
            {
                pathMap = new ApplicationDataCompositeValue();
                await _localSettings.SaveSettingAsync(_pathMapKey, pathMap);
            }

            foreach (var pair in pathMap)
            {
                PathMap.Add(pair.Key, (string)pair.Value);
            }
        }

        public static async Task SaveLatestCommand(string command)
        {
            command = command.Trim();
            await _localSettings.SaveSettingAsync(_latestCommandKey, command); // Save the latest command used
        }


        public static async Task<string?> GetLatestCommand()
        {
            return await _localSettings.ReadSettingAsync<string>(_latestCommandKey);
        }

        public static async Task SaveLatestPath(string path)
        {
            path = path.Trim();
            await _localSettings.SaveSettingAsync(_latestPathKey, path); // Save the latest path used
        }

        public static async Task<string?> GetLatestPath()
        {
            return await _localSettings.ReadSettingAsync<string>(_latestPathKey);
        }

        public static bool AddCommand(string command)
        {
            command = command.Trim();
            if (CommandMap.ContainsValue(command)) // Command already exists
            {
                return false;
            }

            if (CommandMap.Count >= _maxCommands) // Remove the oldest command (smallest timestamp or key)
            {
                var oldestCommand = CommandMap.First();
                CommandMap.Remove(oldestCommand.Key);
            }

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            CommandMap.Add(timestamp, command);

            return true;
        }

        public static bool AddPath(string path)
        {
            path = path.Trim();
            if (PathMap.ContainsValue(path)) // Path already exists
            {
                return false;
            }

            if (PathMap.Count >= _maxCommands) // Remove the oldest path (smallest timestamp or key)
            {
                var oldestPath = PathMap.First();
                PathMap.Remove(oldestPath.Key);
            }

            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            PathMap.Add(timestamp, path);

            return true;
        }

        public static async Task SaveCommands()
        {
            var commandMap = new ApplicationDataCompositeValue();
            foreach (var pair in CommandMap)
            {
                commandMap[pair.Key] = pair.Value;
            }

            await _localSettings.SaveSettingAsync(_commandMapKey, commandMap);
        }

        public static async Task SavePaths()
        {
            var pathMap = new ApplicationDataCompositeValue();
            foreach (var pair in PathMap)
            {
                pathMap[pair.Key] = pair.Value;
            }

            await _localSettings.SaveSettingAsync(_pathMapKey, pathMap);
        }

        public static IEnumerable<string> GetCommandList()
        {
            return CommandMap.Values.Reverse(); // Reverse to get the largest timestamp values (newest) commands first
        }

        public static IEnumerable<string> GetPathList()
        {
            return PathMap.Values.Reverse(); // Reverse to get the largest timestamp values (newest) paths first
        }
    }
}