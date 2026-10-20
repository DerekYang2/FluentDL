#nullable enable
namespace FluentDL.Core.Updates;

public enum UpdateChannel { Unsupported, MicrosoftStore, Github }

public static class UpdateChannels
{
    public const string Name = "DerekYang2.FluentDL";
    public const string StorePublisher = "CN=DD45FB67-1F5A-4180-88DE-453755BBC3D4";
    public const string UnsignedPublisher = StorePublisher + ", OID.2.25.311729368913984317654407730594956997722=1";

    public static UpdateChannel Select(UpdateInstallation installation, bool development, bool storeSigned, bool unsigned, bool windows11)
    {
        if (development || installation.Name != Name) return UpdateChannel.Unsupported;
        if (storeSigned && installation.Publisher == StorePublisher) return UpdateChannel.MicrosoftStore;
        return unsigned && windows11 && installation.Publisher == UnsignedPublisher && installation.Architecture is "x64" or "arm64"
            ? UpdateChannel.Github : UpdateChannel.Unsupported;
    }
}
