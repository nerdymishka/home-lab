# TODO convert to pulumi code

$processes = Get-Process docker -EA SilentlyContinue
if(!$processes)
{
    & "C:\Program Files\Docker\Docker\Docker Desktop.exe"
}

$mods = @(
    "Microsoft.PowerShell.SecretManagement",
    "Microsoft.PowerShell.SecretStore"
)

foreach($m in $mods)
{
    $set = Get-module $m -ListAvailable -EA SilentlyContinue
    if($null -eq $set)
    {
        Install-Module $m -Force -SkipPublisherCheck
    }
}

# TODO: created self-signed cert & use it to encrypt/decrypt vault password
if(!(Test-SecretVault -Name "Docker" -EA SilentlyContinue))
{
    Register-SecretVault -Name "Docker"  -ModuleName Microsoft.PowerShell.SecretStore -DefaultVault
    Set-SecretStoreConfiguration -Scope CurrentUser -Authentication None -PasswordTimeout -1 -Interaction None -Confirm:$false 
}

function New-Password()
{
    ([char[]]([char]33..[char]95) + ([char[]]([char]97..[char]126)) + 0..9 | Sort-Object {Get-Random})[0..20] -join ''
}

$secrets = @(
    "MSSQL_DEV_SA_PASSWORD",
    "MYSQL_DEV_ROOT_PASSWORD"
    "MYSQL_DEV_PASSWORD",
    "REDIS_DEV_PASSWORD",
    "POSTGRES_DEV_PASSWORD"
)

foreach($name in $secrets)
{
    $value = Get-Secret -Name $name -Vault "Docker" -AsPlainText -EA SilentlyContinue
    if($null -eq $value)
    {
        $value = New-Password
        $value = $value.Replace("'", "#").Replace(";", "@").Replace("`"", "!").Replace("&", "_").Replace("%", "-");
        Set-Secret -Name $name -Vault "Docker" -Secret $value
    }

    Set-Item "Env:\$name" -Value $value 
}



& docker network inspect "nm-config-plane-vnet"

if($LASTEXITCODE -ne 0)
{
    docker network create `
        --driver=bridge `
        --subnet=172.2.0.0/16 `
        --gateway=172.2.0.1 `
        nm-config-plane-vnet 
}

& docker network inspect "nm-backend-vnet"

if($LASTEXITCODE -ne 0)
{
    docker network create `
        --driver=bridge `
        --subnet=172.3.0.0/16 `
        --gateway=172.3.0.1 `
        nm-backend-vnet 
}

& docker network inspect "nm-frontend-vnet"

if($LASTEXITCODE -ne 0)
{
    docker network create `
        --driver=bridge `
        --subnet=172.4.0.0/16 `
        --gateway=172.4.0.1 `
        nm-frontend-vnet 
}

$d = "c:/apps/docker"
$env:DOCKER_DATA = "$d/var"
$env:DOCKER_LOG = "$d/var/log"
$env:DOCKER_ETC = "$d/etc"

$directories = @(
    "$d",
    "$d/var",
    "$d/var/log",
    "$d/etc"
)

foreach($directory in $directories)
{
    if(!(Test-Path $directory))
    {
        New-Item $directory -ItemType Directory
    }
}

docker-compose.exe -p ectd-dev -f "$PSScriptRoot/dev/etcd/docker-compose.dev.yml" up -d 
docker-compose.exe -p vault-dev -f "$PSScriptRoot/dev/vault/docker-compose.dev.yml" up -d 