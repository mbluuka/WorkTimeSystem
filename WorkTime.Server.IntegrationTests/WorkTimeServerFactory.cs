using System;
using System.Linq;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using WorkTime.Server.Data;

namespace WorkTime.Server.IntegrationTests;

public class WorkTimeServerFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly TestcontainersContainer _pgContainer;

    public WorkTimeServerFactory()
    {
        // configure Postgres testcontainer
        var postgresConfig = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(new PostgreSqlTestcontainerConfiguration
            {
                Database = "worktime_test",
                Username = "postgres",
                Password = "postgres"
            })
            .WithImage("postgres:15-alpine")
            .WithCleanUp(true)
            .Build();

        _pgContainer = postgresConfig;
        _pgContainer.StartAsync().GetAwaiter().GetResult();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace DbContext with one pointing to testcontainer Postgres
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<WorkTimeDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            var connStr = _pgContainer is null ? throw new InvalidOperationException("Postgres container not started") : _pgContainer.ConnectionString;

            services.AddDbContext<WorkTimeDbContext>(options =>
                options.UseNpgsql(connStr));
        });

        return base.CreateHost(builder);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pgContainer != null)
        {
            await _pgContainer.StopAsync();
            _pgContainer.Dispose();
        }
    }
}
