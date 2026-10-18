using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<PriceCheck.Data.PriceCheckContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("PriceCheck") ?? "Data Source=pricecheck.db"));
builder.Services.AddHttpClient<PriceCheck.Services.ProductPagePriceImporter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<PriceCheck.Services.PriceHistoryService>();
builder.Services.AddHostedService<PriceCheck.Services.DailyPriceCheckService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<PriceCheck.Data.PriceCheckContext>();
    await database.Database.EnsureCreatedAsync();
    await database.EnsureCategoryColumnAsync();
    await PriceCheck.Data.PriceCheckContext.SeedAsync(database);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
