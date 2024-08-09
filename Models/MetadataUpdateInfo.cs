using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ATL;
using FluentDL.Helpers;

namespace FluentDL.Models;

internal class MetadataUpdateInfo
{
    private MetadataObject metadataObject;
    private string FilePath;
    private string? ImgPath;

    public MetadataUpdateInfo(MetadataObject metadataObject, string filePath, string imgPath)
    {
        this.metadataObject = metadataObject;
        FilePath = filePath;
        ImgPath = imgPath;
    }

    public JsonObject GetJsonObject()
    {
        var rootNode = new JsonObject { ["Path"] = FilePath, ["MetadataList"] = new JsonArray(), ["ImagePath"] = ImgPath ?? "" }; // Create the root json object

        //foreach (var pair in MetadataList) // Add all metadata pairs to the json object
        //{
        //    rootNode["MetadataList"].AsArray().Add(new JsonObject() { ["Key"] = pair.Key, ["Value"] = pair.Value });
        //}

        return rootNode;
    }

    public void SetImagePath(string path)
    {
        ImgPath = path;
    }

    public ObservableCollection<MetadataPair> GetMetadataList()
    {
        return metadataObject.GetMetadataPairCollection();
    }

    public string? GetImagePath()
    {
        return ImgPath;
    }

    public async Task SaveMetadata()
    {
        if (ImgPath != null) // If an image path is set, save the image
        {
            metadataObject.AlbumArt = await new HttpClient().GetByteArrayAsync(ImgPath);
        }

        metadataObject.Save(FilePath); // Save the metadata to the file
    }
}