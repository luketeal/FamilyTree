using FamilyTree.App;
using FamilyTree.Application.Services;
using FamilyTree.Storage.Browser;
using FamilyTree.UI.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// No HttpClient is registered: this app is client-only with no backend (ADR-004).
builder.Services.AddBrowserStorage();
builder.Services.AddScoped<PersonService>();
builder.Services.AddScoped<CircularReferenceChecker>();
builder.Services.AddScoped<TreeStatsService>();
builder.Services.AddScoped<TreeDataNotifier>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<OverlayInterop>();
builder.Services.AddScoped<IOverlayInterop>(sp => sp.GetRequiredService<OverlayInterop>());

// Persistent storage is requested from MainLayout on first render rather than
// here. JS interop needs the Blazor runtime to be up, which RunAsync starts —
// calling a JS module before it leaves the app hanging with nothing rendered.
await builder.Build().RunAsync();
