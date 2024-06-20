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

namespace Axe.TaskManagement.MockApi
{
    public class Startup : BaseStartup
    {
        public Startup(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void AddCustomService(IServiceCollection services)
        {
            // Api domain
            BuildApiDomain();

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

            // Config EvenHandling
            if (Configuration.GetValue("RabbitMq:UseRabbitMq", false))
            {
                services.AddTransient<TaskIntegrationEventHandler>();
            }

            base.AddCustomService(services);
        }

        protected override void UseCustomService(IApplicationBuilder app, IWebHostEnvironment env)
        {
            Task.Run(async () => await SeedData(app));

            if (Configuration.GetValue("RabbitMq:UseRabbitMq", false))
            {
                var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();

                // Mapping event bus event with the handler to handling
                eventBus.Subscribe<TaskEvent, TaskIntegrationEventHandler>(nameof(TaskEvent).ToLower());
            }

            base.UseCustomService(app, env);
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
