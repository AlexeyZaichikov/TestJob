# Сборочный образ: SDK используется и для сборки, и для запуска.
# Исходный код монтируется из репозитория (./src => /app/src),
# поэтому при каждом старте контейнера выполняется сборка и запуск приложения.
FROM mcr.microsoft.com/dotnet/sdk:10.0

WORKDIR /app/src/TestJob.Api

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["/bin/sh", "-c", "dotnet build -c Release && dotnet run -c Release --no-build --no-launch-profile"]