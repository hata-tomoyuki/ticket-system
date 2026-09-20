using HelpDesk.Core;
using HelpDesk.Infrastructure;
using HelpDesk.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// DbContext そのものではなく「DbContext を作る工場」を登録する。
// DbContext はスレッド安全ではなく、Blazor では 1 つのスコープが長く生きるため、
// 使うたびに作って捨てる形にしないと同時アクセスで壊れる。
builder.Services.AddDbContextFactory<HelpDeskDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("HelpDesk")));

// 実装クラスの名前を知っているのは、アプリ全体でこの 1 行だけ。
// ステージ 1 では InMemoryTicketRepository だった。
builder.Services.AddScoped<ITicketRepository, EfTicketRepository>();

var app = builder.Build();

// 起動時にマイグレーションを適用し、空なら初期データを入れる。
// 学習用の割り切りで、本番では通常やらない（デプロイ手順として別に流す）。
await using (var db = await app.Services
    .GetRequiredService<IDbContextFactory<HelpDeskDbContext>>()
    .CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();
