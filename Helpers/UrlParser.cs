using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace FluentDL.Helpers
{
    class UrlParser
    {
        public static void ParseTextBlock(TextBlock textblock, string str)
        {
            // Parse <a href=''></a> tags and <b></b> tags
            var r = new Regex(@"<a.*?href=(""|')(?<href>.*?)(""|').*?>(?<value>.*?)</a>|<b>(?<value>.*?)</b>");
            var prevIdx = 0;
            textblock.Inlines.Clear();

            foreach (Match match in r.Matches(str))
            {
                // Check if the match is a hyperlink or bold text
                if (match.Groups["href"].Success)
                {
                    var link = match.Groups["href"].Value;

                    // if value is empty, use the link as the value
                    string text;
                    if (match.Groups["value"].Value == "")
                    {
                        text = link;
                    }
                    else
                    {
                        text = match.Groups["value"].Value;
                    }

                    // Get index of match
                    var index = match.Index;
                    var length = match.Length;

                    // Get the text before the match
                    var before = str.Substring(prevIdx, index - prevIdx);
                    textblock.Inlines.Add(new Run { Text = before });

                    // Add the hyperlink
                    Hyperlink hyperLink;
                    // Check if hyperlink is online or local file
                    if (Directory.Exists(link) || File.Exists(link))
                    {
                        hyperLink = new Hyperlink();
                        hyperLink.Inlines.Add(new Run { Text = text });

                        // Custom click event to open file in explorer (selects the file)
                        hyperLink.Click += (sender, e) =>
                        {
                            if (Directory.Exists(link))
                            {
                                // Open the folder
                                Process.Start("explorer.exe", link);
                            }
                            else if (File.Exists(link))
                            {
                                var argument = $"/select, \"{link}\"";
                                System.Diagnostics.Process.Start("explorer.exe", argument);
                            }
                        };
                    }
                    else
                    {
                        hyperLink = new Hyperlink { NavigateUri = new Uri(link) };
                        hyperLink.Inlines.Add(new Run { Text = text });
                    }

                    textblock.Inlines.Add(hyperLink);

                    // Update previous index
                    prevIdx = index + length;
                }
                else if (match.Groups["value"].Success)
                {
                    var text = match.Groups["value"].Value;

                    // Get index of match
                    var index = match.Index;
                    var length = match.Length;

                    // Get the text before the match
                    var before = str.Substring(prevIdx, index - prevIdx);
                    textblock.Inlines.Add(new Run { Text = before });

                    // Add the bold text
                    textblock.Inlines.Add(new Run { Text = text, FontWeight = FontWeights.SemiBold });

                    // Update previous index
                    prevIdx = index + length;
                }
            }

            // Add the remaining text
            textblock.Inlines.Add(new Run { Text = str.Substring(prevIdx) });
        }
    }
}