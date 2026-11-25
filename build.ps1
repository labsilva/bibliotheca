param(
    [string]$Registry = '',
    [switch]$Web,
    [switch]$Api,
    [switch]$Playwright,
    [switch]$App,
    # Use only for a registry that is explicitly served over plain HTTP.
    [switch]$RegistryHttp
)

$ErrorActionPreference = 'Stop'
$builderArgs = @()
if ($RegistryHttp -and ($Web -or $Api -or $Playwright -or $App)) {
    if ([string]::IsNullOrWhiteSpace($Registry) -or $Registry -match '^[a-zA-Z][a-zA-Z0-9+.-]*://') {
        throw 'For -RegistryHttp, provide a registry without a URL scheme, e.g. -Registry registry.example.org:5000/.'
    }
    $registryUri = $null
    if (-not [Uri]::TryCreate("http://$Registry", [UriKind]::Absolute, [ref]$registryUri) -or
        -not $registryUri.Host -or $registryUri.UserInfo -or $registryUri.Query -or $registryUri.Fragment) {
        throw 'Invalid registry address.'
    }
    # A separate builder keeps the existing builder and its registry policy unchanged.
    $registryHost = $registryUri.Authority.ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($registryHost))).Replace('-', '').Substring(0, 12).ToLowerInvariant()
    } finally { $sha.Dispose() }
    $builder = "bibliotheca-http-$digest"
    & docker buildx inspect $builder 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $configFile = [System.IO.Path]::GetTempFileName()
        try {
            $config = "[registry.`"$registryHost`"]`n  http = true`n"
            [System.IO.File]::WriteAllText($configFile, $config, [System.Text.UTF8Encoding]::new($false))
            & docker buildx create --name $builder --driver docker-container --buildkitd-config $configFile | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not create the HTTP registry builder (exit code $LASTEXITCODE)." }
        } finally { Remove-Item -LiteralPath $configFile -Force -ErrorAction SilentlyContinue }
    }
    $builderArgs = @('--builder', $builder)
    Write-Host "Using HTTP registry $registryHost with builder $builder"
}

function Docker-CreateImage {
    param(
        [string]$Registry = '',
        [Parameter(Mandatory = $true)][string]$Dockerfile,
        [Parameter(Mandatory = $true)][string]$ImageName,
        [Parameter(Mandatory = $true)][string]$ImageVersion,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $tag = "${Registry}${ImageName}:${ImageVersion}"
    Write-Host "Creating Docker Image from $Dockerfile"
    Write-Host "docker buildx build $($builderArgs -join ' ') --platform linux/amd64,linux/arm64 -t $tag --push -f $Dockerfile $Path" -ForegroundColor Cyan
    & docker buildx build @builderArgs --platform 'linux/amd64,linux/arm64' -t $tag --push -f $Dockerfile $Path
    if ($LASTEXITCODE -ne 0) { throw "Image build and push failed with exit code $LASTEXITCODE." }
}

if ($Web) {
    Docker-CreateImage -Dockerfile (Join-Path $PSScriptRoot 'docker/web/Dockerfile') -Registry $Registry -ImageName 'bibliotheca-web' -ImageVersion 'latest' -Path $PSScriptRoot
}
if ($Api) {
    Docker-CreateImage -Dockerfile (Join-Path $PSScriptRoot 'docker/api/Dockerfile') -Registry $Registry -ImageName 'bibliotheca-api' -ImageVersion 'latest' -Path $PSScriptRoot
}
if ($Playwright) {
    $browserContext = Join-Path $PSScriptRoot 'docker/playwright'
    Docker-CreateImage -Dockerfile (Join-Path $browserContext 'Dockerfile') -Registry $Registry -ImageName 'bibliotheca-playwright' -ImageVersion '1.65.1' -Path $browserContext
}
if ($App) {
    Docker-CreateImage -Dockerfile (Join-Path $PSScriptRoot 'docker/app/Dockerfile') -Registry $Registry -ImageName 'bibliotheca-app' -ImageVersion 'latest' -Path $PSScriptRoot
}
