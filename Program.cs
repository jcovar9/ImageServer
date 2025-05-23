using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

Directory.CreateDirectory("wwwroot/uploads");

var builder = WebApplication.CreateBuilder(args);

NetVips.Cache.MaxFiles = 0;
NetVips.Cache.MaxMem = 0;
NetVips.Cache.Max = 0;

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSession();
string dataSource = $"Data Source={(builder.Environment.IsProduction() ? "/publish/data/" : "")}localdb.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(dataSource));

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();
app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=EnterDirectory}/{id?}");

app.Run();
