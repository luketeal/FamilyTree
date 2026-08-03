using FamilyTree.App;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// No HttpClient is registered: this app is client-only with no backend (ADR-004).
// Storage and application services are registered here from PR 2 onward.

await builder.Build().RunAsync();
