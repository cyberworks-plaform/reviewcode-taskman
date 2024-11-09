using System.Data;
using AutoMapper;
using Axe.TaskManagement.Data.Configs;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Configs;
using Axe.TaskManagement.Service.Mappers;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding;
using Axe.Utility.Definitions;
using Ce.Client.All.Configs;
using Ce.Common.Lib.StartupExtensions;
using Ce.Constant.Lib.Definitions;
using Ce.EventBus.Lib.Abstractions;
using Ce.Interaction.Lib.StartupExtensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Threading.Tasks;
using Axe.TaskManagement.Data.DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;
using System.Collections.Generic;
using System.Reflection;
using System;
using System.Linq;

namespace Axe.TaskManagement.Api
{
    public class Startup : BaseStartup
    {
        private readonly string _providerName;

        public Startup(IConfiguration configuration) : base(configuration)
        {
            _providerName = Configuration.GetConnectionString("ProviderName") ?? "";
        }

        protected override void AddCustomService(IServiceCollection services)
        {
            // Api domain
            BuildApiDomain();

            // Inject DbContext and IDbConnection, with implementation from SqlConnection, NpgsqlConnection,... class.
            switch (_providerName)
            {
                case ProviderTypeConstants.SqlServer:
                    // Config DbContexts
                    services.AddDbContext<AxeTaskManagementDbContext>(options =>
                        options.UseSqlServer(Configuration.GetConnectionString("AxeTaskManagementDbConnection"), o => o.MigrationsAssembly("Axe.TaskManagement.Data")));
                    // Config Unit Of Work
                    services.AddTransient(typeof(IAxeTaskManagementUnitOfWork), typeof(AxeTaskManagementUnitOfWork));

                    services.AddTransient<IDbConnection>(sp => new SqlConnection(Configuration.GetConnectionString("AxeTaskManagementDbConnection")));
                    break;
                case ProviderTypeConstants.Postgre:
                    // Config DbContexts
                    services.AddDbContext<AxeTaskManagementDbContext>(options =>
                        options.UseNpgsql(Configuration.GetConnectionString("AxeTaskManagementDbConnection"), o => o.MigrationsAssembly("Axe.TaskManagement.Data")));
                    // Config Unit Of Work
                    services.AddTransient(typeof(IAxeTaskManagementUnitOfWork), typeof(AxeTaskManagementUnitOfWork));

                    services.AddTransient<IDbConnection>(sp => new NpgsqlConnection(Configuration.GetConnectionString("AxeTaskManagementDbConnection")));
                    break;
                case ProviderTypeConstants.MySql:
                    break;
                case ProviderTypeConstants.Oracle:
                    break;
                case ProviderTypeConstants.Sqlite:
                    break;
                default:
                    // Config DbContexts
                    services.AddDbContext<AxeTaskManagementDbContext>(options =>
                        options.UseSqlServer(Configuration.GetConnectionString("AxeTaskManagementDbConnection"), o => o.MigrationsAssembly("Axe.TaskManagement.Data")));
                    // Config Unit Of Work
                    services.AddTransient(typeof(IAxeTaskManagementUnitOfWork), typeof(AxeTaskManagementUnitOfWork));

                    services.AddTransient<IDbConnection>(sp => new SqlConnection(Configuration.GetConnectionString("AxeTaskManagementDbConnection")));
                    break;
            }

            // Config MongoDB
            services.AddMongoDb(Configuration);

            // Config Mapper
            var mappingConfig = AutoMapperConfig.RegisterMappings();
            IMapper mapper = mappingConfig.CreateMapper();
            services.AddSingleton(mapper);

            //Config redis
            services.AddSingleton<IConnectionMultiplexer>(x => ConnectionMultiplexer.Connect($"{Configuration["Cache:Host"]}:{Configuration["Cache:Port"]}"));

            // Config Repositories
            services.DependencyInjectionRepos();

            //Config Services
            services.DependencyInjectionService();

            // Config BaseHttpClient to call other service api
            services.AddBaseHttpClient();

            // Config Client All
            services.DependencyInjectionClientService();

            // Config RabbitMq
            services.AddServiceBusPersistentConnection(Configuration);
            services.AddEventBus(Configuration);
            services.AddRabbitMqAdminClient(Configuration);

            // Config EvenHandling
            if (Configuration.GetValue("RabbitMq:UseRabbitMq", false))
            {
                services.AddTransient<TaskIntegrationEventHandler>();
                services.AddTransient<RetryDocIntegrationEventHandler>();
                services.AddTransient<QueueLockIntegrationEventHandler>();
                services.AddTransient<AfterProcessSegmentLabelingIntegrationEventHandler>();
                services.AddTransient<AfterProcessDataEntryIntegrationEventHandler>();
                services.AddTransient<AfterProcessDataEntryBoolIntegrationEventHandler>();
                services.AddTransient<AfterProcessDataCheckIntegrationEventHandler>();
                services.AddTransient<AfterProcessDataConfirmIntegrationEventHandler>();
                services.AddTransient<AfterProcessCheckFinalIntegrationEventHandler>();
                services.AddTransient<AfterProcessQaCheckFinalIntegrationEventHandler>();
            }

            base.AddCustomService(services);
        }

        protected override void UseCustomService(IApplicationBuilder app, IWebHostEnvironment env)
        {
            UseMigration(app);

            Task.Run(async () => await SeedData(app));

            if (Configuration.GetValue("RabbitMq:UseRabbitMq", false))
            {
                var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();

                // Mapping event bus event with the handler to handling
                eventBus.Subscribe<TaskEvent, TaskIntegrationEventHandler>(nameof(TaskEvent).ToLower());
                eventBus.Subscribe<RetryDocEvent, RetryDocIntegrationEventHandler>(nameof(RetryDocEvent).ToLower());
                eventBus.Subscribe<QueueLockEvent, QueueLockIntegrationEventHandler>(nameof(QueueLockEvent).ToLower());
                eventBus.Subscribe<AfterProcessSegmentLabelingEvent, AfterProcessSegmentLabelingIntegrationEventHandler>(nameof(AfterProcessSegmentLabelingEvent).ToLower());
                eventBus.Subscribe<AfterProcessDataEntryEvent, AfterProcessDataEntryIntegrationEventHandler>(nameof(AfterProcessDataEntryEvent).ToLower());
                eventBus.Subscribe<AfterProcessDataEntryBoolEvent, AfterProcessDataEntryBoolIntegrationEventHandler>(nameof(AfterProcessDataEntryBoolEvent).ToLower());
                eventBus.Subscribe<AfterProcessDataCheckEvent, AfterProcessDataCheckIntegrationEventHandler>(nameof(AfterProcessDataCheckEvent).ToLower());
                eventBus.Subscribe<AfterProcessDataConfirmEvent, AfterProcessDataConfirmIntegrationEventHandler>(nameof(AfterProcessDataConfirmEvent).ToLower());
                eventBus.Subscribe<AfterProcessCheckFinalEvent, AfterProcessCheckFinalIntegrationEventHandler>(nameof(AfterProcessCheckFinalEvent).ToLower());
                eventBus.Subscribe<AfterProcessQaCheckFinalEvent, AfterProcessQaCheckFinalIntegrationEventHandler>(nameof(AfterProcessQaCheckFinalEvent).ToLower());
            }

            base.UseCustomService(app, env);
        }

        private void UseMigration(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<AxeTaskManagementDbContext>();
                List<string> migrationIds = null;
                try
                {
                    var dbAssebmly = Assembly.GetAssembly(context.GetType());
                    var types = dbAssebmly.GetTypes();
                    if (types.Any())
                    {
                        migrationIds = types.Where(x => x.BaseType == typeof(Migration))
                            .Select(item => item.GetCustomAttributes<MigrationAttribute>().First().Id)
                            .OrderBy(o => o).ToList();

                        if (Configuration["UseDebugMode"] == "true")
                        {
                            Log.Information("MigrationId List:");
                            for (int i = migrationIds.Count - 1; i >= 0; i--)
                            {
                                Log.Information($"MigrationId => {migrationIds[i]}");
                            }
                        }
                    }

                    // 1. Migration
                    if (Configuration["AutoMigration"] == "true")   // automatic migrations: add migration + update database
                    {
                        context.Database.Migrate();
                    }
                    else // insert all of migrations
                    {
                        if (migrationIds != null && migrationIds.Any())
                        {
                            var version = typeof(Migration).Assembly.GetName().Version;
                            string efVersion = $"{version.Major}.{version.Minor}.{version.Build}";

                            foreach (var mid in migrationIds)
                            {
                                string sql;
                                if (_providerName == ProviderTypeConstants.Postgre)
                                {
                                    sql =
                                        $@"DO
                                        $do$
                                        BEGIN
                                        IF NOT EXISTS ( SELECT 1 FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '{mid}' ) THEN
                                            INSERT INTO ""__EFMigrationsHistory""(""MigrationId"",""ProductVersion"") VALUES ('{mid}','{efVersion}');
                                        END IF;
                                        END;
                                        $do$";
                                }
                                else
                                {
                                    sql =
                                        $@" IF NOT EXISTS ( SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '{mid}' )
                                BEGIN
                                    INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES ('{mid}','{efVersion}')
                                END";
                                }

                                context.Database.ExecuteSqlRaw(sql);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"AutoMigration StackTrace: {ex.StackTrace}");
                    Log.Error($"AutoMigration Message: {ex.Message}");
                }
            }
        }

        private async Task SeedData(IApplicationBuilder app)
        {
            if (Configuration["EnableSeedData"] == "true")
            {
                using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
                {
                    var sequenceJobRepo = serviceScope.ServiceProvider.GetRequiredService<ISequenceJobRepository>();
                    var existed = await sequenceJobRepo.GetSequenceValue(SequenceJobNameConstants.SequenceJobName);
                    if (existed == 0)
                    {
                        await sequenceJobRepo.AddAsync(new SequenceJob
                        {
                            SequenceName = SequenceJobNameConstants.SequenceJobName,
                            SequenceValue = 1
                        });
                    }
                }
            }
        }

        private void BuildApiDomain()
        {
            ApiDomain.AuthEndpoint = Configuration["ApiDomain:AuthEndpoint"];
            ApiDomain.AxeCoreEndpoint = Configuration["ApiDomain:AxeCoreEndpoint"];
            ApiDomain.AxeTaskManagementEndpoint = Configuration["ApiDomain:AxeTaskManagementEndpoint"];
            ApiDomain.AxeReportEndpoint = Configuration["ApiDomain:AxeReportEndpoint"];
            ApiDomain.AxePaymentEndpoint = Configuration["ApiDomain:AxePaymentEndpoint"];
            ApiDomain.CommonMasterDataEndpoint = Configuration["ApiDomain:CommonMasterDataEndpoint"];
            ApiDomain.FileEndpoint = Configuration["ApiDomain:FileEndpoint"];
            ApiDomain.OcrEndpoint = Configuration["ApiDomain:OcrEndpoint"];
            ApiDomain.LogEndpoint = Configuration["ApiDomain:LogEndpoint"];
            ApiDomain.NotificationEndpoint = Configuration["ApiDomain:NotificationEndpoint"];
            ApiDomain.ScheduleEndpoint = Configuration["ApiDomain:ScheduleEndpoint"];
            ApiDomain.WorkflowEndpoint = Configuration["ApiDomain:WorkflowEndpoint"];
        }

    }
}
