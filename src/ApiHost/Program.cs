using ApiHost;

var compiledWebRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var builderOptions = new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(compiledWebRoot) ? compiledWebRoot : null
};

var builder = WebApplication.CreateBuilder(builderOptions);
builder.AddBaselineApiHostServices();

var app = builder.Build();
app.MapBaselineApiHost();

app.Run();

public partial class Program;
