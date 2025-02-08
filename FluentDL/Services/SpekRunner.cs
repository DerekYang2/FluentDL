using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentDL.Services
{
    internal class SpekRunner
    {
        public static string SpekPath = "Assets\\Spek\\spek.exe";
        public static async void RunSpek(string filePath) {

            Thread t = new Thread(()=>{
                var fullPath = Path.Combine(AppContext.BaseDirectory, SpekPath);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = false;
                startInfo.FileName = fullPath;
                startInfo.Arguments = $"\"{filePath}\"";
                Process.Start(startInfo);
            });
            t.Start();
        }
    }
}
