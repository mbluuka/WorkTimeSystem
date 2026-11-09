using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WorkTime.Server.Data;

namespace WorkTime.Server.IntegrationTests;

public class WorkTimeServerFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly PostgreSqlContainer _pgContainer;

    public WorkTimeServerFactory()
    {
        _pgContainer = new PostgreSqlBuilder()
            .WithDatabase("worktime_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();

        _pgContainer.StartAsync().GetAwaiter().GetResult();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<WorkTimeDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<WorkTimeDbContext>(options =>
                options.UseNpgsql(_pgContainer.GetConnectionString()));
        });

        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_pgContainer != null)
        {
            await _pgContainer.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
