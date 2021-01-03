FROM microsoft/dotnet:2.1-sdk as sdk
WORKDIR /app
COPY src/FsTweet.Web .
RUN dotnet publish FsTweet.Web.fsproj -c Release

FROM microsoft/dotnet:2.1-runtime as runtime
WORKDIR /app
COPY --from=sdk /app/bin/Release/netcoreapp2.1/publish .
CMD dotnet FsTweet.Web.dll
