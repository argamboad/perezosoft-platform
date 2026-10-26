using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Perezosoft.Client.Web;
using AppBrand.Shared.Ui;

// The web host (Pkg 4.2, ADR-034): the app's root components, then the platform's client for the browser
// (Perezosoft.Platform.Web: API clients, session, preferences), the saved culture, run.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.AddPlatformWeb();

var host = builder.Build();
await host.UseStoredCultureAsync();
await host.RunAsync();
