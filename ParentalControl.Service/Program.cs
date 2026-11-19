using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParentalControl.Service;

Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ParentalControl")
    .ConfigureServices(services =>
    {
        services.AddSingleton<DailyPolicy>();
        services.AddHostedService<MonitoringService>();
    })
    .Build()
    .Run();
