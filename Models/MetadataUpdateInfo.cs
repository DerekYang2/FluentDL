using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ATL;

namespace FluentDL.Models;

public class MetadataUpdateInfo
{
    private ObservableCollection<MetadataPair> MetadataList;
    private string FilePath;
    private string? ImgPath;

    public MetadataUpdateInfo(ObservableCollection<MetadataPair> metadataList, string filePath, string imgPath)
    {
        MetadataList = metadataList;
        FilePath = filePath;
        ImgPath = imgPath;
    }

    public JsonObject GetJsonObject()
    {
        var rootNode = new JsonObject { ["Path"] = FilePath, ["MetadataList"] = new JsonArray(), ["ImagePath"] = ImgPath ?? "" }; // Create the root json object

        foreach (var pair in MetadataList) // Add all metadata pairs to the json object
        {
            rootNode["MetadataList"].AsArray().Add(new JsonObject() { ["Key"] = pair.Key, ["Value"] = pair.Value });
        }

        return rootNode;
    }

    public void SetMetadataList(ObservableCollection<MetadataPair> list)
    {
        MetadataList = list;
    }

    public void SetImagePath(string path)
    {
        ImgPath = path;
    }

    public ObservableCollection<MetadataPair> GetMetadataList()
    {
        return MetadataList;
    }

    public string? GetImagePath()
    {
        return ImgPath;
    }
}