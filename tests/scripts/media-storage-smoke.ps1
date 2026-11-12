#Requires -Version 5.1
<#
.SYNOPSIS
Real HTTP smoke checks for private media storage in a disposable local stack.
.DESCRIPTION
Requires ENGLISHCENTER_SMOKE_API_URL (API origin, for example http://127.0.0.1:18080),
ENGLISHCENTER_SMOKE_ADMIN_EMAIL and ENGLISHCENTER_SMOKE_ADMIN_PASSWORD.
Run with: powershell.exe -NoProfile -File tests/scripts/media-storage-smoke.ps1
Run ONLY against the caller's isolated, disposable stack. Creates two random
students and several files; deliberately does not delete users, files or volumes.
Both the API and signed storage URLs must be loopback HTTP(S) endpoints. Redirects
are disabled. Credentials, tokens, response bodies and signed URLs are not logged.
No build, Docker command, jq, external module or pre-created student is required.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$VerbosePreference = 'SilentlyContinue'
$DebugPreference = 'SilentlyContinue'
$script:Stage = 'initialization'
$script:SafeFailure = $null
$script:Client = $null
$handler = $null

function Stop-Smoke {
    param([string] $Message)
    $script:SafeFailure = $Message
    throw $Message
}

function Assert-Smoke {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { Stop-Smoke $Message }
}

function Assert-Status {
    param($Response, [int] $Expected)
    if ($Response.Status -ne $Expected) {
        Stop-Smoke ('Expected HTTP {0}; received HTTP {1}.' -f $Expected, $Response.Status)
    }
}

function Get-LoopbackUri {
    param([string] $Value)
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref] $uri)) {
        Stop-Smoke 'An absolute loopback HTTP(S) endpoint is required.'
    }
    $address = $null
    $hostName = $uri.DnsSafeHost.Trim('[', ']')
    $local = $hostName -eq 'localhost'
    if ([System.Net.IPAddress]::TryParse($hostName, [ref] $address)) {
        $local = [System.Net.IPAddress]::IsLoopback($address)
    }
    Assert-Smoke ($local -and $uri.Scheme -in @('http', 'https') -and
        [string]::IsNullOrEmpty($uri.UserInfo) -and [string]::IsNullOrEmpty($uri.Fragment)) `
        'Only loopback HTTP(S) endpoints without user-info or fragments are permitted.'
    return $uri
}

function Invoke-SmokeHttp {
    param(
        [string] $Method,
        [Uri] $Uri,
        [string] $Token = '',
        [AllowNull()] $Json = $null,
        [AllowNull()] [byte[]] $Bytes = $null,
        [AllowNull()] $RequiredHeaders = $null
    )
    $request = $null
    $response = $null
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Uri)
        if (-not [string]::IsNullOrEmpty($Token)) {
            $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
        }
        if ($null -ne $Json) {
            $body = ConvertTo-Json -InputObject $Json -Depth 8 -Compress
            $request.Content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, 'application/json')
        }
        elseif ($null -ne $Bytes) {
            $request.Content = [System.Net.Http.ByteArrayContent]::new($Bytes)
        }
        if ($null -ne $RequiredHeaders) {
            foreach ($header in $RequiredHeaders.PSObject.Properties) {
                # Content headers belong to HttpContent, not HttpRequestHeaders.
                if ($header.Name.StartsWith('Content-', [StringComparison]::OrdinalIgnoreCase)) {
                    Assert-Smoke ($null -ne $request.Content) 'Required content headers need a request body.'
                    [void] $request.Content.Headers.Remove($header.Name)
                    $added = $request.Content.Headers.TryAddWithoutValidation($header.Name, [string] $header.Value)
                }
                else {
                    $added = $request.Headers.TryAddWithoutValidation($header.Name, [string] $header.Value)
                }
                Assert-Smoke $added 'A required signed-request header could not be applied.'
            }
        }
        # Never use EnsureSuccessStatusCode: negative HTTP statuses are assertions.
        # Network, timeout and TLS errors propagate to the sanitized, failing exit.
        $response = $script:Client.SendAsync($request).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        return [pscustomobject] @{ Status = [int] $response.StatusCode; Bytes = $content }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        if ($null -ne $request) { $request.Dispose() }
    }
}

function Invoke-SmokeApi {
    param([string] $Method, [string] $Path, [string] $Token = '', [AllowNull()] $Json = $null)
    $uri = [Uri]::new($script:ApiOrigin + $Path)
    return Invoke-SmokeHttp -Method $Method -Uri $uri -Token $Token -Json $Json
}

function Read-SmokeJson {
    param($Response)
    return ConvertFrom-Json -InputObject ([System.Text.Encoding]::UTF8.GetString($Response.Bytes))
}

function Get-SmokeToken {
    param([string] $Email, [string] $Password)
    $response = Invoke-SmokeApi -Method POST -Path '/api/identity/auth/login' -Json @{
        email = $Email; password = $Password
    }
    Assert-Status $response 200
    $session = Read-SmokeJson $response
    Assert-Smoke (-not [string]::IsNullOrWhiteSpace($session.accessToken)) 'Login did not return an access token.'
    return [string] $session.accessToken
}

function New-SmokePdf {
    param([string] $Text)
    # A small, self-contained one-page PDF with correct ASCII byte offsets/xref.
    $stream = "BT /F1 12 Tf 20 100 Td ($Text) Tj ET`n"
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>',
        ("<< /Length {0} >>`nstream`n{1}endstream" -f $stream.Length, $stream),
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    )
    $pdf = [System.Text.StringBuilder]::new()
    [void] $pdf.Append("%PDF-1.4`n")
    $offsets = @()
    for ($i = 0; $i -lt $objects.Count; $i++) {
        $offsets += $pdf.Length
        [void] $pdf.Append(("{0} 0 obj`n{1}`nendobj`n" -f ($i + 1), $objects[$i]))
    }
    $xref = $pdf.Length
    [void] $pdf.Append("xref`n0 6`n0000000000 65535 f `n")
    foreach ($offset in $offsets) {
        [void] $pdf.Append(("{0:D10} 00000 n `n" -f $offset))
    }
    [void] $pdf.Append(("trailer`n<< /Size 6 /Root 1 0 R >>`nstartxref`n{0}`n%%EOF`n" -f $xref))
    return ,([System.Text.Encoding]::ASCII.GetBytes($pdf.ToString()))
}

function New-SmokeUpload {
    param([string] $Token, [long] $Size)
    $response = Invoke-SmokeApi -Method POST -Path '/api/media-notification/files/presign' -Token $Token -Json @{
        fileName = 'lesson.pdf'; contentType = 'application/pdf'; sizeBytes = $Size; purpose = 'submission'
    }
    Assert-Status $response 200
    $ticket = Read-SmokeJson $response
    $id = [Guid]::Empty
    Assert-Smoke ([Guid]::TryParse([string] $ticket.fileId, [ref] $id) -and $id -ne [Guid]::Empty) `
        'Upload ticket must contain a nonempty file ID.'
    Assert-Smoke (-not [string]::IsNullOrWhiteSpace($ticket.fileKey)) 'Upload ticket must contain a staging key.'
    Assert-Smoke ($ticket.method -ceq 'PUT') 'Upload ticket must specify PUT.'
    Assert-Smoke ($ticket.requiredHeaders.'Content-Type' -ceq 'application/pdf') `
        'Upload ticket must require the declared content type.'
    Assert-Smoke ([DateTimeOffset]::Parse($ticket.expiresAt) -gt [DateTimeOffset]::UtcNow) `
        'Upload ticket must not already be expired.'
    [void] (Get-LoopbackUri $ticket.url)
    return $ticket
}

function Send-SmokeUpload {
    param($Ticket, [byte[]] $Bytes)
    $response = Invoke-SmokeHttp -Method $Ticket.method -Uri (Get-LoopbackUri $Ticket.url) `
        -Bytes $Bytes -RequiredHeaders $Ticket.requiredHeaders
    Assert-Status $response 200
}

function Assert-DownloadedBytes {
    param([string] $Token, [string] $FileId, [byte[]] $Expected)
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $FileId) -Token $Token
    Assert-Status $response 200
    $ticket = Read-SmokeJson $response
    Assert-Smoke ([DateTimeOffset]::Parse($ticket.expiresAt) -gt [DateTimeOffset]::UtcNow) `
        'Download ticket must not already be expired.'
    $uri = Get-LoopbackUri $ticket.url
    $download = Invoke-SmokeHttp -Method GET -Uri $uri
    Assert-Status $download 200
    Assert-Smoke ($download.Bytes.Length -eq $Expected.Length) 'Downloaded byte count differs from original.'
    for ($i = 0; $i -lt $Expected.Length; $i++) {
        if ($download.Bytes[$i] -ne $Expected[$i]) { Stop-Smoke 'Downloaded content differs from original.' }
    }
    return $uri
}

function Assert-ForeignAccessDenied {
    param([string] $Token, [string] $FileId)
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $FileId) -Token $Token
    Assert-Status $response 404
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $FileId) -Token $Token
    Assert-Status $response 404
}

try {
    Add-Type -AssemblyName System.Net.Http
    $script:Stage = 'loopback-only disposable-stack preconditions'
    Assert-Smoke (-not [string]::IsNullOrWhiteSpace($env:ENGLISHCENTER_SMOKE_API_URL)) `
        'Set ENGLISHCENTER_SMOKE_API_URL to the isolated local API origin.'
    Assert-Smoke (-not [string]::IsNullOrWhiteSpace($env:ENGLISHCENTER_SMOKE_ADMIN_EMAIL) -and
        -not [string]::IsNullOrWhiteSpace($env:ENGLISHCENTER_SMOKE_ADMIN_PASSWORD)) `
        'Set ENGLISHCENTER_SMOKE_ADMIN_EMAIL and ENGLISHCENTER_SMOKE_ADMIN_PASSWORD.'
    $api = Get-LoopbackUri $env:ENGLISHCENTER_SMOKE_API_URL
    Assert-Smoke ($api.AbsolutePath -eq '/' -and [string]::IsNullOrEmpty($api.Query)) `
        'The API URL must be an origin without a path or query.'
    $script:ApiOrigin = $api.GetLeftPart([UriPartial]::Authority)
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseProxy = $false
    $script:Client = [System.Net.Http.HttpClient]::new($handler, $false)
    $script:Client.Timeout = [TimeSpan]::FromSeconds(30)
    $script:Client.MaxResponseContentBufferSize = 2 * 1024 * 1024

    $script:Stage = 'two distinct Student identities'
    $admin = Get-SmokeToken $env:ENGLISHCENTER_SMOKE_ADMIN_EMAIL $env:ENGLISHCENTER_SMOKE_ADMIN_PASSWORD
    $students = @()
    foreach ($number in @(1, 2)) {
        $email = 'media-smoke-' + [Guid]::NewGuid().ToString('N') + '@example.invalid'
        $password = 'Smoke!9-' + [Guid]::NewGuid().ToString('N')
        $response = Invoke-SmokeApi -Method POST -Path '/api/identity/users' -Token $admin -Json @{
            email = $email; fullName = ('Media smoke student {0}' -f $number); password = $password; role = 'Student'
        }
        Assert-Status $response 201
        $user = Read-SmokeJson $response
        Assert-Smoke ($user.role -ceq 'Student') 'Created identity must have Student role.'
        $students += [pscustomobject] @{ Id = [Guid] $user.id; Token = (Get-SmokeToken $email $password) }
    }
    Assert-Smoke ($students[0].Id -ne [Guid]::Empty -and $students[1].Id -ne [Guid]::Empty -and
        $students[0].Id -ne $students[1].Id) 'Students must have distinct, nonempty IDs.'
    $owner = $students[0].Token
    $other = $students[1].Token
    Write-Output 'PASS distinct students created and authenticated'

    $original = New-SmokePdf 'original'
    $changed = New-SmokePdf 'modified'
    Assert-Smoke ($original.Length -eq $changed.Length) 'Replay fixture must have the same byte length.'
    $script:Stage = 'unauthenticated media requests'
    $response = Invoke-SmokeApi -Method POST -Path '/api/media-notification/files/presign' -Json @{
        fileName = 'lesson.pdf'; contentType = 'application/pdf'; sizeBytes = $original.Length; purpose = 'submission'
    }
    Assert-Status $response 401
    $unknown = [Guid]::NewGuid().ToString()
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $unknown)
    Assert-Status $response 401
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $unknown)
    Assert-Status $response 401
    Write-Output 'PASS unauthenticated presign, confirm and download return 401'

    $script:Stage = 'unsupported format'
    $response = Invoke-SmokeApi -Method POST -Path '/api/media-notification/files/presign' -Token $owner -Json @{
        fileName = 'lesson.exe'; contentType = 'application/octet-stream'; sizeBytes = $original.Length; purpose = 'submission'
    }
    Assert-Status $response 400
    Write-Output 'PASS unsupported format returns 400'

    $script:Stage = 'owner presign and pending privacy'
    $upload = New-SmokeUpload $owner $original.Length
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $upload.fileId) -Token $owner
    Assert-Status $response 409
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $upload.fileId) -Token $owner
    Assert-Status $response 409
    Assert-ForeignAccessDenied $other $upload.fileId
    Assert-ForeignAccessDenied $admin $upload.fileId
    Write-Output 'PASS pending download and absent-upload confirm return 409; nonowners receive 404'

    $script:Stage = 'signed PUT, owner confirm and byte-exact download'
    Send-SmokeUpload $upload $original
    # An object arriving in storage must still not be downloadable before confirm.
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $upload.fileId) -Token $owner
    Assert-Status $response 409
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $upload.fileId) -Token $owner
    Assert-Status $response 200
    $accepted = Read-SmokeJson $response
    Assert-Smoke ($accepted.fileId -eq $upload.fileId -and $accepted.fileName -ceq 'lesson.pdf' -and
        $accepted.contentType -ceq 'application/pdf' -and $accepted.sizeBytes -eq $original.Length -and
        -not [string]::IsNullOrWhiteSpace($accepted.fileKey) -and $accepted.fileKey -cne $upload.fileKey) `
        'Confirmation must return matching metadata and a final key distinct from staging.'
    $downloadUri = Assert-DownloadedBytes $owner $upload.fileId $original
    Write-Output 'PASS signed PUT with required headers, owner confirm and byte-exact signed GET'

    $script:Stage = 'other Student and Admin owner isolation'
    Assert-ForeignAccessDenied $other $upload.fileId
    Assert-ForeignAccessDenied $admin $upload.fileId
    Write-Output 'PASS other Student and Admin confirm/download return 404'

    $script:Stage = 'unsigned final object and tampered signature'
    $unsigned = [UriBuilder]::new($downloadUri)
    $unsigned.Query = ''
    $response = Invoke-SmokeHttp -Method GET -Uri $unsigned.Uri
    Assert-Status $response 403
    $signed = $downloadUri.AbsoluteUri
    $signature = [regex]::Match($signed, '(?i)([?&]X-Amz-Signature=)([0-9a-f]+)')
    Assert-Smoke $signature.Success 'Download ticket must contain an S3 signature.'
    $index = $signature.Groups[2].Index
    $replacement = '0'
    if ($signed[$index] -eq '0') { $replacement = '1' }
    $tampered = $signed.Remove($index, 1).Insert($index, $replacement)
    $response = Invoke-SmokeHttp -Method GET -Uri ([Uri]::new($tampered))
    Assert-Status $response 403
    Write-Output 'PASS unsigned final object and tampered signed GET return 403'

    $script:Stage = 'staging replay cannot mutate accepted content'
    Send-SmokeUpload $upload $changed
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $upload.fileId) -Token $owner
    Assert-Status $response 200
    $reconfirmed = Read-SmokeJson $response
    Assert-Smoke ($reconfirmed.fileId -eq $accepted.fileId -and $reconfirmed.fileKey -ceq $accepted.fileKey -and
        $reconfirmed.uploadedAt -eq $accepted.uploadedAt -and $reconfirmed.sizeBytes -eq $accepted.sizeBytes) `
        'Repeated confirmation must preserve the accepted file identity, key, size and upload time.'
    # Check both the already-issued final URL and a fresh download capability.
    $response = Invoke-SmokeHttp -Method GET -Uri $downloadUri
    Assert-Status $response 200
    Assert-Smoke ([Convert]::ToBase64String($response.Bytes) -ceq [Convert]::ToBase64String($original)) `
        'An already-issued download must still return the original bytes after staging replay.'
    [void] (Assert-DownloadedBytes $owner $upload.fileId $original)
    Write-Output 'PASS same-length staging PUT replay leaves idempotent confirm and original final bytes unchanged'

    $script:Stage = 'declared size mismatch'
    $mismatch = New-SmokeUpload $owner ($original.Length + 1)
    Send-SmokeUpload $mismatch $original
    $response = Invoke-SmokeApi -Method POST -Path ("/api/media-notification/files/{0}/confirm" -f $mismatch.fileId) -Token $owner
    Assert-Status $response 400
    $response = Invoke-SmokeApi -Method GET -Path ("/api/media-notification/files/{0}/download" -f $mismatch.fileId) -Token $owner
    Assert-Status $response 409
    Write-Output 'PASS declared size mismatch rejects confirm with 400 and download with 409'
    Write-Output 'PASS media-storage-smoke: all real HTTP guarantees verified'
}
catch {
    # Do not expose exceptions from HttpClient/JSON/URI parsing: they may contain
    # request URLs or response fragments. A transport failure is ALWAYS a failure.
    $detail = 'Request or response processing failed; sensitive details suppressed.'
    if ($null -ne $script:SafeFailure) { $detail = $script:SafeFailure }
    throw ('FAIL media-storage-smoke [{0}]: {1}' -f $script:Stage, $detail)
}
finally {
    if ($null -ne $script:Client) { $script:Client.Dispose() }
    if ($null -ne $handler) { $handler.Dispose() }
}
