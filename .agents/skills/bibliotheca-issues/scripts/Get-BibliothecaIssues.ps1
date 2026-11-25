[CmdletBinding()]
param (
    [ValidateRange(1, [int]::MaxValue)]
    [int] $Number
)

$ErrorActionPreference = 'Stop'
$repo = 'labsilva/bibliotheca'
$baseUrl = "https://api.github.com/repos/$repo/issues"

$headers = @{ Accept = 'application/vnd.github+json' }
$token = $env:GH_TOKEN
if (!$token) { $token = $env:GITHUB_TOKEN }
if (!$token) {
    $previousPrompt = $env:GIT_TERMINAL_PROMPT
    $previousInteractive = $env:GCM_INTERACTIVE
    try {
        $env:GIT_TERMINAL_PROMPT = '0'
        $env:GCM_INTERACTIVE = 'never'
        $credential = "protocol=https`nhost=github.com`npath=$repo`n`n" | git credential fill 2>$null
        if ($LASTEXITCODE -eq 0) {
            $token = ($credential | Where-Object { $_.StartsWith('password=') } | Select-Object -First 1) -replace '^password=', ''
        }
    } finally {
        $env:GIT_TERMINAL_PROMPT = $previousPrompt
        $env:GCM_INTERACTIVE = $previousInteractive
    }
}
if ($token) { $headers.Authorization = "Bearer $token" }

function Get-GitHubPages([string] $Url) {
    $separator = if ($Url.Contains('?')) { '&' } else { '?' }
    for ($page = 1; ; $page++) {
        $response = Invoke-RestMethod -Headers $headers -Uri "${Url}${separator}per_page=100&page=$page"
        foreach ($item in $response) { $item }
        if ($response.Count -lt 100) { break }
    }
}

if ($Number) {
    $issue = Invoke-RestMethod -Headers $headers -Uri "$baseUrl/$Number"
    if ($issue.pull_request) { throw "#$Number is a pull request." }
    [ordered]@{
        issue = $issue | Select-Object number, title, state, html_url, body
        comments = @(Get-GitHubPages "$baseUrl/$Number/comments" | Select-Object body, html_url)
        subIssues = @(Get-GitHubPages "$baseUrl/$Number/sub_issues" | Select-Object number, title, state, html_url)
    } | ConvertTo-Json -Depth 6
} else {
    $issues = @(Get-GitHubPages "${baseUrl}?state=open" |
        Where-Object { !$_.pull_request } | Sort-Object number |
        Select-Object number, title, state, html_url, body, parent_issue_url,
            @{ name = 'labels'; expression = { @($_.labels.name) } })
    ConvertTo-Json -InputObject $issues -Depth 6
}
