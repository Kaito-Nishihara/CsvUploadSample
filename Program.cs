using CsvUploadSample.Entities;
using CsvUploadSample.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddDbContext<CsvAppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddTransient<ICsvUploadService<TempCsvMaster,CsvMaster>, CsvUploadService<TempCsvMaster,CsvMaster>>();
builder.Services.AddTransient<ICsvUploadService<TempCsvMaster, SubMaster>, CsvUploadService<TempCsvMaster, SubMaster>>();
builder.Services.AddTransient<ISampleService, SampleService> ();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    endpoints.MapHub<ProgressHub>("/progressHub"); // SignalRのハブをマッピング
});


app.Run();
