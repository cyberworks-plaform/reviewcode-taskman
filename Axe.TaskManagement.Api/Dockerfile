FROM alpine:3.9.4

# Add some libs required by .NET runtime 
# https://github.com/dotnet/core/blob/master/Documentation/build-and-install-rhel6-prerequisites.md#troubleshooting
RUN apk add --no-cache \ 
    openssh libunwind \
    nghttp2-libs libidn krb5-libs libuuid lttng-ust zlib \
    libstdc++ libintl \
    icu

EXPOSE 80
EXPOSE 443
ENV ASPNETCORE_URLS http://*:80
ENV ServiceCode AxeTaskManagement
ENV Tokens_Key 0123456789ABCDEF
ENV Tokens_Issuer http://ce.com.vn
ENV Tokens_Audience http://ce-axe.com.vn
ENV ConnectionStrings__AxeTaskmanagementDbConnection connectionStrings
ENV ConnectionStrings__ProviderName Npgsql
ENV EnableSeedData true
ENV UseDebugMode false
ENV UseLog true
ENV UseLogAction false
ENV UseLogCrud false
ENV UseLogWorkflow true
ENV Swagger__UseSwagger true
ENV Swagger__ShowHostName true
ENV AutoMigration true
ENV Mongo__ConnectionString mongodb://host.docker.internal:27017
ENV Mongo__DatabaseName Axe_Dev
ENV Cache__UseCache true
ENV Cache__Type redis
ENV Cache__CacheConnection=""
ENV Cache__Host host.docker.internal
ENV Cache__Port 6379
ENV Cache__DefaultDatabase=""
ENV RabbitMq__UseRabbitMq true
ENV RabbitMq__ConnectionStrings__EventBusHostName host.docker.internal
ENV RabbitMq__ConnectionStrings__Port 5672
ENV RabbitMq__ConnectionStrings__EventBusUserName guest
ENV RabbitMq__ConnectionStrings__EventBusPassword guest
ENV RabbitMq__ConnectionStrings__VirtualHost="/"
ENV RabbitMq__EventBusRetryCount 5
ENV RabbitMq__PrefetchCount 1000
ENV RabbitMq__DefaultExchange microservice_event_bus
ENV ApiDomain__AuthEndpoint http://host.docker.internal:10281/api/auth
ENV ApiDomain__AxeCoreEndpoint http://host.docker.internal:10292/api/axe-core
ENV ApiDomain__AxeTaskManagementEndpoint http://host.docker.internal:10293/api/axe-task-management
ENV ApiDomain__AxeReportEndpoint http://host.docker.internal:10294/api/axe-report
ENV ApiDomain__AxePaymentEndpoint http://host.docker.internal:10295/api/axe-payment
ENV ApiDomain__CommonMasterDataEndpoint http://host.docker.internal:10284/api/common-master-data
ENV ApiDomain__FileEndpoint http://host.docker.internal:10282/api/file
ENV ApiDomain__OcrEndpoint http://host.docker.internal:10283/api/ocr
ENV ApiDomain__LogEndpoint http://host.docker.internal:10285/api/log
ENV ApiDomain__NotificationEndpoint http://host.docker.internal:10286/api/notification
ENV ApiDomain__ScheduleEndpoint http://host.docker.internal:10287/api/schedule
ENV ApiDomain__WorkflowEndpoint http://host.docker.internal:10288/api/workflow

# Copy 
WORKDIR /app
COPY ./bin/Release/net6.0/publish ./

ENTRYPOINT ["./Axe.TaskManagement.Api", "--urls", "http://0.0.0.0:80"]