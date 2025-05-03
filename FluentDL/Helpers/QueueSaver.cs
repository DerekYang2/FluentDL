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
        public static string fileName = Path.Combine(ApplicationData.Current.LocalFolder.Path, "queue.json");
        private static readonly object _lockObject = new object();
        private static StreamWriter? _writer = null;

        public static void Init() {
            _writer = new StreamWriter(fileName, append: false);
        }
        public static string GetPath() {
            return fileName;
        }

        public static void SaveString(string str) {
            lock (_lockObject) {
                if (_writer == null) return;
        
                // Set the position back to the beginning of the file
                _writer.BaseStream.SetLength(0);
                _writer.BaseStream.Position = 0;
                _writer.Write(str);
                _writer.Flush();
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
