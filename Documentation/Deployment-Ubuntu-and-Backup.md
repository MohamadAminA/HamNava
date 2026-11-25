# Ubuntu 22.04 / 24.04 Production Deployment & Backup Guide

## Prerequisites
- Ubuntu Server 22.04 LTS or 24.04 LTS with root/sudo access
- Domain `hamnavamusic.ir` DNS `A` record pointing to the server public IP
- Ports `80` and `443` open via UFW; SQL Server port `1433` **closed** to public internet (localhost/private network only)

## Step-by-Step Production Deployment (17 Steps)

1. **System Update & Base Packages:**
   ```bash
   sudo apt update && sudo apt upgrade -y
   sudo apt install -y curl wget gnupg2 nginx ffmpeg rsync ufw certbot python3-certbot-nginx
   ```
2. **Install .NET 10 SDK & ASP.NET Core Runtime:**
   Install the official Microsoft `.NET 10` package repository for Ubuntu 22.04/24.04 and install `aspnetcore-runtime-10.0` (plus `dotnet-sdk-10.0` on build hosts).
3. **Install SQL Server 2022 for Linux & `sqlcmd`:**
   Follow Microsoft's official Ubuntu SQL Server 2022 setup (`mssql-server`, `mssql-tools18`), bind SQL Server to `127.0.0.1` (`mssql-conf set network.ipaddress 127.0.0.1`), and create a dedicated least-privilege database user (`hamnava_app`) for `HamnavaDb`.
4. **Create Non-Root Service User & Directories:**
   ```bash
   sudo useradd --system --home /opt/hamnava --shell /usr/sbin/nologin hamnava
   sudo mkdir -p /opt/hamnava/releases /etc/hamnava /var/lib/hamnava/{media,temp,dataprotection-keys} /var/log/hamnava /var/backups/hamnava/sql
   sudo chown -R hamnava:hamnava /opt/hamnava /var/lib/hamnava /var/log/hamnava
   ```
5. **Configure Production Secrets (`/etc/hamnava/hamnava.env`):**
   Copy `/Hamnava/.env.example` to `/etc/hamnava/hamnava.env`, fill real SQL Server connection string, strong random `Security__OtpHashPepper`, Zarinpal MerchantId (`Production`), Pishgaman SMS credentials, and `Hamnava__InitialAdminMobile`. Lock permissions:
   ```bash
   sudo chown root:hamnava /etc/hamnava/hamnava.env
   sudo chmod 640 /etc/hamnava/hamnava.env
   ```
6. **Generate Initial EF Core Migration (First Release Only):**
   On your build machine with `.NET 10 SDK`:
   ```bash
   cd Hamnava
   dotnet tool install --global dotnet-ef
   dotnet ef migrations add InitialCreate --project Hamnava.DataLayer --startup-project Hamnava.Web
   ```
7. **Build & Publish Release Artifact:**
   ```bash
   chmod +x Deployment/scripts/*.sh
   ./Deployment/scripts/publish.sh
   ```
8. **Apply Idempotent SQL Migrations Manually (NEVER auto-migrate Production):**
   ```bash
   /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U hamnava_migrator -d HamnavaDb -i artifacts/publish/migrations.idempotent.sql
   ```
9. **Install systemd Service:**
   ```bash
   sudo cp Deployment/systemd/hamnava.service /etc/systemd/system/hamnava.service
   sudo systemctl daemon-reload
   sudo systemctl enable hamnava.service
   ```
10. **Deploy Timestamped Release:**
    ```bash
    ./Deployment/scripts/deploy.sh ./artifacts/publish
    ```
11. **Verify Local Health Endpoints:**
    ```bash
    curl -fsS http://127.0.0.1:5000/health/live
    curl -fsS http://127.0.0.1:5000/health/ready
    ```
12. **Configure Nginx Reverse Proxy:**
    ```bash
    sudo cp Deployment/nginx/hamnava.conf /etc/nginx/sites-available/hamnava.conf
    sudo ln -sf /etc/nginx/sites-available/hamnava.conf /etc/nginx/sites-enabled/hamnava.conf
    sudo rm -f /etc/nginx/sites-enabled/default
    sudo nginx -t && sudo systemctl reload nginx
    ```
13. **Provision Let's Encrypt TLS Certificate:**
    ```bash
    sudo certbot --nginx -d hamnavamusic.ir -d www.hamnavamusic.ir
    ```
14. **Configure Firewall (UFW):**
    ```bash
    sudo ufw allow OpenSSH
    sudo ufw allow 'Nginx Full'
    sudo ufw enable
    ```
15. **Schedule Daily Verified SQL Backups (cron):**
    ```bash
    0 2 * * * /opt/hamnava/current/Deployment/scripts/backup-database.sh >> /var/log/hamnava/backup.log 2>&1
    ```
16. **Verify Rollback Procedure:**
    Run `./Deployment/scripts/rollback.sh` whenever a deployment needs instant atomic reversion to the previous timestamped release.
17. **Restore Drill (Quarterly):**
    Test restoring the latest `.bak` file onto a staging SQL Server instance via `RESTORE DATABASE [HamnavaDb_RestoreTest] FROM DISK = ... WITH MOVE ...` to validate RPO/RTO.
