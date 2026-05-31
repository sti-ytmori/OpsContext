using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.AI;
using MudBlazor.Services;
using OpsContext.Agents.Agents;
using OpsContext.Agents.Models;
using OpsContext.Agents.Options;
using OpsContext.Agents.Services;
using OpsContext.Agents.Tools;
using OpsContext.Agents;
using OpsContext.Web.Components;
using OpsContext.Web.Services;
using System.Security.Claims;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor
builder.Services.AddMudServices();

// 設定バインド: appsettings "OpsContext" セクション → OpsContextOptions
builder.Services.Configure<OpsContextOptions>(
    builder.Configuration.GetSection("OpsContext"));

// IChatClient — モックモード時は MockChatClient、本番時は Azure OpenAI を使う
// (モック判定は後続の mockMode 変数が確定する前に実行されるため、ここでも判定する)
var isMockMode = builder.Configuration.GetValue<bool>("OpsContext:MockMode")
    || args.Contains("--mock")
    || string.IsNullOrEmpty(builder.Configuration["OpsContext:SqlConnectionString"]);

if (isMockMode)
{
    builder.Services.AddSingleton<IChatClient, OpsContext.Agents.Services.MockChatClient>();
}
else
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpsContextOptions>>().Value;
        var aoai = new AzureOpenAIClient(
            new Uri(opts.AzureOpenAi.Endpoint),
            new AzureKeyCredential(opts.AzureOpenAi.ApiKey));
        return aoai.GetChatClient(opts.AzureOpenAi.ChatDeployment).AsIChatClient();
    });
}

// --- Tool 層 ---
// モックモード: appsettings に接続情報が無い場合は Mock 実装を使う
var mockMode = isMockMode; // 上記で計算済み

if (mockMode)
{
    builder.Services.AddSingleton<ISqlErpTool, MockSqlErpTool>();
    builder.Services.AddSingleton<IAiSearchTool, MockAiSearchTool>();
    builder.Services.AddSingleton<MockContextStoreTool>();
    builder.Services.AddSingleton<IContextStoreTool>(sp => sp.GetRequiredService<MockContextStoreTool>());
    builder.Services.AddSingleton<IDemoService>(sp => sp.GetRequiredService<MockContextStoreTool>());
    builder.Services.AddSingleton<IDataGridStore, MockDataGridStore>();
    builder.Services.AddSingleton<IUserStore, MockUserStore>();
    builder.Services.AddSingleton<IErpDatasetStore, MockErpDatasetStore>();
    builder.Services.AddSingleton<IRolePromptStore, MockRolePromptStore>();
    builder.Services.AddSingleton<ILoginHistoryStore, MockLoginHistoryStore>();
}
else
{
    builder.Services.AddScoped<ISqlErpTool, SqlErpTool>();
    builder.Services.AddSingleton<IAiSearchTool, AiSearchTool>();
    builder.Services.AddScoped<IContextStoreTool, ContextStoreTool>();
    builder.Services.AddScoped<IDemoService, ProdDemoService>();
    builder.Services.AddSingleton<IDataGridStore, SqlDataGridStore>();
    builder.Services.AddScoped<IUserStore, SqlUserStore>();
    builder.Services.AddScoped<IErpDatasetStore, SqlErpDatasetStore>();
    builder.Services.AddScoped<IRolePromptStore, SqlRolePromptStore>();
    builder.Services.AddSingleton<ILoginHistoryStore, SqlLoginHistoryStore>();
}

builder.Services.AddScoped<ICsvTool, CsvService>();
builder.Services.AddSingleton<ICalcTool, CalcTool>();
builder.Services.AddScoped<OpsContext.Agents.QuoteValidationService>();
builder.Services.AddScoped<OpsContext.Agents.GridAgentService>();

// --- エージェント ---
builder.Services.AddScoped<OrchestratorAgent>();

// --- Curator (IHostedService + Channel) ---
var curatorChannel = Channel.CreateBounded<ConversationEvent>(100);
builder.Services.AddSingleton(curatorChannel.Writer);
builder.Services.AddSingleton(curatorChannel.Reader);
builder.Services.AddSingleton<ICuratorQueue, CuratorQueue>();
builder.Services.AddHostedService<CuratorHostedService>();

// --- Blazor UI サービス ---
builder.Services.AddScoped<RoleStateService>();

// --- 認証: Cookie 認証 ---
// Entra ID 移行時は AddCookie を AddMicrosoftIdentityWebApp に差し替えるだけでよい。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath         = "/login";
        options.AccessDeniedPath  = "/login";
        options.ExpireTimeSpan    = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AnyRole",    p => p.RequireAuthenticatedUser())
    .AddPolicy("AdminOnly",  p => p.RequireAuthenticatedUser()
                                   .RequireClaim("opscontext_is_admin", "true"));

// AuthenticationState を Blazor コンポーネントへカスケードするために必要
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// -----------------------------------------------------------------------
// 認証エンドポイント (Blazor インタラクティブ内では SignInAsync / SignOutAsync が
// 使えないため、通常の HTTP エンドポイントとして実装する)
// -----------------------------------------------------------------------

// POST /auth/login — form から userName と password を受け取り認証する
app.MapPost("/auth/login", async (
    HttpContext ctx,
    [Microsoft.AspNetCore.Mvc.FromForm] string userName,
    [Microsoft.AspNetCore.Mvc.FromForm] string password,
    IUserStore store,
    ILoginHistoryStore history,
    ILogger<Program> logger) =>
{
    // クライアント IP: リバースプロキシ配下では X-Forwarded-For を優先する
    var clientIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
                   ?? ctx.Connection.RemoteIpAddress?.ToString();

    // User-Agent は 512 文字で切り詰め
    var userAgent = ctx.Request.Headers.UserAgent.ToString();
    if (userAgent.Length > 512) userAgent = userAgent[..512];
    if (string.IsNullOrEmpty(userAgent)) userAgent = null;

    var user = await store.ValidateAsync(userName, password);
    if (user is null)
    {
        try { await history.RecordAsync(userName, null, null, success: false, clientIp, userAgent); }
        catch (Exception ex) { logger.LogWarning(ex, "ログイン履歴（失敗）の記録に失敗しました。"); }

        return Results.Redirect("/login?error=1");
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name,         user.UserName),
        new Claim(ClaimTypes.GivenName,    user.DisplayName),
        new Claim(ClaimTypes.Role,         user.Role),
        new Claim("opscontext_role",       user.Role),
        new Claim("opscontext_is_admin",   user.Role == "Admin" ? "true" : "false"),
    };
    var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    try { await history.RecordAsync(user.UserName, user.DisplayName, user.Role, success: true, clientIp, userAgent); }
    catch (Exception ex) { logger.LogWarning(ex, "ログイン履歴（成功）の記録に失敗しました。"); }

    return Results.Redirect("/");

}).DisableAntiforgery();

// GET /auth/logout — セッションを破棄してログイン画面へ戻る
// Blazor Server (Interactive) 内の form POST は SignalR に横取りされるため GET で受ける
app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// -----------------------------------------------------------------------
// 本番モードの場合、起動時に Users テーブルが空であればデモデータを投入する
// -----------------------------------------------------------------------
if (!mockMode)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        _ = Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();

            // Users シード
            var userStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
            if (userStore is SqlUserStore sqlUserStore)
            {
                try { await sqlUserStore.SeedIfEmptyAsync(); }
                catch (Exception ex)
                {
                    var logger = app.Services.GetRequiredService<ILogger<SqlUserStore>>();
                    logger.LogWarning(ex, "Users テーブルのシード投入に失敗しました。schema_users.sql を適用済みか確認してください。");
                }
            }

            // RolePrompts シード
            var roleStore = scope.ServiceProvider.GetRequiredService<IRolePromptStore>();
            if (roleStore is SqlRolePromptStore sqlRoleStore)
            {
                try { await sqlRoleStore.SeedIfEmptyAsync(); }
                catch (Exception ex)
                {
                    var logger = app.Services.GetRequiredService<ILogger<SqlRolePromptStore>>();
                    logger.LogWarning(ex, "RolePrompts テーブルのシード投入に失敗しました。schema_role_prompts.sql を適用済みか確認してください。");
                }
            }

            // LoginHistory スキーマ自動作成
            var historyStore = app.Services.GetRequiredService<ILoginHistoryStore>();
            if (historyStore is SqlLoginHistoryStore sqlHistoryStore)
            {
                try { await sqlHistoryStore.EnsureSchemaAsync(); }
                catch (Exception ex)
                {
                    var logger = app.Services.GetRequiredService<ILogger<SqlLoginHistoryStore>>();
                    logger.LogWarning(ex, "LoginHistory テーブルのスキーマ作成に失敗しました。");
                }
            }
        });
    });
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
