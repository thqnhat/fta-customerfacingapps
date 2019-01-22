#region using
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;
#endregion

namespace Relecloud.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseApplicationInsights()
                .ConfigureAppConfiguration((context, builder) => 
                {
                    var config = builder.Build();

                    // Create Managed Service Identity token provider
                    var tokenProvider = new AzureServiceTokenProvider();
                    var kvClient = new KeyVaultClient((authority, resource, scope) => tokenProvider.KeyVaultTokenCallback(authority, resource, scope));
                    builder.AddAzureKeyVault(config["App:KeyVault:Url"], kvClient, new DefaultKeyVaultSecretManager());
                })
                .UseStartup<Startup>()
                .Build();
    }
}