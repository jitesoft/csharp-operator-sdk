FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app
COPY . ./

RUN dotnet restore
RUN dotnet publish samples/basic/k8s.Operators.Samples.Basic.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "k8s.Operators.Samples.Basic.dll", "--debug"]
