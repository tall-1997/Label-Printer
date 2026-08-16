param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
$publishRoot = (Resolve-Path $PublishDirectory).Path
$requiredFiles = @(
    "BarTenderPrinter.exe",
    "BarTenderPreviewHost.exe",
    "Deployment/workstation-client-contract.json",
    "Deployment/local-ledger-migrations.sql"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $publishRoot $relativePath
    if (!(Test-Path $path -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
}

$releaseFiles = @(Get-ChildItem $publishRoot -Recurse -File)
$forbiddenExtensions = @(
    ".pfx", ".p12", ".pem", ".key", ".cer", ".crt", ".der", ".p7b", ".p7c",
    ".snk", ".jks", ".keystore", ".db", ".sqlite", ".sqlite3", ".log"
)
$forbiddenNamePattern = "(?i)(MobileMes|HASP|Sentinel|SafeNet|Hardlock|Dongle)"
$forbiddenFiles = @($releaseFiles | Where-Object {
    $_.Name -match $forbiddenNamePattern -or $forbiddenExtensions -contains $_.Extension.ToLowerInvariant()
})

if ($forbiddenFiles) {
    $relativePaths = $forbiddenFiles | ForEach-Object { [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName) }
    throw "Forbidden release assets found: $($relativePaths -join ', ')"
}

$forbiddenContentName = "MobileMes"
$managedBinaries = @($releaseFiles | Where-Object { $_.Extension -in ".dll", ".exe" })
foreach ($binary in $managedBinaries) {
    $assemblyName = $null
    try {
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($binary.FullName).Name
    }
    catch [System.BadImageFormatException], [System.IO.FileLoadException] {
        # Native and mixed-mode binaries do not always expose managed assembly metadata.
    }

    $bytes = [System.IO.File]::ReadAllBytes($binary.FullName)
    $asciiContent = [System.Text.Encoding]::ASCII.GetString($bytes)
    $unicodeContent = [System.Text.Encoding]::Unicode.GetString($bytes)
    if ($assemblyName -match "(?i)$forbiddenContentName" -or
        $asciiContent -match "(?i)$forbiddenContentName" -or
        $unicodeContent -match "(?i)$forbiddenContentName") {
        $relativePath = [System.IO.Path]::GetRelativePath($publishRoot, $binary.FullName)
        throw "Forbidden DLL/EXE content name found: $relativePath"
    }
}

$textExtensions = @(".json", ".yaml", ".yml", ".txt", ".xml", ".config", ".ini", ".env", ".sql")
$configurationFiles = @($releaseFiles | Where-Object {
    $_.Extension.ToLowerInvariant() -in $textExtensions -or $_.Name -like ".env*"
})
$sensitiveKey = "Password|Token|ApiKey|Secret|ClientSecret|AccessToken|PrivateKey|Credential"
$sensitiveValuePatterns = @(
    ('(?im)(?<![A-Za-z0-9_])(?:["''`]?)(?:{0})(?:["''`]?)\s*[:=]\s*(?<value>[^\r\n,;#]+)' -f $sensitiveKey),
    ('(?is)<(?:{0})(?:\s[^>]*)?>(?<value>.*?)</(?:{0})\s*>' -f $sensitiveKey),
    ('(?is)\b(?:key|name)\s*=\s*["''`](?:{0})["''`][^>]*\bvalue\s*=\s*["''`](?<value>.*?)["''`]' -f $sensitiveKey),
    ('(?is)\bvalue\s*=\s*["''`](?<value>.*?)["''`][^>]*\b(?:key|name)\s*=\s*["''`](?:{0})["''`]' -f $sensitiveKey)
)
$projectPlaceholderPattern = '^\$\{PROJECT_[A-Za-z0-9_]+\}$'
$pemPrivateKeyPattern = '(?im)-----BEGIN(?: [A-Z0-9]+)* PRIVATE KEY-----'

foreach ($configurationFile in $configurationFiles) {
    $content = Get-Content $configurationFile.FullName -Raw
    $relativePath = [System.IO.Path]::GetRelativePath($publishRoot, $configurationFile.FullName)

    if ($content -match $pemPrivateKeyPattern) {
        throw "Configuration contains a PEM private key: $relativePath"
    }

    foreach ($pattern in $sensitiveValuePatterns) {
        foreach ($match in [regex]::Matches($content, $pattern)) {
            $value = $match.Groups["value"].Value.Trim().Trim([char[]]@([char]0x22, [char]0x27, [char]0x60))
            $isProjectPlaceholder = [regex]::IsMatch($value, $projectPlaceholderPattern)
            if ($value -and !$isProjectPlaceholder) {
                throw "Configuration contains a non-placeholder sensitive value: $relativePath"
            }
        }
    }
}

Write-Host "Release artifact validation passed for $publishRoot"
