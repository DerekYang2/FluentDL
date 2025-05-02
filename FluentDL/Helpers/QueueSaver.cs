using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace FluentDL.Helpers
{
    internal class QueueSaver
    {
        public static string fileName = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "queue.json");
        private static readonly object _lockObject = new object();
        private static StreamWriter? _writer = null;

        public static void Init() {
            _writer = new StreamWriter(fileName, append: false) { AutoFlush = true };
        }
        public static string GetPath() {
            return fileName;
        }

        public static void SaveString(string str) {
            lock (_lockObject) {
                if (_writer == null) return;
                _writer.Write(str);
            }
        }

        public static void Close() {
            lock (_lockObject) {
                if (_writer == null) return;
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
        }
    }
}
