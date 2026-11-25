[CmdletBinding()]
param(
    [Parameter(Mandatory)] [uri] $ApiBase,
    [Parameter(Mandatory)] [string] $ProviderId,
    [Parameter(Mandatory)] [string] $SamplePath,
    [string] $BaselinePath
)

$ErrorActionPreference = 'Stop'
if (!$ApiBase.IsAbsoluteUri -or $ApiBase.Scheme -ne 'https' -or
    $ApiBase.UserInfo -or $ApiBase.Query -or $ApiBase.Fragment) {
    throw 'ApiBase must be an HTTPS URL without credentials, a query, or a fragment.'
}

if ([string]::IsNullOrWhiteSpace($env:BIBLIOTHECA_BENCHMARK_ACCESS_TOKEN)) {
    throw 'Set BIBLIOTHECA_BENCHMARK_ACCESS_TOKEN to a Bibliotheca access token.'
}

function ConvertTo-CanonicalIsbn([string] $Value) {
    $isbn = ($Value -replace '[ -]', '').ToUpperInvariant()
    if ($isbn -match '^[0-9]{9}[0-9X]$') {
        $prefix = '978' + $isbn.Substring(0, 9)
        $sum = 0
        for ($i = 0; $i -lt $prefix.Length; $i++) {
            $sum += ([int]$prefix[$i] - [int][char]'0') * (1 + 2 * ($i % 2))
        }
        return $prefix + ((10 - $sum % 10) % 10)
    }
    return $isbn
}

$sample = @(Get-Content -Raw -LiteralPath $SamplePath | ConvertFrom-Json)
if ($sample.Count -ne 10 -or @($sample | Where-Object {
    !$_.title -or !$_.author -or (ConvertTo-CanonicalIsbn $_.isbn) -notmatch '^97[89][0-9]{10}$'
}).Count) {
    throw 'The sample must contain ten records with title, author and ISBN.'
}

$sampleHash = (Get-FileHash -LiteralPath $SamplePath -Algorithm SHA256).Hash
$baseline = if ($BaselinePath) { Get-Content -Raw -LiteralPath $BaselinePath | ConvertFrom-Json }
if ($baseline -and ($baseline.sampleSha256 -ne $sampleHash -or @($baseline.queries).Count -ne 10)) {
    throw 'The baseline must be a report for the same sample file.'
}

$api = $ApiBase.AbsoluteUri.TrimEnd('/') + '/api/external-editions'
$request = @{
    Authentication = 'Bearer'
    Token = (ConvertTo-SecureString $env:BIBLIOTHECA_BENCHMARK_ACCESS_TOKEN -AsPlainText -Force)
    MaximumRedirection = 0
    TimeoutSec = 120
}
$active = @(Invoke-RestMethod @request -Uri "$api/providers" | ForEach-Object { $_ } |
    Where-Object { $_.state -eq 'active' -and $_.available })
if ($active.Count -ne 1 -or $active[0].id -cne $ProviderId) {
    throw 'Enable only the chosen provider on the benchmark instance before running the sample.'
}

$queryIndex = 0
$rows = foreach ($book in $sample) {
    $queryIndex++
    $title = [uri]::EscapeDataString($book.title)
    $author = [uri]::EscapeDataString($book.author)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $result = Invoke-RestMethod @request -Uri "$api/search?title=$title&author=$author"
        $elapsed = $clock.ElapsedMilliseconds
    } catch {
        [pscustomobject]@{ query = $queryIndex; exactRank = $null; elapsedMs = $clock.ElapsedMilliseconds; failed = $true }
        continue
    }
    $expected = ConvertTo-CanonicalIsbn $book.isbn
    $items = @($result.items)
    $rank = $null
    for ($i = 0; $i -lt $items.Count; $i++) {
        if ((ConvertTo-CanonicalIsbn $items[$i].isbn) -eq $expected) {
            $rank = $i + 1
            break
        }
    }
    [pscustomobject]@{
        query = $queryIndex
        exactRank = $rank
        elapsedMs = $elapsed
        failed = $ProviderId -cin @($result.failedProviders)
    }
}

$times = @($rows.elapsedMs | Sort-Object)
$additionalMatches = if ($baseline) {
    @($rows | Where-Object { $_.exactRank -and !$baseline.queries[$_.query - 1].exactRank }).Count
}
[pscustomobject]@{
    providerId = $ProviderId
    sampleSha256 = $sampleHash
    exactMatches = @($rows | Where-Object exactRank).Count
    additionalMatches = $additionalMatches
    medianElapsedMs = ($times[4] + $times[5]) / 2
    failedQueries = @($rows | Where-Object failed).Count
    queries = $rows
} | ConvertTo-Json -Depth 4
