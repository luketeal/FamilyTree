using FamilyTree.Domain.Repositories;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Repositories;
using FamilyTree.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=familytree.db";
builder.Services.AddDbContext<FamilyTreeDbContext>(opts => opts.UseSqlite(connectionString));

builder.Services.AddScoped<IPersonRepository, PersonRepository>();
builder.Services.AddScoped<IBiologicalRelationshipRepository, BiologicalRelationshipRepository>();
builder.Services.AddScoped<IAdoptiveRelationshipRepository, AdoptiveRelationshipRepository>();
builder.Services.AddScoped<IMarriageRepository, MarriageRepository>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FamilyTreeDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
