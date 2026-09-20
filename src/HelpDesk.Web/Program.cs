using HelpDesk.Core;
using HelpDesk.Web.Components;
using HelpDesk.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// チケットの保管庫。ここが「どの実装を使うか」を決める唯一の場所で、
// 画面側は ITicketRepository しか知らない。
// Singleton なのは、メモリ上の List をアプリ全体で 1 つだけ持ちたいため。
// ステージ 2 で EF Core に差し替えるときは、この行と寿命の両方を見直す。
builder.Services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

var app = builder.Build();

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
