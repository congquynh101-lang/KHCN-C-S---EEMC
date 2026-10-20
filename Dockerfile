# Ảnh Docker của EEMC Work Manager Web: chạy trên Synology Container Manager hoặc máy chủ Linux.
# Hỗ trợ amd64 và arm64 (biên dịch chéo, không cần giả lập khi build mã .NET).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
ARG VERSION=2.0.0
WORKDIR /src
COPY src/EEMC.WM.Core/EEMC.WM.Core.csproj src/EEMC.WM.Core/
COPY src/EEMC.WM.Data/EEMC.WM.Data.csproj src/EEMC.WM.Data/
COPY src/EEMC.WM.Web/EEMC.WM.Web.csproj src/EEMC.WM.Web/
RUN dotnet restore src/EEMC.WM.Web/EEMC.WM.Web.csproj -a $TARGETARCH
COPY src/EEMC.WM.Core/ src/EEMC.WM.Core/
COPY src/EEMC.WM.Data/ src/EEMC.WM.Data/
COPY src/EEMC.WM.Web/ src/EEMC.WM.Web/
RUN dotnet publish src/EEMC.WM.Web/EEMC.WM.Web.csproj -c Release -a $TARGETARCH --no-restore \
    -o /app -p:Version=$VERSION -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# Phông chữ cho báo cáo PDF tiếng Việt, múi giờ, curl để kiểm tra tình trạng
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-liberation fonts-dejavu-core tzdata curl \
    && rm -rf /var/lib/apt/lists/*
ENV TZ=Asia/Ho_Chi_Minh \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    EEMC_WM_DATA=/data
WORKDIR /app
COPY --from=build /app ./
# Toàn bộ dữ liệu (CSDL, tệp đính kèm, bản sao lưu, khóa đăng nhập, nhật ký) nằm trong /data
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "EEMC.WM.Web.dll"]
