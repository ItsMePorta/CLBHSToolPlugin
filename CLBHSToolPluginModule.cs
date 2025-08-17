using AssettoServer.Server.Plugin;
using Autofac;

namespace CLBHSToolPlugin;

public class CLBHSToolPluginModule : AssettoServerModule<CLBHSToolPluginConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<CLBHSToolPlugin>().As<IAssettoServerAutostart>().SingleInstance();
    }
}
