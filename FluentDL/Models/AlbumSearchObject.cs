using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentDL.Models
{
    class AlbumSearchObject : SongSearchObject
    {
        public int TracksCount { get; set; } = 0;
        public ICollection<string> TrackIds { get; set; } = [];

        // Alias, Upc can be stored in ISRC
        public string? Upc
        {
            get { return Isrc; }
            set { Isrc = value; }
        }

        public override string ToString()
        {
            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Error = (sender, args) => args.ErrorContext.Handled = true
            };
            return JsonConvert.SerializeObject(this, settings);
        }
    }
}